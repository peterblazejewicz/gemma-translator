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

namespace GemmaTranslator.Services;

/// <summary>
/// Tells the model to answer with one JSON object, and reads that answer.
/// </summary>
/// <remarks>
/// The two methods are one pair. The message tells the model what to send, and
/// the reader accepts what the model sends. A change to one without the other
/// gives an empty display. They are here, and not in
/// <see cref="LiteRtTranslator"/>, because they are a property of the model and
/// not of the connection. A translator that uses the C API needs this same
/// pair.
/// </remarks>
internal static class JsonEnvelopePrompt
{
    /// <summary>
    /// Makes the system message for one direction of translation.
    /// </summary>
    /// <param name="source">The language of the person who spoke.</param>
    /// <param name="target">The language of the other person.</param>
    /// <returns>The text of the system message.</returns>
    // Two dollar signs make the interpolation {{ }}, thus a single brace is a
    // literal brace. The message shows JSON to the model.
    public static string Make(Language source, Language target)
        => string.Create(
            CultureInfo.InvariantCulture,
            $$"""
              You are a high-performance translator. Your task is to translate text from {{source.Name}} into {{target.Name}}.
              You MUST format your response as a valid JSON object matching this structure:
              {
                "translation": "High-quality, natural translation into {{target.Name}}"
              }
              Do NOT return anything else except this JSON object. No Markdown block wraps (no ```json), no introductory text, no conversational text. Start directly with "{" and end directly with "}".
              """);

    /// <summary>
    /// Reads the translation out of the text that the model sent.
    /// </summary>
    /// <remarks>
    /// The method takes the text between the first brace and the last brace.
    /// Thus a Markdown fence, an introduction, and a text after the object all
    /// go away with one rule. Upstream uses three text tests
    /// (<c>api.js:128-137</c>), and those tests fail if the model writes
    /// <c>```JSON</c> with capital letters.
    /// </remarks>
    /// <param name="modelText">The text that the model sent.</param>
    /// <param name="translation">The translation, if the method gives true.</param>
    /// <returns>
    /// True if the model obeyed the format. False if the caller must show
    /// <paramref name="modelText"/> and write a line in the log.
    /// </returns>
    public static bool TryRead(string modelText, out string translation)
    {
        translation = string.Empty;

        if (string.IsNullOrWhiteSpace(modelText))
        {
            return false;
        }

        int start = modelText.IndexOf('{', StringComparison.Ordinal);
        int end = modelText.LastIndexOf('}');

        if (start < 0 || end <= start)
        {
            return false;
        }

        try
        {
            TranslationEnvelope? envelope = JsonSerializer.Deserialize(
                modelText[start..(end + 1)],
                OpenAiJsonContext.Default.TranslationEnvelope);

            if (string.IsNullOrWhiteSpace(envelope?.Translation))
            {
                return false;
            }

            translation = envelope.Translation;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
