// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using GemmaTranslator.Services;

namespace GemmaTranslator.ViewModels;

/// <summary>
/// The charge of the cells, as the display shows it.
/// </summary>
/// <remarks>
/// <para>
/// NEW FUNCTION. Upstream has no cells and no UPS. The owner approved this
/// addition, and its limits are narrow: it shows a condition and it does not
/// diagnose. There is no estimate of the time that stays, no rate of charge,
/// and no diagnostic of the charger.
/// </para>
/// <para>
/// This record makes the numbers of <see cref="PowerState"/> into the values
/// that the display needs. It starts no shutdown: a control that stops the
/// machine is in a different service. See <see cref="IPowerMonitor"/>.
/// </para>
/// </remarks>
public sealed record BatteryStatus
{
    /// <summary>The charge at which the indicator gives a warning.</summary>
    public const int WarningPercent = 20;

    /// <summary>The charge at which the indicator gives a danger.</summary>
    public const int DangerPercent = 10;

    /// <summary>The charge that puts a warning on the full surface.</summary>
    public const int CriticalPercent = 5;

    /// <summary>The text that the display shows for a charge that nobody read.</summary>
    private const string UnknownText = "—";

    /// <summary>
    /// The charge from 0 to 100, or <c>null</c> if there is no fuel gauge.
    /// </summary>
    /// <remarks>
    /// The fuel gauge computes the charge in its own hardware and it can give
    /// more than 100. This value has a limit of 100, and the log keeps the
    /// value that the gauge gave.
    /// </remarks>
    public int? Percent { get; private init; }

    /// <summary>
    /// <c>true</c> if the appliance has its electrical supply.
    /// </summary>
    public bool IsCharging => MainsOnline == true;

    /// <summary>
    /// The mains line: <c>true</c>, <c>false</c>, or <c>null</c> for a line
    /// that the software cannot read or that is not stable.
    /// </summary>
    /// <remarks>
    /// CAUTION: the three values are not two. SysfsPowerMonitor gives
    /// <c>null</c> while the line changes many times each second, which is what
    /// occurs when the supply cannot give the current that the X1201 and the
    /// Raspberry Pi use together. That is the condition of a person who
    /// connects a small charger to an appliance with empty cells.
    /// </remarks>
    public bool? MainsOnline { get; private init; }

    /// <summary>
    /// <c>true</c> if there is no fuel gauge, thus no charge to show.
    /// </summary>
    /// <remarks>
    /// A charge that nobody read must not look like 0 % and must not look like
    /// a full battery. The glyph shows a question mark and no fill.
    /// </remarks>
    public bool IsUnknown => Percent is null;

    /// <summary>
    /// <c>true</c> when the charge is low and the appliance is on its cells.
    /// </summary>
    public bool IsWarning => IsBelow(WarningPercent);

    /// <summary>
    /// <c>true</c> when the charge is very low and the appliance is on its
    /// cells.
    /// </summary>
    public bool IsDanger => IsBelow(DangerPercent);

    /// <summary>
    /// <c>true</c> when the display must show the warning on the full surface.
    /// </summary>
    /// <remarks>
    /// CAUTION: this one needs <c>false</c> and not "not true". The warning
    /// covers the surface and nothing below it takes a touch. A mains line
    /// that is not stable would else show "Connect power now" on a panel that
    /// answers nothing, to a person who already connected the supply.
    /// </remarks>
    public bool IsCritical => MainsOnline == false
        && Percent is { } percent
        && percent <= CriticalPercent;

    /// <summary>
    /// The charge for the indicator, for example <c>64%</c>.
    /// </summary>
    public string Text => Percent is { } percent
        ? string.Create(CultureInfo.InvariantCulture, $"{percent}%")
        : UnknownText;

    /// <summary>
    /// The line of the ABOUT panel of the settings screen.
    /// </summary>
    public string AboutText => this switch
    {
        { IsUnknown: true } => "State unknown",
        { IsCharging: true, Percent: >= 100 } => "Powered · 100%",
        { IsCharging: true } => $"Charging · {Text}",
        _ => $"On battery · {Text}",
    };

    /// <summary>
    /// Makes the condition that the display shows from the condition that the
    /// hardware gave.
    /// </summary>
    /// <remarks>
    /// CAUTION: a machine that gives a charge but no mains line counts as a
    /// machine on its cells for the COLOUR of the indicator. A warning while
    /// the appliance charges is a small problem, and no warning while the cells
    /// go empty stops the appliance in the middle of a conversation. The
    /// warning that covers the surface is different: see IsCritical.
    /// </remarks>
    /// <param name="state">The condition that <see cref="IPowerMonitor"/> read.</param>
    /// <returns>The condition for the display.</returns>
    public static BatteryStatus From(PowerState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new BatteryStatus
        {
            Percent = state.Cells is { } cells ? Math.Clamp(cells.Percent, 0, 100) : null,
            MainsOnline = state.MainsOnline,
        };
    }

    private bool IsBelow(int limit) => !IsCharging && Percent is { } percent && percent <= limit;
}
