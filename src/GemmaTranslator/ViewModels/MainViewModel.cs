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

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Threading.Channels;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GemmaTranslator.Configuration;
using GemmaTranslator.Services.Audio;
using GemmaTranslator.Services.Power;
using GemmaTranslator.Services.PushToTalk;
using GemmaTranslator.Services.Settings;
using GemmaTranslator.Services.Speakerphone;
using GemmaTranslator.Services.Speech;
using GemmaTranslator.Services.Translation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GemmaTranslator.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private static readonly TimeSpan StatusLife = TimeSpan.FromSeconds(6);

    private static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(33);

    // The reduced-motion setting makes the bars slow and it does not stop
    // them: a strip that does not move says that the appliance cannot hear.
    private static readonly TimeSpan CalmFrameInterval = TimeSpan.FromMilliseconds(200);

    /// <remarks>
    /// CAUTION: this is a time and it does NOT say how much of the model came
    /// in the memory. A different process puts the model there, and systemd starts
    /// at the same moment as this software, and that process gives no signal
    /// that this software can read. Thus the bar shows how much of this time
    /// went, and nothing else.
    ///
    /// TO BE MEASURED: nobody has measured the true time on the appliance. If
    /// the model needs more than this, the first translation gives
    /// "Translation service isn't responding." and the person holds the button
    /// again.
    ///
    /// A correct signal needs a test of the endpoint before the first
    /// translation. That is a new function and the owner must agree to it.
    /// </remarks>
    private static readonly TimeSpan WarmUpTime = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan QuietTime = TimeSpan.FromMinutes(3);

    /// <remarks>
    /// The time that the selector must stop before the software makes the
    /// models. A touch of the arrow moves one language, thus a person who goes
    /// from English to Korean goes past three languages that nobody wants.
    /// </remarks>
    private static readonly TimeSpan WarmSettleTime = TimeSpan.FromSeconds(1.5);

    // The wait for ONE piece is SpeechOptions.TimeoutSeconds and this margin.
    // ServiceRegistration gives that timeout to the HttpClient, thus one call
    // is already bounded and this margin covers the moment between two calls.
    // It starts again at each piece, thus it finds a server that stopped and
    // not an answer that is long. SpeechBudget does that work.
    private static readonly TimeSpan PieceMargin = TimeSpan.FromSeconds(10);

    // The longest that the appliance speaks one answer, and the one limit on
    // the count of the pieces. The longest recording is 120 s
    // (AudioOptions.MaximumRecordingSeconds), and a measurement gives 258
    // characters for 16 s of sound; thus the answer to that recording is about
    // 2000 characters, 12 pieces, and 130 s of sound. With no limit an answer
    // of 1,000,000 characters gives 5,556 pieces, which is 46 minutes in
    // AppState.Working with each button dead.
    private static readonly TimeSpan SpeechBudget = TimeSpan.FromMinutes(5);

    // The design gives these three. The limit of 12 turns is also the second
    // privacy control of the thread: the screensaver removes all of them, and
    // until it comes the appliance keeps 12 and no more.
    private const int MaxTurns = 12;
    private const int BrightTurns = 2;
    private const double OldTurnOpacity = 0.72;

    private readonly ITranslator _translator;
    private readonly ISpeechService _speech;
    private readonly IAudioCapture _capture;
    private readonly IAudioPlayback _playback;
    private readonly ICallIndicator _callIndicator;
    private readonly IUserSettingsStore _store;
    private readonly AudioOptions _audioOptions;
    private readonly TimeSpan _pieceWait;
    private readonly ILogger<MainViewModel> _logger;

    // One stop for each lane, in the sequence of LaneViewModel.Number. A change
    // of a language cancels the call that the previous change made.
    private readonly CancellationTokenSource?[] _warmStops =
        new CancellationTokenSource?[2];

    // Each call to the server and to the speaker takes this token, thus a stop
    // of the software does not wait for the timeout of the client. See Dispose.
    private readonly CancellationTokenSource _shutdown = new();

    // The turn that the pipeline writes. It is at the end of Turns, and it is
    // null between two exchanges.
    private Exchange? _turn;

    private long _pressTicks;

    private DispatcherTimer? _limitTimer;

    private DispatcherTimer? _frameTimer;

    private DispatcherTimer? _statusTimer;

    private DispatcherTimer? _warmUpTimer;
    private DispatcherTimer? _quietTimer;
    private DispatcherTimer? _clockTimer;
    private long _warmUpTicks;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdlePrompt))]
    [NotifyPropertyChangedFor(nameof(IsRecording))]
    [NotifyPropertyChangedFor(nameof(IsStatusVisible))]
    private AppState _state = AppState.WarmUp;


    // A count of the requests and not a condition. A finger can move the thread
    // between two exchanges, thus the same request comes again, and a value
    // that does not change raises no event. See
    // Views/Behaviors/ConversationScroll.cs.
    [ObservableProperty]
    private int _pinRequest;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsStatusVisible))]
    private string? _statusText;

    [ObservableProperty]
    private BatteryStatus _battery = BatteryStatus.From(new PowerState(null, null));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSpeakerPresent))]
    [NotifyPropertyChangedFor(nameof(IsSpeakerMissing))]
    private bool? _speakerPresent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecordingLabel))]
    private int _recordingLane;

    [ObservableProperty]
    private string _recordingTime = "0:00";

    [ObservableProperty]
    private bool _isSettingsOpen;

    [ObservableProperty]
    private string _clock = "--:--";

    public MainViewModel(
        ITranslator translator,
        ISpeechService speech,
        IAudioCapture capture,
        IAudioPlayback playback,
        ICallIndicator callIndicator,
        IPushToTalk pushToTalk,
        IPowerMonitor power,
        IUserSettingsStore store,
        SettingsViewModel settings,
        IOptions<AudioOptions> audioOptions,
        IOptions<SpeechOptions> speechOptions,
        ILogger<MainViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(translator);
        ArgumentNullException.ThrowIfNull(speech);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(playback);
        ArgumentNullException.ThrowIfNull(callIndicator);
        ArgumentNullException.ThrowIfNull(pushToTalk);
        ArgumentNullException.ThrowIfNull(power);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(audioOptions);
        ArgumentNullException.ThrowIfNull(speechOptions);
        ArgumentNullException.ThrowIfNull(logger);

        _translator = translator;
        _speech = speech;
        _capture = capture;
        _playback = playback;
        _callIndicator = callIndicator;
        _store = store;
        Settings = settings;
        _audioOptions = audioOptions.Value;
        _pieceWait = TimeSpan.FromSeconds(speechOptions.Value.TimeoutSeconds) + PieceMargin;
        _logger = logger;

        Lane1 = new LaneViewModel(1, Languages.FromCode("ja"), Turn);
        Lane2 = new LaneViewModel(2, Languages.FromCode("en"), Turn);

        // The view model listens only. App starts the buttons, because the
        // Windows implementation needs the top level and that does not exist
        // when the container makes this class.
        pushToTalk.Changed += OnButtonChanged;
        power.Changed += OnPowerChanged;
        store.Changed += OnSettingsChanged;
        capture.DevicePresenceChanged += OnSpeakerPresenceChanged;

        SpeakerPresent = capture.IsDevicePresent;

        Battery = BatteryStatus.From(power.Current);

        // CAUTION: the line above can change nothing. BatteryStatus is a record
        // and it compares by value, thus a machine with no fuel gauge gives the
        // same value that the field already had and OnBatteryChanged does not
        // run. Then the settings screen would show an empty line for ever.
        Settings.BatteryAbout = Battery.AboutText;

        ResetLevels();
        StartWarmUp();

        LogUserInterfaceStarted(_logger, Lane1.Language.Name, Lane2.Language.Name);
    }

    /// <summary>
    /// The text that the Moonshine licence conditions make necessary.
    /// </summary>
    public static string Attribution => "Powered by Moonshine AI";

    public SettingsViewModel Settings { get; }

    public LaneViewModel Lane1 { get; }

    public LaneViewModel Lane2 { get; }

    public ObservableCollection<Exchange> Turns { get; } = [];

    public int BarCount => _store.Current.VisualizerBars;

    public bool IsLiquidGlass => _store.Current.LiquidGlass;

    public bool IsReducedMotion => _store.Current.ReducedMotion;

    public bool IsIdlePrompt => State == AppState.Idle && Turns.Count == 0;


    public bool IsRecording => State == AppState.Recording;

    /// <remarks>
    /// A message and the pill of the recording go in the same position. The
    /// recording wins: a person must see that the microphone is open.
    /// </remarks>
    public bool IsStatusVisible => StatusText is not null && !IsRecording;

    public bool IsWarmUp => State == AppState.WarmUp;

    public bool IsScreensaver => State == AppState.Screensaver;

    public bool IsConversation => State
        is AppState.Idle or AppState.Recording or AppState.Working or AppState.Result;

    public bool IsCriticalBattery => Battery.IsCritical;

    /// <remarks>
    /// A machine that cannot answer shows the glyph. A red pill on a
    /// development host that has no speakerphone says a defect that is not
    /// there.
    /// </remarks>
    public bool IsSpeakerPresent => SpeakerPresent != false;

    public bool IsSpeakerMissing => SpeakerPresent == false;

#pragma warning disable CA1822 // A binding of AXAML needs a member of the instance.
    public string WarmUpText => "The appliance is starting. This takes a few seconds.";
#pragma warning restore CA1822

    public string RecordingLabel => string.Create(
        CultureInfo.InvariantCulture,
        $"RECORDING · SPEAKER {(RecordingLane == 0 ? 1 : RecordingLane)}");

    private void AddTurn(Exchange turn)
    {
        Turns.Add(turn);
        _turn = turn;

        while (Turns.Count > MaxTurns)
        {
            Turns.RemoveAt(0);
        }

        Fade();
        OnPropertyChanged(nameof(IsIdlePrompt));
    }

    private void DropTurn()
    {
        if (_turn is null)
        {
            return;
        }

        Turns.Remove(_turn);
        _turn = null;

        Fade();
        OnPropertyChanged(nameof(IsIdlePrompt));
    }

    private void ClearTurns()
    {
        Turns.Clear();
        _turn = null;

        OnPropertyChanged(nameof(IsIdlePrompt));
    }

    private void Fade()
    {
        for (int index = 0; index < Turns.Count; index++)
        {
            Turns[index].Opacity = index >= Turns.Count - BrightTurns
                ? 1
                : OldTurnOpacity;
        }
    }

    private void GoTo(AppState next)
    {
        if (State == next)
        {
            return;
        }

        LogState(_logger, State, next);
        State = next;

        OnPropertyChanged(nameof(IsWarmUp));
        OnPropertyChanged(nameof(IsScreensaver));
        OnPropertyChanged(nameof(IsConversation));

        if (next is AppState.Idle or AppState.Result)
        {
            StartQuietTimer();
        }
        else
        {
            StopQuietTimer();
        }
    }

    [RelayCommand]
    private void OpenSettings() => Safely(() =>
    {
        NoteActivity();
        IsSettingsOpen = true;
    });

    [RelayCommand]
    private void CloseSettings() => Safely(() =>
    {
        NoteActivity();
        IsSettingsOpen = false;
    });

    [RelayCommand]
    private void Wake()
    {
        if (State != AppState.Screensaver)
        {
            return;
        }

        LogWake(_logger);
        StopClock();

        // Always Idle. The screensaver removed the conversation, thus there is
        // nothing to come back to. See the privacy control in StartQuietTimer.
        GoTo(AppState.Idle);
    }

    private void NoteActivity()
    {
        if (State is AppState.Idle or AppState.Result)
        {
            StartQuietTimer();
        }
    }

    private void StartWarmUp()
    {
        LogWarmUpStarted(_logger, (int)WarmUpTime.TotalSeconds);

        _warmUpTicks = Stopwatch.GetTimestamp();

        _warmUpTimer = new DispatcherTimer { Interval = FrameInterval };

        _warmUpTimer.Tick += (_, _) => Safely(() =>
        {
            if (Stopwatch.GetElapsedTime(_warmUpTicks) < WarmUpTime)
            {
                return;
            }

            _warmUpTimer?.Stop();
            _warmUpTimer = null;

            // The test is necessary. A person can push a button, and the state
            // is then not WarmUp when this timer comes to its end.
            if (State == AppState.WarmUp)
            {
                Warm(Lane1);
                Warm(Lane2);

                GoTo(AppState.Idle);
            }
        });

        _warmUpTimer.Start();
    }

    private void StartQuietTimer()
    {
        StopQuietTimer();

        _quietTimer = new DispatcherTimer { Interval = QuietTime };

        _quietTimer.Tick += (_, _) => Safely(() =>
        {
            StopQuietTimer();

            IsSettingsOpen = false;

            // PRIVACY CONTROL. Do not remove this line to "keep the
            // conversation on the display".
            //
            // This appliance stands in a public place. The screensaver is the
            // end of a session: the person who spoke has walked away. Without
            // this line, the next person to touch the panel reads what the
            // previous person said and the translation of it, in two
            // languages, at 42 pixels. In the European Union the speech of a
            // person is personal data, and showing it to an unrelated person
            // is a disclosure.
            //
            // The thread makes this control more important, and not less: the
            // display holds a maximum of 12 turns of that session. This line
            // must remove all of them, and it must not keep one of them.
            ClearTurns();

            StartClock();
            GoTo(AppState.Screensaver);
        });

        _quietTimer.Start();
    }

    private void StopQuietTimer()
    {
        _quietTimer?.Stop();
        _quietTimer = null;
    }

    /// <remarks>
    /// CAUTION: one write is not sufficient. The screensaver shows this value
    /// at 150 pixels and it stays for hours. A value that a person reads as the
    /// time, and that stopped when the display went dark, is worse than no
    /// clock at all.
    ///
    /// TO BE UNDERSTOOD: the appliance is fully offline and it has no NTP. If
    /// the Raspberry Pi has no cell for its clock, this value is not correct
    /// after each start. Measure this on the appliance.
    /// </remarks>
    private void StartClock()
    {
        StopClock();
        UpdateClock();

        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _clockTimer.Tick += (_, _) => Safely(UpdateClock);
        _clockTimer.Start();
    }

    private void StopClock()
    {
        _clockTimer?.Stop();
        _clockTimer = null;
    }

    private void UpdateClock()
        => Clock = DateTime.Now.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <remarks>
    /// CAUTION: an error that goes out of a Tick has no catch and the process
    /// stops. Each one of these callbacks writes a property, and Avalonia then
    /// applies a style on this thread, thus each one can throw. The appliance
    /// has no keyboard: the display becomes black, systemd starts the software
    /// again, and the same condition stops it again.
    /// </remarks>
    private void Safely(Action work)
    {
        try
        {
            work();
        }
#pragma warning disable CA1031 // The appliance must not stop. See the remark.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogTickFailed(_logger, exception);
        }
    }

    private void OnSettingsChanged(object? sender, UserSettings settings)
    {
        OnPropertyChanged(nameof(BarCount));
        OnPropertyChanged(nameof(IsLiquidGlass));
        OnPropertyChanged(nameof(IsReducedMotion));
        ResetLevels();
        NoteActivity();
    }

    partial void OnBatteryChanged(BatteryStatus value)
    {
        Settings.BatteryAbout = value.AboutText;
        OnPropertyChanged(nameof(IsCriticalBattery));
    }

    /// <remarks>
    /// The step goes past the language of the other lane, in the direction that
    /// the person asked for. Thus a touch always changes the language, and the
    /// two lanes never agree.
    /// </remarks>
    private void Turn(LaneViewModel lane, int direction)
    {
        if (State == AppState.Recording)
        {
            return;
        }

        LaneViewModel other = lane.Number == 1 ? Lane2 : Lane1;
        int count = Languages.All.Count;

        int index = Languages.IndexOf(lane.Language);
        index = ((index + direction) % count + count) % count;

        if (Languages.All[index].Code == other.Language.Code)
        {
            index = ((index + direction) % count + count) % count;
        }

        lane.Language = Languages.All[index];

        Warm(lane);

        NoteActivity();
        LogLanguage(_logger, lane.Number, lane.Language.Name);
    }

    /// <remarks>
    /// <see cref="ISpeechService.WarmAsync"/> gives the measured cost and the
    /// lock of the server. The two callers are the end of the warm-up screen
    /// and a change of a language; the test below refuses each other moment,
    /// because only those two are moments when nobody waits.
    /// </remarks>
    private void Warm(LaneViewModel lane)
    {
        if (State is not (AppState.Idle or AppState.WarmUp or AppState.Result))
        {
            return;
        }

        int slot = lane.Number - 1;
        Language language = lane.Language;

        // One call for each lane, and the last one wins. Each touch past the
        // first makes a model that nobody uses.
        _warmStops[slot]?.Cancel();

        CancellationTokenSource stop = new();
        _warmStops[slot] = stop;

        // Nothing awaits this task. A warm call that fails changes nothing
        // that a person sees: the first exchange of that language stays slow.
        _ = Task.Run(async () =>
        {
            try
            {
                // The selector stops first. Without this each touch makes a
                // call, and each call holds the lock of the server.
                await Task.Delay(WarmSettleTime, stop.Token).ConfigureAwait(false);

                await _speech.WarmAsync(language, stop.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected: a later touch cancelled this call.
            }
#pragma warning disable CA1031 // A model that does not come must not stop the appliance.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                LogWarmFailed(_logger, language.Code, exception);
            }
            finally
            {
                // The slot loses this source, and nothing disposes it. A source
                // with no wait handle and no timer needs no dispose, and a
                // dispose here would make the Cancel above throw for a call
                // that came to its end one moment before it.
                Interlocked.CompareExchange(ref _warmStops[slot], null, stop);
            }
        });
    }

    private void ShowStatus(string text)
    {
        StatusText = text;

        _statusTimer?.Stop();
        _statusTimer = new DispatcherTimer { Interval = StatusLife };

        _statusTimer.Tick += (_, _) => Safely(() =>
        {
            _statusTimer?.Stop();
            _statusTimer = null;
            StatusText = null;
        });

        _statusTimer.Start();
    }

    /// <remarks>
    /// The turn goes away with the message. A bubble that holds "Listening…"
    /// or a dash says that the appliance heard something, and the message below
    /// it says that it did not. The turns before it stay: they are complete.
    /// </remarks>
    private void ShowNoResult(string status) => Safely(() =>
    {
        DropTurn();
        GoTo(AppState.Idle);
        ShowStatus(status);
    });

    /// <remarks>
    /// CAUTION: this event comes on the thread that reads the files of the
    /// <c>power_supply</c> class. Each write to a property must go to the
    /// thread of the user interface.
    /// </remarks>
    private void OnPowerChanged(object? sender, PowerState state)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Safely(() => Battery = BatteryStatus.From(state));
        }
        else
        {
            Dispatcher.UIThread.Post(() => Safely(() => Battery = BatteryStatus.From(state)));
        }
    }

    /// <remarks>
    /// CAUTION: this event comes on the thread that reads the list of the
    /// devices. Each write to a property must go to the thread of the user
    /// interface.
    /// </remarks>
    private void OnSpeakerPresenceChanged(object? sender, bool? present)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            Safely(() => SpeakerPresent = present);
        }
        else
        {
            Dispatcher.UIThread.Post(() => Safely(() => SpeakerPresent = present));
        }
    }

    private async Task RunPipelineAsync(LaneViewModel lane, Recording recording)
    {
        LaneViewModel other = lane.Number == 1 ? Lane2 : Lane1;

        // The times of each part go in one line at the end. A person who looks
        // for the slow part must not add four lines of the journal, and the
        // total is more than their sum: it holds the work of the display also.
        long pipelineTicks = Stopwatch.GetTimestamp();
        double recorded = recording.Duration.TotalSeconds;
        double transcribeSeconds = 0;
        double translateSeconds = 0;
        double speakSeconds = 0;

        string heard;

        Exchange turn = new()
        {
            SourceLanguage = lane.Language,
            TargetLanguage = other.Language,
            SourceText = "Listening…",
            TargetText = "—",
            SourceIsLane2 = lane.Number == 2,
            IsSourceMuted = true,
            IsTargetMuted = true,
        };

        try
        {
            // SECURITY CONTROL. This method owns the recording, and this block
            // wipes the samples as soon as the words come back. StopRecording
            // cannot do it with a `using` statement: nothing awaits this task,
            // so the array would already be cleared when the speech service
            // reads it. Everything that can throw is inside the block, because
            // an error before it would leave the speech of a person in memory.
            using (recording)
            {
                GoTo(AppState.Working);

                AddTurn(turn);

                long ticks = Stopwatch.GetTimestamp();

                heard = await _speech
                    .TranscribeAsync(recording.Samples, lane.Language, _shutdown.Token)
                    .ConfigureAwait(true);

                transcribeSeconds = Stopwatch.GetElapsedTime(ticks).TotalSeconds;

                LogTranscribed(_logger, transcribeSeconds, heard.Length);
            }
        }
        catch (SpeechException exception)
        {
            LogTranscriptionFailed(_logger, exception);
            ShowNoResult("Speech service isn't responding.");
            return;
        }
#pragma warning disable CA1031 // The appliance must not stop. See the remark.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            // CAUTION: this catch is the last one on purpose. See the same
            // catch on the translation below.
            LogTranscriptionFailed(_logger, exception);
            ShowNoResult("Speech service isn't responding.");
            return;
        }

        if (string.IsNullOrWhiteSpace(heard))
        {
            ShowNoResult("No speech detected. Hold the button and try again.");
            return;
        }

        turn.SourceText = heard;
        turn.TargetText = "Translating…";
        turn.IsSourceMuted = false;
        turn.IsTargetMuted = true;

        string? translated = null;

        try
        {
            TranslationResult result = await _translator
                .TranslateAsync(heard, lane.Language, other.Language, _shutdown.Token)
                .ConfigureAwait(true);

            turn.TargetText = result.Translation;
            turn.IsTargetMuted = false;

            translated = result.Translation;

            translateSeconds = result.Duration.TotalSeconds;

            LogTranslated(_logger, translateSeconds, result.TotalTokens);
        }
        catch (TranslationException exception)
        {
            LogTranslationFailed(_logger, exception);
            turn.TargetText = "—";
            turn.IsTargetMuted = true;
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
            turn.TargetText = "—";
            turn.IsTargetMuted = true;
            ShowStatus("Translation service isn't responding.");
        }

        // The state stays at Working while the appliance speaks. Thus the
        // button of the other person does nothing until the sound is complete.
        // The microphone also does not hear the speaker.
        if (translated is not null && _store.Current.SpeakTranslations)
        {
            long speakTicks = Stopwatch.GetTimestamp();

            await SpeakAsync(translated, other.Language, _shutdown.Token).ConfigureAwait(true);

            speakSeconds = Stopwatch.GetElapsedTime(speakTicks).TotalSeconds;
        }

        double totalSeconds = Stopwatch.GetElapsedTime(pipelineTicks).TotalSeconds;

        LogExchange(
            _logger,
            recorded,
            transcribeSeconds,
            translateSeconds,
            speakSeconds,
            totalSeconds);

        // The turn is complete. DropTurn must not find it: a message that comes
        // after this belongs to the next exchange, and it must not remove this
        // one from the thread.
        _turn = null;

        // CAUTION: this line is inside the guard on purpose. GoTo raises
        // PropertyChanged, Avalonia then applies a style, and that can throw.
        // Nothing awaits this task, thus the error goes away in silence and the
        // state stays at Working. StartRecording refuses each button in that
        // state, and the appliance then hears nobody again and says nothing.
        Safely(() => GoTo(AppState.Result));
    }

    /// <remarks>
    /// CAUTION: an error here must not remove the translation from the display.
    /// A person can read the words, and the sound does not come.
    /// <para>
    /// A speech that gave nothing shows no message: the words are on the
    /// display. A speech that stopped in its middle shows one, because silence
    /// after two sentences of five is the sound of the end of the answer.
    /// </para>
    /// </remarks>
    private async Task SpeakAsync(
        string text,
        Language language,
        CancellationToken cancellationToken)
    {
        // The pieces cost the server about 6 % more. A measurement gives
        // 11.25 s of synthesis for 258 characters in one call, and 11.94 s for
        // the same text in four. But the person hears the first sentence after
        // the first piece, thus the appliance answers more quickly.
        IReadOnlyList<string> pieces = SpeechChunks.Split(text);

        if (pieces.Count == 0)
        {
            return;
        }

        // The cue comes on before the call to the server and not after it. That
        // call takes 1 s to 3 s, and for all of that time the state is Working:
        // the two buttons do nothing and nothing on the display says that the
        // appliance still works.
        SetSpeaking(true);

        // A queue of one piece. A measurement on the appliance gives synthesis
        // at 0.7 times the length of the audio that it makes, at each length
        // and for English and Japanese, thus each piece is complete before the
        // piece in front of it stops. Three are live at the peak: the one that
        // plays, the one in the queue, and the one that the producer holds
        // while it waits to write.
        Channel<SpokenAudio> line = Channel.CreateBounded<SpokenAudio>(1);

        using CancellationTokenSource stop =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        stop.CancelAfter(SpeechBudget);

        Task producer = SynthesizeIntoAsync(line.Writer, pieces, language, stop.Token);

        int spoken = 0;

        try
        {
            // WaitToReadAsync and TryRead, and not ReadAsync. An error of the
            // producer comes out of WaitToReadAsync as the error that the
            // producer gave; ReadAsync puts it inside a ChannelClosedException,
            // and the journal would then name the queue and not the cause.
            while (await WaitForPieceAsync(line.Reader, stop.Token).ConfigureAwait(true))
            {
                while (line.Reader.TryRead(out SpokenAudio? audio))
                {
                    try
                    {
                        // CAUTION: one reader, and one play at a time. PlayAsync
                        // retires the player of the call before it, thus two
                        // calls together cut the first piece in its middle.
                        await _playback.PlayAsync(audio, stop.Token).ConfigureAwait(true);

                        spoken++;
                    }
                    finally
                    {
                        // SECURITY CONTROL. PlayAsync wipes these bytes at each
                        // exit of its own. This line does not depend on that: a
                        // caller must not give that work to the code it calls.
                        Array.Clear(audio.WavBytes);
                    }
                }
            }
        }
#pragma warning disable CA1031 // The appliance must not stop. See the remark.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogSpeechFailed(_logger, spoken, pieces.Count, exception);

            if (spoken > 0)
            {
                Safely(() => ShowStatus("The appliance did not speak all of the answer."));
            }
        }
        finally
        {
            await stop.CancelAsync().ConfigureAwait(true);
            await producer.ConfigureAwait(true);

            // SECURITY CONTROL. Do not delete this loop. PlayAsync wipes the
            // bytes that it played, but a piece that waits in the queue never
            // reaches PlayAsync when the software stops or a later piece fails.
            // Those bytes are the spoken sentence of a person, and the producer
            // ended above, so nothing can add a piece after this line. The wipe
            // covers the array of this code only: HttpClient holds a copy of
            // the same WAV that nothing wipes, thus a memory dump is not clean.
            while (line.Reader.TryRead(out SpokenAudio? left))
            {
                Array.Clear(left.WavBytes);
            }

            SetSpeaking(false);
        }
    }

    private async Task<bool> WaitForPieceAsync(
        ChannelReader<SpokenAudio> reader,
        CancellationToken cancellationToken)
        => await reader
            .WaitToReadAsync(cancellationToken)
            .AsTask()
            .WaitAsync(_pieceWait, cancellationToken)
            .ConfigureAwait(true);

    // One worker, and the pieces in their sequence. backend/server.py holds
    // _tts_lock while it makes the audio. Thus two calls together give no
    // speed on a machine that has 4 GB and a model in its memory.
    //
    // ConfigureAwait(false) below, and true at each other await of this class.
    // This method makes the next piece while the appliance speaks, thus a
    // continuation on the thread of the user interface would put the copy of
    // some megabytes of WAV on the thread that draws.
    private async Task SynthesizeIntoAsync(
        ChannelWriter<SpokenAudio> writer,
        IReadOnlyList<string> pieces,
        Language language,
        CancellationToken cancellationToken)
    {
        Exception? failure = null;

        try
        {
            foreach (string piece in pieces)
            {
                SpokenAudio audio = await _speech
                    .SynthesizeAsync(piece, language, cancellationToken)
                    .ConfigureAwait(false);

                try
                {
                    await writer.WriteAsync(audio, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // SECURITY CONTROL. Do not delete this line. The reader
                    // stopped, so this piece never reaches PlayAsync, the code
                    // that usually wipes it. It is the spoken sentence of a
                    // person. As above, this clears the array of this code
                    // only, and HttpClient keeps a copy that nothing wipes.
                    Array.Clear(audio.WavBytes);
                    throw;
                }
            }
        }
#pragma warning disable CA1031 // The reader gets each error through the queue.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            failure = exception;
        }

        writer.Complete(failure);
    }

    private void SetSpeaking(bool speaking) => Safely(() => _turn?.IsSpeaking = speaking);

    /// <remarks>
    /// CAUTION: this is the one protection against a release that does not
    /// come. The limit of the buffer stops the memory from increasing, but only
    /// this gives the lane back. Without it the appliance shows a lane that is
    /// bright and it refuses the other person for ever.
    /// </remarks>
    private void StartLimitTimer(int lane)
    {
        StopLimitTimer();

        _limitTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(_audioOptions.MaximumRecordingSeconds),
        };

        _limitTimer.Tick += (_, _) => Safely(() =>
        {
            LogLimitTimer(_logger, lane, _audioOptions.MaximumRecordingSeconds);

            // A release that the person did not make. It goes through
            // HandleButtonSafely, which is the one entry that catches. An error
            // out of a Tick has no catch, and the process stops.
            HandleButtonSafely(new PushToTalkChange(lane, IsPressed: false));
        });

        _limitTimer.Start();
    }

    private void StopLimitTimer()
    {
        _limitTimer?.Stop();
        _limitTimer = null;
    }

    private void StartFrameTimer(LaneViewModel lane)
    {
        StopFrameTimer();

        long start = Stopwatch.GetTimestamp();

        _frameTimer = new DispatcherTimer
        {
            Interval = _store.Current.ReducedMotion ? CalmFrameInterval : FrameInterval,
        };

        _frameTimer.Tick += (_, _) => Safely(() =>
        {
            double seconds = Stopwatch.GetElapsedTime(start).TotalSeconds;

            lane.Levels = VisualizerLevels.At(BarCount, seconds);

            int whole = (int)seconds;
            RecordingTime = string.Create(
                CultureInfo.InvariantCulture,
                $"{whole / 60}:{whole % 60:D2}");
        });

        _frameTimer.Start();
    }

    private void StopFrameTimer()
    {
        _frameTimer?.Stop();
        _frameTimer = null;

        ResetLevels();
    }

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

    /// <remarks>
    /// CAUTION: the event can come on a thread that is not the thread of the
    /// user interface. The Raspberry Pi reads the input device on its own
    /// thread. Each write to a property must go to the correct thread, or
    /// Avalonia throws.
    /// </remarks>
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

    /// <remarks>
    /// CAUTION: this method operates in a callback of the dispatcher, thus an
    /// error that goes out of it has no catch and the process stops. The
    /// appliance has no keyboard: the display becomes black, systemd starts the
    /// software, and the same button stops it again.
    /// </remarks>
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
        finally
        {
            // This is the last path that ends a recording, thus the ring goes
            // dark here although the microphone threw. A ring that stays green
            // says that the appliance records, and it does not.
            _callIndicator.EndCall();
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
        // A push on a button while the display is dark wakes the appliance and
        // does nothing else. The microphone is not open at that moment, thus a
        // recording loses the first word of the person.
        if (State == AppState.Screensaver)
        {
            if (change.IsPressed)
            {
                Wake();
            }

            return;
        }

        // CAUTION: a release goes to StopRecording before each test below it.
        // The tests refuse a NEW recording; a release ends one that operates.
        // A release that returns here leaves the microphone open, leaves the
        // limit timer to repeat for ever with nothing in the journal, and
        // leaves the lane bright. The buffer then keeps the speech of a person.
        if (!change.IsPressed)
        {
            StopRecording(change.Lane);
            return;
        }

        // A push below a layer that covers the conversation does nothing. The
        // pill that says "RECORDING" is in that conversation, thus a recording
        // that starts here is a microphone that is open with no signal on the
        // panel. See MainView.axaml.
        if (State == AppState.WarmUp || IsSettingsOpen || Battery.IsCritical)
        {
            LogButtonIgnored(_logger, change.Lane);
            return;
        }

        StartRecording(change.Lane);
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

        // A recording that cannot hear is worse than a refusal that says why.
        if (IsSpeakerMissing)
        {
            ShowStatus("Speaker not found. Check the device connections.");
            return;
        }

        // The ring lights before the microphone accumulates, and it goes dark
        // after the microphone stops. An indicator that comes after the
        // condition that it shows tells the room the truth too late.
        _callIndicator.StartCall();

        try
        {
            _capture.StartRecording();
        }
        catch (AudioCaptureException exception)
        {
            _callIndicator.EndCall();
            LogCaptureFailed(_logger, lane, exception);
            ShowStatus("Microphone not found. Check the device connections.");
            return;
        }

        LogRecordingStarted(_logger, lane);

        _pressTicks = Stopwatch.GetTimestamp();
        RecordingLane = lane;
        RecordingTime = "0:00";
        StatusText = null;

        // A person who reads the history and then speaks wants to see the words
        // that they say. This takes the thread back to the newest turn.
        PinRequest++;

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

        Recording? recording = _capture.StopRecording();
        bool handedOver = false;

        try
        {
            _callIndicator.EndCall();

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

            // Section 8.19 of deploy/README.md: the device can be present, open,
            // and correct in each format, and give the value 0 for each sample with
            // no error at all. A count of the samples does not find that condition,
            // and the level does.
            if (recording.PeakLevel == 0)
            {
                ShowStatus("Speaker not found. Check the device connections.");
                GoTo(AppState.Idle);
                return;
            }

            // Nothing awaits this task on purpose. RunPipelineAsync catches each
            // error of its own, and the user interface must not stop while the
            // model works.
            _ = RunPipelineAsync(speaking, recording);
            handedOver = true;
        }
        finally
        {
            // SECURITY CONTROL. RunPipelineAsync owns the recording once it
            // starts, and it wipes the samples itself. Every other path out of
            // this method must wipe them here, or the speech of a person stays
            // in the heap. Do not remove this.
            if (!handedOver)
            {
                recording?.Dispose();
            }
        }
    }

    /// <remarks>
    /// The container calls this when the software stops. It cancels only: a
    /// source that this method disposes can be inside a call of HttpClient at
    /// that moment, and the call then gives an error of a different type. The
    /// process ends immediately after this.
    /// </remarks>
    public void Dispose()
    {
        _shutdown.Cancel();

        foreach (CancellationTokenSource? stop in _warmStops)
        {
            stop?.Cancel();
        }
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
        Message = "The warm-up screen is on the panel for {seconds} s.")]
    private static partial void LogWarmUpStarted(ILogger logger, int seconds);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "A timer of the user interface gave an error.")]
    private static partial void LogTickFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "A person woke the appliance.")]
    private static partial void LogWake(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The state goes from {from} to {to}.")]
    private static partial void LogState(ILogger logger, AppState from, AppState to);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Lane {lane} is now {language}.")]
    private static partial void LogLanguage(ILogger logger, int lane, string language);

    /// <remarks>
    /// The count of the characters, and not the text. The words of a person are
    /// personal data and they must not go in the journal.
    /// </remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The speech-to-text took {seconds:F2} s and gave {characters} characters.")]
    private static partial void LogTranscribed(ILogger logger, double seconds, int characters);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The speech-to-text did not occur.")]
    private static partial void LogTranscriptionFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The translation took {seconds:F2} s and used {tokens} tokens.")]
    private static partial void LogTranslated(ILogger logger, double seconds, int tokens);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The translation did not occur.")]
    private static partial void LogTranslationFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The models of {language} are not ready. The first exchange in that language is slow.")]
    private static partial void LogWarmFailed(ILogger logger, string language, Exception exception);

    /// <remarks>
    /// One line for each exchange, for a measurement of the speed. A value of 0
    /// for the speech says that the settings do not speak the translations, and
    /// not that the sound was immediate.
    /// </remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The exchange is complete. Recorded {recorded:F2} s, speech-to-text {transcribe:F2} s, translation {translate:F2} s, speech {speak:F2} s, total {total:F2} s.")]
    private static partial void LogExchange(
        ILogger logger,
        double recorded,
        double transcribe,
        double translate,
        double speak,
        double total);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The appliance spoke {spoken} of the {pieces} pieces of the translation. The words stay on the display.")]
    private static partial void LogSpeechFailed(
        ILogger logger,
        int spoken,
        int pieces,
        Exception exception);

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
