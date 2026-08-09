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
/// <para>
/// These three values are the three text fields of
/// <c>SettingsOverlay.jsx</c>, at lines 136, 149, and 163. A person cannot
/// type in a text field on the appliance, because the display has no keyboard.
/// Thus the values come from <c>appsettings.json</c> or from the environment.
/// </para>
/// <para>
/// The upstream <c>useProxy</c> value is not here. The browser sent each call
/// through <c>/proxy</c> in <c>server.py</c> to keep the same origin and to
/// prevent a CORS result. C# has no browser and no same-origin rule, thus the
/// software speaks to the endpoint directly and the proxy is not necessary.
/// </para>
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
    /// The address of the LiteRT-LM server.
    /// </summary>
    /// <remarks>
    /// The server is OpenAI-compatible. Use <see cref="GetBaseUrl"/> to get
    /// the address in the correct form.
    /// </remarks>
    public string EndpointUrl { get; set; } = DefaultEndpointUrl;

    /// <summary>
    /// The name of the model, for example <c>gemma4-e2b</c>.
    /// </summary>
    public string ModelName { get; set; } = "gemma4-e2b";

    /// <summary>
    /// The key for the endpoint, or an empty text if the endpoint needs none.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CAUTION: this value is a secret. Do not write it in the log.
    /// </para>
    /// <para>
    /// This value is not in <c>appsettings.json</c>, and it must not go there,
    /// because that file is in the repository. The endpoint is on the same
    /// machine and usually needs no key. If a key is necessary, give it in the
    /// environment with <c>GEMMA_LiteRt__ApiKey</c>. The systemd unit can read
    /// it from a file that the repository does not hold.
    /// </para>
    /// </remarks>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets the address in the form that the API calls need.
    /// </summary>
    /// <remarks>
    /// This is the C# form of <c>getNormalizedBaseUrl</c> in
    /// <c>api.js</c>. It removes each slash at the end and puts <c>/v1</c>
    /// at the end if it is not there. Thus <c>http://localhost:9379</c> and
    /// <c>http://localhost:9379/v1/</c> give the same result.
    /// </remarks>
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
