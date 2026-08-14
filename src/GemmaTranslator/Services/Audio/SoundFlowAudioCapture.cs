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
// been modified. It replaces frontend/src/hooks/useAudioRecorder.js.

using System.Diagnostics;
using GemmaTranslator.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SoundFlow.Abstracts.Devices;
using SoundFlow.Backends.MiniAudio;
using SoundFlow.Backends.MiniAudio.Devices;
using SoundFlow.Enums;
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
/// path is behind a canceller that needs the playback reference. Nothing in
/// the mixer makes that playback silence. Do not make this a capture device to
/// remove one object: the microphone then gives 0.000 for each press, and no
/// error says why.
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
public sealed partial class SoundFlowAudioCapture : IAudioCapture
{
    private const int PresenceUnknown = 0;
    private const int PresenceThere = 1;
    private const int PresenceGone = 2;

    // The longest that a person waits to see that the speakerphone went away.
    // Each read gives the list of the devices of the machine and it opens
    // nothing, thus the cost is not the 1.22 s of an open.
    private static readonly TimeSpan PresenceInterval = TimeSpan.FromSeconds(2);

    private readonly AudioOptions _options;
    private readonly ILogger<SoundFlowAudioCapture> _logger;
    private readonly CancellationTokenSource _stop = new();

    // This lock is for the life of the device only. The audio thread never
    // takes it. See the remark on the class.
    private readonly Lock _deviceLock = new();

    // One buffer, made one time. The audio thread writes in it and it never
    // increases. See AudioOptions.MaximumRecordingSeconds.
    private readonly float[] _buffer;

    private MiniAudioEngine? _engine;
    private FullDuplexDevice? _device;
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

    // 0 says that this machine cannot answer, 1 that the device is there, and 2
    // that it is not. This is an int and not a bool?, because Volatile and
    // Interlocked read and write a value of this size in one operation and a
    // nullable of a bool is two fields.
    private int _presence;
    private int _presenceStarted;
    private bool _disposed;

    public SoundFlowAudioCapture(
        IOptions<AudioOptions> options,
        ILogger<SoundFlowAudioCapture> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options.Value;
        _logger = logger;

        _buffer = new float[_options.SampleRate * _options.MaximumRecordingSeconds];
        _deviceSampleRate = _options.SampleRate;
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
            Array.Clear(_buffer);
        }

        LogStopped(_logger, duration.TotalSeconds, written, peak, _deviceSampleRate);

        if (_reachedLimit)
        {
            LogReachedLimit(_logger, _options.MaximumRecordingSeconds);
        }

        return new Recording(samples, duration, peak, _deviceSampleRate, _reachedLimit);
    }

    public void Dispose()
    {
        FullDuplexDevice? device;
        MiniAudioEngine? engine;

        // The reads of the list stop first. A read that begins after the lines
        // below would make the engine a second time.
        _stop.Cancel();

        // CAUTION: take the device out of the field inside the lock, and speak
        // to miniaudio outside it. `ma_device_stop` waits for the audio
        // thread, and that thread must not wait for this lock.
        lock (_deviceLock)
        {
            _disposed = true;
            _sessionOpen = false;
            _accumulating = false;

            // A person can hold a button while the software stops. Then no
            // StopRecording occurs and the buffer keeps the speech.
            Array.Clear(_buffer);

            device = _device;
            engine = _engine;
            _device = null;
            _engine = null;
        }

        CloseDevice(device);
        engine?.Dispose();
        _stop.Dispose();
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

            AudioFormat format = new()
            {
                Format = SampleFormat.F32,
                Channels = 1,
                SampleRate = _options.SampleRate,
            };

            FullDuplexDevice device = _engine.InitializeFullDuplexDevice(
                speaker,
                info,
                format,
                new MiniAudioDeviceConfig());

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
        Message = "The recording stopped after {seconds:F2} s with {samples} samples at {sampleRate} Hz. The largest level is {peak:F3}.")]
    private static partial void LogStopped(
        ILogger logger,
        double seconds,
        int samples,
        float peak,
        int sampleRate);

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
        Message = "The speakerphone is present: {present}. The settings look for a device with \"{wanted}\" in its name.")]
    private static partial void LogPresenceChanged(ILogger logger, bool present, string wanted);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The reads of the list of the devices stopped. The display keeps the last condition of the speakerphone for the life of the process.")]
    private static partial void LogPresenceLoopFailed(ILogger logger, Exception exception);
}
