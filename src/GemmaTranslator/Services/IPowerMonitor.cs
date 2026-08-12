// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0

namespace GemmaTranslator.Services;

/// <summary>
/// The cells of the X1201, from the fuel gauge.
/// </summary>
/// <param name="Percent">The state of charge.</param>
/// <param name="Microvolts">The voltage of the cells.</param>
/// <remarks>
/// <para>
/// The two values come from one part, thus they are here together. A machine
/// with no fuel gauge has no <c>CellCharge</c> and not a <c>CellCharge</c> of
/// zero.
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

/// <summary>
/// The condition of the electrical supply of the appliance.
/// </summary>
/// <param name="MainsOnline">
/// <c>true</c> if the X1201 has its electrical supply, or <c>null</c> if this
/// machine gives no such signal.
/// </param>
/// <param name="Cells">The cells, or <c>null</c> if there is no fuel gauge.</param>
/// <remarks>
/// The two members are nullable because a machine with no X1201 must say
/// "there is none". A value of 0 for a charge that nobody measured looks the
/// same in the log as a battery that is empty.
/// </remarks>
public sealed record PowerState(bool? MainsOnline, CellCharge? Cells);

/// <summary>
/// Reads the electrical supply of the appliance.
/// </summary>
/// <remarks>
/// <para>
/// NEW FUNCTION. Upstream has no UPS and no battery. Peter Blazejewicz asked
/// for this signal.
/// </para>
/// <para>
/// This interface gives numbers only. It starts no shutdown and it changes no
/// condition of the machine. A control that stops the machine must not be in
/// the same software as the user interface: the moment that such a control is
/// necessary is the moment that this software can be stopped or occupied.
/// </para>
/// <para>
/// CAUTION: this interface is here for a true difference of the platform, and
/// not for a fake. The Raspberry Pi reads the <c>power_supply</c> class of
/// Linux. The Windows development host has no X1201, thus it says that there
/// is none.
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
    /// Starts to read the electrical supply.
    /// </summary>
    void Start();
}
