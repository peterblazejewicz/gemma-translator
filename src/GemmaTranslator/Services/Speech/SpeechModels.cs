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
// This file is part of a fork of google-gemma/gemma-translator and has been
// modified. It replaces SUPPORTED_STT_LANGS, TTS_LANG_MAP and TTS_VOICE_MAP of
// backend/server.py lines 45, 69 and 79, and the rows of MODEL_INFO of the
// moonshine_voice package that those six languages use.

namespace GemmaTranslator.Services.Speech;

/// <summary>The values of <c>moonshine_model_arch_t</c>.</summary>
internal enum ModelArchitecture : uint
{
    Tiny = 0,
    Base = 1,
    TinyStreaming = 2,
    BaseStreaming = 3,
    SmallStreaming = 4,
    MediumStreaming = 5,
}

/// <remarks>
/// <c>TtsLanguage</c> is the tag that the C API takes, with a low line. A
/// <c>Voice</c> of null lets the library take the voice of the language.
/// </remarks>
internal sealed record SpeechModel(
    string ModelDirectory,
    ModelArchitecture Architecture,
    string TtsLanguage,
    string? Voice);

/// <remarks>
/// CAUTION: upstream keeps this knowledge in four tables that a change must
/// touch together, and it falls back to English with no message when one of
/// them does not have the language. There is one table here for that reason.
/// </remarks>
internal static class SpeechModels
{
    // MEASURED, in the same cache as the paths below. The assets of the
    // text-to-speech part are in this one directory, the six languages share
    // it, and it goes to the library as the g2p_root option.
    public const string TtsAssetDirectory = "tts";

    // MEASURED. get_model_for_language of the package gives each path and each
    // architecture, read on the appliance where all six are on the disk.
    //
    // Two traps: English ends at "quantized" and each other language repeats
    // its name below it, because the path is the local copy of the download URL
    // and those URLs are not the same shape. Korean is TINY and its directory
    // is "tiny-ko", and the package calls that model "base-ko" in its own
    // registry, a name that is in neither the path nor the architecture.
    private static readonly Dictionary<string, SpeechModel> Models = new(StringComparer.Ordinal)
    {
        ["ar"] = new("model/base-ar/quantized/base-ar", ModelArchitecture.Base, "ar_msa", null),
        ["en"] = new(
            "model/medium-streaming-en/quantized",
            ModelArchitecture.MediumStreaming,
            "en_us",
            null),
        ["es"] = new("model/base-es/quantized/base-es", ModelArchitecture.Base, "es_es", null),
        ["ja"] = new("model/base-ja/quantized/base-ja", ModelArchitecture.Base, "ja_jp", null),

        // The soft, gentle female Mandarin of upstream. Each other language
        // takes the voice that the library gives it.
        ["zh"] = new(
            "model/base-zh/quantized/base-zh",
            ModelArchitecture.Base,
            "zh_hans",
            "kokoro_zf_xiaoxiao"),

        ["ko"] = new("model/tiny-ko/quantized/tiny-ko", ModelArchitecture.Tiny, "ko_kr", null),
    };

    /// <remarks>
    /// CAUTION: this method throws and upstream falls back to English. Each
    /// caller here gets its language from <see cref="Languages.All"/>, thus a
    /// code that is not in this table is a defect of the software and not the
    /// input of a person.
    /// </remarks>
    public static SpeechModel For(Language language)
    {
        ArgumentNullException.ThrowIfNull(language);

        return Models.TryGetValue(language.Code, out SpeechModel? model)
            ? model
            : throw new KeyNotFoundException(
                $"There is no speech model for the language {language.Code}.");
    }

    /// <remarks>
    /// The package uses platformdirs, thus the location is not the same on the
    /// two targets: <c>%LOCALAPPDATA%\moonshine_voice\moonshine_voice\Cache</c>
    /// on Windows and <c>~/.cache/moonshine_voice</c> on Linux. Both were read
    /// from the package itself.
    /// </remarks>
    public static string DefaultCacheRoot()
    {
        string root = OperatingSystem.IsWindows()
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "moonshine_voice",
                "moonshine_voice",
                "Cache")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache",
                "moonshine_voice");

        return Path.Combine(root, "download.moonshine.ai");
    }
}
