// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0

using Avalonia.Controls;

namespace GemmaTranslator.Views;

/// <summary>
/// The screen that comes after a quiet interval, and that keeps the charge of
/// the cells while the appliance waits.
/// </summary>
public partial class ScreensaverView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ScreensaverView"/> class.
    /// </summary>
    public ScreensaverView()
    {
        InitializeComponent();
    }
}
