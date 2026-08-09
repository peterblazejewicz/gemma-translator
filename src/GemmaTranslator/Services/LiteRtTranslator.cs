// Copyright 2026 Google LLC
// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// This file is part of a fork of google-gemma/gemma-translator and has been
// modified. It replaces translateText and generatePayloadJSON of
// frontend/src/utils/api.js.

using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GemmaTranslator.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GemmaTranslator.Services;

/// <summary>
/// Translates with the LiteRT-LM server, which speaks the OpenAI protocol.
/// </summary>
/// <remarks>
/// The upstream <c>useProxy</c> value is not here. The browser sent each call
/// through <c>/proxy</c> in <c>server.py</c> to keep the same origin. C# has no
/// browser and no same-origin rule, thus the software speaks to the endpoint
/// directly.
/// </remarks>
public sealed partial class LiteRtTranslator : ITranslator
{
    private readonly HttpClient _httpClient;
    private readonly LiteRtOptions _options;
    private readonly ILogger<LiteRtTranslator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="LiteRtTranslator"/> class.
    /// </summary>
    /// <param name="httpClient">The client from the container.</param>
    /// <param name="options">The settings of the server.</param>
    /// <param name="logger">The logger from the container.</param>
    public LiteRtTranslator(
        HttpClient httpClient,
        IOptions<LiteRtOptions> options,
        ILogger<LiteRtTranslator> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<TranslationResult> TranslateAsync(
        string text,
        Language source,
        Language target,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        Uri url = new(_options.GetBaseUri(), "chat/completions");

        ChatCompletionRequest body = new(
            _options.ModelName,
            [
                new ChatMessage("system", JsonEnvelopePrompt.Make(source, target)),
                new ChatMessage("user", text),
            ]);

        // CAUTION: make the JSON first and send it as a StringContent.
        //
        // JsonContent.Create does not know the length of the body, thus
        // HttpClient sends `Transfer-Encoding: chunked` and no
        // `Content-Length`. The server of `litert-lm serve` is a simple Python
        // HTTP server: it reads `Content-Length` only, it gets no body, and it
        // answers `400 Invalid JSON`. A test on 2026-08-09 confirmed this.
        string json = JsonSerializer.Serialize(
            body,
            OpenAiJsonContext.Default.ChatCompletionRequest);

        using HttpRequestMessage request = new(HttpMethod.Post, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

        string apiKey = _options.ApiKey?.Trim() ?? string.Empty;
        if (apiKey.Length > 0)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        long startTicks = Stopwatch.GetTimestamp();

        HttpResponseMessage response;
        try
        {
            response = await _httpClient
                .SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            LogNoServer(_logger, url.AbsoluteUri, exception);
            throw new TranslationException(
                "The translation server did not answer.",
                exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogTooSlow(_logger, url.AbsoluteUri, _options.TimeoutSeconds, exception);
            throw new TranslationException(
                "The translation server took too much time.",
                exception);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                string errorBody = await ReadBodyAsync(response, cancellationToken)
                    .ConfigureAwait(false);

                int status = (int)response.StatusCode;

                // The body goes to the log and not to the display. The person
                // at the appliance cannot act on a Python traceback, and the
                // text comes from a machine that we do not control.
                LogBadStatus(_logger, status, url.AbsoluteUri, errorBody);

                throw new TranslationException(
                    $"The translation server gave status {status}.");
            }

            ChatCompletionResponse? completion;
            try
            {
                completion = await response.Content
                    .ReadFromJsonAsync(
                        OpenAiJsonContext.Default.ChatCompletionResponse,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (
                exception is JsonException
                    or InvalidOperationException
                    or NotSupportedException)
            {
                // InvalidOperationException comes from a character set in
                // `Content-Type` that .NET does not know. One header of that
                // shape stopped the software before this catch was here.
                LogBadJson(_logger, url.AbsoluteUri, exception);
                throw new TranslationException(
                    "The translation server sent a body that is not JSON.",
                    exception);
            }

            TimeSpan duration = Stopwatch.GetElapsedTime(startTicks);

            string modelText = completion?.Choices is { Count: > 0 } choices
                ? choices[0].Message?.Content ?? string.Empty
                : string.Empty;

            // CAUTION: an empty answer must be an error and not an empty
            // display. Status 200 with no `choices` gave a correct log line, a
            // time, and no text on the screen. The two channels of the
            // appliance then told the person that the operation was correct.
            if (string.IsNullOrWhiteSpace(modelText))
            {
                LogNoAnswer(_logger, url.AbsoluteUri);
                throw new TranslationException(
                    "The translation server gave an answer with no text.");
            }

            if (!JsonEnvelopePrompt.TryRead(modelText, out string translation))
            {
                // Upstream shows the full text of the model, and a person must
                // see something. But the log must say that this occurred.
                LogModelDidNotObey(_logger, modelText.Length);
                translation = modelText;
            }

            int tokens = completion?.Usage?.TotalTokens ?? 0;

            LogTranslated(_logger, source.Code, target.Code, duration.TotalSeconds, tokens);

            return new TranslationResult(translation, duration, tokens);
        }
    }

    /// <summary>
    /// Reads the body of a response that gave an error.
    /// </summary>
    /// <remarks>
    /// The read can throw if the character set of the response is not known.
    /// The caller is already on an error path, thus a second error must not
    /// replace the first one.
    /// </remarks>
    /// <param name="response">The response.</param>
    /// <param name="cancellationToken">Stops the read.</param>
    /// <returns>The body, or a note that the body could not be read.</returns>
    private static async Task<string> ReadBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or NotSupportedException or HttpRequestException)
        {
            return $"(the body could not be read: {exception.Message})";
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Translated {source} to {target} in {seconds:F2} s with {tokens} tokens.")]
    private static partial void LogTranslated(
        ILogger logger,
        string source,
        string target,
        double seconds,
        int tokens);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The translation server at {url} did not answer.")]
    private static partial void LogNoServer(ILogger logger, string url, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The translation server at {url} took more than {seconds} seconds.")]
    private static partial void LogTooSlow(
        ILogger logger,
        string url,
        int seconds,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The translation server at {url} gave status {status}. The body is: {body}")]
    private static partial void LogBadStatus(
        ILogger logger,
        int status,
        string url,
        string body);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The translation server at {url} sent a body that is not JSON.")]
    private static partial void LogBadJson(ILogger logger, string url, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The translation server at {url} gave an answer with no text.")]
    private static partial void LogNoAnswer(ILogger logger, string url);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The model did not send a JSON object. The display shows the full text of {length} characters.")]
    private static partial void LogModelDidNotObey(ILogger logger, int length);
}
