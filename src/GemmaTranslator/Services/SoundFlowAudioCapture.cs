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

namespace GemmaTranslator.Services;

/// <summary>
/// Records the microphone with miniaudio, through SoundFlow.
/// </summary>
/// <remarks>
/// <para>
/// The software asks for 16 kHz, one channel, and F32. miniaudio converts from
/// the format of the device in native code, thus the Jabra at 48 kHz needs no
/// work here.
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
    private readonly AudioOptions _options;
    private readonly ILogger<SoundFlowAudioCapture> _logger;

    // This lock is for the life of the device only. The audio thread never
    // takes it. See the remark on the class.
    private readonly Lock _deviceLock = new();

    // One buffer, made one time. The audio thread writes in it and it never
    // increases. See AudioOptions.MaximumRecordingSeconds.
    private readonly float[] _buffer;

    private MiniAudioEngine? _engine;
    private AudioCaptureDevice? _device;
    private long _startTicks;
    private int _deviceSampleRate;

    // The audio thread writes these and the thread of the user interface reads
    // them. They are volatile, thus no lock is necessary.
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

    /// <summary>
    /// Initializes a new instance of the <see cref="SoundFlowAudioCapture"/> class.
    /// </summary>
    /// <param name="options">The settings of the microphone.</param>
    /// <param name="logger">The logger from the container.</param>
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

    /// <inheritdoc/>
    public void Prepare()
    {
        lock (_deviceLock)
        {
            OpenDevice();
        }
    }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public void Dispose()
    {
        AudioCaptureDevice? device;
        MiniAudioEngine? engine;

        // CAUTION: take the device out of the field inside the lock, and speak
        // to miniaudio outside it. `ma_device_stop` waits for the audio
        // thread, and that thread must not wait for this lock.
        lock (_deviceLock)
        {
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
    }

    /// <summary>
    /// Opens the microphone and starts it, if it does not run.
    /// </summary>
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

            DeviceInfo info = SelectDevice(_engine.CaptureDevices);

            AudioFormat format = new()
            {
                Format = SampleFormat.F32,
                Channels = 1,
                SampleRate = _options.SampleRate,
            };

            AudioCaptureDevice device =
                _engine.InitializeCaptureDevice(info, format, new MiniAudioDeviceConfig());

            device.OnAudioProcessed += OnAudioProcessed;
            device.Start();

            // CAUTION: the format above is a request. This is the format that
            // the machine gave. A log line that shows the request only hides a
            // machine that gives 48 kHz, and then the speech is not what
            // Moonshine needs.
            _deviceSampleRate = device.Format.SampleRate;
            _device = device;

            LogMicrophoneStarted(
                _logger,
                info.Name ?? "(no name)",
                _options.SampleRate,
                device.Format.SampleRate,
                device.Format.Channels);

            if (device.Format.SampleRate != _options.SampleRate || device.Format.Channels != 1)
            {
                LogFormatIsDifferent(
                    _logger,
                    _options.SampleRate,
                    device.Format.SampleRate,
                    device.Format.Channels);
            }
        }
        catch (Exception exception) when (exception is not AudioCaptureException)
        {
            LogNoMicrophone(_logger, exception);
            throw new AudioCaptureException("The microphone did not open.", exception);
        }
    }

    /// <summary>
    /// Stops one device and releases it.
    /// </summary>
    /// <remarks>
    /// CAUTION: the caller must not hold <see cref="_deviceLock"/>.
    /// </remarks>
    /// <param name="device">The device, or <c>null</c>.</param>
    private void CloseDevice(AudioCaptureDevice? device)
    {
        if (device is null)
        {
            return;
        }

        device.OnAudioProcessed -= OnAudioProcessed;

        try
        {
            device.Stop();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogStopFailed(_logger, exception);
        }

        device.Dispose();
    }

    /// <summary>
    /// Selects the microphone.
    /// </summary>
    /// <remarks>
    /// The sequence is: the name that the settings give, then the default
    /// device, then the first device. See
    /// <see cref="AudioOptions.PreferredDeviceName"/> for the cause.
    /// </remarks>
    /// <param name="devices">Each capture device that the machine has.</param>
    /// <returns>The device to open.</returns>
    private DeviceInfo SelectDevice(DeviceInfo[] devices)
    {
        if (devices.Length == 0)
        {
            throw new AudioCaptureException("The machine has no microphone.");
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

            LogPreferredNotFound(_logger, wanted, names);
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

    /// <summary>
    /// Takes the samples from the audio thread of miniaudio.
    /// </summary>
    /// <remarks>
    /// CAUTION: this operates on a thread with a high priority. It takes no
    /// lock, it makes no memory, and it does no work that has no limit.
    /// </remarks>
    /// <param name="samples">The new samples.</param>
    /// <param name="capability">The type of the device.</param>
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
            // The button did not come up. This limit is the one protection
            // against a recording with no end. The view model sees the flag
            // and it gives the lane back.
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
        Message = "The microphone is {device}. The software asked for {wanted} Hz and the machine gave {actual} Hz with {channels} channel(s).")]
    private static partial void LogMicrophoneStarted(
        ILogger logger,
        string device,
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
        Message = "No microphone has \"{wanted}\" in its name. The machine gives: {names}. The software uses the default device, which can record no sound.")]
    private static partial void LogPreferredNotFound(ILogger logger, string wanted, string names);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The microphone did not stop correctly.")]
    private static partial void LogStopFailed(ILogger logger, Exception exception);
}
