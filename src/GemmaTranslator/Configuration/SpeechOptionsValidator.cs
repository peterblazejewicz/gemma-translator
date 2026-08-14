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

/// <remarks>
/// The appliance has no keyboard and no settings screen. Thus an incorrect
/// value in a file must give a clear message at the start, and not a strange
/// failure at the first press of a button.
/// </remarks>
public sealed class SpeechOptionsValidator : IValidateOptions<SpeechOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, SpeechOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        const string addressName =
            $"{SpeechOptions.SectionName}:{nameof(SpeechOptions.BaseAddress)}";

        // The test uses the text that GetBaseUri gives to the Uri constructor.
        // A test of a different text can pass while that call throws.
        if (!Uri.TryCreate(options.GetBaseUrl() + "/", UriKind.Absolute, out Uri? uri))
        {
            failures.Add($"{addressName} is not a full address.");
        }
        else
        {
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                failures.Add(
                    $"{addressName} must start with http or https. " +
                    $"The scheme is \"{uri.Scheme}\".");
            }

            // SECURITY CONTROL. This is not a convenience check, and it is not
            // a style preference. Do not weaken it into a warning, and do not
            // add a setting that turns it off, without a decision from the
            // owner of the project.
            //
            // What it stops: this appliance sends the recorded voice of every
            // person who presses a button to the address below, as raw audio
            // samples. Without this check, anybody who can edit
            // appsettings.json or set GEMMA_Speech__BaseAddress can point the
            // software at a machine they own, and every word spoken into the
            // appliance is recorded on that machine. That is a wiretap
            // installed by configuration, and nothing on the display would
            // show it.
            //
            // Upstream could not have this defect: the browser called the
            // relative path /api/stt, so the audio could only reach the server
            // that served the page. A configurable address is new here, and it
            // needs this check.
            //
            // Uri.IsLoopback matches localhost, 127.0.0.1 and ::1. CAUTION:
            // ::1 agrees with this test and does not connect. backend/server.py
            // binds ThreadingTCPServer(("127.0.0.1", PORT)), which is AF_INET,
            // thus the server listens on IPv4 only. Keep 127.0.0.1 in
            // appsettings.json.
            //
            // CAUTION: this check is one half of the control and not all of it.
            // It says which machine the address names. It does not stop a proxy
            // of the environment from taking the request to a different one:
            // that is UseProxy = false in ServiceRegistration.cs, and the two
            // lines go together.
            if (!uri.IsLoopback)
            {
                failures.Add(
                    $"{addressName} must be on the local machine. " +
                    "The software sends the voice of a person to this address, " +
                    "and it speaks to the Moonshine server on this machine only.");
            }
        }

        if (options.TimeoutSeconds is < 1 or > 600)
        {
            failures.Add(
                $"{SpeechOptions.SectionName}:{nameof(SpeechOptions.TimeoutSeconds)} " +
                $"must be 1 to 600. The value is {options.TimeoutSeconds}.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
