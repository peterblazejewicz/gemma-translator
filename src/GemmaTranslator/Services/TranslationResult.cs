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
/// What one translation gives back.
/// </summary>
/// <param name="Translation">The text in the language of the other person.</param>
/// <param name="Duration">The time that the call to the server took.</param>
/// <param name="TotalTokens">
/// The quantity of tokens that the model used, or <c>null</c> if the server
/// sends no count.
/// </param>
/// <remarks>
/// <c>null</c> and 0 are not the same. `litert-lm serve` sends no
/// <c>usage</c> object, thus the value is always <c>null</c> today. A server
/// that sends 0 said 0. Upstream cannot see this difference, because
/// <c>api.js:149</c> makes each absent count a 0.
/// </remarks>
public sealed record TranslationResult(
    string Translation,
    TimeSpan Duration,
    int? TotalTokens);
