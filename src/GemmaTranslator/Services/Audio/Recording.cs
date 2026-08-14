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
// been modified. It replaces frontend/src/hooks/useAudioRecorder.js.

namespace GemmaTranslator.Services.Audio;

/// <param name="Samples">
/// The audio, as 16 kHz mono samples in the range -1 to 1. This is the form
/// that Moonshine needs.
/// </param>
/// <param name="Duration">The time of the recording.</param>
/// <param name="PeakLevel">
/// The largest value in the recording, from 0 to 1. A value near 0 says that
/// the microphone heard nothing.
/// </param>
/// <param name="SampleRate">
/// The rate that the machine gave, which is not always the rate that the
/// software asked for.
/// </param>
/// <param name="ReachedLimit">
/// True if the recording came to
/// <see cref="Configuration.AudioOptions.MaximumRecordingSeconds"/>. Then the
/// button did not come up, and the audio is not complete.
/// </param>
public sealed record Recording(
    float[] Samples,
    TimeSpan Duration,
    float PeakLevel,
    int SampleRate,
    bool ReachedLimit) : IDisposable
{
    /// <remarks>
    /// SECURITY CONTROL. The caller owns this speech. Use a <c>using</c>
    /// statement, and put the work that needs the samples in that block.
    /// Without this, each recording leaves an array of speech in the heap
    /// until a later allocation takes that memory.
    /// </remarks>
    public void Dispose() => Array.Clear(Samples);
}
