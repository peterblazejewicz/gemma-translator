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

namespace GemmaTranslator.Configuration;

/// <summary>
/// The settings of the LiteRT-LM server, which does the translation.
/// </summary>
/// <remarks>
/// These values are the three text fields of <c>SettingsOverlay.jsx</c>. The
/// appliance has no keyboard, thus they come from <c>appsettings.json</c> or
/// from the environment.
/// </remarks>
public sealed class LiteRtOptions
{
    /// <summary>
    /// The name of the section in <c>appsettings.json</c>.
    /// </summary>
    public const string SectionName = "LiteRt";

    /// <summary>
    /// The address that upstream uses if the field is empty.
    /// </summary>
    public const string DefaultEndpointUrl = "http://localhost:9379/v1";

    /// <summary>
    /// The address of the LiteRT-LM server, which speaks the OpenAI protocol.
    /// </summary>
    public string EndpointUrl { get; set; } = DefaultEndpointUrl;

    /// <summary>
    /// The name of the model, for example <c>gemma4-e2b</c>.
    /// </summary>
    public string ModelName { get; set; } = "gemma4-e2b";

    /// <summary>
    /// The key for the endpoint, or an empty text if the endpoint needs none.
    /// </summary>
    /// <remarks>
    /// CAUTION: this value is a secret. It must not go in
    /// <c>appsettings.json</c>, because the repository holds that file. Give it
    /// in <c>GEMMA_LiteRt__ApiKey</c>. Do not write it in the log.
    /// </remarks>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The maximum time for one translation, in seconds.
    /// </summary>
    /// <remarks>
    /// A Raspberry Pi 5 is slow, and the correct value is not known before the
    /// hardware is here. Thus the value is a setting and not a constant in the
    /// code.
    /// </remarks>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Gets the address in the form that the API calls need.
    /// </summary>
    /// <remarks>
    /// This is the C# form of <c>getNormalizedBaseUrl</c> in <c>api.js</c>. The
    /// address ends with a slash, thus <see cref="Uri"/> can add a relative
    /// part to it correctly.
    /// </remarks>
    /// <returns>The address, which ends with <c>/v1/</c>.</returns>
    /// <exception cref="UriFormatException">The address is not a full address.</exception>
    public Uri GetBaseUri() => new(GetBaseUrl() + "/", UriKind.Absolute);

    /// <summary>
    /// Gets the address as text, with no slash at the end.
    /// </summary>
    /// <returns>The address, which ends with <c>/v1</c>.</returns>
    public string GetBaseUrl()
    {
        string url = EndpointUrl?.Trim() ?? string.Empty;

        if (url.Length == 0)
        {
            return DefaultEndpointUrl;
        }

        url = url.TrimEnd('/');

        if (!url.EndsWith("/v1", StringComparison.Ordinal))
        {
            url += "/v1";
        }

        return url;
    }

}
