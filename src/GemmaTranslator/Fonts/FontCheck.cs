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
// been modified.

using Avalonia.Media;
using Microsoft.Extensions.Logging;

namespace GemmaTranslator.Fonts;

/// <summary>
/// Examines the fonts at the start and writes the result in the log.
/// </summary>
/// <remarks>
/// <para>
/// The appliance has no console and no keyboard. If the text of one language
/// becomes empty boxes, a person at the device can do nothing and can see no
/// cause. This check puts the cause in the journal at each start.
/// </para>
/// <para>
/// It also gives the one method to make sure of this work on Windows. Windows
/// has a font for each of these languages, thus text on the display is not
/// proof. <c>GlyphTypeface.FamilyName</c> gives the font that Avalonia
/// selected. If it gives the name of our font, the software does not use a
/// font of the operating system, and Raspberry Pi OS Lite gives the same
/// result.
/// </para>
/// </remarks>
public static partial class FontCheck
{
    /// <summary>
    /// One language, its font, and a character that must have a glyph.
    /// </summary>
    private static readonly (string Script, string Family, int Codepoint, string Sample)[] Checks =
    [
        ("Latin", AppFonts.Latin, 0x00F1, "ñ"),
        ("Arabic", AppFonts.Arabic, 0x0645, "م"),
        ("Japanese", AppFonts.Japanese, 0x3042, "あ"),
        ("Chinese", AppFonts.Chinese, 0x4E2D, "中"),
        ("Korean", AppFonts.Korean, 0xD55C, "한"),
    ];

    /// <summary>
    /// Examines each font of the software.
    /// </summary>
    /// <param name="logger">The logger from the container.</param>
    /// <returns>True if each font gave a glyph for its sample character.</returns>
    public static bool Run(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        bool allGood = true;

        foreach ((string script, string family, int codepoint, string sample) in Checks)
        {
            Typeface typeface = new(new FontFamily(family));

            if (!FontManager.Current.TryGetGlyphTypeface(typeface, out GlyphTypeface? glyphs)
                || glyphs is null)
            {
                LogNoFont(logger, script, family);
                allGood = false;
                continue;
            }

            // The name that comes back is the font that Avalonia selected. A
            // different name says that our font did not load and that a font
            // of the operating system took its position.
            string expected = family[(family.IndexOf('#', StringComparison.Ordinal) + 1)..];
            bool correctFont = string.Equals(glyphs.FamilyName, expected, StringComparison.Ordinal);

            // Glyph 0 is .notdef, which the display shows as an empty box.
            bool hasGlyph = glyphs.CharacterToGlyphMap.TryGetGlyph(codepoint, out ushort glyph)
                && glyph != 0;

            if (correctFont && hasGlyph)
            {
                LogFontGood(logger, script, glyphs.FamilyName, sample, glyphs.GlyphCount);
            }
            else
            {
                LogFontBad(logger, script, expected, glyphs.FamilyName, sample, hasGlyph);
                allGood = false;
            }
        }

        if (allGood)
        {
            LogAllGood(logger, Checks.Length);
        }
        else
        {
            LogNotAllGood(logger);
        }

        return allGood;
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Font for {script}: {family} gives a glyph for \"{sample}\" and has {glyphCount} glyphs.")]
    private static partial void LogFontGood(
        ILogger logger,
        string script,
        string family,
        string sample,
        int glyphCount);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Font for {script} is not correct. The software asked for {expected} and got {actual}. A glyph for \"{sample}\": {hasGlyph}.")]
    private static partial void LogFontBad(
        ILogger logger,
        string script,
        string expected,
        string actual,
        string sample,
        bool hasGlyph);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Font for {script} did not load at all. The address is {family}.")]
    private static partial void LogNoFont(ILogger logger, string script, string family);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Each of the {count} fonts of the software loaded correctly.")]
    private static partial void LogAllGood(ILogger logger, int count);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "CAUTION: one font or more did not load. Text of that language becomes empty boxes.")]
    private static partial void LogNotAllGood(ILogger logger);
}
