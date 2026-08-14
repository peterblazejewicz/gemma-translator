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
/// The settings of the Moonshine server, which does the speech-to-text part and
/// the text-to-speech part.
/// </summary>
public sealed class SpeechOptions
{
    /// <summary>
    /// The name of the section in <c>appsettings.json</c>.
    /// </summary>
    public const string SectionName = "Speech";

    /// <summary>
    /// The address of <c>backend/server.py</c>, which listens on port 3000.
    /// </summary>
    public const string DefaultBaseAddress = "http://127.0.0.1:3000";

    /// <remarks>
    /// CAUTION: this address must be on the local machine. It receives the
    /// recorded voice of a person. See <see cref="SpeechOptionsValidator"/>.
    /// </remarks>
    public string BaseAddress { get; set; } = DefaultBaseAddress;

    /// <summary>
    /// The maximum time for one call, in seconds.
    /// </summary>
    /// <remarks>
    /// A measurement on the appliance gives 0.5 s to 1.6 s for the
    /// speech-to-text part and 1 s to 5.5 s for the text-to-speech part, and
    /// about 6 s more for the first call of a language.
    /// <para>
    /// CAUTION: this time is also how long the appliance is dead if the server
    /// stops in the middle of a call. The state is then Working: each button
    /// does nothing and the screensaver does not come. Thus the value is four
    /// times the slowest measurement and not twenty times it.
    /// </para>
    /// </remarks>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Gets the address in the form that <see cref="Uri"/> can add a relative
    /// part to.
    /// </summary>
    /// <returns>The address, which ends with a slash.</returns>
    /// <exception cref="UriFormatException">The address is not a full address.</exception>
    public Uri GetBaseUri() => new(GetBaseUrl() + "/", UriKind.Absolute);

    /// <returns>The address, with no slash at the end.</returns>
    public string GetBaseUrl()
    {
        string url = BaseAddress?.Trim() ?? string.Empty;

        return url.Length == 0
            ? DefaultBaseAddress
            : url.TrimEnd('/');
    }
}
