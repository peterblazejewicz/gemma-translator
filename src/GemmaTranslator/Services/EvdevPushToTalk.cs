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
// been modified. It replaces the keydown and keyup handlers of
// TranslatorApp.jsx, lines 250 to 312.

using Microsoft.Extensions.Logging;

namespace GemmaTranslator.Services;

/// <summary>
/// The buttons, from the input devices of Linux.
/// </summary>
/// <remarks>
/// <para>
/// CAUTION: this class is necessary because Avalonia gives no key event on the
/// Raspberry Pi. The DRM backend of <c>Avalonia.LinuxFramebuffer</c> 12.1.1
/// raises a pointer event and a touch event only. <c>RawKeyEventArgs</c> is
/// not in that assembly, thus <c>KeyDown</c> never occurs.
/// </para>
/// <para>
/// The two buttons come to Linux as key events, because the device tree makes
/// them keys:
/// </para>
/// <code>
/// dtoverlay=gpio-key,gpio=17,active_low=1,gpio_pull=up,label=SPEAKER_1,keycode=183
/// dtoverlay=gpio-key,gpio=27,active_low=1,gpio_pull=up,label=SPEAKER_2,keycode=184
/// </code>
/// <para>
/// This class reads each device of <c>/dev/input/</c> and not one device with
/// a name. Thus it finds the two buttons, and it finds a USB keyboard that a
/// person connects to the device for a test. Then Z and X operate on the
/// appliance also. The user must be in the <c>input</c> group.
/// </para>
/// </remarks>
public sealed partial class EvdevPushToTalk : IPushToTalk
{
    // struct input_event on 64-bit Linux: two 8-byte values for the time,
    // then __u16 type, __u16 code, __s32 value.
    private const int EventSize = 24;
    private const ushort EvKey = 0x01;

    private const ushort KeyZ = 44;
    private const ushort KeyX = 45;
    private const ushort KeyF13 = 183;
    private const ushort KeyF14 = 184;

    private readonly ILogger<EvdevPushToTalk> _logger;
    private readonly CancellationTokenSource _stopping = new();
    private readonly List<FileStream> _streams = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="EvdevPushToTalk"/> class.
    /// </summary>
    /// <param name="logger">The logger from the container.</param>
    public EvdevPushToTalk(ILogger<EvdevPushToTalk> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc/>
    public event EventHandler<PushToTalkChange>? Changed;

    /// <inheritdoc/>
    public void Start()
    {
        string[] devices;

        try
        {
            devices = Directory.GetFiles("/dev/input", "event*");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            LogNoInputDirectory(_logger, exception);
            return;
        }

        int opened = 0;

        foreach (string path in devices)
        {
            FileStream stream;

            try
            {
                stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                // A device that the user cannot read is not an error. The
                // touch screen and the buttons are different devices.
                LogDeviceNotOpen(_logger, path, exception.Message);
                continue;
            }

            _streams.Add(stream);
            opened++;

            Thread thread = new(() => ReadLoop(stream, path))
            {
                IsBackground = true,
                Name = $"evdev {Path.GetFileName(path)}",
            };

            thread.Start();
        }

        // The appliance has no console. This line is the first thing to read
        // if a button does nothing.
        LogStarted(_logger, opened, devices.Length);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _stopping.Cancel();

        foreach (FileStream stream in _streams)
        {
            stream.Dispose();
        }

        _streams.Clear();
        _stopping.Dispose();
    }

    /// <summary>
    /// Gets the lane of one key code, or 0 if the key is not a button.
    /// </summary>
    /// <param name="code">The code of the key, from the Linux headers.</param>
    /// <returns>1, 2, or 0.</returns>
    private static int LaneOf(ushort code) => code switch
    {
        KeyF13 or KeyZ => 1,
        KeyF14 or KeyX => 2,
        _ => 0,
    };

    private void ReadLoop(FileStream stream, string path)
    {
        byte[] buffer = new byte[EventSize];

        try
        {
            while (!_stopping.IsCancellationRequested)
            {
                int read = 0;

                while (read < EventSize)
                {
                    int count = stream.Read(buffer, read, EventSize - read);

                    if (count == 0)
                    {
                        return;
                    }

                    read += count;
                }

                ushort type = BitConverter.ToUInt16(buffer, 16);
                ushort code = BitConverter.ToUInt16(buffer, 18);
                int value = BitConverter.ToInt32(buffer, 20);

                if (type != EvKey)
                {
                    continue;
                }

                // 2 is autorepeat. A person holds one button, and that is one
                // press and not many.
                if (value is not (0 or 1))
                {
                    continue;
                }

                int lane = LaneOf(code);

                if (lane == 0)
                {
                    continue;
                }

                Changed?.Invoke(this, new PushToTalkChange(lane, value == 1));
            }
        }
        catch (Exception exception) when (
            exception is IOException or ObjectDisposedException)
        {
            if (!_stopping.IsCancellationRequested)
            {
                LogReadFailed(_logger, path, exception);
            }
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The buttons come from Linux. The software reads {opened} of {found} input devices. Lane 1 is F13 or Z, and lane 2 is F14 or X.")]
    private static partial void LogStarted(ILogger logger, int opened, int found);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The software cannot read /dev/input. The user must be in the input group.")]
    private static partial void LogNoInputDirectory(ILogger logger, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Debug,
        Message = "The input device {path} did not open: {reason}")]
    private static partial void LogDeviceNotOpen(ILogger logger, string path, string reason);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The software cannot continue to read the input device {path}.")]
    private static partial void LogReadFailed(ILogger logger, string path, Exception exception);
}
