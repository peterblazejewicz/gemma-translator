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

using System.Diagnostics;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GemmaTranslator.Configuration;
using GemmaTranslator.Fonts;
using GemmaTranslator.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    private readonly IAudioCapture _capture;
    private readonly AudioOptions _audioOptions;
    private readonly ILogger<MainViewModel> _logger;

    private long _pressTicks;

    /// <summary>
    /// The language of lane 1, which is the person on the left.
    /// </summary>
    /// <remarks>
    /// The names are the names of the upstream lanes (<c>laneId</c> 1 and 2 in
    /// <c>TranslatorApp.jsx</c>). They do not say "left" and "right", because
    /// the position of a lane is a property of the layout.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TranslatedFont))]
    private Language _lane1Language = Languages.FromCode("ja");

    /// <summary>
    /// The language of lane 2, which is the person on the right.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SourceFont))]
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
    /// The lane that records now, or 0.
    /// </summary>
    /// <remarks>
    /// This is the condition of the operation and the value that the display
    /// shows. The button of the other person does nothing while it is not 0.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLane1Recording))]
    [NotifyPropertyChangedFor(nameof(IsLane2Recording))]
    private int _recordingLane;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModel"/> class.
    /// </summary>
    /// <param name="translator">The translation service from the container.</param>
    /// <param name="capture">The microphone from the container.</param>
    /// <param name="pushToTalk">The two buttons from the container.</param>
    /// <param name="audioOptions">The settings of the microphone.</param>
    /// <param name="logger">The logger from the container.</param>
    public MainViewModel(
        ITranslator translator,
        IAudioCapture capture,
        IPushToTalk pushToTalk,
        IOptions<AudioOptions> audioOptions,
        ILogger<MainViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(translator);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(pushToTalk);
        ArgumentNullException.ThrowIfNull(audioOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _translator = translator;
        _capture = capture;
        _audioOptions = audioOptions.Value;
        _logger = logger;

        pushToTalk.Changed += OnButtonChanged;
        pushToTalk.Start();

        LogStarted(_logger, Lane1Language.Name, Lane2Language.Name);
    }

    /// <summary>
    /// The text that the Moonshine licence conditions make necessary.
    /// </summary>
    public static string Attribution => "Powered by Moonshine AI";

    /// <summary>
    /// The font for <see cref="SourceText"/>, which is the language of lane 2.
    /// </summary>
    /// <remarks>
    /// The display gives the font for each area, because a fallback list
    /// cannot give the correct shape for Chinese and for Japanese at the same
    /// time. See <see cref="AppFonts.For(Language)"/>.
    /// </remarks>
    public FontFamily SourceFont => AppFonts.For(Lane2Language);

    /// <summary>
    /// The font for <see cref="TranslatedText"/>, which is the language of
    /// lane 1.
    /// </summary>
    public FontFamily TranslatedFont => AppFonts.For(Lane1Language);

    /// <summary>
    /// True while the person of lane 1 speaks.
    /// </summary>
    /// <remarks>
    /// The display must show which person the software hears. The button is
    /// a physical part and it gives no light.
    /// </remarks>
    public bool IsLane1Recording => RecordingLane == 1;

    /// <summary>
    /// True while the person of lane 2 speaks.
    /// </summary>
    public bool IsLane2Recording => RecordingLane == 2;

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
    /// A button of a person went down or came up.
    /// </summary>
    /// <remarks>
    /// CAUTION: the event can come on a thread that is not the thread of the
    /// user interface. The Raspberry Pi reads the input device on its own
    /// thread. Each write to a property must go to the correct thread, or
    /// Avalonia throws.
    /// </remarks>
    /// <param name="sender">The source of the event.</param>
    /// <param name="change">The lane, and the new condition of the button.</param>
    private void OnButtonChanged(object? sender, PushToTalkChange change)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            HandleButton(change);
        }
        else
        {
            Dispatcher.UIThread.Post(() => HandleButton(change));
        }
    }

    /// <summary>
    /// Does the work of one change of a button, on the thread of the user
    /// interface.
    /// </summary>
    /// <param name="change">The lane, and the new condition of the button.</param>
    private void HandleButton(PushToTalkChange change)
    {
        if (change.IsPressed)
        {
            StartRecording(change.Lane);
        }
        else
        {
            StopRecording(change.Lane);
        }
    }

    private void StartRecording(int lane)
    {
        // The first press wins. The button of the other person does nothing
        // until the full operation is complete, and not only until the
        // recording stops.
        if (RecordingLane != 0 || TranslateCommand.IsRunning)
        {
            LogButtonIgnored(_logger, lane);
            return;
        }

        try
        {
            _capture.StartRecording();
        }
        catch (AudioCaptureException exception)
        {
            LogCaptureFailed(_logger, exception);
            StatusText = exception.Message;
            return;
        }

        _pressTicks = Stopwatch.GetTimestamp();
        RecordingLane = lane;
        TranslatedText = string.Empty;
        StatusText = "Listening...";
    }

    private void StopRecording(int lane)
    {
        // A release for a lane that does not record is not an error. It occurs
        // if a button was down when the software started, because the software
        // did not see the press.
        if (RecordingLane != lane)
        {
            return;
        }

        TimeSpan held = Stopwatch.GetElapsedTime(_pressTicks);

        RecordingLane = 0;

        Recording? recording = _capture.StopRecording();

        if (held.TotalMilliseconds < _audioOptions.MinimumPressMilliseconds)
        {
            // A physical button in a public location gets an accidental touch.
            LogPressTooShort(_logger, lane, held.TotalMilliseconds);
            StatusText = string.Empty;
            return;
        }

        if (recording is null)
        {
            return;
        }

        SaveForTest(recording);

        // The speech-to-text part has no C# replacement, thus the audio stops
        // here. The next slice gives it to Moonshine and puts the text in
        // SourceText.
        StatusText = string.Create(
            CultureInfo.InvariantCulture,
            $"Lane {lane}: {recording.Duration.TotalSeconds:F1} s, level {recording.PeakLevel:F2}. Speech-to-text comes later.");
    }

    /// <summary>
    /// Writes the recording to a file, if the settings ask for it.
    /// </summary>
    /// <remarks>
    /// This is for a test of the microphone with real speech. It is off if
    /// <see cref="AudioOptions.SaveRecordingsTo"/> is empty.
    /// </remarks>
    /// <param name="recording">The audio.</param>
    private void SaveForTest(Recording recording)
    {
        string directory = _audioOptions.SaveRecordingsTo?.Trim() ?? string.Empty;

        if (directory.Length == 0)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(directory);

            string name = string.Create(
                CultureInfo.InvariantCulture,
                $"recording-{DateTime.Now:yyyyMMdd-HHmmss-fff}.wav");

            string path = Path.Combine(directory, name);

            WavFile.Write(path, recording.Samples, _audioOptions.SampleRate);

            LogRecordingSaved(_logger, path);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            LogRecordingNotSaved(_logger, directory, exception);
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

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The button of lane {lane} did nothing, because the software is occupied.")]
    private static partial void LogButtonIgnored(ILogger logger, int lane);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The press on lane {lane} was {milliseconds:F0} ms, which is too short.")]
    private static partial void LogPressTooShort(ILogger logger, int lane, double milliseconds);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The microphone did not start.")]
    private static partial void LogCaptureFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The recording is in {path}. This is for a test only.")]
    private static partial void LogRecordingSaved(ILogger logger, string path);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The software cannot write a recording in {directory}.")]
    private static partial void LogRecordingNotSaved(ILogger logger, string directory, Exception exception);
}
