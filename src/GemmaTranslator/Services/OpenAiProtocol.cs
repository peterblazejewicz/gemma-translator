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

using System.Text.Json.Serialization;

namespace GemmaTranslator.Services;

// The part of the OpenAI protocol that this software uses.
//
// The types are internal because they are the shape of the wire and not a
// part of the design of the software. `TranslationResult` is what a caller
// gets.

/// <summary>
/// One message in the conversation with the model.
/// </summary>
/// <param name="Role">Either <c>system</c> or <c>user</c>.</param>
/// <param name="Content">The text of the message.</param>
internal sealed record ChatMessage(
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] string Content);

/// <summary>
/// The body that the software sends to <c>/v1/chat/completions</c>.
/// </summary>
/// <param name="Model">The name of the model, for example <c>gemma4-e2b</c>.</param>
/// <param name="Messages">The system message and then the message of the user.</param>
internal sealed record ChatCompletionRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("messages")] IReadOnlyList<ChatMessage> Messages);

/// <summary>
/// The message that the model sends back.
/// </summary>
/// <param name="Content">The text of the model.</param>
internal sealed record ResponseMessage(
    [property: JsonPropertyName("content")] string? Content);

/// <summary>
/// One answer of the model. The software reads the first one only.
/// </summary>
/// <param name="Message">The message of the model.</param>
internal sealed record ChatChoice(
    [property: JsonPropertyName("message")] ResponseMessage? Message);

/// <summary>
/// The quantity of tokens that the call used.
/// </summary>
/// <param name="TotalTokens">The sum of the input tokens and the output tokens.</param>
internal sealed record ChatUsage(
    [property: JsonPropertyName("total_tokens")] int TotalTokens);

/// <summary>
/// The body that <c>/v1/chat/completions</c> sends back.
/// </summary>
/// <param name="Choices">The answers. Upstream reads the first one.</param>
/// <param name="Usage">The count of the tokens, which can be absent.</param>
internal sealed record ChatCompletionResponse(
    [property: JsonPropertyName("choices")] IReadOnlyList<ChatChoice>? Choices,
    [property: JsonPropertyName("usage")] ChatUsage? Usage);

/// <summary>
/// The object that the system message tells the model to send.
/// </summary>
/// <param name="Translation">The translated text.</param>
/// <remarks>
/// The model puts its answer in JSON, thus the software does not have to
/// remove an introduction such as "Here is the translation:". See
/// <see cref="LiteRtTranslator"/>.
/// </remarks>
internal sealed record TranslationEnvelope(
    [property: JsonPropertyName("translation")] string? Translation);

/// <summary>
/// The JSON that the source generator makes for the types above.
/// </summary>
/// <remarks>
/// <para>
/// Each name comes from a <c>JsonPropertyName</c> attribute above. Do not add
/// a naming policy here. The names are the names of a protocol that is not
/// ours, thus a change to a C# name must not change the message.
/// </para>
/// <para>
/// A model does not always obey the letters of a name, thus the comparison
/// ignores the case. Upstream reads <c>parsed.translation</c> and gets nothing
/// from <c>Translation</c>. This is one defect of upstream that the port does
/// not copy.
/// </para>
/// </remarks>
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ChatCompletionRequest))]
[JsonSerializable(typeof(ChatCompletionResponse))]
[JsonSerializable(typeof(TranslationEnvelope))]
internal sealed partial class OpenAiJsonContext : JsonSerializerContext;
