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

/// <summary>
/// One language that the software can hear, translate, and speak.
/// </summary>
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

/// <summary>
/// The set of languages, in one location.
/// </summary>
/// <remarks>
/// <para>
/// Upstream keeps this set in four locations: <c>AVAILABLE_LANGUAGES</c> in
/// <c>TranslatorApp.jsx</c>, and <c>SUPPORTED_STT_LANGS</c>,
/// <c>TTS_LANG_MAP</c>, and <c>TTS_VOICE_MAP</c> in <c>server.py</c>. If you
/// change one location and miss the others, upstream falls back to English and
/// gives no error.
/// </para>
/// <para>
/// This class is the one location. To add a language, add one item to
/// <see cref="All"/>.
/// </para>
/// </remarks>
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

    /// <summary>
    /// English. The software uses this language if a code is not known.
    /// </summary>
    public static Language Default { get; } = All.Single(x => x.Code == "en");

    /// <summary>
    /// Finds the language with this code.
    /// </summary>
    /// <param name="code">A code of the user interface, for example <c>ko</c>.</param>
    /// <returns>
    /// The language, or <see cref="Default"/> if the code is not known.
    /// </returns>
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
