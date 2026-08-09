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

namespace GemmaTranslator.Services;

/// <summary>
/// Translates text from one language into a different language.
/// </summary>
/// <remarks>
/// <para>
/// The interface is here because the source of the translation can change. Now
/// it is the LiteRT-LM server, which speaks the OpenAI protocol on port 9379.
/// A C API is in the LiteRT-LM package (<c>c/engine.h</c>), thus a machine with
/// no Python is possible later. That change must be a change of one line in
/// <c>ServiceRegistration.cs</c>, and not a change at each call.
/// </para>
/// <para>
/// See section 5.2 of CLAUDE.md. An interface is for a different platform or a
/// different distribution, not for a fake.
/// </para>
/// </remarks>
public interface ITranslator
{
    /// <summary>
    /// Translates the text that the microphone heard.
    /// </summary>
    /// <param name="text">The text in the language of the person who spoke.</param>
    /// <param name="source">The language of the person who spoke.</param>
    /// <param name="target">The language of the other person.</param>
    /// <param name="cancellationToken">Stops the call.</param>
    /// <returns>The translation, the time, and the quantity of tokens.</returns>
    /// <exception cref="TranslationException">
    /// The server is not available, or it sends an error.
    /// </exception>
    /// <remarks>
    /// The names are <c>source</c> and <c>target</c> and not <c>from</c> and
    /// <c>to</c>. <c>To</c> is a keyword of Visual Basic, and rule CA1716 does
    /// not permit it on an interface.
    /// </remarks>
    Task<TranslationResult> TranslateAsync(
        string text,
        Language source,
        Language target,
        CancellationToken cancellationToken = default);
}
