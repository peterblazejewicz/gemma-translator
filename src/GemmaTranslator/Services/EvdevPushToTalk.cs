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
// been modified. It replaces the record keys of handleKeyDown and
// handleKeyUp, in upstream/main:frontend/src/TranslatorApp.jsx.

using System.Runtime.InteropServices;
using Avalonia.Controls;
using Microsoft.Extensions.Logging;

namespace GemmaTranslator.Services;

/// <summary>
/// The two buttons, from one input device of Linux.
/// </summary>
/// <remarks>
/// <para>
/// CAUTION: this class is necessary because Avalonia gives no key event on the
/// Raspberry Pi. The DRM backend of <c>Avalonia.LinuxFramebuffer</c> 12.1.1
/// gives a pointer event and a touch event only. <c>RawKeyEventArgs</c> is not
/// in that assembly, thus <c>KeyDown</c> does not occur.
/// </para>
/// <para>
/// SECURITY CONTROL. This class opens one path, and that path is a symlink
/// that udev makes for the GPIO button harness. Do not change it to a scan of
/// /dev/input, do not fall back to matching on the reported device name, and
/// do not put the service account in group "input".
/// </para>
/// <para>
/// What that would give away: every /dev/input/event* node on this machine is
/// 0660 root:input by default, so any member of that group can read the
/// touchscreen and every keystroke from any USB keyboard somebody plugs in
/// later, including a password typed at a console. This appliance stands in a
/// public place and already records what people say. Widen this and it becomes
/// a keylogger, and nothing on the display would show it.
/// </para>
/// <para>
/// The udev rule matches the kernel device topology and not the reported name,
/// because a USB device supplies its own name string and can claim to be the
/// button harness. See <c>deploy/99-gemma-translator.rules</c>.
/// </para>
/// <para>
/// CAUTION: the software opens the device one time, at the start. A harness
/// that a person connects subsequently is not found. This is permitted because
/// the buttons are on the GPIO header and they are there when the machine
/// starts.
/// </para>
/// </remarks>
public sealed partial class EvdevPushToTalk : IPushToTalk
{
    /// <summary>
    /// The one device that this class opens.
    /// </summary>
    /// <remarks>
    /// The udev rule of <c>deploy/99-gemma-translator.rules</c> makes this
    /// symlink. The number of an event device is not the same after each
    /// start, thus the software must not use one.
    /// </remarks>
    private const string DevicePath = "/dev/input/recorder-buttons";

    private const ushort EvKey = 0x01;
    private const ushort KeyF13 = 183;
    private const ushort KeyF14 = 184;

    private readonly ILogger<EvdevPushToTalk> _logger;

    private FileStream? _stream;

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

        // CAUTION: a second call opens the device again. Each open of an
        // input device makes its own reader in the kernel, and each reader
        // gets each event. Thus the software then gets each press two times.
        //
        // The test is _stream, which is not null only after an open that
        // operates. Thus a call after a failure can try again.
        if (_stream is not null)
        {
            LogAlreadyStarted(_logger, DevicePath);
            return;
        }

        FileStream stream;

        try
        {
            stream = new FileStream(DevicePath, FileMode.Open, FileAccess.Read);
        }
        catch (FileNotFoundException)
        {
            // The appliance has no console. This line is the first thing to
            // read if a button does nothing.
            LogDeviceMissing(_logger, DevicePath);
            return;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            LogDeviceNotOpen(_logger, DevicePath, exception.Message);
            return;
        }

        _stream = stream;

        LogDeviceOpen(_logger, DevicePath);

        // CAUTION: this thread is a background thread and it stops with the
        // process. A blocking read cannot be stopped: neither a cancellation
        // token nor a close of the handle interrupts it. Thus Dispose does not
        // stop this thread, and no method of this class can.
        Thread thread = new(() => ReadLoop(stream))
        {
            IsBackground = true,
            Name = "evdev push-to-talk",
        };

        thread.Start();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // CAUTION: this method does nothing on purpose, and that is not an
        // omission.
        //
        // The reader thread is in a blocking read that no method can stop. See
        // the CAUTION in Start. A close of the stream does not stop that
        // thread. It makes the next read give an error. The software then
        // writes a line at level Error for a stop that is correct.
        //
        // The one caller is the exit of the process, thus the system takes the
        // file back immediately after this.
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

    private void ReadLoop(FileStream stream)
    {
        byte[] buffer = new byte[InputEvent.Size];

        while (true)
        {
            try
            {
                stream.ReadExactly(buffer);
            }
            catch (Exception exception) when (
                exception is IOException or ObjectDisposedException)
            {
                // The device is gone. A read gives no more data, thus the
                // thread stops here.
                //
                // CAUTION: the filter is narrow on purpose. An error of a
                // different type is a defect of this software. It goes out of
                // this thread, the process stops, and systemd starts it again.
                // A wide filter makes a defect look like a fault of the
                // hardware.
                LogReadFailed(_logger, DevicePath, exception);
                return;
            }

            InputEvent inputEvent = MemoryMarshal.Read<InputEvent>(buffer);

            if (inputEvent.Type != EvKey)
            {
                continue;
            }

            // 0 is up and 1 is down. 2 is autorepeat, and a person who holds
            // one button makes one press and not many.
            if (inputEvent.Value is not (0 or 1))
            {
                continue;
            }

            int lane = LaneOf(inputEvent.Code);

            if (lane == 0)
            {
                continue;
            }

            try
            {
                Changed?.Invoke(this, new PushToTalkChange(lane, inputEvent.Value == 1));
            }
#pragma warning disable CA1031 // The appliance must not stop. See the remark.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                // This catch is insurance and it is not a live path today.
                // The one subscriber posts to the dispatcher and returns
                // immediately. Thus its error comes on the thread of the user
                // interface. A subscriber that does its work here would give
                // its error here.
                //
                // CAUTION: an event goes to each subscriber in sequence. A
                // subscriber that gives an error stops the subscribers after
                // it, thus this event can go to no subscriber.
                LogSubscriberFailed(_logger, exception);
            }
        }
    }

    /// <summary>
    /// One record of <c>/dev/input/event*</c>, which is <c>struct
    /// input_event</c> of Linux.
    /// </summary>
    /// <remarks>
    /// The two values of the time are 8 bytes each on a 64-bit machine, thus
    /// the record is 24 bytes. This software has no 32-bit target. The
    /// software does not use the time: <c>MainViewModel</c> measures the press
    /// with <c>Stopwatch</c>, which is monotonic.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct InputEvent
    {
        /// <summary>The size of one record, in bytes.</summary>
        public const int Size = 24;

        /// <summary>The seconds of the time of the event.</summary>
        public readonly long Seconds;

        /// <summary>The microseconds of the time of the event.</summary>
        public readonly long Microseconds;

        /// <summary>The type of the event. 1 is a key.</summary>
        public readonly ushort Type;

        /// <summary>The code of the key.</summary>
        public readonly ushort Code;

        /// <summary>0 is up, 1 is down, and 2 is autorepeat.</summary>
        public readonly int Value;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The two buttons are at {path}.")]
    private static partial void LogDeviceOpen(ILogger logger, string path);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "There is no {path}, thus the two buttons do nothing. Install the device tree overlay and the udev rule. See deploy/recorder-keys-overlay.dts.")]
    private static partial void LogDeviceMissing(ILogger logger, string path);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The device {path} did not open: {reason} The udev rule gives this device to the account of the service.")]
    private static partial void LogDeviceNotOpen(ILogger logger, string path, string reason);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Start came again for {path}. This class opens the device one time only, thus this call does nothing.")]
    private static partial void LogAlreadyStarted(ILogger logger, string path);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The software cannot continue to read the buttons at {path}. They do nothing now.")]
    private static partial void LogReadFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "A subscriber of the buttons gave an error. The subscribers after it did not get this event.")]
    private static partial void LogSubscriberFailed(ILogger logger, Exception exception);
}
