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

namespace GemmaTranslator;

/// <param name="Code">
/// The code of the user interface, for example <c>ja</c>. The speech-to-text
/// part and the translation part use this code.
/// </param>
/// <param name="Name">The name that the display shows.</param>
/// <param name="TtsLanguage">
/// The Moonshine language code for the text-to-speech part, for example
/// <c>ja-jp</c>. It is not the same as <paramref name="Code"/>.
/// </param>
/// <param name="TtsVoice">
/// The Moonshine voice, or <c>null</c> to use the default voice of the
/// language.
/// </param>
public sealed record Language(
    string Code,
    string Name,
    string TtsLanguage,
    string? TtsVoice = null);

public static class Languages
{
    /// <summary>
    /// Each language, in the sequence that the language selector shows.
    /// </summary>
    public static IReadOnlyList<Language> All { get; } =
    [
        new Language("ar", "Arabic", "ar-msa"),
        new Language("en", "English", "en-us"),
        new Language("es", "Spanish", "es-es"),
        new Language("ja", "Japanese", "ja-jp"),

        // Upstream gives Chinese a voice that is not the default one.
        new Language("zh", "Chinese", "zh-hans", "kokoro_zf_xiaoxiao"),

        new Language("ko", "Korean", "ko-kr"),
    ];

    public static Language Default { get; } = All.Single(x => x.Code == "en");

    /// <returns>The position of this language in <see cref="All"/>.</returns>
    public static int IndexOf(Language language)
    {
        ArgumentNullException.ThrowIfNull(language);

        for (int index = 0; index < All.Count; index++)
        {
            if (All[index].Code == language.Code)
            {
                return index;
            }
        }

        // A language always comes from All, thus this line is for a value that
        // no code makes. It gives the first position, and the drum then shows a
        // language and not an empty window.
        return 0;
    }

    public static Language FromCode(string? code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return Default;
        }

        foreach (Language language in All)
        {
            if (string.Equals(language.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                return language;
            }
        }

        return Default;
    }
}
