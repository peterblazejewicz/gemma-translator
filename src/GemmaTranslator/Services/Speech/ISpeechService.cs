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

namespace GemmaTranslator.Services.Speech;

public interface ISpeechService
{
    /// <remarks>
    /// The samples are 16 kHz mono, in the range -1 to 1. This is the form that
    /// <see cref="Audio.Recording.Samples"/> gives.
    /// </remarks>
    /// <returns>
    /// What the person said. The text is empty if the microphone heard no
    /// speech, and that condition is not an error.
    /// </returns>
    /// <exception cref="SpeechException">
    /// The server is not available, or it sends an error.
    /// </exception>
    Task<string> TranscribeAsync(
        ReadOnlyMemory<float> samples,
        Language language,
        CancellationToken cancellationToken = default);

    /// <returns>
    /// The WAV file that the server sent. Give these bytes to the audio
    /// decoder. See <see cref="SpokenAudio"/>: this software does not decode
    /// them, because the decoder is the one component that changes the rate.
    /// </returns>
    /// <exception cref="SpeechException">
    /// The text is empty, the server is not available, or the server sends an
    /// error or a body that is not audio.
    /// </exception>
    Task<SpokenAudio> SynthesizeAsync(
        string text,
        Language language,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes the models of one language before a person needs them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A measurement on the appliance gives 6.0 s for a language that the
    /// server does not hold, and 0.001 s for one that it holds. Thus the first
    /// exchange in a language costs 6 s more than each exchange after it.
    /// </para>
    /// <para>
    /// CAUTION: the server holds one lock for the speech-to-text part, and it
    /// holds that lock while it makes the model. A recording that comes at that
    /// moment waits for the model. Thus the software calls this while the
    /// appliance is idle, and never in front of a push.
    /// </para>
    /// </remarks>
    /// <exception cref="SpeechException">
    /// The server is not available, or it does not know the language.
    /// </exception>
    Task WarmAsync(Language language, CancellationToken cancellationToken = default);
}
