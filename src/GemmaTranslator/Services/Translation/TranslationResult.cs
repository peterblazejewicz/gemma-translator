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

namespace GemmaTranslator.Services.Translation;

/// <param name="Translation">The text in the language of the other person.</param>
/// <param name="Duration">The time that the call to the server took.</param>
/// <param name="TotalTokens">
/// The quantity of tokens that the model used, or 0 if the server sends no
/// count. `litert-lm serve` sends no <c>usage</c> object, thus it is 0 today.
/// </param>
public sealed record TranslationResult(
    string Translation,
    TimeSpan Duration,
    int TotalTokens);
