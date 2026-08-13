// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0

using Avalonia.Controls;

namespace GemmaTranslator.Views;

/// <summary>
/// The warning of the very low charge, on the full surface.
/// </summary>
/// <remarks>
/// This screen gives the warning only. deploy/gemma-battery-guard.sh stops the
/// machine, because that operation must occur also if this software is not in
/// operation.
/// </remarks>
public partial class CriticalBatteryView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CriticalBatteryView"/> class.
    /// </summary>
    public CriticalBatteryView()
    {
        InitializeComponent();
    }
}
