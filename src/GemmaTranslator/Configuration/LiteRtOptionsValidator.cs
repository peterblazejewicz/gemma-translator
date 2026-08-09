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

using Microsoft.Extensions.Options;

namespace GemmaTranslator.Configuration;

/// <summary>
/// Examines the <see cref="LiteRtOptions"/> values.
/// </summary>
/// <remarks>
/// The appliance has no keyboard and no settings screen. Thus an incorrect
/// value in a file must give a clear message at the start, and not a strange
/// failure at the first translation.
/// </remarks>
public sealed class LiteRtOptionsValidator : IValidateOptions<LiteRtOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, LiteRtOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        string baseUrl = options.GetBaseUrl();

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out Uri? uri))
        {
            failures.Add(
                $"{LiteRtOptions.SectionName}:{nameof(LiteRtOptions.EndpointUrl)} " +
                $"is not a full address. The value is \"{options.EndpointUrl}\".");
        }
        else if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            failures.Add(
                $"{LiteRtOptions.SectionName}:{nameof(LiteRtOptions.EndpointUrl)} " +
                $"must start with http or https. The value is \"{options.EndpointUrl}\".");
        }

        if (string.IsNullOrWhiteSpace(options.ModelName))
        {
            failures.Add(
                $"{LiteRtOptions.SectionName}:{nameof(LiteRtOptions.ModelName)} " +
                "is empty. Give the name of the model, for example \"gemma4-e2b\".");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
