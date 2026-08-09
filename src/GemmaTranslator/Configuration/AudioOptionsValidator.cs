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
/// Examines the <see cref="AudioOptions"/> values.
/// </summary>
/// <remarks>
/// A value of the environment that is not correct must stop the software at
/// the start, with a clear message. The appliance has no keyboard and no
/// console: a rate of 0 gives a microphone that hears nothing, and a minimum
/// press of 99999 ms gives an appliance that does nothing. Neither condition
/// shows a cause on the display.
/// </remarks>
public sealed class AudioOptionsValidator : IValidateOptions<AudioOptions>
{
    /// <inheritdoc/>
    public ValidateOptionsResult Validate(string? name, AudioOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<string> failures = [];

        if (options.SampleRate is < 8000 or > 48000)
        {
            failures.Add(
                $"{AudioOptions.SectionName}:{nameof(AudioOptions.SampleRate)} " +
                $"must be 8000 to 48000. The value is {options.SampleRate}. " +
                "Moonshine needs 16000.");
        }

        if (options.MinimumPressMilliseconds is < 0 or > 5000)
        {
            failures.Add(
                $"{AudioOptions.SectionName}:{nameof(AudioOptions.MinimumPressMilliseconds)} " +
                $"must be 0 to 5000. The value is {options.MinimumPressMilliseconds}.");
        }

        if (options.MaximumRecordingSeconds is < 1 or > 600)
        {
            failures.Add(
                $"{AudioOptions.SectionName}:{nameof(AudioOptions.MaximumRecordingSeconds)} " +
                $"must be 1 to 600. The value is {options.MaximumRecordingSeconds}.");
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
