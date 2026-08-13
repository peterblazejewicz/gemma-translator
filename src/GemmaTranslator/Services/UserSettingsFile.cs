// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0

namespace GemmaTranslator.Services;

/// <summary>
/// The shape of the file of the settings, where each member can be absent.
/// </summary>
/// <remarks>
/// <para>
/// CAUTION: this type exists because <c>System.Text.Json</c> does not use the
/// value that a member of a record gives. The generated code makes the object
/// and then writes each member from the file, thus a member that the file does
/// not hold becomes the value of its type: <c>false</c> for a Boolean and 0 for
/// a number. A file that holds the accent only would then make an appliance
/// that is light and quiet.
/// </para>
/// <para>
/// Each member here can be absent, and <see cref="ToSettings"/> gives the
/// default for each one that is. This is the true condition of an appliance
/// that has a file already at the moment that a new setting goes in.
/// </para>
/// <para>
/// The type also keeps the three colours of <see cref="UserSettings"/> out of
/// the file. Those come from the accent, thus a file that holds them makes a
/// second source of truth and a larger surface to read.
/// </para>
/// </remarks>
internal sealed record UserSettingsFile
{
    /// <summary>The accent, or <c>null</c> if the file does not hold one.</summary>
    public string? AccentColor { get; init; }

    /// <summary>The variant, or <c>null</c>.</summary>
    public bool? IsDark { get; init; }

    /// <summary>The speech output, or <c>null</c>.</summary>
    public bool? SpeakTranslations { get; init; }

    /// <summary>The count of the bars, or <c>null</c>.</summary>
    public int? VisualizerBars { get; init; }

    /// <summary>
    /// Makes the shape of the file from the settings of a person.
    /// </summary>
    /// <param name="settings">The settings to write.</param>
    /// <returns>The shape that goes in the file.</returns>
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

    /// <summary>
    /// Makes the settings, and gives the default for each member that the file
    /// does not hold.
    /// </summary>
    /// <returns>The settings of a person.</returns>
    public UserSettings ToSettings() => new()
    {
        AccentColor = AccentColor ?? UserSettings.Default.AccentColor,
        IsDark = IsDark ?? UserSettings.Default.IsDark,
        SpeakTranslations = SpeakTranslations ?? UserSettings.Default.SpeakTranslations,
        VisualizerBars = VisualizerBars ?? UserSettings.Default.VisualizerBars,
    };
}
