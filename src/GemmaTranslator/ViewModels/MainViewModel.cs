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
// been modified. It replaces frontend/src/TranslatorApp.jsx.

using System.Diagnostics;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using GemmaTranslator.Configuration;
using GemmaTranslator.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GemmaTranslator.ViewModels;

/// <summary>
/// The shell of the user interface, and the one state machine of the
/// appliance.
/// </summary>
/// <remarks>
/// <para>
/// This fork has no test project, thus the sequence of the states must be
/// legible in one location. Each change of the state writes one line of the
/// log, and the journal of systemd is the record of what the appliance did.
/// </para>
/// <para>
/// Upstream keeps the same condition in one React component with a conditional
/// render, and it has no router. See <c>TranslatorApp.jsx</c>.
/// </para>
/// <para>
/// CAUTION: the speech-to-text part has no C# code. The release of a button
/// goes to <see cref="ExampleText"/> and not to the words of the person. Each
/// other part of the sequence is complete.
/// </para>
/// </remarks>
public sealed partial class MainViewModel : ObservableObject
{
    /// <summary>
    /// The text that the software translates until the microphone gives words.
    /// </summary>
    /// <remarks>
    /// CAUTION: this constant goes away with the speech-to-text slice. Then
    /// Moonshine makes this text from the audio of the person.
    /// </remarks>
    private const string ExampleText = "Where is the nearest train station?";

    /// <summary>
    /// How long a message stays on the display.
    /// </summary>
    /// <remarks>
    /// The value comes from the design. A message that stays for all time
    /// covers the conversation, and the appliance has no keyboard that can
    /// remove it.
    /// </remarks>
    private static readonly TimeSpan StatusLife = TimeSpan.FromSeconds(6);

    /// <summary>
    /// The interval between two frames of the visualizer and of the time of the
    /// recording.
    /// </summary>
    /// <remarks>
    /// 33 ms is about 30 frames each second. The design gives 60, and the bars
    /// are decoration: 30 looks the same to a person and it gives the Raspberry
    /// Pi half of the work.
    /// </remarks>
    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(33);

    private readonly ITranslator _translator;
    private readonly IAudioCapture _capture;
    private readonly IUserSettingsStore _settings;
    private readonly AudioOptions _audioOptions;
    private readonly ILogger<MainViewModel> _logger;

    private long _pressTicks;

    // The same limit as the buffer, on the side of the user interface. The
    // buffer protects the memory; this gives the lane back. A release can go
    // away, and then nothing else ends the recording.
    private DispatcherTimer? _limitTimer;

    // The bars of the visualizer and the time of the recording.
    private DispatcherTimer? _frameTimer;

    // The message goes away without a touch, because the appliance has no
    // keyboard.
    private DispatcherTimer? _statusTimer;

    /// <summary>
    /// What the appliance does now.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdlePrompt))]
    [NotifyPropertyChangedFor(nameof(IsRecording))]
    [NotifyPropertyChangedFor(nameof(IsStatusVisible))]
    private AppState _state = AppState.Idle;

    /// <summary>
    /// Which of the two operations of <see cref="AppState.Working"/> operates.
    /// </summary>
    [ObservableProperty]
    private WorkStage _workStage;

    /// <summary>
    /// What one person said and the translation of it, or <c>null</c>.
    /// </summary>
    /// <remarks>
    /// This stays after the operation is complete. A person reads the answer
    /// while the appliance is idle again, thus the display keeps the two texts
    /// until the next person speaks.
    /// </remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasExchange))]
    [NotifyPropertyChangedFor(nameof(IsIdlePrompt))]
    private Exchange? _exchange;

    /// <summary>
    /// One short sentence for a person, or <c>null</c>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusVisible))]
    private string? _statusText;

    /// <summary>
    /// The charge of the cells, as the display shows it.
    /// </summary>
    [ObservableProperty]
    private BatteryStatus _battery = BatteryStatus.From(new PowerState(null, null));

    /// <summary>
    /// The lane that records now, or 0.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordingLabel))]
    private int _recordingLane;

    /// <summary>
    /// How long the person has held the button, as <c>m:ss</c>.
    /// </summary>
    [ObservableProperty]
    private string _recordingTime = "0:00";

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModel"/> class.
    /// </summary>
    /// <param name="translator">The translation service from the container.</param>
    /// <param name="capture">The microphone from the container.</param>
    /// <param name="pushToTalk">The two buttons from the container.</param>
    /// <param name="power">The electrical supply from the container.</param>
    /// <param name="settings">The selections of a person, from the container.</param>
    /// <param name="audioOptions">The settings of the microphone.</param>
    /// <param name="logger">The logger from the container.</param>
    public MainViewModel(
        ITranslator translator,
        IAudioCapture capture,
        IPushToTalk pushToTalk,
        IPowerMonitor power,
        IUserSettingsStore settings,
        IOptions<AudioOptions> audioOptions,
        ILogger<MainViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(translator);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(pushToTalk);
        ArgumentNullException.ThrowIfNull(power);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(audioOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _translator = translator;
        _capture = capture;
        _settings = settings;
        _audioOptions = audioOptions.Value;
        _logger = logger;

        // The two lanes cannot hold the same language. That rule is a rule
        // about the pair, thus it is here and not in the lane.
        Lane1 = new LaneViewModel(1, Languages.FromCode("ja"), Turn);
        Lane2 = new LaneViewModel(2, Languages.FromCode("en"), Turn);

        // The view model listens only. App starts the buttons, because the
        // Windows implementation needs the top level and that does not exist
        // when the container makes this class.
        pushToTalk.Changed += OnButtonChanged;
        power.Changed += OnPowerChanged;

        Battery = BatteryStatus.From(power.Current);
        ResetLevels();

        LogUserInterfaceStarted(_logger, Lane1.Language.Name, Lane2.Language.Name);
    }

    /// <summary>
    /// The text that the Moonshine licence conditions make necessary.
    /// </summary>
    public static string Attribution => "Powered by Moonshine AI";

    /// <summary>The person on the left.</summary>
    public LaneViewModel Lane1 { get; }

    /// <summary>The person on the right.</summary>
    public LaneViewModel Lane2 { get; }

    /// <summary>
    /// The count of the bars of the visualizer, from the settings.
    /// </summary>
    public int BarCount => _settings.Current.VisualizerBars;

    /// <summary>
    /// <c>true</c> when the display shows "Hold a button to talk".
    /// </summary>
    /// <remarks>
    /// The prompt goes away at the first conversation and it does not come
    /// back. A person who used the appliance one time knows what to do.
    /// </remarks>
    public bool IsIdlePrompt => State == AppState.Idle && Exchange is null;

    /// <summary>
    /// <c>true</c> when the display shows the two texts.
    /// </summary>
    public bool HasExchange => Exchange is not null;

    /// <summary>
    /// <c>true</c> while the software hears a person.
    /// </summary>
    public bool IsRecording => State == AppState.Recording;

    /// <summary>
    /// <c>true</c> when the display shows a message.
    /// </summary>
    /// <remarks>
    /// A message and the pill of the recording go in the same position. The
    /// recording wins: a person must see that the microphone is open.
    /// </remarks>
    public bool IsStatusVisible => StatusText is not null && !IsRecording;

    /// <summary>
    /// The text of the pill while the microphone is open.
    /// </summary>
    public string RecordingLabel => string.Create(
        CultureInfo.InvariantCulture,
        $"RECORDING · SPEAKER {(RecordingLane == 0 ? 1 : RecordingLane)}");

    /// <summary>
    /// Changes the state and writes one line of the log.
    /// </summary>
    /// <remarks>
    /// Each change goes through this method. With no test project the journal
    /// is what says that the appliance did what it must do.
    /// </remarks>
    /// <param name="next">The state that comes now.</param>
    private void GoTo(AppState next)
    {
        if (State == next)
        {
            return;
        }

        LogState(_logger, State, next);
        State = next;
    }

    /// <summary>
    /// Turns the drum of one lane and keeps the two languages different.
    /// </summary>
    /// <remarks>
    /// The step goes past the language of the other lane, in the direction that
    /// the person asked for. Thus a touch always changes the language, and the
    /// two lanes never agree.
    /// </remarks>
    /// <param name="lane">The lane of the arrow that the person touched.</param>
    /// <param name="direction">-1 for the arrow above, 1 for the arrow below.</param>
    private void Turn(LaneViewModel lane, int direction)
    {
        if (State == AppState.Recording)
        {
            return;
        }

        LaneViewModel other = lane.Number == 1 ? Lane2 : Lane1;
        int count = Languages.All.Count;

        int index = Languages.All.ToList().FindIndex(x => x.Code == lane.Language.Code);
        index = ((index + direction) % count + count) % count;

        if (Languages.All[index].Code == other.Language.Code)
        {
            index = ((index + direction) % count + count) % count;
        }

        lane.Language = Languages.All[index];

        LogLanguage(_logger, lane.Number, lane.Language.Name);
    }

    /// <summary>
    /// Shows one sentence, and removes it after <see cref="StatusLife"/>.
    /// </summary>
    /// <param name="text">The sentence for the person.</param>
    private void ShowStatus(string text)
    {
        StatusText = text;

        _statusTimer?.Stop();
        _statusTimer = new DispatcherTimer { Interval = StatusLife };

        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer?.Stop();
            _statusTimer = null;
            StatusText = null;
        };

        _statusTimer.Start();
    }

    /// <summary>
    /// The electrical supply changed.
    /// </summary>
    /// <remarks>
    /// CAUTION: this event comes on the thread that reads the files of the
    /// <c>power_supply</c> class. Each write to a property must go to the
    /// thread of the user interface.
    /// </remarks>
    /// <param name="sender">The source of the event.</param>
    /// <param name="state">The new condition of the electrical supply.</param>
    private void OnPowerChanged(object? sender, PowerState state)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Battery = BatteryStatus.From(state);
        }
        else
        {
            Dispatcher.UIThread.Post(() => Battery = BatteryStatus.From(state));
        }
    }

    /// <summary>
    /// Makes the sequence of the operation after a person releases a button.
    /// </summary>
    /// <remarks>
    /// CAUTION: the first stage has no C# code. The speech-to-text part comes
    /// later, thus the software takes <see cref="ExampleText"/> and goes to the
    /// translation. The stages and the display are the stages and the display
    /// of the complete operation.
    /// </remarks>
    /// <param name="lane">The lane of the person who spoke.</param>
    /// <returns>The task of the operation.</returns>
    private async Task RunPipelineAsync(LaneViewModel lane)
    {
        LaneViewModel other = lane.Number == 1 ? Lane2 : Lane1;

        GoTo(AppState.Working);
        WorkStage = WorkStage.Listening;

        Exchange = new Exchange
        {
            SourceLanguage = lane.Language,
            TargetLanguage = other.Language,
            SourceText = "Listening…",
            TargetText = "—",
            SourceIsLane2 = lane.Number == 2,
            IsSourceMuted = true,
            IsTargetMuted = true,
        };

        // The speech-to-text part goes here.
        string heard = ExampleText;

        WorkStage = WorkStage.Translating;

        Exchange = Exchange with
        {
            SourceText = heard,
            TargetText = "Translating…",
            IsSourceMuted = false,
            IsTargetMuted = true,
        };

        try
        {
            TranslationResult result = await _translator
                .TranslateAsync(heard, lane.Language, other.Language)
                .ConfigureAwait(true);

            Exchange = Exchange with
            {
                TargetText = result.Translation,
                IsTargetMuted = false,
            };

            LogTranslated(_logger, result.Duration.TotalSeconds, result.TotalTokens);
        }
        catch (TranslationException exception)
        {
            LogTranslationFailed(_logger, exception);
            Exchange = Exchange with { TargetText = "—", IsTargetMuted = true };
            ShowStatus("Translation service isn't responding.");
        }
#pragma warning disable CA1031 // The appliance must not stop. See the remark.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            // CAUTION: this catch is the last one on purpose. Nothing awaits
            // this task, thus an error of a type that we did not expect stops
            // the process. The Raspberry Pi has no keyboard and no console: the
            // display becomes black, systemd starts the software again, and the
            // same answer stops it again.
            LogTranslationFailed(_logger, exception);
            Exchange = Exchange with { TargetText = "—", IsTargetMuted = true };
            ShowStatus("Translation service isn't responding.");
        }

        GoTo(AppState.Result);
    }

    /// <summary>
    /// Starts the timer that ends a recording with no end.
    /// </summary>
    /// <remarks>
    /// CAUTION: this is the one protection against a release that does not
    /// come. The limit of the buffer stops the memory from increasing, but only
    /// this gives the lane back. Without it the appliance shows a lane that is
    /// bright and it refuses the other person for ever.
    /// </remarks>
    /// <param name="lane">The lane that records.</param>
    private void StartLimitTimer(int lane)
    {
        StopLimitTimer();

        _limitTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(_audioOptions.MaximumRecordingSeconds),
        };

        _limitTimer.Tick += (_, _) =>
        {
            LogLimitTimer(_logger, lane, _audioOptions.MaximumRecordingSeconds);

            // A release that the person did not make. It goes through
            // HandleButtonSafely, which is the one entry that catches. An error
            // out of a Tick has no catch, and the process stops.
            HandleButtonSafely(new PushToTalkChange(lane, IsPressed: false));
        };

        _limitTimer.Start();
    }

    private void StopLimitTimer()
    {
        _limitTimer?.Stop();
        _limitTimer = null;
    }

    /// <summary>
    /// Starts the bars of the visualizer and the time of the pill.
    /// </summary>
    private void StartFrameTimer(LaneViewModel lane)
    {
        StopFrameTimer();

        long start = Stopwatch.GetTimestamp();

        _frameTimer = new DispatcherTimer { Interval = FrameInterval };

        _frameTimer.Tick += (_, _) =>
        {
            double seconds = Stopwatch.GetElapsedTime(start).TotalSeconds;

            lane.Levels = VisualizerLevels.At(BarCount, seconds);

            int whole = (int)seconds;
            RecordingTime = string.Create(
                CultureInfo.InvariantCulture,
                $"{whole / 60}:{whole % 60:D2}");
        };

        _frameTimer.Start();
    }

    private void StopFrameTimer()
    {
        _frameTimer?.Stop();
        _frameTimer = null;

        ResetLevels();
    }

    /// <summary>
    /// Gives each lane a bar for each count of the settings, all at zero.
    /// </summary>
    /// <remarks>
    /// CAUTION: an empty list is not the same as a list of zeros. The
    /// visualizer makes one bar for each value, thus an empty list gives a
    /// strip with nothing in it. The design shows a row of short bars while the
    /// appliance is quiet.
    ///
    /// The two lanes share the array. Nothing writes into it: each new
    /// condition of the bars is a new array.
    /// </remarks>
    private void ResetLevels()
    {
        double[] quiet = new double[BarCount];

        Lane1.Levels = quiet;
        Lane2.Levels = quiet;
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
            HandleButtonSafely(change);
        }
        else
        {
            Dispatcher.UIThread.Post(() => HandleButtonSafely(change));
        }
    }

    /// <summary>
    /// Does the work of one change of a button, and catches each error.
    /// </summary>
    /// <remarks>
    /// CAUTION: this method operates in a callback of the dispatcher, thus an
    /// error that goes out of it has no catch and the process stops. The
    /// appliance has no keyboard: the display becomes black, systemd starts the
    /// software, and the same button stops it again.
    /// </remarks>
    /// <param name="change">The lane, and the new condition of the button.</param>
    private void HandleButtonSafely(PushToTalkChange change)
    {
        try
        {
            HandleButton(change);
        }
#pragma warning disable CA1031 // The appliance must not stop. See the remark.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogButtonFailed(_logger, change.Lane, exception);
            DiscardRecording();
        }
    }

    /// <summary>
    /// Stops the microphone and the timers after an error, and lets the samples
    /// go.
    /// </summary>
    /// <remarks>
    /// CAUTION: each path that ends a recording must also stop the microphone.
    /// Without that the session stays open and the buffer keeps the speech of a
    /// person.
    /// </remarks>
    private void DiscardRecording()
    {
        try
        {
            StopLimitTimer();
            StopFrameTimer();

            using Recording? discarded = _capture.StopRecording();
        }
#pragma warning disable CA1031 // An error out of this method stops the process.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogDiscardFailed(_logger, exception);
        }

        // CAUTION: each of these raises PropertyChanged, and Avalonia then
        // applies a style on this thread. Thus each one can throw, and this
        // method operates in a catch.
        try
        {
            RecordingLane = 0;
            Lane1.IsRecording = false;
            Lane2.IsRecording = false;
            Lane1.CanTurn = true;
            Lane2.CanTurn = true;
            GoTo(AppState.Idle);
            ShowStatus("Microphone not found. Check the device connections.");
        }
#pragma warning disable CA1031 // An error out of this method stops the process.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogDiscardFailed(_logger, exception);
        }
    }

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
        // The first push wins. The button of the other person does nothing
        // until the full operation is complete, and not only until the
        // recording stops.
        if (RecordingLane != 0 || State == AppState.Working)
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
            LogCaptureFailed(_logger, lane, exception);
            ShowStatus("Microphone not found. Check the device connections.");
            return;
        }

        // The lane goes in the log here, and not in the audio service, which
        // has no lane. Without this line each push of the two buttons gives the
        // same text, thus the journal cannot say that the second button
        // operates.
        LogRecordingStarted(_logger, lane);

        _pressTicks = Stopwatch.GetTimestamp();
        RecordingLane = lane;
        RecordingTime = "0:00";
        StatusText = null;

        LaneViewModel speaking = lane == 1 ? Lane1 : Lane2;

        speaking.IsRecording = true;
        Lane1.CanTurn = false;
        Lane2.CanTurn = false;

        GoTo(AppState.Recording);

        StartLimitTimer(lane);
        StartFrameTimer(speaking);
    }

    private void StopRecording(int lane)
    {
        // A release for a lane that does not record is not an error. It occurs
        // if a button was down when the software started, because the software
        // did not see the push.
        if (RecordingLane != lane)
        {
            return;
        }

        TimeSpan held = Stopwatch.GetElapsedTime(_pressTicks);

        StopLimitTimer();
        StopFrameTimer();

        LaneViewModel speaking = lane == 1 ? Lane1 : Lane2;

        RecordingLane = 0;
        speaking.IsRecording = false;
        Lane1.CanTurn = true;
        Lane2.CanTurn = true;

        using Recording? recording = _capture.StopRecording();

        if (held.TotalMilliseconds < _audioOptions.MinimumPressMilliseconds)
        {
            // A physical button in a public location gets an accidental touch.
            LogPressTooShort(_logger, lane, held.TotalMilliseconds);
            GoTo(AppState.Idle);
            ShowStatus("Press and hold the button while you speak.");
            return;
        }

        if (recording is null)
        {
            GoTo(AppState.Idle);
            return;
        }

        LogRecordingStopped(_logger, lane, recording.Duration.TotalSeconds, recording.PeakLevel);

        // Nothing awaits this task on purpose. RunPipelineAsync catches each
        // error of its own, and the user interface must not stop while the
        // model works.
        _ = RunPipelineAsync(speaking);
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The user interface started. Lane 1 is {lane1Language} and lane 2 is {lane2Language}.")]
    private static partial void LogUserInterfaceStarted(
        ILogger logger,
        string lane1Language,
        string lane2Language);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The state goes from {from} to {to}.")]
    private static partial void LogState(ILogger logger, AppState from, AppState to);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Lane {lane} is now {language}.")]
    private static partial void LogLanguage(ILogger logger, int lane, string language);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The translation took {seconds:F2} s and used {tokens} tokens.")]
    private static partial void LogTranslated(ILogger logger, double seconds, int tokens);

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
        Message = "The push on lane {lane} was {milliseconds:F0} ms, which is too short.")]
    private static partial void LogPressTooShort(ILogger logger, int lane, double milliseconds);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The recording started for lane {lane}.")]
    private static partial void LogRecordingStarted(ILogger logger, int lane);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The recording of lane {lane} was {seconds:F1} s at level {level:F2}.")]
    private static partial void LogRecordingStopped(ILogger logger, int lane, double seconds, double level);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The microphone did not start for lane {lane}.")]
    private static partial void LogCaptureFailed(ILogger logger, int lane, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The microphone stopped, but the software did not complete the work after it. The buffer can keep the speech of a person.")]
    private static partial void LogDiscardFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The button of lane {lane} gave an error.")]
    private static partial void LogButtonFailed(ILogger logger, int lane, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The button of lane {lane} did not come up in {seconds} s. The software ends the recording and gives the lane back.")]
    private static partial void LogLimitTimer(ILogger logger, int lane, int seconds);
}
