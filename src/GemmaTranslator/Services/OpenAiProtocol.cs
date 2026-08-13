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

internal sealed record ResponseMessage(
    [property: JsonPropertyName("content")] string? Content);

/// <summary>
/// One answer of the model. The software reads the first one only.
/// </summary>
internal sealed record ChatChoice(
    [property: JsonPropertyName("message")] ResponseMessage? Message);

/// <param name="TotalTokens">The sum of the input tokens and the output tokens.</param>
internal sealed record ChatUsage(
    [property: JsonPropertyName("total_tokens")] int TotalTokens);

internal sealed record ChatCompletionResponse(
    [property: JsonPropertyName("choices")] IReadOnlyList<ChatChoice>? Choices,
    [property: JsonPropertyName("usage")] ChatUsage? Usage);

/// <summary>
/// The object that the system message tells the model to send.
/// </summary>
internal sealed record TranslationEnvelope(
    [property: JsonPropertyName("translation")] string? Translation);

/// <remarks>
/// Each name comes from a <c>JsonPropertyName</c> attribute above. Do not add
/// a naming policy here. The names are the names of a protocol that is not
/// ours, thus a change to a C# name must not change the message.
/// </remarks>
[JsonSerializable(typeof(ChatCompletionRequest))]
[JsonSerializable(typeof(ChatCompletionResponse))]
[JsonSerializable(typeof(TranslationEnvelope))]
internal sealed partial class OpenAiJsonContext : JsonSerializerContext;
