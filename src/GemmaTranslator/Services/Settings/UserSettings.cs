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

namespace GemmaTranslator.Services.Settings;

/// <remarks>
/// <para>
/// These are the settings of a person. The operator gets
/// <c>appsettings.json</c> and the <c>GEMMA_</c> variables of the environment,
/// which a person cannot change: the appliance has no keyboard.
/// </para>
/// <para>
/// CAUTION: this type is not the shape of the file, and the cause is not
/// obvious. A record with <c>init</c> members and no constructor of positions
/// does NOT keep the value that a member gives here.
/// <c>System.Text.Json</c> makes the object and then writes EACH member from
/// the file, thus a member that the file does not hold becomes the value of its
/// type: <c>false</c> for a Boolean and 0 for a number.
///
/// A measurement with this exact shape, this serializer, and a source
/// generator:
///
///   {"AccentColor":"#FF4444"}  ->  Dark=False  Speak=False  Bars=0
///   {}                         ->  Accent=""   Dark=False   Speak=False  Bars=0
///
/// A record of positions with default values does NOT have this condition; it
/// keeps each default. The shape here is the one that needs the care.
/// </para>
/// <para>
/// <see cref="UserSettingsFile"/> is the shape of the file. Each of its members
/// can be absent, and it gives the default for each one that is. A new setting
/// must go in that type also, or each appliance that has a file already gives
/// the new setting the value of its type.
/// </para>
/// </remarks>
public sealed record UserSettings
{
    public const int MinimumBars = 8;

    public const int MaximumBars = 64;

    public const int BarStep = 8;

    /// <remarks>It is the ink of the design and not pure black.</remarks>
    private const string InkOnBright = "#1A1A18";

    private const string InkOnDark = "#FFFFFF";

    /// <remarks>
    /// A glyph of 22 pixels in <c>#FFD100</c> on the white card of the light
    /// variant has too little contrast to see. The design gives this darker
    /// yellow at that one position. The dark variant needs no such value,
    /// because the card is dark there.
    /// </remarks>
    private const string DeepOnLight = "#8A6E00";

    /// <remarks>White text on <c>#FFD100</c> cannot be read.</remarks>
    private static readonly string[] Bright = ["#FFD100", "#FFA500"];

    /// <summary>
    /// The 6 accents of the settings screen, in the sequence that it shows.
    /// </summary>
    /// <remarks>
    /// The design gives these 6. There is no white and no light yellow: on the
    /// light variant the surface is almost white, and neither can be seen.
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

    /// <remarks>
    /// CAUTION: the value is here and it is not an index of
    /// <see cref="AccentColors"/>. Thus a change of the sequence of the swatches
    /// does not move the default without an intention to move it.
    /// </remarks>
    public static UserSettings Default { get; } = new();

    /// <summary>One value of <see cref="AccentColors"/>.</summary>
    public string AccentColor { get; init; } = "#FFD100";

    /// <summary><c>true</c> for the dark variant of the surface.</summary>
    public bool IsDark { get; init; } = true;

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

    public Color Accent => Color.Parse(AccentColor);

    /// <remarks>
    /// CAUTION: the file comes from a disk and a person can change it with an
    /// editor. A colour that no swatch holds would make a settings screen where
    /// no swatch has the ring, and it would make <see cref="Accent"/> throw. A
    /// count of 0 bars would make a visualizer that shows nothing. Neither
    /// condition has a message on an appliance with no keyboard.
    /// </remarks>
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
