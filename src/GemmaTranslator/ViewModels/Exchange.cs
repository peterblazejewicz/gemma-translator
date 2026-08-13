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

/// <remarks>
/// <para>
/// CAUTION: the font comes from the language of each text and not from a
/// fallback list. Chinese and Japanese use the same Han characters with a
/// different correct shape, and a fallback can give only one of the two. See
/// <see cref="AppFonts.For(Language)"/>.
/// </para>
/// </remarks>
public sealed record Exchange
{
    private const double NormalSize = 42;

    /// <remarks>
    /// The bubble takes 66 % of the width and it must not scroll. A sentence of
    /// more than 70 characters at 42 pixels goes below the bubble, thus the
    /// design makes it smaller. 70 is the value of the design.
    /// </remarks>
    private const double SmallSize = 34;

    private const int LongText = 70;

    public required Language SourceLanguage { get; init; }

    public required Language TargetLanguage { get; init; }

    public required string SourceText { get; init; }

    public required string TargetText { get; init; }

    public required bool SourceIsLane2 { get; init; }

    public bool IsSourceMuted { get; init; }

    public bool IsTargetMuted { get; init; }

    public bool IsSpeaking { get; init; }

    public HorizontalAlignment SourceSide => SourceIsLane2
        ? HorizontalAlignment.Right
        : HorizontalAlignment.Left;

    public HorizontalAlignment TargetSide => SourceIsLane2
        ? HorizontalAlignment.Left
        : HorizontalAlignment.Right;

    public string SourceLabel => Label(SourceLanguage, "HEARD");

    public string TargetLabel => Label(TargetLanguage, "TRANSLATION");

    public FontFamily SourceFont => AppFonts.For(SourceLanguage);

    public FontFamily TargetFont => AppFonts.For(TargetLanguage);

    public FlowDirection SourceFlow => FlowFor(SourceLanguage);

    public FlowDirection TargetFlow => FlowFor(TargetLanguage);

    public double SourceFontSize => SizeFor(SourceText);

    public double TargetFontSize => SizeFor(TargetText);

    public double SourceLineHeight => SourceFontSize * LineFactor(SourceLanguage);

    public double TargetLineHeight => TargetFontSize * LineFactor(TargetLanguage);

    private static string Label(Language language, string what)
        => $"{language.Name.ToUpperInvariant()} · {what}";

    private static FlowDirection FlowFor(Language language)
        => language.Code == "ar" ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    private static double SizeFor(string text)
        => text.Length > LongText ? SmallSize : NormalSize;

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
