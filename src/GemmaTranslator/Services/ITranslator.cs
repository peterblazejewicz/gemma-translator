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

public interface ITranslator
{
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
