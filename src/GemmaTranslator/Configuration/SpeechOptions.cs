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
// modified. It replaces GEMMA_MAX_MODELS and the paths of the models of
// backend/server.py.

using GemmaTranslator.Services.Speech;

namespace GemmaTranslator.Configuration;

/// <summary>
/// The settings of the speech part, which operates in this process.
/// </summary>
/// <remarks>
/// There is no address here. Upstream had an HTTP server on port 3000 and this
/// software calls the library directly.
/// </remarks>
public sealed class SpeechOptions
{
    public const string SectionName = "Speech";

    /// <summary>
    /// The directory that holds <c>libmoonshine.so</c> or
    /// <c>moonshine.dll</c>. With no value the software looks for it.
    /// </summary>
    /// <remarks>
    /// The library comes in a wheel of Python and it stays in the directory of
    /// that package. The venv holds `litert-lm` on the appliance in any case,
    /// thus the file is there. See
    /// <see cref="Services.Speech.Native.MoonshineLocator"/>.
    /// </remarks>
    public string? LibraryDirectory { get; set; }

    /// <summary>
    /// The directory that holds the models. With no value the software takes
    /// the cache of the package.
    /// </summary>
    public string? ModelCacheRoot { get; set; }

    /// <summary>
    /// The count of models that EACH cache holds. It replaces
    /// <c>GEMMA_MAX_MODELS</c>.
    /// </summary>
    /// <remarks>
    /// A value of 6 makes twelve models, six that hear and six that speak. A
    /// model costs about 800 MB, and a measurement of twelve on the appliance
    /// gives 5214 MB. Two lanes fill a value of 2 with no free space left: a
    /// third language then evicts the first, and its next use waits 2.7 s to
    /// 8.1 s for the load. At 6 a change of language costs 0.00 s.
    /// <para>
    /// CAUTION: this value decides how much of the speech of a person the
    /// appliance holds, and not the memory only. A transcriber of the library
    /// keeps the audio and the words of the last thing said in its language,
    /// and the library exports no way to free a transcript on its own, thus
    /// that copy goes only when its transcriber goes. The software has six
    /// languages, so at a value of 6 a seventh key never comes and nothing is
    /// ever evicted: the appliance then holds one utterance of each language
    /// until it stops. The user chose this value with that cost known.
    /// </para>
    /// </remarks>
    public int MaxModels { get; set; } = SpeechEngineCache.DefaultCapacity;

    /// <summary>
    /// How long the software waits for one piece of the sound.
    /// </summary>
    /// <remarks>
    /// This was the timeout of the HTTP client, and the wait it controls did
    /// not go away with the server: <c>MainViewModel</c> speaks piece N while
    /// it makes piece N+1, and it must give up if a piece does not come.
    /// <para>
    /// A measurement on the appliance gives 0.5 s to 1.6 s for the
    /// speech-to-text part and 1 s to 5.5 s for the text-to-speech part, and
    /// about 6 s more for the first call of a language. CAUTION: this time is
    /// also how long the appliance is dead if the library stops in the middle
    /// of a call, thus the value is four times the slowest measurement and not
    /// twenty times it.
    /// </para>
    /// </remarks>
    public int TimeoutSeconds { get; set; } = 30;

    /// <returns>The directory of the models.</returns>
    public string ResolveModelCacheRoot() =>
        string.IsNullOrWhiteSpace(ModelCacheRoot)
            ? SpeechModels.DefaultCacheRoot()
            : Path.GetFullPath(ModelCacheRoot.Trim());
}
