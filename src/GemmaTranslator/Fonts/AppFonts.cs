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

namespace GemmaTranslator.Fonts;

public static class AppFonts
{
    public const string Latin = $"{GemmaFontCollection.CollectionUri}#Noto Sans";

    public const string Arabic = $"{GemmaFontCollection.CollectionUri}#Noto Sans Arabic";

    public const string Japanese = $"{GemmaFontCollection.CollectionUri}#Noto Sans JP";

    public const string Chinese = $"{GemmaFontCollection.CollectionUri}#Noto Sans SC";

    public const string Korean = $"{GemmaFontCollection.CollectionUri}#Noto Sans KR";

    public const string Mono = $"{GemmaFontCollection.CollectionUri}#Noto Sans Mono";

    /// <remarks>
    /// A style needs this type. <c>x:Static</c> with the constant gives a
    /// string, and a setter of a style does not convert it.
    /// </remarks>
    public static FontFamily MonoFamily { get; } = new(Mono);

    /// <remarks>
    /// <para>
    /// CAUTION: this method is necessary, and the fallback list is not
    /// sufficient. Chinese and Japanese use the same Han characters, at
    /// U+4E00 to U+9FFF, but the correct shape of some characters is not the
    /// same in the two languages. A fallback list gives one font for one range
    /// of characters, thus it must select one of the two languages and make
    /// the other one incorrect.
    /// </para>
    /// <para>
    /// The software knows the language of each area of the display, because
    /// the person selected it in the lane. Thus it gives the font directly and
    /// the two languages are correct.
    /// </para>
    /// </remarks>
    public static FontFamily For(Language language)
    {
        ArgumentNullException.ThrowIfNull(language);

        return new FontFamily(language.Code switch
        {
            "ar" => Arabic,
            "ja" => Japanese,
            "zh" => Chinese,
            "ko" => Korean,
            _ => Latin,
        });
    }

    /// <remarks>
    /// <para>
    /// <c>DefaultFamilyName</c> is the value that stops the failure at the
    /// start. Avalonia asks the operating system for a default family if this
    /// value is not here, and Raspberry Pi OS Lite can give none.
    /// </para>
    /// <para>
    /// The fallback list is for a character that comes in text of a different
    /// language, for example a name in Arabic in an English sentence. The
    /// documents say that a fallback operates before all other work when the
    /// font manager looks for one character.
    /// </para>
    /// <para>
    /// The Han range goes to the Chinese font. Chinese text is Han only, and
    /// Japanese text is mostly kana with some Han. Thus this selection makes
    /// fewer characters incorrect if the language is not known. See
    /// <see cref="For(Language)"/>, which removes the condition.
    /// </para>
    /// </remarks>
    public static FontManagerOptions MakeOptions() => new()
    {
        DefaultFamilyName = Latin,
        FontFallbacks =
        [
            new FontFallback
            {
                FontFamily = new FontFamily(Arabic),
                UnicodeRange = UnicodeRange.Parse(
                    "U+0600-06FF, U+0750-077F, U+0870-089F, U+08A0-08FF, " +
                    "U+FB50-FDFF, U+FE70-FEFF"),
            },
            new FontFallback
            {
                FontFamily = new FontFamily(Japanese),
                UnicodeRange = UnicodeRange.Parse(
                    "U+3040-309F, U+30A0-30FF, U+31F0-31FF"),
            },
            new FontFallback
            {
                FontFamily = new FontFamily(Korean),
                UnicodeRange = UnicodeRange.Parse(
                    "U+1100-11FF, U+3130-318F, U+A960-A97F, U+AC00-D7AF, " +
                    "U+D7B0-D7FF"),
            },
            new FontFallback
            {
                FontFamily = new FontFamily(Chinese),
                UnicodeRange = UnicodeRange.Parse(
                    "U+2E80-2EFF, U+3000-303F, U+3400-4DBF, U+4E00-9FFF, " +
                    "U+F900-FAFF, U+FF00-FFEF"),
            },
        ],
    };
}
