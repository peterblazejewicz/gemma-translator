// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Microsoft.Extensions.Logging;

namespace GemmaTranslator.Services;

/// <summary>
/// The electrical supply of the appliance, from the <c>power_supply</c> class
/// of Linux.
/// </summary>
/// <remarks>
/// <para>
/// Two device tree overlays make these files. <c>recorder-keys</c> puts GPIO6
/// on the <c>gpio-charger</c> driver and gives <c>mains</c>.
/// <c>i2c-sensor,max17040</c> gives the fuel gauge at address 0x36 of bus 1
/// and gives <c>battery</c>. See <c>deploy/README.md</c>.
/// </para>
/// <para>
/// CAUTION: <c>battery/status</c> is not here on purpose. The fuel gauge
/// measures no current, thus that file gives <c>Unknown</c> and it does not
/// change.
/// <c>mains/online</c> is the signal that says if the charge increases.
/// </para>
/// </remarks>
public sealed partial class SysfsPowerMonitor : IPowerMonitor
{
    private const string MainsOnlinePath = "/sys/class/power_supply/mains/online";
    private const string ChargePath = "/sys/class/power_supply/battery/capacity";
    private const string VoltagePath = "/sys/class/power_supply/battery/voltage_now";

    // The mains line is a level and the charge moves slowly, thus 5 s finds
    // each change that a person can act on. This value is not a setting: it
    // changes nothing that a person sees, and no operator has a reason to move
    // it.
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private readonly ILogger<SysfsPowerMonitor> _logger;
    private readonly CancellationTokenSource _stop = new();

    // The read loop is the one user of this set.
    private readonly HashSet<string> _quiet = new(StringComparer.Ordinal);

    private volatile PowerState _current = new(null, null);
    private bool _started;
    private bool _disposed;
    private bool _first = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="SysfsPowerMonitor"/> class.
    /// </summary>
    /// <param name="logger">The logger from the container.</param>
    public SysfsPowerMonitor(ILogger<SysfsPowerMonitor> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc/>
    public PowerState Current => _current;

    /// <inheritdoc/>
    public void Start()
    {
        if (_started)
        {
            LogMonitorAlreadyStarted(_logger);
            return;
        }

        _started = true;

        // Read the token here and not in the task. A read of Token after
        // Dispose throws, and the task can start after Dispose.
        CancellationToken token = _stop.Token;

        // The first read of the fuel gauge is an I2C transaction, and Start
        // operates on the thread of the user interface. Thus the first read
        // goes on a thread of the pool with each read after it.
        _ = Task.Run(() => ReadLoopAsync(token), token);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // CAUTION: Cancel comes before Dispose and the sequence is not free.
        // After Cancel the token is cancelled for all time, thus the loop gets
        // a cancelled result and puts no callback on a source that is gone.
        //
        // CAUTION: the Raspberry Pi does not call this method. App.axaml.cs
        // disposes the container on the exit of the Windows head only. The
        // process of the appliance stops and the system takes the memory back.
        _stop.Cancel();
        _stop.Dispose();
    }

    /// <summary>
    /// Reads one file of the <c>power_supply</c> class.
    /// </summary>
    /// <param name="path">The full path of the file.</param>
    /// <returns>The value, or <c>null</c> if the software cannot read it.</returns>
    private int? Read(string path)
    {
        try
        {
            // CAUTION: open the file for each read. A handle that stays open
            // gives no new value: the second read starts at the end of the
            // file and gives nothing.
            string text = File.ReadAllText(path).Trim();

            _quiet.Remove(path);

            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? value
                : null;
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or DirectoryNotFoundException)
        {
            // The overlay is not installed. The first log line of Poll says so
            // one time, thus this path is quiet.
            return null;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // The file is there and the read did not operate. On this hardware
            // that is an I2C transaction that failed, which is not the same as
            // a machine with no fuel gauge. Write one line for each such
            // condition, and write it again if the read operates and then
            // stops again.
            if (_quiet.Add(path))
            {
                LogSupplyReadFailed(_logger, path, exception.Message);
            }

            return null;
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using PeriodicTimer timer = new(Interval);

            do
            {
                Poll();
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
            // of here is lost and the process continues with values that do
            // not change. This line is the one signal that the reading stopped.
            LogLoopFailed(_logger, exception);
        }
    }

    private void Poll()
    {
        int? online = Read(MainsOnlinePath);
        int? percent = Read(ChargePath);
        int? microvolts = Read(VoltagePath);

        // The two values of the cells come from one driver of one part, thus
        // one of the two alone is not a condition that the hardware makes.
        CellCharge? cells = percent is not null && microvolts is not null
            ? new CellCharge(percent.Value, microvolts.Value)
            : null;

        PowerState state = new(online is null ? null : online != 0, cells);
        PowerState previous = _current;

        _current = state;

        // CAUTION: the voltage is not part of this test. It is a measurement
        // and not a condition: under a load that changes it moves at almost
        // each read, and a test that holds it writes a line each 5 s for the
        // life of the appliance. The mains line and the charge are conditions.
        //
        // The first read always writes, thus a machine with no overlay says so
        // one time and is not silent.
        bool changed = _first
            || state.MainsOnline != previous.MainsOnline
            || state.Cells?.Percent != previous.Cells?.Percent;

        if (!changed)
        {
            return;
        }

        _first = false;

        // The time of this line is the time of the read and not the time of
        // the change. A change comes up to one interval before this.
        LogChanged(
            _logger,
            state.MainsOnline is null ? -1 : state.MainsOnline.Value ? 1 : 0,
            state.Cells?.Percent ?? -1,
            state.Cells?.Microvolts ?? -1);
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The electrical supply: mains {mains} (1 is on and 0 is off), charge {charge} percent, {microvolts} microvolts. A value of -1 says that the machine gives no such signal.")]
    private static partial void LogChanged(
        ILogger logger,
        int mains,
        int charge,
        int microvolts);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The file {path} is there and the software cannot read it: {reason}")]
    private static partial void LogSupplyReadFailed(ILogger logger, string path, string reason);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The software cannot continue to read the electrical supply. The values do not change now.")]
    private static partial void LogLoopFailed(ILogger logger, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Start came again for the electrical supply. This class reads it one time only, thus this call does nothing.")]
    private static partial void LogMonitorAlreadyStarted(ILogger logger);
}
