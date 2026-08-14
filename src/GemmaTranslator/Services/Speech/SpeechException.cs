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

/// <remarks>
/// SECURITY CONTROL. Keep the body of an answer out of this message. No caller
/// shows this message today; MainViewModel shows a fixed text and sends the
/// exception to the log. That is why the rule is here and not at the caller: a
/// later change that does show the message must not be the moment somebody
/// discovers that the body of the speech-to-text answer holds the words of a
/// person, and that the body of an error is text the server chose.
/// </remarks>
public sealed class SpeechException : Exception
{
    public SpeechException()
    {
    }

    public SpeechException(string message)
        : base(message)
    {
    }

    public SpeechException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
