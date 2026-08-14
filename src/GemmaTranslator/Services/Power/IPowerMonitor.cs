// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0

namespace GemmaTranslator.Services.Power;

/// <summary>
/// The cells of the X1201, from the fuel gauge.
/// </summary>
/// <remarks>
/// <para>
/// A machine with no fuel gauge has no <c>CellCharge</c> and not a
/// <c>CellCharge</c> of zero.
/// </para>
/// <para>
/// <c>Microvolts</c> is microvolts, because that is the unit of the
/// <c>power_supply</c> class of Linux. The formula of the vendor gives
/// millivolts. A value of 4146250 is 4.146 V.
/// </para>
/// <para>
/// <c>Percent</c> can be more than 100. The fuel gauge computes it in its own
/// hardware and the driver does not limit it. Give a limit to the value when
/// the display shows it, and keep the measured value in the log.
/// </para>
/// </remarks>
public sealed record CellCharge(int Percent, int Microvolts);

/// <param name="MainsOnline">
/// <c>true</c> if the X1201 has its electrical supply, or <c>null</c> if this
/// machine gives no such signal.
/// </param>
/// <param name="Cells">The cells, or <c>null</c> if there is no fuel gauge.</param>
public sealed record PowerState(bool? MainsOnline, CellCharge? Cells);

/// <remarks>
/// <para>
/// The Raspberry Pi reads the <c>power_supply</c> class of Linux. The Windows
/// development host has no X1201, thus it says that there is none.
/// </para>
/// <para>
/// CAUTION: this interface gives numbers. Do not put a shutdown of the machine
/// behind it. The moment that a shutdown is necessary is the moment that this
/// software can be stopped or occupied, thus it must come from a service that
/// does not draw the user interface. See deploy/gemma-battery-guard.sh.
/// </para>
/// </remarks>
public interface IPowerMonitor : IDisposable
{
    /// <summary>
    /// Gets the last condition that the software read.
    /// </summary>
    /// <remarks>
    /// A different thread reads the values, thus this property can change
    /// between two reads of it.
    /// </remarks>
    PowerState Current { get; }

    /// <summary>
    /// Occurs when the mains line or the charge of the cells changes.
    /// </summary>
    /// <remarks>
    /// CAUTION: this event comes on the thread that reads the values, and not
    /// on the thread of the user interface. A listener that writes a property
    /// must go to the correct thread first, or Avalonia throws.
    ///
    /// The event does not come for a change of the voltage. That value is a
    /// measurement and not a condition: it moves at almost each read, and an
    /// event for it would wake the user interface every 5 s for the life of the
    /// appliance.
    /// </remarks>
    event EventHandler<PowerState>? Changed;

    void Start();
}
