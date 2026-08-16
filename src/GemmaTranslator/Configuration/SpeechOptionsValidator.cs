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
/// <para>
/// The appliance has no keyboard and no settings screen. Thus an incorrect
/// value in a file must give a clear message at the start, and not a strange
/// failure at the first press of a button.
/// </para>
/// <para>
/// The loopback test that stood here is gone with the server that it protected.
/// It made sure that the recorded voice of a person went to this machine only.
/// The speech now stays in this process and never reaches a socket, thus the
/// property it defended holds by construction. Do not put an address back in
/// this section without putting that test back with it.
/// </para>
/// </remarks>
public sealed class SpeechOptionsValidator : IValidateOptions<SpeechOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, SpeechOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        if (options.MaxModels < 1)
        {
            failures.Add(
                $"{SpeechOptions.SectionName}:{nameof(SpeechOptions.MaxModels)} " +
                $"must be 1 or more. The value is {options.MaxModels}.");
        }

        if (options.TimeoutSeconds < 1)
        {
            failures.Add(
                $"{SpeechOptions.SectionName}:{nameof(SpeechOptions.TimeoutSeconds)} " +
                $"must be 1 or more. The value is {options.TimeoutSeconds}.");
        }

        // The values are tested here and no file is opened. MoonshineSpeechService
        // finds the library and the models at the first press, and its message
        // names each directory that it looked in. A test at the start would say
        // that all is well and then fail at that same press, because the disk can
        // change after the start.
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
