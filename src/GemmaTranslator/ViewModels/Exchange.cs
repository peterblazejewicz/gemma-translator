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
// been modified. It replaces frontend/src/components/ResponseDrawer.jsx.

using Avalonia.Layout;
using Avalonia.Media;
using GemmaTranslator.Fonts;

namespace GemmaTranslator.ViewModels;

/// <summary>
/// What one person said, and the same words in the language of the other
/// person.
/// </summary>
/// <remarks>
/// <para>
/// This record holds the two texts and each value that the display needs to
/// show them. It has no property that changes, thus
/// <see cref="MainViewModel"/> replaces the whole record and the display gets
/// one notification.
/// </para>
/// <para>
/// CAUTION: the font comes from the language of each text and not from a
/// fallback list. Chinese and Japanese use the same Han characters with a
/// different correct shape, and a fallback can give only one of the two. See
/// <see cref="AppFonts.For(Language)"/>.
/// </para>
/// </remarks>
public sealed record Exchange
{
    /// <summary>
    /// The dimension of the text, from the design.
    /// </summary>
    private const double NormalSize = 42;

    /// <summary>
    /// The dimension of a text that is long.
    /// </summary>
    /// <remarks>
    /// The bubble takes 66 % of the width and it must not scroll. A sentence of
    /// more than 70 characters at 42 pixels goes below the bubble, thus the
    /// design makes it smaller. 70 is the value of the design.
    /// </remarks>
    private const double SmallSize = 34;

    /// <summary>The count of characters that makes the text smaller.</summary>
    private const int LongText = 70;

    /// <summary>The language of the person who spoke.</summary>
    public required Language SourceLanguage { get; init; }

    /// <summary>The language of the other person.</summary>
    public required Language TargetLanguage { get; init; }

    /// <summary>What the person said.</summary>
    public required string SourceText { get; init; }

    /// <summary>The same words in the other language.</summary>
    public required string TargetText { get; init; }

    /// <summary>
    /// <c>true</c> if the person who spoke is the person of lane 2.
    /// </summary>
    /// <remarks>
    /// The bubble of the speaker goes on the side of the lane of that speaker,
    /// and the translation goes on the other side. Thus each person reads the
    /// text that is near to them.
    /// </remarks>
    public required bool SourceIsLane2 { get; init; }

    /// <summary>
    /// <c>true</c> while the text of the speaker is a message and not speech.
    /// </summary>
    /// <remarks>
    /// The software shows "Listening..." in the muted ink while the
    /// speech-to-text part operates.
    /// </remarks>
    public bool IsSourceMuted { get; init; }

    /// <summary>
    /// <c>true</c> while the translation is a message and not the translation.
    /// </summary>
    public bool IsTargetMuted { get; init; }

    /// <summary>
    /// <c>true</c> while the appliance speaks the translation.
    /// </summary>
    public bool IsSpeaking { get; init; }

    /// <summary>
    /// The side that the bubble of the speaker goes to.
    /// </summary>
    /// <remarks>
    /// Each person reads the text that is above their own lane. Thus the words
    /// of the speaker go to the side of that speaker, and the translation goes
    /// to the side of the person who must read it.
    /// </remarks>
    public HorizontalAlignment SourceSide => SourceIsLane2
        ? HorizontalAlignment.Right
        : HorizontalAlignment.Left;

    /// <summary>The side that the bubble of the translation goes to.</summary>
    public HorizontalAlignment TargetSide => SourceIsLane2
        ? HorizontalAlignment.Left
        : HorizontalAlignment.Right;

    /// <summary>The label above the text of the speaker.</summary>
    public string SourceLabel => Label(SourceLanguage, "HEARD");

    /// <summary>The label above the translation.</summary>
    public string TargetLabel => Label(TargetLanguage, "TRANSLATION");

    /// <summary>The font of the text of the speaker.</summary>
    public FontFamily SourceFont => AppFonts.For(SourceLanguage);

    /// <summary>The font of the translation.</summary>
    public FontFamily TargetFont => AppFonts.For(TargetLanguage);

    /// <summary>The direction of the text of the speaker.</summary>
    public FlowDirection SourceFlow => FlowFor(SourceLanguage);

    /// <summary>The direction of the translation.</summary>
    public FlowDirection TargetFlow => FlowFor(TargetLanguage);

    /// <summary>The dimension of the text of the speaker.</summary>
    public double SourceFontSize => SizeFor(SourceText);

    /// <summary>The dimension of the translation.</summary>
    public double TargetFontSize => SizeFor(TargetText);

    /// <summary>The height of one line of the text of the speaker.</summary>
    public double SourceLineHeight => SourceFontSize * LineFactor(SourceLanguage);

    /// <summary>The height of one line of the translation.</summary>
    public double TargetLineHeight => TargetFontSize * LineFactor(TargetLanguage);

    private static string Label(Language language, string what)
        => $"{language.Name.ToUpperInvariant()} · {what}";

    /// <summary>
    /// Gives the direction of the text of one language.
    /// </summary>
    /// <remarks>
    /// Arabic goes from the right to the left. Upstream gives no direction at
    /// all, thus its Arabic starts at the left of the bubble, which is not
    /// correct.
    /// </remarks>
    private static FlowDirection FlowFor(Language language)
        => language.Code == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    private static double SizeFor(string text)
        => text.Length > LongText ? SmallSize : NormalSize;

    /// <summary>
    /// Gives the height of one line, as a multiple of the dimension of the
    /// text.
    /// </summary>
    /// <remarks>
    /// The three values come from the design. Arabic needs the most, because
    /// its marks go above and below the line. Chinese, Japanese, and Korean
    /// need more than Latin, because their characters fill the full height of
    /// the line.
    /// </remarks>
    private static double LineFactor(Language language) => language.Code switch
    {
        "ar" => 1.75,
        "ja" or "zh" or "ko" => 1.65,
        _ => 1.45,
    };
}
