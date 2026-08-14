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
// modified. It replaces transcribeAudio of frontend/src/utils/api.js and the
// /api/tts calls of frontend/src/TranslatorApp.jsx.

using System.Buffers;
using System.Buffers.Text;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using GemmaTranslator.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GemmaTranslator.Services.Speech;

/// <summary>
/// Speaks to the Moonshine server of <c>backend/server.py</c>, which holds the
/// speech-to-text part and the text-to-speech part.
/// </summary>
public sealed partial class MoonshineSpeechService : ISpeechService
{
    private const string TranscribePath = "api/stt";
    private const string SynthesizePath = "api/tts";
    private const string WarmPath = "api/warm";
    private const string WavMediaType = "audio/wav";

    // `{"audio_base64":"` is 17 bytes and `","language":"xx"}` is 18. 64 keeps
    // a margin, thus the writer of WriteTranscribeBody does not grow.
    private const int JsonOverheadBytes = 64;

    private const int MaximumQueryLength = 65_000;

    private readonly HttpClient _httpClient;
    private readonly SpeechOptions _options;
    private readonly ILogger<MoonshineSpeechService> _logger;

    public MoonshineSpeechService(
        HttpClient httpClient,
        IOptions<SpeechOptions> options,
        ILogger<MoonshineSpeechService> logger)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string> TranscribeAsync(
        ReadOnlyMemory<float> samples,
        Language language,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(language);

        // With no test, backend/server.py answers 500 for an empty
        // audio_base64, and the display then accuses the server.
        if (samples.IsEmpty)
        {
            throw new SpeechException("There is no audio to transcribe.");
        }

        Uri url = new(_options.GetBaseUri(), TranscribePath);

        ArrayBufferWriter<byte> body = WriteTranscribeBody(samples.Span, language);
        long startTicks = Stopwatch.GetTimestamp();

        HttpResponseMessage response;
        try
        {
            // CAUTION: the length of the body must be known before the send.
            //
            // A content that cannot give its length makes HttpClient send
            // `Transfer-Encoding: chunked` and no `Content-Length`. The server
            // is a simple HTTP server in Python: backend/server.py reads
            // `Content-Length` only, it then gets no body, and it answers 500.
            ReadOnlyMemoryContent content = new(body.WrittenMemory);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

            using HttpRequestMessage request = new(HttpMethod.Post, url)
            {
                Content = content,
            };

            response = await CallAsync(request, TranscribePath, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            // SECURITY CONTROL. Do not delete this line. This buffer holds the
            // recorded voice of a person as base64, and without the wipe that
            // audio is readable in a memory dump of the process until a later
            // allocation takes the memory. Recording.Dispose does the same for
            // the samples of the microphone.
            //
            // The wipe covers the buffer that this code owns. HttpClient copies
            // the bytes into pooled write buffers and returns them unwiped,
            // thus a memory dump is not clean.
            body.Clear();
        }

        using (response)
        {
            ThrowIfNotSuccess(response, TranscribePath);

            TranscriptResponse? result;
            try
            {
                result = await response.Content
                    .ReadFromJsonAsync(SpeechJsonContext.Default.TranscriptResponse, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (JsonException exception)
            {
                // The message of a JsonException holds the characters where the
                // read stopped, and this body holds the words of a person. Thus
                // the exception does not go to the log. The path and the
                // position are a name of a field and a number.
                LogBadJson(_logger, exception.Path ?? "(none)", exception.BytePositionInLine ?? -1);

                throw new SpeechException("The speech server sent a body that is not JSON.");
            }
            catch (Exception exception) when (
                exception is InvalidOperationException or NotSupportedException)
            {
                LogBadCharacterSet(_logger, exception);

                throw new SpeechException(
                    "The speech server sent a body that is not JSON.",
                    exception);
            }

            TimeSpan duration = Stopwatch.GetElapsedTime(startTicks);

            // An empty text is not an error. Moonshine gives no words when the
            // microphone heard no speech, and upstream puts that empty text on
            // the display. The translation part is not the same: there an empty
            // answer is an error.
            string text = result?.Text ?? string.Empty;

            LogTranscribed(_logger, language.Code, samples.Length, duration.TotalSeconds, text.Length);

            return text;
        }
    }

    public async Task<SpokenAudio> SynthesizeAsync(
        string text,
        Language language,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(language);

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new SpeechException("There is no text to speak.");
        }

        // `lang` is Language.Code. TTS_LANG_MAP in backend/server.py makes the
        // change to the code of Moonshine, and TTS_VOICE_MAP selects the voice
        // of Chinese.
        Uri url = MakeSynthesizeUrl(text, language);

        long startTicks = Stopwatch.GetTimestamp();

        using HttpRequestMessage request = new(HttpMethod.Get, url);

        using HttpResponseMessage response =
            await CallAsync(request, SynthesizePath, cancellationToken).ConfigureAwait(false);

        ThrowIfNotSuccess(response, SynthesizePath);

        long declared = response.Content.Headers.ContentLength ?? -1;

        // The test is before the read, because ReadAsByteArrayAsync copies the
        // buffer of SendAsync and that copy is 16 MB at the limit. The test is
        // strict, thus `audio/wave` is refused: backend/server.py sends
        // `audio/wav` and this software speaks to that server only.
        string mediaType = response.Content.Headers.ContentType?.MediaType ?? "(none)";

        if (!string.Equals(mediaType, WavMediaType, StringComparison.OrdinalIgnoreCase))
        {
            LogBadMediaType(_logger, mediaType, declared);
            throw new SpeechException($"The speech server sent {mediaType} and not {WavMediaType}.");
        }

        byte[] wav = await response.Content
            .ReadAsByteArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        // SendAsync buffers the body and throws if it is not complete. An answer
        // of 200 with 0 bytes does not throw.
        if (wav.Length == 0)
        {
            LogBadLength(_logger, declared, wav.Length);
            throw new SpeechException("The speech server sent audio that is not complete.");
        }

        TimeSpan duration = Stopwatch.GetElapsedTime(startTicks);

        LogSpoke(_logger, language.Code, text.Length, duration.TotalSeconds, wav.Length);

        return new SpokenAudio(wav);
    }

    /// <remarks>
    /// CAUTION: this makes one call for all the text. Upstream cut the text
    /// into pieces of about 180 characters and chained the calls, thus the
    /// browser started the audio of the first piece while the server made the
    /// second one. A long text gives no sound for a longer time before the
    /// first word.
    /// </remarks>
    private Uri MakeSynthesizeUrl(string text, Language language)
    {
        string query =
            $"{SynthesizePath}?text={Uri.EscapeDataString(text)}&lang={Uri.EscapeDataString(language.Code)}";

        // backend/server.py reads the request line with readline(65537) and
        // answers 414 above 65536 bytes. That line is "GET /", the query, and
        // " HTTP/1.1", thus 65000 keeps a margin. EscapeDataString gives ASCII,
        // thus one character of the query is one byte and one Japanese
        // character is 9 of them.
        //
        // CAUTION: Uri makes no limit of its own. A measurement on .NET 10
        // built an address of 200043 characters with no error, thus a test of
        // UriFormatException here catches nothing.
        if (query.Length > MaximumQueryLength)
        {
            LogTextTooLong(_logger, text.Length);

            throw new SpeechException(
                "The text to speak is too long for one call to the speech server.");
        }

        return new Uri(_options.GetBaseUri(), query);
    }

    /// <remarks>
    /// The writer gets the full length at the start. Without it each array that
    /// it outgrows keeps a part of the speech of a person until a collection.
    /// </remarks>
    private static ArrayBufferWriter<byte> WriteTranscribeBody(
        ReadOnlySpan<float> samples,
        Language language)
    {
        // backend/server.py reads the bytes with
        // np.frombuffer(raw, dtype=np.float32), thus it needs raw
        // little-endian float32 and not a WAV file. This cast gives the
        // sequence of the bytes of the machine, and the two targets of this
        // software are little-endian.
        ReadOnlySpan<byte> raw = MemoryMarshal.AsBytes(samples);

        ArrayBufferWriter<byte> body =
            new(Base64.GetMaxEncodedToUtf8Length(raw.Length) + JsonOverheadBytes);

        try
        {
            using Utf8JsonWriter writer = new(body);

            writer.WriteStartObject();
            writer.WriteBase64String("audio_base64"u8, raw);
            writer.WriteString("language"u8, language.Code);
            writer.WriteEndObject();
        }
        catch
        {
            // SECURITY CONTROL. Do not delete this. The buffer above is 10 MB
            // for the longest recording, and the lines above fill it with the
            // voice of a person. The caller wipes it after the send, but the
            // caller never receives it if a write throws here. Out of memory is
            // the failure to expect: the appliance has 4 GB and a model of
            // 2.4 GB is already in it. Without this, that speech stays readable
            // in the heap until a later allocation takes the memory.
            body.Clear();
            throw;
        }

        return body;
    }

    public async Task WarmAsync(Language language, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(language);

        // CAUTION: the address must be absolute. The client of this service has
        // no BaseAddress, thus a relative address gives
        // InvalidOperationException at the call and not at the build.
        Uri url = new(
            _options.GetBaseUri(),
            $"{WarmPath}?lang={Uri.EscapeDataString(language.Code)}");

        long startTicks = Stopwatch.GetTimestamp();

        using HttpRequestMessage request = new(HttpMethod.Get, url);

        using HttpResponseMessage response =
            await CallAsync(request, WarmPath, cancellationToken).ConfigureAwait(false);

        ThrowIfNotSuccess(response, WarmPath);

        // The body says which models the server made now and which it held
        // already. The software does not read it: the time in the line below
        // gives the same fact, and a value near 0 says that the models were
        // there.
        double seconds = Stopwatch.GetElapsedTime(startTicks).TotalSeconds;

        LogWarmed(_logger, language.Code, seconds);
    }

    private async Task<HttpResponseMessage> CallAsync(
        HttpRequestMessage request,
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException exception)
        {
            LogNoServer(_logger, path, exception);
            throw new SpeechException("The speech server did not answer.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogTooSlow(_logger, path, _httpClient.Timeout.TotalSeconds, exception);
            throw new SpeechException("The speech server took too much time.", exception);
        }
    }

    private void ThrowIfNotSuccess(HttpResponseMessage response, string path)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        int status = (int)response.StatusCode;

        // The body does not go to the log. backend/server.py sends str(error)
        // as plain text and not as JSON, and that message comes from a process
        // that holds the speech of a person. The status line and the length
        // name the failure and hold no speech.
        LogBadStatus(
            _logger,
            path,
            status,
            response.ReasonPhrase ?? "(none)",
            response.Content.Headers.ContentLength ?? -1);

        throw new SpeechException($"The speech server gave status {status}.");
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Transcribed {samples} samples of {language} in {seconds:F2} s. The text has {characters} characters.")]
    private static partial void LogTranscribed(
        ILogger logger,
        string language,
        int samples,
        double seconds,
        int characters);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The models of {language} are ready. The call took {seconds:F2} s; a value near 0 says that the server held them already.")]
    private static partial void LogWarmed(ILogger logger, string language, double seconds);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Spoke {characters} characters of {language} in {seconds:F2} s. The audio is {bytes} bytes.")]
    private static partial void LogSpoke(
        ILogger logger,
        string language,
        int characters,
        double seconds,
        int bytes);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The speech server did not answer at {path}.")]
    private static partial void LogNoServer(ILogger logger, string path, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The speech server took more than {seconds:F0} seconds at {path}.")]
    private static partial void LogTooSlow(
        ILogger logger,
        string path,
        double seconds,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The speech server gave status {status} ({reason}) at {path}. The body is {bytes} bytes, and -1 says that the server gave no length.")]
    private static partial void LogBadStatus(
        ILogger logger,
        string path,
        int status,
        string reason,
        long bytes);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The speech server sent a body that is not JSON. The read stopped at {path}, {bytePosition} bytes into the line.")]
    private static partial void LogBadJson(ILogger logger, string path, long bytePosition);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The speech server sent a character set that .NET does not know.")]
    private static partial void LogBadCharacterSet(ILogger logger, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The speech server sent {mediaType} and not audio. The body is {bytes} bytes, and -1 says that the server gave no length.")]
    private static partial void LogBadMediaType(ILogger logger, string mediaType, long bytes);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The speech server told of {declared} bytes of audio and sent {received}. A value of -1 says that the server gave no length.")]
    private static partial void LogBadLength(ILogger logger, long declared, int received);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The text of {characters} characters makes an address that is too long for one call.")]
    private static partial void LogTextTooLong(ILogger logger, int characters);
}
