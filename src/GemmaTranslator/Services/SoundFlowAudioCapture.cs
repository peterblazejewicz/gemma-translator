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
/// The software asks for 16 kHz, one channel, and F32. miniaudio converts from
/// the format of the device in native code, thus the Jabra at 48 kHz needs no
/// work here.
/// </remarks>
public sealed partial class SoundFlowAudioCapture : IAudioCapture
{
    private readonly AudioOptions _options;
    private readonly ILogger<SoundFlowAudioCapture> _logger;
    private readonly Lock _lock = new();
    private readonly List<float> _samples = [];

    private MiniAudioEngine? _engine;
    private AudioCaptureDevice? _device;
    private long _startTicks;

    // The device stays open and it runs. This flag says if the samples go in
    // the buffer. See Prepare for the cause.
    private bool _accumulating;

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
    }

    /// <inheritdoc/>
    public bool IsRecording
    {
        get
        {
            lock (_lock)
            {
                return _accumulating;
            }
        }
    }

    /// <inheritdoc/>
    public void Prepare()
    {
        lock (_lock)
        {
            OpenDevice();
        }
    }

    /// <inheritdoc/>
    public void StartRecording()
    {
        lock (_lock)
        {
            if (_accumulating)
            {
                return;
            }

            // The device usually opens at the start. This call is for the
            // condition where the first attempt did not operate, for example
            // if a person connected the Jabra after the start.
            OpenDevice();

            _samples.Clear();
            _startTicks = Stopwatch.GetTimestamp();
            _accumulating = true;
        }
    }

    /// <inheritdoc/>
    public Recording? StopRecording()
    {
        lock (_lock)
        {
            if (!_accumulating)
            {
                return null;
            }

            _accumulating = false;

            TimeSpan duration = Stopwatch.GetElapsedTime(_startTicks);

            float[] samples = [.. _samples];
            _samples.Clear();

            float peak = 0f;
            foreach (float sample in samples)
            {
                float value = Math.Abs(sample);
                if (value > peak)
                {
                    peak = value;
                }
            }

            // A peak near 0 means that the software recorded silence. On the
            // appliance this is the sign that it opened the wrong device.
            LogStopped(_logger, duration.TotalSeconds, samples.Length, peak);

            return new Recording(samples, duration, peak);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_lock)
        {
            Release();
            _engine?.Dispose();
            _engine = null;
        }
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
    /// <returns>The device, or <c>null</c> to let the backend select.</returns>
    private DeviceInfo? SelectDevice(DeviceInfo[] devices)
    {
        if (devices is null || devices.Length == 0)
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

            // The name is in the settings and no device has it. This is a
            // condition that a person must correct, thus it goes in the log.
            LogPreferredNotFound(_logger, wanted, devices.Length);
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

    private void OnAudioProcessed(Span<float> samples, Capability capability)
    {
        if (capability != Capability.Record)
        {
            return;
        }

        lock (_lock)
        {
            // The device runs always. The samples go in the buffer only
            // between the press and the release of a button.
            if (_accumulating)
            {
                _samples.AddRange(samples);
            }
        }
    }

    /// <summary>
    /// Opens the microphone and starts it, if it is not open.
    /// </summary>
    /// <remarks>
    /// The caller holds the lock.
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

            DeviceInfo? info = SelectDevice(_engine.CaptureDevices);

            AudioFormat format = new()
            {
                Format = SampleFormat.F32,
                Channels = 1,
                SampleRate = _options.SampleRate,
            };

            // DeviceConfig is abstract. The MiniAudio backend needs its own
            // config type.
            _device = _engine.InitializeCaptureDevice(info, format, new MiniAudioDeviceConfig());
            _device.OnAudioProcessed += OnAudioProcessed;
            _device.Start();

            LogStarted(_logger, info?.Name ?? "(the default device)", _options.SampleRate);
        }
        catch (Exception exception) when (exception is not AudioCaptureException)
        {
            Release();
            LogNoMicrophone(_logger, exception);
            throw new AudioCaptureException("The microphone did not open.", exception);
        }
    }

    private void Release()
    {
        if (_device is not null)
        {
            _device.OnAudioProcessed -= OnAudioProcessed;

            try
            {
                _device.Stop();
            }
            catch (Exception exception)
            {
                LogStopFailed(_logger, exception);
            }

            _device.Dispose();
            _device = null;
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The recording started on {device} at {sampleRate} Hz.")]
    private static partial void LogStarted(ILogger logger, string device, int sampleRate);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The recording stopped after {seconds:F2} s with {samples} samples. The largest level is {peak:F3}.")]
    private static partial void LogStopped(
        ILogger logger,
        double seconds,
        int samples,
        float peak);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The microphone did not open.")]
    private static partial void LogNoMicrophone(ILogger logger, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "No microphone has \"{wanted}\" in its name. The machine has {count} devices. The software uses the default device, which can record silence.")]
    private static partial void LogPreferredNotFound(ILogger logger, string wanted, int count);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The microphone did not stop correctly.")]
    private static partial void LogStopFailed(ILogger logger, Exception exception);
}
