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
/// The translation did not occur.
/// </summary>
/// <remarks>
/// Upstream throws a plain <c>Error</c> with the status and the body of the
/// response, at <c>api.js:113</c>. The user interface shows
/// "(Translation failed)". This class keeps that behaviour, but it gives the
/// caller one type to catch.
/// </remarks>
public sealed class TranslationException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TranslationException"/> class.
    /// </summary>
    public TranslationException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TranslationException"/> class.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    public TranslationException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TranslationException"/> class.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The first error.</param>
    public TranslationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
