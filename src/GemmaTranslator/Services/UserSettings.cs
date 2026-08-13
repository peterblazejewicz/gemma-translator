// Copyright 2026 Google LLC
// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//     http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// This file is part of a fork of google-gemma/gemma-translator and has
// been modified. It replaces the config object of frontend/src/App.jsx and
// the themeColor item of localStorage that App.jsx:40 reads.

using Avalonia.Media;

namespace GemmaTranslator.Services;

/// <summary>
/// What a person selected on the settings screen.
/// </summary>
/// <remarks>
/// <para>
/// These are the settings of a person and not the settings of the operator.
/// The operator gets <c>appsettings.json</c> and the <c>GEMMA_</c> variables of
/// the environment, which a person cannot change: the appliance has no
/// keyboard.
/// </para>
/// <para>
/// Upstream keeps the same set in the <c>config</c> object of
/// <c>App.jsx:37-41</c>, and it keeps <c>themeColor</c> in <c>localStorage</c>.
/// </para>
/// <para>
/// CAUTION: each member gives its value here and not in a constructor of
/// positions. <c>System.Text.Json</c> gives an absent member the value of the
/// type, which is <c>false</c> for a Boolean. With a constructor of positions a
/// file that holds the accent only would make an appliance that is light and
/// quiet, and the journal would say nothing.
/// </para>
/// </remarks>
public sealed record UserSettings
{
    /// <summary>The smallest count of bars that a person can select.</summary>
    public const int MinimumBars = 8;

    /// <summary>The largest count of bars that a person can select.</summary>
    public const int MaximumBars = 64;

    /// <summary>The step between two counts of bars.</summary>
    public const int BarStep = 8;

    /// <summary>
    /// The ink of a swatch that is too bright for white ink.
    /// </summary>
    /// <remarks>It is the ink of the design and not pure black.</remarks>
    private const string InkOnBright = "#1A1A18";

    /// <summary>The ink of the other four swatches.</summary>
    private const string InkOnDark = "#FFFFFF";

    /// <summary>
    /// The accent of a small glyph on a light surface, for a bright swatch.
    /// </summary>
    /// <remarks>
    /// A glyph of 22 pixels in <c>#FFD100</c> on the white card of the light
    /// variant has too little contrast to see. The design gives this darker
    /// yellow at that one position. The dark variant needs no such value,
    /// because the card is dark there.
    /// </remarks>
    private const string DeepOnLight = "#8A6E00";

    /// <summary>
    /// The two swatches that carry dark ink.
    /// </summary>
    /// <remarks>
    /// This is a property of those two colours and not a selection of a style.
    /// White text on <c>#FFD100</c> cannot be read.
    /// </remarks>
    private static readonly string[] Bright = ["#FFD100", "#FFA500"];

    /// <summary>
    /// The 6 accents of the settings screen, in the sequence that it shows.
    /// </summary>
    /// <remarks>
    /// Upstream has 6 values at <c>SettingsOverlay.jsx:32-37</c>: red, white,
    /// yellow <c>#ffeb3b</c>, blue, green, and orange. The design keeps the
    /// count and makes three changes: it removes white and that yellow, which
    /// have too little contrast on a light surface; it adds
    /// <c>#FFD100</c> and the teal <c>#007A73</c>; and it puts the sequence in
    /// a different order.
    /// </remarks>
    public static IReadOnlyList<string> AccentColors { get; } =
    [
        "#FFD100",
        "#FF4444",
        "#2196F3",
        "#4CAF50",
        "#FFA500",
        "#007A73",
    ];

    /// <summary>
    /// The settings of an appliance that nobody changed.
    /// </summary>
    /// <remarks>
    /// CAUTION: upstream starts at orange (<c>App.jsx:40</c>). The design
    /// starts at <c>#FFD100</c>. The value is here and not an index of
    /// <see cref="AccentColors"/>, thus a change of that sequence does not move
    /// the default without a person who intended it.
    /// </remarks>
    public static UserSettings Default { get; } = new();

    /// <summary>One value of <see cref="AccentColors"/>.</summary>
    public string AccentColor { get; init; } = "#FFD100";

    /// <summary><c>true</c> for the dark variant of the surface.</summary>
    /// <remarks>
    /// NEW: this has no upstream equivalent. Upstream has one variant, where
    /// the background takes the colour of the theme and all the ink is black.
    /// The design of the appliance gives a light variant and a dark variant,
    /// and the owner approved that design.
    /// </remarks>
    public bool IsDark { get; init; } = true;

    /// <summary><c>true</c> if the appliance speaks the translation.</summary>
    public bool SpeakTranslations { get; init; } = true;

    /// <summary>The count of the bars, from 8 to 64 in steps of 8.</summary>
    public int VisualizerBars { get; init; } = 16;

    /// <summary>
    /// The ink that a person can read on top of this accent.
    /// </summary>
    public Color Ink => Color.Parse(IsBright(AccentColor) ? InkOnBright : InkOnDark);

    /// <summary>
    /// The accent of a small glyph, which stays legible on a light card.
    /// </summary>
    public Color DeepAccent => IsBright(AccentColor) && !IsDark
        ? Color.Parse(DeepOnLight)
        : Accent;

    /// <summary>This accent as a colour.</summary>
    public Color Accent => Color.Parse(AccentColor);

    /// <summary>
    /// Gives back these settings with each value inside its limits.
    /// </summary>
    /// <remarks>
    /// CAUTION: the file comes from a disk and a person can change it with an
    /// editor. A colour that no swatch holds would make a settings screen where
    /// no swatch has the ring, and it would make <see cref="Accent"/> throw. A
    /// count of 0 bars would make a visualizer that shows nothing. Neither
    /// condition has a message on an appliance with no keyboard.
    /// </remarks>
    /// <returns>Settings that the user interface can use.</returns>
    public UserSettings Sanitized()
    {
        string accent = AccentColors.Contains(AccentColor, StringComparer.OrdinalIgnoreCase)
            ? AccentColor
            : Default.AccentColor;

        // Integer arithmetic on purpose. Math.Round gives the even value at
        // one half, thus 12 and 20 would both give 16.
        int bars = Math.Clamp(VisualizerBars, MinimumBars, MaximumBars) / BarStep * BarStep;

        return this with { AccentColor = accent, VisualizerBars = Math.Max(bars, MinimumBars) };
    }

    private static bool IsBright(string accent) => Array.Exists(
        Bright,
        candidate => string.Equals(candidate, accent, StringComparison.OrdinalIgnoreCase));
}
