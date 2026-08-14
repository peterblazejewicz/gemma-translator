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

namespace GemmaTranslator.Services.Speech;

/// <remarks>
/// CAUTION: keep the bytes. Do not decode them here and do not add a SampleRate
/// property. The decoder of miniaudio is the one component that changes the
/// rate, and it takes the rate from the header of these bytes. The engine of
/// each language has its own rate, and a measurement gives 24000 Hz for
/// English, thus samples that this software decodes speak at two thirds of the
/// speed.
/// <para>
/// These bytes are the spoken form of what a person said. The playback clears
/// them when the sound is complete; this type does not.
/// </para>
/// </remarks>
public sealed record SpokenAudio(byte[] WavBytes);
