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
// been modified. It replaces frontend/src/hooks/useAudioRecorder.js and
// playTTS of frontend/src/TranslatorApp.jsx.

using System.Diagnostics;
using GemmaTranslator.Configuration;
using GemmaTranslator.Services.Speech;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Backends.MiniAudio.Devices;
using SoundFlow.Components;
using SoundFlow.Enums;
using SoundFlow.Providers;
using SoundFlow.Structs;

namespace GemmaTranslator.Services.Audio;

/// <remarks>
/// <para>
/// The software asks for 16 kHz, one channel, and F32. miniaudio converts from
/// the format of the device in native code, thus the Jabra at 48 kHz needs no
/// work here.
/// </para>
/// <para>
/// CAUTION: the device is full duplex although the software plays no sound.
/// The Jabra Speak2 40 gives no microphone data while its playback interface
/// is stopped. A measurement on the appliance gives this, with each other
/// condition equal:
/// </para>
/// <list type="table">
/// <item><description>Playback stopped: the read gives EIO, and miniaudio
/// gives buffers of zeros with no error at all.</description></item>
/// <item><description>Playback open: the microphone operates, and it
/// continues to operate.</description></item>
/// </list>
/// <para>
/// The device does echo cancellation in its own hardware, thus the microphone
/// path is behind a canceller that needs the playback reference. The playback
/// interface stays started for the life of this object, and that is the
/// condition that keeps the microphone alive; the contents of the mixer are
/// not, since a mixer with no enabled player gives the same silence either
/// way. Do not make this a capture device to remove one object: the
/// microphone then gives 0.000 for each press, and no error says why.
/// </para>
/// <para>
/// CAUTION: <see cref="OnAudioProcessed"/> operates on the audio thread of
/// miniaudio, which has a high priority. It must take no lock and it must make
/// no memory. Thus the buffer has a fixed dimension that the software makes
/// one time, and the flags are volatile. A lock in that method gave a
/// deadlock: <c>ma_device_stop</c> waits for the audio thread, and the audio
/// thread waited for the lock that the caller of <c>Dispose</c> held.
/// </para>
/// </remarks>
public sealed partial class SoundFlowAudioDevice : IAudioCapture, IAudioPlayback
{
    private const int PresenceUnknown = 0;
    private const int PresenceThere = 1;
    private const int PresenceGone = 2;

    // The longest that a person waits to see that the speakerphone went away.
    // Each read gives the list of the devices of the machine and it opens
    // nothing, thus the cost is not the 1.22 s of an open.
    private static readonly TimeSpan PresenceInterval = TimeSpan.FromSeconds(2);

    // CAUTION: this margin is the one protection against an end that does not
    // come. PlaybackEnded comes from the audio thread, and that thread stops if
    // a person disconnects the speakerphone. The state then stays at Working
    // for ever, and each button does nothing.
    private static readonly TimeSpan PlaybackMargin = TimeSpan.FromSeconds(5);

    // CAUTION: SoundPlayerBase gives exactly 0 when the provider cannot give a
    // length, and that is a value of the library and not an error. The budget
    // is then the margin alone, and a sentence of more than 5 s stops in its
    // middle with no signal. A measurement gives 2.35 s for a short phrase,
    // thus a full sentence goes past that limit.
    private const double DefaultPlaybackSeconds = 60;

    // The client of the speech server refuses a body of more than 16 MB, which
    // is about 5.5 minutes of the 24 kHz 16-bit audio that a measurement gives.
    // Thus no correct answer needs more than this, and a header that asks for
    // more makes the appliance wait for hours with each button dead.
    private const double MaximumPlaybackSeconds = 360;

    // 4096 floats are 256 ms at 16 kHz, thus one whole tick of the display is
    // always in the ring, also the 200 ms of the reduced-motion setting.
    private const int SpectrumRingLength = 4096;

    private readonly AudioOptions _options;
    private readonly ILogger<SoundFlowAudioDevice> _logger;
    private readonly CancellationTokenSource _stop = new();

    // This lock is for the life of the device only. The audio thread never
    // takes it. See the remark on the class.
    private readonly Lock _deviceLock = new();

    // One buffer, made one time. The audio thread writes in it and it never
    // increases. See AudioOptions.MaximumRecordingSeconds.
    private readonly float[] _buffer;

    private readonly float[] _spectrumRing = new float[SpectrumRingLength];
    private readonly Spectrum _spectrum;

    private readonly AudioFormat _format;
    private readonly PlaybackMeter _meter;

    private MiniAudioEngine? _engine;
    private FullDuplexDevice? _device;

    // The player that speaks now. PlayAsync clears this field when the sound
    // is complete, thus it holds null while the appliance says nothing.
    private SoundPlayer? _player;

    private long _startTicks;
    private int _deviceSampleRate;

    // Two flags, and not one. _sessionOpen says that a person holds a button.
    // _accumulating says that the samples still go in the buffer. The buffer
    // becomes full before the person releases the button, thus the second
    // becomes false first. With one flag, StopRecording gave nothing back at
    // that moment and no line said that the limit operated.
    private volatile bool _sessionOpen;
    private volatile bool _accumulating;
    private volatile bool _reachedLimit;
    private volatile int _written;
    private volatile int _peakBits;
    private volatile int _ringWritten;

    // 0 says that this machine cannot answer, 1 that the device is there, and 2
    // that it is not. This is an int and not a bool?, because Volatile and
    // Interlocked read and write a value of this size in one operation and a
    // nullable of a bool is two fields.
    private int _presence;
    private int _presenceStarted;
    private bool _disposed;

    public SoundFlowAudioDevice(
        IOptions<AudioOptions> options,
        ILogger<SoundFlowAudioDevice> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;

        _buffer = new float[_options.SampleRate * _options.MaximumRecordingSeconds];
        _deviceSampleRate = _options.SampleRate;
        _spectrum = new Spectrum();

        _format = new AudioFormat
        {
            Format = SampleFormat.F32,
            Channels = 1,
            SampleRate = _options.SampleRate,
        };

        _meter = new PlaybackMeter(_format);
    }

    public bool? IsDevicePresent => Volatile.Read(ref _presence) switch
    {
        PresenceThere => true,
        PresenceGone => false,
        _ => null,
    };

    public event EventHandler<bool?>? DevicePresenceChanged;

    public void Prepare()
    {
        // The loop starts before the open. A device that is absent at the start
        // makes OpenDevice throw, and that is the one condition that the
        // display must show.
        StartPresenceLoop();

        lock (_deviceLock)
        {
            OpenDevice();
        }
    }

    public void StartRecording()
    {
        lock (_deviceLock)
        {
            if (_sessionOpen)
            {
                return;
            }

            // CAUTION: this does not open a device that went away. That work
            // takes 1.22 s and it would stop the display of a machine that a
            // person touches. Prepare opens the device at the start.
            if (_device is null)
            {
                throw new AudioCaptureException("The microphone is not open.");
            }

            // SECURITY CONTROL. Do not delete this to save a memset.
            // StopRecording clears the buffer, but an audio callback that is
            // already past its test of the flag can write into it after that
            // clear, and nothing else ever removes those samples: the next
            // StopRecording sees no open session and gives null back. About one
            // period of the speech of the last person, some 10 ms of it, stays
            // in the heap. Here the audio thread has been quiet since the last
            // release of this lock. The cost is a memset of 7.68 MB, far less
            // than a millisecond, and the device is already 1.22 s warm.
            // The ring of the visualizer holds the same speech and it gets the
            // same clear. Spectrum wipes its own transform after each frame,
            // and the values that leave it are magnitudes with no phase, which
            // do not invert.
            Array.Clear(_buffer);
            Array.Clear(_spectrumRing);

            _ringWritten = 0;
            _spectrum.Reset();
            _written = 0;
            _peakBits = 0;
            _reachedLimit = false;
            _startTicks = Stopwatch.GetTimestamp();
            _sessionOpen = true;
            _accumulating = true;
        }
    }

    public Recording? StopRecording()
    {
        bool wasOpen = _sessionOpen;
        _sessionOpen = false;
        _accumulating = false;

        if (!wasOpen)
        {
            return null;
        }

        TimeSpan duration = Stopwatch.GetElapsedTime(_startTicks);

        // A callback that operates now can write some more samples. It cannot
        // write outside the buffer, thus this copy is safe.
        int written = _written;
        float[] samples;
        float peak;

        try
        {
            samples = _buffer.AsSpan(0, written).ToArray();
            peak = BitConverter.Int32BitsToSingle(_peakBits);
        }
        finally
        {
            // SECURITY CONTROL. Do not remove this to save a memset. Without
            // it the speech of the last person stays in this buffer for the
            // life of the process. This appliance keeps no recording, and this
            // line is part of what makes that true.
            //
            // The finally makes the clear come also when the copy above does
            // not operate. No later call can do it: StopRecording gives null
            // when no session is open.
            // The frame timer stops before this call, thus nothing reads the
            // ring now.
            Array.Clear(_buffer);
            Array.Clear(_spectrumRing);

            _ringWritten = 0;
        }

        LogStopped(
            _logger,
            duration.TotalSeconds,
            written,
            peak,
            _deviceSampleRate,
            _spectrum.Loudest);

        if (_reachedLimit)
        {
            LogReachedLimit(_logger, _options.MaximumRecordingSeconds);
        }

        return new Recording(samples, duration, peak, _deviceSampleRate, _reachedLimit);
    }

    public async Task PlayAsync(
        SpokenAudio speech,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(speech);

        try
        {
            await PlayCoreAsync(speech, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // SECURITY CONTROL. Do not delete this. These bytes are the spoken
            // sentence of a person, as audio. This wipe covers every exit: the
            // first test inside PlayCoreAsync throws before any wipe of its
            // own, and by then the caller has taken that piece out of its
            // queue, so nothing else would ever clear it.
            //
            // The wipe covers the array of this code only. HttpClient buffered
            // the whole WAV inside the response, and miniaudio decodes a second
            // copy into native memory; neither is wiped, thus a memory dump is
            // not clean. Recording.Dispose does the same work for the input.
            Array.Clear(speech.WavBytes);
        }
    }

    /// <remarks>
    /// <para>
    /// CAUTION: the provider gets the format of the DEVICE and not the format
    /// of the WAV. Nothing in SoundFlow changes the rate; this value makes the
    /// decoder of miniaudio change it in native code. The server sends 24000 Hz
    /// and this device operates at 16000 Hz. Thus the other constructor, which
    /// reads the rate from the file, speaks at two thirds of the speed.
    /// </para>
    /// <para>
    /// CAUTION: <c>PlaybackEnded</c> comes on the audio thread, from
    /// <c>GenerateAudio</c>. Thus its handler obeys the same rules as
    /// <see cref="OnAudioProcessed"/> and it only makes a signal.
    /// <c>RemoveComponent</c> takes a lock and it makes an array, thus the
    /// <c>finally</c> below does that work. That code comes after the await of
    /// a source that continues asynchronously, thus it operates on a thread of
    /// the pool and never on the audio thread.
    /// </para>
    /// </remarks>
    private async Task PlayCoreAsync(
        SpokenAudio speech,
        CancellationToken cancellationToken)
    {
        SoundPlayer? previous;
        MiniAudioEngine engine;
        Mixer mixer;
        AudioFormat format;

        lock (_deviceLock)
        {
            if (_disposed || _device is null || _engine is null)
            {
                throw new AudioPlaybackException("The speaker is not open.");
            }

            previous = _player;
            _player = null;

            engine = _engine;
            mixer = _device.MasterMixer;
            format = _device.PlaybackDevice.Format;
        }

        Retire(previous);

        MemoryStream wav = new(speech.WavBytes, writable: false);
        StreamDataProvider? provider = null;
        SoundPlayer? built = null;
        TimeSpan budget;

        try
        {
            provider = new StreamDataProvider(engine, format, wav);
            built = new SoundPlayer(engine, format, provider);

            // The read of the length is inside this block, because a header
            // that is not correct throws. Outside the block that error leaves a
            // decoder of native code with no owner.
            budget = MakeBudget(built.Duration);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (built is null)
            {
                provider?.Dispose();
                wav.Dispose();
            }
            else
            {
                built.Dispose();
            }

            // SECURITY CONTROL. See the finally of PlayAsync.
            Array.Clear(speech.WavBytes);

            LogPlaybackFailed(_logger, speech.WavBytes.Length, exception);

            throw new AudioPlaybackException(
                "The audio of the translation did not open.",
                exception);
        }

        SoundPlayer player = built;

        // RunContinuationsAsynchronously keeps the work after the await off the
        // audio thread. Without it that thread does the work of the caller.
        TaskCompletionSource ended = new(TaskCreationOptions.RunContinuationsAsynchronously);

        player.PlaybackEnded += (_, _) => ended.TrySetResult();

        long ticks = Stopwatch.GetTimestamp();

        bool added = false;

        lock (_deviceLock)
        {
            if (!_disposed)
            {
                mixer.AddComponent(player);
                _player = player;
                added = true;
            }
        }

        if (!added)
        {
            Retire(player);

            // SECURITY CONTROL. See the finally of PlayAsync.
            Array.Clear(speech.WavBytes);

            throw new AudioPlaybackException("The speaker is not open.");
        }

        try
        {
            player.Play();

            await ended.Task.WaitAsync(budget, cancellationToken).ConfigureAwait(false);

            double seconds = Stopwatch.GetElapsedTime(ticks).TotalSeconds;

            // TakeLoudest puts the value back to 0, thus it must operate one
            // time for each piece and not one time for each piece that the log
            // writes. Inside the argument list a reader can make the log
            // conditional and stop the reset.
            double loudest = _meter.TakeLoudest();

            LogPlayed(
                _logger,
                seconds,
                speech.WavBytes.Length,
                format.SampleRate,
                loudest,
                _meter.Frames);
        }
        catch (TimeoutException)
        {
            // This path must take the value too, or the largest level of a
            // piece that stopped goes in the line of the piece after it.
            LogPlaybackDidNotEnd(
                _logger,
                player.Duration,
                budget.TotalSeconds,
                _meter.TakeLoudest());
        }
        finally
        {
            // A component that is not enabled gives the buffer back with no
            // change. The library does this at the usual end; this line is for
            // the limit above and for a stop that the caller asked for.
            player.Enabled = false;

            bool mine;

            lock (_deviceLock)
            {
                mine = ReferenceEquals(_player, player);

                if (mine)
                {
                    _player = null;
                }
            }

            // Dispose takes the player out of the field itself and retires it.
            // Two calls of Retire on one player would dispose it two times.
            if (mine)
            {
                Retire(player);
            }

            // SECURITY CONTROL. The player is disposed above, thus nothing
            // reads these bytes after this line. See the finally of PlayAsync.
            Array.Clear(speech.WavBytes);
        }
    }

    /// <remarks>
    /// CAUTION: the value comes from the header of the WAV, which the speech
    /// server writes. A measurement on .NET 10 gives an ArgumentException for a
    /// value that is not a number, an OverflowException for 1e18, and an
    /// ArgumentOutOfRangeException for 1e9, because the longest timer is about
    /// 49.7 days.
    /// </remarks>
    private static TimeSpan MakeBudget(double declared)
    {
        double seconds = double.IsFinite(declared) && declared > 0
            ? Math.Min(declared, MaximumPlaybackSeconds)
            : DefaultPlaybackSeconds;

        return TimeSpan.FromSeconds(seconds) + PlaybackMargin;
    }

    public void Dispose()
    {
        FullDuplexDevice? device;
        MiniAudioEngine? engine;
        SoundPlayer? player;

        // CAUTION: take the device out of the field inside the lock, and speak
        // to miniaudio outside it. `ma_device_stop` waits for the audio
        // thread, and that thread must not wait for this lock.
        lock (_deviceLock)
        {
            // CAUTION: the container gives this one object to three
            // registrations. It collects the same instance one time for each of
            // them, thus it disposes this object three times. Without this test
            // the second call throws at `_stop.Cancel()`, on a source that the
            // first call disposed.
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _sessionOpen = false;
            _accumulating = false;

            // A person can hold a button while the software stops. Then no
            // StopRecording occurs and the two buffers keep the speech.
            Array.Clear(_buffer);
            Array.Clear(_spectrumRing);

            _ringWritten = 0;

            device = _device;
            engine = _engine;
            player = _player;
            _device = null;
            _engine = null;
            _player = null;
        }

        // The reads of the list stop here. A read that starts after this line
        // sees the flag above and makes no engine.
        _stop.Cancel();

        // The device goes first. Then no audio thread is in the player when the
        // line below closes it.
        CloseDevice(device);
        Retire(player);

        // CloseDevice only ASKED the audio thread to stop and it swallows an
        // error from Stop, thus a callback can still write after this line.
        // That is of no consequence: the object holds numbers and no samples.
        _meter.Reset();

        engine?.Dispose();
        _stop.Dispose();
    }

    /// <remarks>
    /// CAUTION: <c>Dispose</c> does not remove the player from the mixer,
    /// although a component with a parent usually leaves it there.
    /// <c>SoundPlayerBase</c> does not call the method of
    /// <c>SoundComponent</c> that does this work. Without the line below, the
    /// mixer keeps a player with a closed decoder. It then asks that player for
    /// samples for the life of the process.
    /// </remarks>
    private static void Retire(SoundPlayer? player)
    {
        if (player is null)
        {
            return;
        }

        player.Parent?.RemoveComponent(player);

        // This also disposes the provider, which disposes the stream that holds
        // the WAV.
        player.Dispose();
    }

    private void StartPresenceLoop()
    {
        if (Interlocked.Exchange(ref _presenceStarted, 1) == 1)
        {
            return;
        }

        // Read the token here and not in the task. A read of Token after
        // Dispose throws, and the task can start after Dispose.
        CancellationToken token = _stop.Token;

        _ = Task.Run(() => PresenceLoopAsync(token), token);
    }

    private async Task PresenceLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using PeriodicTimer timer = new(PresenceInterval);

            do
            {
                PollPresence();
            }
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
            // Dispose stopped the loop. This is the correct end.
        }
#pragma warning disable CA1031 // Nothing observes this task. See the comment.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            // CAUTION: nobody waits for this task, thus an error that goes out
            // of here is lost and the display keeps the last value for the life
            // of the process. This line is the one signal that the reads
            // stopped.
            LogPresenceLoopFailed(_logger, exception);
        }
    }

    private void PollPresence()
    {
        int found = ReadPresence();
        int previous = Interlocked.Exchange(ref _presence, found);

        if (found == previous)
        {
            return;
        }

        LogPresenceChanged(_logger, found == PresenceThere, _options.PreferredDeviceName);

        DevicePresenceChanged?.Invoke(this, IsDevicePresent);
    }

    private int ReadPresence()
    {
        string wanted = _options.PreferredDeviceName?.Trim() ?? string.Empty;

        if (wanted.Length == 0)
        {
            return PresenceUnknown;
        }

        // The audio thread never takes this lock, and a read of the list does
        // not wait for that thread. Thus this is safe here, and a call of Stop
        // or Dispose in this position is not. See the remark on the class.
        lock (_deviceLock)
        {
            if (_disposed)
            {
                return PresenceUnknown;
            }

            _engine ??= new MiniAudioEngine();
            _engine.UpdateAudioDevicesInfo();

            // Section 8.19 of deploy/README.md makes the playback interface the
            // condition of the microphone. Thus a speakerphone that gives one
            // of the two is not a device that operates.
            return HasDevice(_engine.CaptureDevices, wanted)
                && HasDevice(_engine.PlaybackDevices, wanted)
                    ? PresenceThere
                    : PresenceGone;
        }
    }

    private static bool HasDevice(DeviceInfo[] devices, string wanted)
    {
        foreach (DeviceInfo device in devices)
        {
            if (device.Name is not null
                && device.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <remarks>
    /// The caller holds <see cref="_deviceLock"/>.
    /// </remarks>
    private void OpenDevice()
    {
        if (_device is not null)
        {
            return;
        }

        try
        {
            _engine ??= new MiniAudioEngine();
            _engine.UpdateAudioDevicesInfo();

            DeviceInfo info = SelectDevice(_engine.CaptureDevices, "microphone");
            DeviceInfo speaker = SelectDevice(_engine.PlaybackDevices, "speaker");

            // The field, because the meter needs the value before this method
            // operates. CAUTION: AudioFormat is a struct, thus each holds a
            // COPY and the field makes no single source of truth.
            FullDuplexDevice device = _engine.InitializeFullDuplexDevice(
                speaker,
                info,
                _format,
                new MiniAudioDeviceConfig());

            // The master mixer is the one path to the speaker, and SoundFlow
            // asks the analyzer at each period, also with no player in it. The
            // mixer belongs to the device, thus the catch below removes nothing.
            device.MasterMixer.AddAnalyzer(_meter);

            // CAUTION: Start starts the capture device and then the playback
            // device, and it does not go back if the second one fails. Without
            // this, the microphone of the machine stays open and no field holds
            // the object that can close it. Each press subsequently says that
            // the microphone is not open, until the process stops.
            try
            {
                device.CaptureDevice.OnAudioProcessed += OnAudioProcessed;
                device.Start();
            }
            catch
            {
                device.CaptureDevice.OnAudioProcessed -= OnAudioProcessed;
                device.Dispose();
                throw;
            }

            AudioFormat given = device.CaptureDevice.Format;

            // CAUTION: the format above is a request. This is the format that
            // the machine gave. A log line that shows the request only hides a
            // machine that gives 48 kHz, and then the speech is not what
            // Moonshine needs.
            _deviceSampleRate = given.SampleRate;
            _device = device;

            // The speaker is in this line because it is the condition that
            // decides if the microphone gives sound at all. See section 8.19 of
            // deploy/README.md. A journal that names the microphone only cannot
            // tell a dead microphone from the incorrect speaker.
            LogMicrophoneStarted(
                _logger,
                info.Name ?? "(no name)",
                speaker.Name ?? "(no name)",
                _options.SampleRate,
                given.SampleRate,
                given.Channels);

            if (given.SampleRate != _options.SampleRate || given.Channels != 1)
            {
                LogFormatIsDifferent(
                    _logger,
                    _options.SampleRate,
                    given.SampleRate,
                    given.Channels);
            }
        }
        catch (Exception exception) when (exception is not AudioCaptureException)
        {
            LogNoMicrophone(_logger, exception);
            throw new AudioCaptureException("The microphone did not open.", exception);
        }
    }

    /// <remarks>
    /// CAUTION: the caller must not hold <see cref="_deviceLock"/>.
    /// </remarks>
    private void CloseDevice(FullDuplexDevice? device)
    {
        if (device is null)
        {
            return;
        }

        device.CaptureDevice.OnAudioProcessed -= OnAudioProcessed;

        try
        {
            device.Stop();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogStopFailed(_logger, exception);
        }
        finally
        {
            // CAUTION: Stop stops the playback device and then the capture
            // device, with no finally between them, and Dispose calls Stop
            // again. Thus a playback device that throws leaves the microphone
            // running and throws the same error a second time here. That error
            // goes out of the container and stops each Dispose after it, and
            // one of those holds the speech of a person.
            try
            {
                device.Dispose();
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                LogStopFailed(_logger, exception);
            }
        }
    }

    private DeviceInfo SelectDevice(DeviceInfo[] devices, string what)
    {
        if (devices.Length == 0)
        {
            throw new AudioCaptureException($"The machine has no {what}.");
        }

        string wanted = _options.PreferredDeviceName?.Trim() ?? string.Empty;

        if (wanted.Length != 0)
        {
            foreach (DeviceInfo device in devices)
            {
                if (device.Name is not null
                    && device.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return device;
                }
            }

            string names = string.Join(
                ", ",
                devices.Select(static device => device.Name ?? "(no name)"));

            LogPreferredNotFound(_logger, what, wanted, names);
        }

        foreach (DeviceInfo device in devices)
        {
            if (device.IsDefault)
            {
                return device;
            }
        }

        return devices[0];
    }

    /// <remarks>
    /// CAUTION: this operates on a thread with a high priority. It takes no
    /// lock, it makes no memory, and it does no work that has no limit.
    /// </remarks>
    private void OnAudioProcessed(Span<float> samples, Capability capability)
    {
        if (!_accumulating)
        {
            return;
        }

        int at = _written;
        int room = _buffer.Length - at;

        if (room <= 0)
        {
            // The button did not come up. The view model sees the flag and it
            // gives the lane back.
            _reachedLimit = true;
            _accumulating = false;
            return;
        }

        Span<float> taken = samples[..Math.Min(room, samples.Length)];
        taken.CopyTo(_buffer.AsSpan(at));

        WriteRing(taken);

        // The peak comes from this same loop, thus StopRecording does not
        // examine each sample again while the audio thread waits.
        float peak = BitConverter.Int32BitsToSingle(_peakBits);

        foreach (float sample in taken)
        {
            float value = Math.Abs(sample);

            if (value > peak)
            {
                peak = value;
            }
        }

        _peakBits = BitConverter.SingleToInt32Bits(peak);
        _written = at + taken.Length;
    }

    private void WriteRing(ReadOnlySpan<float> samples)
    {
        ReadOnlySpan<float> newest = samples.Length > SpectrumRingLength
            ? samples[^SpectrumRingLength..]
            : samples;

        int at = _ringWritten % SpectrumRingLength;
        int first = Math.Min(newest.Length, SpectrumRingLength - at);

        newest[..first].CopyTo(_spectrumRing.AsSpan(at));

        if (first < newest.Length)
        {
            newest[first..].CopyTo(_spectrumRing);
        }

        _ringWritten += newest.Length;
    }

    /// <remarks>
    /// CAUTION: the audio thread can write in the ring while this reads it, and
    /// one frame of bars is then not correct. Spectrum holds its oldest window
    /// 256 samples behind that thread, which is 16 ms at 16 kHz, and that
    /// margin is the guarantee. A lock would put that thread behind the
    /// display.
    /// </remarks>
    public void ReadSpectrum(Span<double> bars) =>
        _spectrum.Fill(_spectrumRing, _ringWritten, bars);

    /// <inheritdoc />
    public double PlaybackLevel => _meter.Read();

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The microphone is {device} and the speaker is {speaker}. The software asked for {wanted} Hz and the machine gave {actual} Hz with {channels} channel(s).")]
    private static partial void LogMicrophoneStarted(
        ILogger logger,
        string device,
        string speaker,
        int wanted,
        int actual,
        int channels);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "CAUTION: the microphone gave {actual} Hz with {channels} channel(s), and the software asked for {wanted} Hz with one channel. The speech-to-text part needs the correct rate.")]
    private static partial void LogFormatIsDifferent(
        ILogger logger,
        int wanted,
        int actual,
        int channels);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The recording stopped after {seconds:F2} s with {samples} samples at {sampleRate} Hz. The largest level is {peak:F3} and the largest bar of the visualizer is {loudestBar:F2} of 1.00.")]
    private static partial void LogStopped(
        ILogger logger,
        double seconds,
        int samples,
        float peak,
        int sampleRate,
        double loudestBar);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The recording came to the limit of {seconds} s. A button can be down mechanically.")]
    private static partial void LogReachedLimit(ILogger logger, int seconds);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The microphone did not open.")]
    private static partial void LogNoMicrophone(ILogger logger, Exception exception);


    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "No {what} has \"{wanted}\" in its name. The machine gives: {names}. The software uses the default device, which can be the incorrect one.")]
    private static partial void LogPreferredNotFound(
        ILogger logger,
        string what,
        string wanted,
        string names);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The microphone did not stop correctly.")]
    private static partial void LogStopFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The speaker played {bytes} bytes in {seconds:F2} s at {sampleRate} Hz. The largest sound is {loudest:F1} dBFS, and the last callback of the speaker gave {frames} samples.")]
    private static partial void LogPlayed(
        ILogger logger,
        double seconds,
        int bytes,
        int sampleRate,
        double loudest,
        int frames);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The audio of {audioSeconds:F1} s did not come to its end in {budgetSeconds:F1} s. The software stops it. The largest sound before it stopped is {loudest:F1} dBFS. A value of 0.0 for the audio says that the decoder gave no length, and that the budget is the value of the software.")]
    private static partial void LogPlaybackDidNotEnd(
        ILogger logger,
        double audioSeconds,
        double budgetSeconds,
        double loudest);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The decoder refused {bytes} bytes of audio from the speech server.")]
    private static partial void LogPlaybackFailed(ILogger logger, int bytes, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The speakerphone is present: {present}. The settings look for a device with \"{wanted}\" in its name.")]
    private static partial void LogPresenceChanged(ILogger logger, bool present, string wanted);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The reads of the list of the devices stopped. The display keeps the last condition of the speakerphone for the life of the process.")]
    private static partial void LogPresenceLoopFailed(ILogger logger, Exception exception);
}
