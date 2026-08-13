// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0

namespace GemmaTranslator.Services;

/// <summary>
/// The shape of the file of the settings, where each member can be absent.
/// </summary>
/// <remarks>
/// <para>
/// CAUTION: this type exists because a record with <c>init</c> members does not
/// keep the value that a member gives, when <c>System.Text.Json</c> reads it.
/// A member that the file does not hold becomes the value of its type. A file
/// that holds the accent only would then make an appliance that is light and
/// quiet. See the measurement in <see cref="UserSettings"/>.
/// </para>
/// </remarks>
internal sealed record UserSettingsFile
{
    public string? AccentColor { get; init; }

    public bool? IsDark { get; init; }

    public bool? SpeakTranslations { get; init; }

    public int? VisualizerBars { get; init; }

    public static UserSettingsFile From(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        return new UserSettingsFile
        {
            AccentColor = settings.AccentColor,
            IsDark = settings.IsDark,
            SpeakTranslations = settings.SpeakTranslations,
            VisualizerBars = settings.VisualizerBars,
        };
    }

    public UserSettings ToSettings() => new()
    {
        AccentColor = AccentColor ?? UserSettings.Default.AccentColor,
        IsDark = IsDark ?? UserSettings.Default.IsDark,
        SpeakTranslations = SpeakTranslations ?? UserSettings.Default.SpeakTranslations,
        VisualizerBars = VisualizerBars ?? UserSettings.Default.VisualizerBars,
    };
}
