// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0

using GemmaTranslator.Services.PushToTalk;
using Microsoft.Extensions.Logging;

namespace GemmaTranslator.Services.Speakerphone;

/// <remarks>
/// <para>
/// SECURITY CONTROL. The text in this block is deliberately plain English, not
/// the Simplified Technical English the rest of this repository uses. A vague
/// comment on a security control gets deleted by the next person who tidies up.
/// </para>
/// <para>
/// This class opens ONE path, and that path is a symlink udev makes for the
/// speakerphone. Do not change it to a scan of /dev/hidraw, and do not choose
/// the node by parsing report descriptors until you find one that declares an
/// Off-Hook output. That was the first version of this class and it was wrong,
/// for the same reason <see cref="EvdevPushToTalk"/> forbids a scan of
/// /dev/input: the device supplies the descriptor, so it is attacker-controlled
/// data, not proof of identity. Section 4.5 of CLAUDE.md puts a USB socket on
/// the exposed rear wall of the case.
/// </para>
/// <para>
/// What a scan would give anyone with thirty seconds of physical access: plug
/// in a gadget that declares an LED-page Off-Hook output and it wins the node,
/// because nothing else distinguishes it. The green ring on the real
/// speakerphone then never lights again while the microphone keeps recording,
/// so the one privacy signal in the room is silently dead. Worse, the gadget
/// receives one byte at the exact moment each person starts and stops speaking.
/// That carries no audio, and it is a perfect trigger for a recorder sitting in
/// the same gadget.
/// </para>
/// <para>
/// The descriptor is still parsed, but only to find the bit inside the node
/// udev already chose. A firmware revision that reorders the LED usages would
/// otherwise make the software set Mute where it means to set Off-Hook.
/// </para>
/// <para>
/// DECISION, and it has a cost: <see cref="StartCall"/> clears the mute bit.
/// One report carries every indicator, so a write must give a value for all of
/// them, and the owner asked for the behaviour of the reference implementation,
/// which is "off hook and not muted" in one write. The cost is that a person
/// who muted the speakerphone with its own button becomes unmuted when anybody
/// holds a push-to-talk button. Do not describe this as a defect without asking
/// the owner: it is a decision. To reverse it, read the input reports of the
/// device, keep its mute bit, and put only the off-hook bit with it.
/// </para>
/// </remarks>
public sealed partial class HidRawCallIndicator : ICallIndicator
{
    /// <remarks>
    /// The udev rule of <c>deploy/99-gemma-translator.rules</c> makes this
    /// symlink. The number of a hidraw node is not the same after each start,
    /// thus the software must not use one.
    /// </remarks>
    private const string DevicePath = "/dev/appliance-speakerphone";

    private const ushort LedUsagePage = 0x08;
    private const ushort OffHookUsage = 0x17;

    // The descriptor comes from the device. Each of these stops a value of the
    // descriptor from becoming an allocation or an index. The report of a
    // telephony device is 3 bytes; these limits are much larger than that and
    // much smaller than the limits of the kernel.
    private const int MaximumFieldBits = 32;
    private const int MaximumFieldCount = 1024;
    private const int MaximumReportBits = 4096;

    private readonly ILogger<HidRawCallIndicator> _logger;
    private readonly Lock _gate = new();

    private FileStream? _stream;
    private byte[]? _offHook;
    private byte[]? _onHook;

    public HidRawCallIndicator(ILogger<HidRawCallIndicator> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <remarks>
    /// The software opens the device at the start and not at the first push. A
    /// push that opens a device does file work on the thread of the user
    /// interface while a person already speaks.
    /// </remarks>
    public void Start()
    {
        lock (_gate)
        {
            Open();
        }
    }

    public void StartCall() => Write(offHook: true);

    public void EndCall() => Write(offHook: false);

    public void Dispose()
    {
        lock (_gate)
        {
            // A green ring on a machine that stopped says that the microphone
            // is live, and it is not. App.axaml.cs takes SIGTERM for this
            // cause: the appliance has no lifetime that raises Exit.
            if (_stream is not null && _onHook is not null)
            {
                TryWrite(_onHook);
            }

            _stream?.Dispose();
            _stream = null;
        }
    }

    private void Write(bool offHook)
    {
        lock (_gate)
        {
            // A device that went away and came back gets a new node behind the
            // same symlink. Without this the ring stays dark for the life of
            // the process after one failure.
            if (_stream is null)
            {
                Open();
            }

            byte[]? report = offHook ? _offHook : _onHook;

            if (_stream is not null && report is not null)
            {
                TryWrite(report);
            }
        }
    }

    /// <remarks>The caller holds <see cref="_gate"/>.</remarks>
    private void TryWrite(byte[] report)
    {
        try
        {
            _stream!.Write(report);
            _stream.Flush();
        }
#pragma warning disable CA1031 // An indicator must never stop a recording.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogWriteFailed(_logger, exception);
            _stream?.Dispose();
            _stream = null;
        }
    }

    /// <remarks>
    /// CAUTION: this method gives no error to its caller. A lamp that cannot
    /// light must not stop the appliance from hearing a person. The caller
    /// holds <see cref="_gate"/>.
    /// </remarks>
    private void Open()
    {
        try
        {
            if (!File.Exists(DevicePath))
            {
                LogNoIndicator(_logger, DevicePath);
                return;
            }

            // The descriptor is in sysfs under the name of the real node, thus
            // the symlink gives the name and udev gives the trust.
            string node = Path.GetFileName(
                File.ResolveLinkTarget(DevicePath, returnFinalTarget: true)?.FullName
                ?? DevicePath);

            byte[] descriptor = ReadSysfs(
                $"/sys/class/hidraw/{node}/device/report_descriptor");

            if (!TryFindOffHook(descriptor, out byte reportId, out int bit, out int length))
            {
                LogNoOffHookField(_logger, node);
                return;
            }

            // FileAccess.Write, and not ReadWrite. The software writes the
            // state of the call and reads nothing. Read access on a hidraw node
            // gives each input report of the device, which is each button that
            // a person pushes on it.
            _stream = new FileStream(DevicePath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);

            // Each indicator of the device is in this one report. Thus a report
            // of zeros with the off-hook bit only is "off hook, and not muted".
            // See the DECISION in the remark on this class.
            _onHook = new byte[length];
            _onHook[0] = reportId;
            _offHook = (byte[])_onHook.Clone();
            _offHook[1 + (bit / 8)] |= (byte)(1 << (bit % 8));

            LogIndicatorFound(_logger, node, reportId, bit);

            // The ring keeps its state through a restart of the software. Thus
            // a start that does not write leaves a green ring from the process
            // before it.
            TryWrite(_onHook);
        }
#pragma warning disable CA1031 // An indicator must never stop a recording.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            LogCannotOpen(_logger, DevicePath, exception);
            _stream?.Dispose();
            _stream = null;
        }
    }

    /// <remarks>
    /// CAUTION: <c>File.ReadAllBytes</c> does not read a file of sysfs. It asks
    /// for the length first, sysfs gives the length of a page, and the method
    /// then throws <c>EndOfStreamException</c> because the file gives 380 bytes
    /// and it waits for 4096. A copy of the stream asks for no length.
    /// </remarks>
    private static byte[] ReadSysfs(string path)
    {
        using FileStream file = new(path, FileMode.Open, FileAccess.Read);

        // The kernel gives 4096 bytes for the largest report descriptor. The
        // limit also stops a file that gives bytes and does not end.
        byte[] buffer = new byte[4096];
        int total = 0;
        int read;

        while (total < buffer.Length && (read = file.Read(buffer.AsSpan(total))) > 0)
        {
            total += read;
        }

        return buffer[..total];
    }

    /// <summary>
    /// Finds the Off-Hook output bit, and the length in bytes of the report
    /// that holds it, with the report identifier counted.
    /// </summary>
    /// <remarks>
    /// CAUTION: each value here comes from the device. The method gives
    /// <c>false</c> for anything that it cannot read with confidence. It does
    /// not make a value correct.
    /// </remarks>
    private static bool TryFindOffHook(
        byte[] descriptor,
        out byte reportId,
        out int bitOffset,
        out int length)
    {
        const int mainOutput = 0x9;
        const int globalUsagePage = 0x0;
        const int globalReportSize = 0x7;
        const int globalReportId = 0x8;
        const int globalReportCount = 0x9;
        const int globalPush = 0xA;
        const int globalPop = 0xB;
        const int localUsage = 0x0;
        const int localUsageMinimum = 0x1;
        const int localUsageMaximum = 0x2;

        reportId = 0;
        bitOffset = 0;
        length = 0;

        ushort page = 0;
        byte report = 0;
        int size = 0;
        int count = 0;
        List<ushort> usages = [];
        bool hasRange = false;
        Stack<(ushort Page, byte Report, int Size, int Count)> saved = new();
        Dictionary<byte, int> outputBits = [];

        bool found = false;
        int i = 0;

        while (i < descriptor.Length)
        {
            byte prefix = descriptor[i++];

            if (prefix == 0xFE)
            {
                if (i >= descriptor.Length)
                {
                    return false;
                }

                i += 2 + descriptor[i];
                continue;
            }

            int itemSize = prefix & 0x03;

            if (itemSize == 3)
            {
                itemSize = 4;
            }

            int type = (prefix >> 2) & 0x03;
            int tag = (prefix >> 4) & 0x0F;

            if (i + itemSize > descriptor.Length)
            {
                return false;
            }

            uint data = 0;

            for (int b = 0; b < itemSize; b++)
            {
                data |= (uint)descriptor[i + b] << (8 * b);
            }

            i += itemSize;

            switch (type)
            {
                case 0:
                    if (tag == mainOutput)
                    {
                        if (size is < 1 or > MaximumFieldBits
                            || count is < 1 or > MaximumFieldCount)
                        {
                            return false;
                        }

                        outputBits.TryGetValue(report, out int at);

                        // A range gives the usages by their number and not one
                        // for each. This device does not use one, thus the
                        // software refuses it and says so, and it does not put
                        // a bit in a position that it computed incorrectly.
                        if (page == LedUsagePage && hasRange)
                        {
                            return false;
                        }

                        if (page == LedUsagePage && !found)
                        {
                            for (int index = 0; index < count && index < usages.Count; index++)
                            {
                                if (usages[index] != OffHookUsage)
                                {
                                    continue;
                                }

                                found = true;
                                reportId = report;
                                bitOffset = at + (index * size);
                                break;
                            }
                        }

                        at += size * count;

                        if (at > MaximumReportBits)
                        {
                            return false;
                        }

                        outputBits[report] = at;
                    }

                    // A main item of each kind clears the local state.
                    usages.Clear();
                    hasRange = false;
                    break;

                case 1:
                    switch (tag)
                    {
                        case globalUsagePage:
                            page = (ushort)data;
                            break;
                        case globalReportSize:
                            size = data > MaximumFieldBits ? int.MaxValue : (int)data;
                            break;
                        case globalReportId:
                            report = (byte)data;
                            break;
                        case globalReportCount:
                            count = data > MaximumFieldCount ? int.MaxValue : (int)data;
                            break;
                        case globalPush:
                            saved.Push((page, report, size, count));
                            break;
                        case globalPop:
                            if (saved.Count == 0)
                            {
                                return false;
                            }

                            (page, report, size, count) = saved.Pop();
                            break;
                    }

                    break;

                case 2:
                    switch (tag)
                    {
                        case localUsage:
                            usages.Add((ushort)data);
                            break;
                        case localUsageMinimum:
                        case localUsageMaximum:
                            hasRange = true;
                            break;
                    }

                    break;
            }
        }

        if (!found || !outputBits.TryGetValue(reportId, out int bits))
        {
            return false;
        }

        // The bit must be inside the report that holds it. A field of the
        // descriptor that gives a position after the end of its own report
        // would make the write go outside the array.
        if (bits is < 1 or > MaximumReportBits || bitOffset < 0 || bitOffset >= bits)
        {
            return false;
        }

        length = 1 + ((bits + 7) / 8);
        return true;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The ring of the speakerphone is on {node}, in report {reportId} at bit {bit}.")]
    private static partial void LogIndicatorFound(
        ILogger logger,
        string node,
        byte reportId,
        int bit);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "There is no {path}. The appliance operates, and the ring of the speakerphone stays dark.")]
    private static partial void LogNoIndicator(ILogger logger, string path);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The device on {node} declares no Off-Hook output on the LED page as a plain usage item. The appliance operates, and the ring stays dark.")]
    private static partial void LogNoOffHookField(ILogger logger, string node);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The software cannot open {path}. See the udev rule of deploy/99-gemma-translator.rules. The appliance operates, and the ring stays dark.")]
    private static partial void LogCannotOpen(ILogger logger, string path, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The call indicator did not take the report. The ring can be incorrect.")]
    private static partial void LogWriteFailed(ILogger logger, Exception exception);
}
