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

using Avalonia.Controls;
using Microsoft.Extensions.Logging;

namespace GemmaTranslator.Services;

/// <summary>
/// The buttons, from the input devices of Linux.
/// </summary>
/// <remarks>
/// <para>
/// CAUTION: this class is necessary because Avalonia gives no key event on the
/// Raspberry Pi. The DRM backend of <c>Avalonia.LinuxFramebuffer</c> 12.1.1
/// gives a pointer event and a touch event only. <c>RawKeyEventArgs</c> is not
/// in that assembly, thus <c>KeyDown</c> never occurs.
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
/// The software opens the two devices with these names only. It does not open
/// each device of <c>/dev/input</c>. A keyboard and the touch screen are
/// different devices, and this software must not read them: the appliance is
/// in a public location, and a process that reads each key is a hazard that no
/// function here needs.
/// </para>
/// <para>
/// CAUTION: the software finds the devices one time, at the start. A button
/// harness that a person connects later is not found. This is acceptable
/// because the buttons are on the GPIO header and they are there when the
/// machine starts.
/// </para>
/// </remarks>
public sealed partial class EvdevPushToTalk : IPushToTalk
{
    // struct input_event on 64-bit Linux: two 8-byte values for the time,
    // then __u16 type, __u16 code, __s32 value.
    private const int EventSize = 24;
    private const ushort EvKey = 0x01;

    private const ushort KeyF13 = 183;
    private const ushort KeyF14 = 184;

    private const string Speaker1 = "SPEAKER_1";
    private const string Speaker2 = "SPEAKER_2";

    private readonly ILogger<EvdevPushToTalk> _logger;
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
    public void Start(TopLevel? topLevel)
    {
        // The Raspberry Pi reads the device of Linux, thus it needs no top
        // level. The argument is for the Windows implementation.
        _ = topLevel;

        if (IntPtr.Size != 8)
        {
            // The offsets of input_event are for a 64-bit system. On a 32-bit
            // image the code would read the wrong bytes and give nonsense.
            LogNot64Bit(_logger);
            return;
        }

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
            string name = ReadDeviceName(path);

            if (name is not (Speaker1 or Speaker2))
            {
                continue;
            }

            FileStream stream;

            try
            {
                stream = new FileStream(path, FileMode.Open, FileAccess.Read);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                LogDeviceNotOpen(_logger, name, path, exception.Message);
                continue;
            }

            _streams.Add(stream);
            opened++;

            LogDeviceOpen(_logger, name, path);

            // CAUTION: this thread is a background thread and it stops with
            // the process. A blocking read cannot be stopped: neither a
            // cancellation token nor a close of the handle interrupts it. Thus
            // this class has no Dispose that stops the threads, because such a
            // method could not do what its name says.
            Thread thread = new(() => ReadLoop(stream, name))
            {
                IsBackground = true,
                Name = $"evdev {name}",
            };

            thread.Start();
        }

        // The appliance has no console. These lines are the first thing to
        // read if a button does nothing.
        if (opened == 0)
        {
            LogNoButtons(_logger, devices.Length, Speaker1, Speaker2);
        }
        else if (opened < 2)
        {
            LogOneButton(_logger, opened);
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // The threads stop with the process. See the remark in Start. This
        // closes the handles that no thread waits on.
        foreach (FileStream stream in _streams)
        {
            try
            {
                stream.Dispose();
            }
            catch (IOException)
            {
                // A thread can wait on this handle. The process stops soon.
            }
        }

        _streams.Clear();
    }

    /// <summary>
    /// Reads the name of one input device.
    /// </summary>
    /// <remarks>
    /// The name is the <c>label</c> of the <c>gpio-key</c> overlay. It is a
    /// plain file in sysfs, thus this needs no <c>EVIOCGNAME</c> and no
    /// P/Invoke.
    /// </remarks>
    /// <param name="path">The path of the device, for example /dev/input/event3.</param>
    /// <returns>The name, or an empty text.</returns>
    private static string ReadDeviceName(string path)
    {
        try
        {
            return File
                .ReadAllText($"/sys/class/input/{Path.GetFileName(path)}/device/name")
                .Trim();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// Gets the lane of one key code, or 0 if the key is not a button.
    /// </summary>
    /// <param name="code">The code of the key, from the Linux headers.</param>
    /// <returns>1, 2, or 0.</returns>
    private static int LaneOf(ushort code) => code switch
    {
        KeyF13 => 1,
        KeyF14 => 2,
        _ => 0,
    };

    private void ReadLoop(FileStream stream, string name)
    {
        byte[] buffer = new byte[EventSize];

        try
        {
            while (true)
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
#pragma warning disable CA1031 // The appliance must not stop. See the remark.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            // CAUTION: this catch takes each type on purpose. Changed calls
            // the code of a subscriber on this thread. An error with no catch
            // on a background thread stops the process, and then the display
            // of the appliance becomes black. The recording continues until
            // AudioOptions.MaximumRecordingSeconds stops it.
            LogReadFailed(_logger, name, exception);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The button {name} is at {path}.")]
    private static partial void LogDeviceOpen(ILogger logger, string name, string path);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "No button is here. The software examined {count} input devices and none has the name {speaker1} or {speaker2}. Examine the dtoverlay lines of /boot/firmware/config.txt.")]
    private static partial void LogNoButtons(
        ILogger logger,
        int count,
        string speaker1,
        string speaker2);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The software found {opened} button of the 2. One person cannot speak.")]
    private static partial void LogOneButton(ILogger logger, int opened);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The software cannot read /dev/input. The user must be in the group that the udev rule gives.")]
    private static partial void LogNoInputDirectory(ILogger logger, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The button {name} at {path} did not open: {reason}")]
    private static partial void LogDeviceNotOpen(
        ILogger logger,
        string name,
        string path,
        string reason);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The software cannot continue to read the button {name}. That button does nothing now.")]
    private static partial void LogReadFailed(ILogger logger, string name, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "This is not a 64-bit system. The buttons do nothing, because the record of an input event has different offsets.")]
    private static partial void LogNot64Bit(ILogger logger);
}
