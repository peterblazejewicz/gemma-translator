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
// modified. It replaces the system message of TranslatorApp.jsx line 225 and
// the answer parser of api.js lines 126 to 144.

using System.Globalization;
using System.Text.Json;

namespace GemmaTranslator.Services.Translation;

/// <remarks>
/// The two methods are one pair. The message tells the model what to send, and
/// the reader accepts what the model sends. A change to one without the other
/// gives an empty display.
/// </remarks>
internal static class TranslationPrompt
{
    /// <remarks>
    /// CAUTION: this message asks for the bare translation, and upstream asks
    /// for a JSON object at TranslatorApp.jsx:225. The envelope costs 10 tokens
    /// of punctuation on EACH answer, and the appliance decodes at 5.18 tokens
    /// each second. A measurement gives 4.96 s with the envelope and 3.03 s
    /// without it, for the same sentence.
    /// </remarks>
    public static string Make(Language source, Language target)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"Translate the text from {source.Name} into {target.Name}. Answer with only the translation.");

    /// <summary>
    /// The words of the answer, with an envelope taken off it if the model sent
    /// one.
    /// </summary>
    /// <remarks>
    /// A model that wraps the answer is not an error, thus this method has no
    /// value for "did not obey". It gives the bare text back, and that is what
    /// the message above asks for.
    /// </remarks>
    public static string Read(string modelText)
    {
        if (string.IsNullOrWhiteSpace(modelText))
        {
            return string.Empty;
        }

        string text = modelText.Trim();

        // A model that still sends the envelope of upstream, or that puts the
        // object in a block of Markdown. The span from the first brace to the
        // last one covers both.
        int start = text.IndexOf('{', StringComparison.Ordinal);
        int end = text.LastIndexOf('}');

        if (start < 0 || end <= start)
        {
            return text;
        }

        try
        {
            TranslationEnvelope? envelope = JsonSerializer.Deserialize(
                text[start..(end + 1)],
                OpenAiJsonContext.Default.TranslationEnvelope);

            return string.IsNullOrWhiteSpace(envelope?.Translation)
                ? text
                : envelope.Translation;
        }
        catch (JsonException)
        {
            // The answer holds a brace and is not an envelope. A sentence can
            // hold one, thus this is not an error and the text goes through.
            return text;
        }
    }
}
