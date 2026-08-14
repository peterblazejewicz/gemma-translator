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

/// <remarks>
/// <c>MainViewModel</c> puts the message of this exception on the display, thus
/// do not put the body of the answer in it: that body can hold the speech of
/// the person.
/// </remarks>
public sealed class TranslationException : Exception
{
    public TranslationException()
    {
    }

    public TranslationException(string message)
        : base(message)
    {
    }

    public TranslationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
