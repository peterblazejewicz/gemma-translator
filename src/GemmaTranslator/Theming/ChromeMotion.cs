// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0

using System.Globalization;
using Avalonia.Media;
using Avalonia.Media.Transformation;

namespace GemmaTranslator.Theming;

public static class ChromeMotion
{
    // The design gives a part of the height of each element: -140 % for the
    // cluster, -220 % for the pill, 105 % for the dock. That unit is a rule of
    // a browser. Avalonia moves a control in device-independent PIXELS, thus
    // each value below is that part of the design height of its own element.
    private const double ClusterHeight = 105;
    private const double StatusPillHeight = 70;
    private const double DockHeight = 224;

    public static ITransform Rest { get; } = Slide(0);

    public static ITransform TopCluster { get; } = Slide(-1.40 * ClusterHeight);

    public static ITransform StatusPill { get; } = Slide(-2.20 * StatusPillHeight);

    public static ITransform Dock { get; } = Slide(1.05 * DockHeight);

    private static ITransform Slide(double pixels) => TransformOperations.Parse(
        string.Create(CultureInfo.InvariantCulture, $"translateY({Math.Round(pixels)}px)"));
}
