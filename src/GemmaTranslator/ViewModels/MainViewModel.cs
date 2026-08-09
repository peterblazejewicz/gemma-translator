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
// This file is part of a fork of google-gemma/gemma-translator and has
// been modified.

using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GemmaTranslator.Services;
using Microsoft.Extensions.Logging;

namespace GemmaTranslator.ViewModels;

/// <summary>
/// The data of the main view.
/// </summary>
/// <remarks>
/// The translation part operates. The speech-to-text part, the audio capture,
/// and the text-to-speech part come later. Until the microphone operates,
/// <see cref="SourceText"/> holds a constant example.
/// </remarks>
public sealed partial class MainViewModel : ObservableObject
{
    /// <summary>
    /// The text that the software translates until the microphone operates.
    /// </summary>
    /// <remarks>
    /// CAUTION: this constant goes away with the speech-to-text slice. Then
    /// the microphone and Moonshine make this text.
    /// </remarks>
    private const string ExampleText = "Where is the railway station?";

    private readonly ITranslator _translator;
    private readonly ILogger<MainViewModel> _logger;

    /// <summary>
    /// The language of lane 1, which is the person on the left.
    /// </summary>
    /// <remarks>
    /// The names are the names of the upstream lanes (<c>laneId</c> 1 and 2 in
    /// <c>TranslatorApp.jsx</c>). They do not say "left" and "right", because
    /// the position of a lane is a property of the layout.
    /// </remarks>
    [ObservableProperty]
    private Language _lane1Language = Languages.FromCode("ja");

    /// <summary>
    /// The language of lane 2, which is the person on the right.
    /// </summary>
    [ObservableProperty]
    private Language _lane2Language = Languages.FromCode("en");

    /// <summary>
    /// The text that the person said, in the language of that person.
    /// </summary>
    [ObservableProperty]
    private string _sourceText = ExampleText;

    /// <summary>
    /// The text in the language of the other person.
    /// </summary>
    [ObservableProperty]
    private string _translatedText = string.Empty;

    /// <summary>
    /// The line that shows the time and the quantity of tokens.
    /// </summary>
    /// <remarks>
    /// Upstream shows this at <c>TranslatorApp.jsx:229</c>.
    /// </remarks>
    [ObservableProperty]
    private string _statusText = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModel"/> class.
    /// </summary>
    /// <param name="translator">The translation service from the container.</param>
    /// <param name="logger">The logger from the container.</param>
    public MainViewModel(ITranslator translator, ILogger<MainViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(translator);
        ArgumentNullException.ThrowIfNull(logger);

        _translator = translator;
        _logger = logger;

        LogStarted(_logger, Lane1Language.Name, Lane2Language.Name);
    }

    /// <summary>
    /// The text that the Moonshine licence conditions make necessary.
    /// </summary>
    public static string Attribution => "Powered by Moonshine AI";

    /// <summary>
    /// Translates <see cref="SourceText"/> from lane 2 into lane 1.
    /// </summary>
    /// <remarks>
    /// The direction is the direction of upstream: the person who speaks is
    /// the source, and the other person is the target. A tap starts this
    /// command now, and the audio slice replaces the tap with the end of the
    /// speech.
    /// </remarks>
    /// <param name="cancellationToken">Stops the translation.</param>
    /// <returns>The task of the operation.</returns>
    [RelayCommand(IncludeCancelCommand = true)]
    private async Task TranslateAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(SourceText))
        {
            // The speech-to-text slice gives an empty text if it hears no
            // speech. A call with no text takes seconds and gives nothing.
            TranslatedText = string.Empty;
            StatusText = "(No speech detected)";
            return;
        }

        StatusText = "Translating...";
        TranslatedText = string.Empty;

        try
        {
            TranslationResult result = await _translator
                .TranslateAsync(SourceText, Lane2Language, Lane1Language, cancellationToken)
                .ConfigureAwait(true);

            TranslatedText = result.Translation;
            StatusText = MakeStatus(result);
        }
        catch (OperationCanceledException)
        {
            TranslatedText = string.Empty;
            StatusText = "(Stopped)";
        }
        catch (TranslationException exception)
        {
            LogTranslationFailed(_logger, exception);
            TranslatedText = "(Translation failed)";
            StatusText = exception.Message;
        }
#pragma warning disable CA1031 // The appliance must not stop. See the remark.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            // CAUTION: this catch is the last one on purpose.
            //
            // AsyncRelayCommand awaits the task and throws again on a thread of
            // the pool. Nothing catches that, thus one error of a type that we
            // did not expect stops the process. The Raspberry Pi has no
            // keyboard and no console: the display becomes black, systemd
            // starts the software again, and the same answer stops it again.
            // A defect in one service must not do this.
            LogTranslationFailed(_logger, exception);
            TranslatedText = "(Translation failed)";
            StatusText = "The translation did not occur.";
        }
    }

    /// <summary>
    /// Makes the line that shows the time and the quantity of tokens.
    /// </summary>
    /// <param name="result">The result of the translation.</param>
    /// <returns>The text for <see cref="StatusText"/>.</returns>
    private static string MakeStatus(TranslationResult result)
        => result.TotalTokens is int tokens
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"Duration: {result.Duration.TotalSeconds:F2}s | Tokens: {tokens}")
            : string.Create(
                CultureInfo.InvariantCulture,
                $"Duration: {result.Duration.TotalSeconds:F2}s");

    /// <summary>
    /// Writes the line that shows that the user interface started.
    /// </summary>
    /// <param name="logger">The logger.</param>
    /// <param name="lane1Language">The name of the language of lane 1.</param>
    /// <param name="lane2Language">The name of the language of lane 2.</param>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The user interface started. Lane 1 is {lane1Language} and lane 2 is {lane2Language}.")]
    private static partial void LogStarted(
        ILogger logger,
        string lane1Language,
        string lane2Language);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The translation did not occur.")]
    private static partial void LogTranslationFailed(ILogger logger, Exception exception);
}
