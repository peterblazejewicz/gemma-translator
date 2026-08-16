// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0

using Avalonia.Media;

namespace GemmaTranslator.Services.Branding;

/// <summary>
/// The mark that the warm-up screen and the screensaver show.
/// </summary>
public interface IBrandMark
{
    /// <summary>
    /// The mark, or null when no file gave one. A null value is not a fault:
    /// the two screens then show no mark and the appliance operates.
    /// </summary>
    IImage? Image { get; }
}
