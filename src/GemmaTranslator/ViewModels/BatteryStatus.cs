// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using GemmaTranslator.Services;

namespace GemmaTranslator.ViewModels;

/// <remarks>
/// CAUTION: this record shows a condition and it does not diagnose. Do not add
/// a time that stays, a rate of charge, or a diagnostic of the charger. The
/// owner made that limit, and each one of the three is a measurement that this
/// hardware cannot make correctly.
/// </remarks>
public sealed record BatteryStatus
{
    public const int WarningPercent = 20;

    public const int DangerPercent = 10;

    public const int CriticalPercent = 5;

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

    public bool IsUnknown => Percent is null;

    public bool IsWarning => IsBelow(WarningPercent);

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

    public string Text => Percent is { } percent
        ? string.Create(CultureInfo.InvariantCulture, $"{percent}%")
        : UnknownText;

    public string AboutText => this switch
    {
        { IsUnknown: true } => "State unknown",
        { IsCharging: true, Percent: >= 100 } => "Powered · 100%",
        { IsCharging: true } => $"Charging · {Text}",
        _ => $"On battery · {Text}",
    };

    /// <remarks>
    /// CAUTION: a machine that gives a charge but no mains line counts as a
    /// machine on its cells for the COLOUR of the indicator. A warning while
    /// the appliance charges is a small problem, and no warning while the cells
    /// go empty stops the appliance in the middle of a conversation. The
    /// warning that covers the surface is different: see IsCritical.
    /// </remarks>
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
