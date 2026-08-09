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

namespace GemmaTranslator.Services;

/// <summary>
/// Writes a recording to a WAV file, to make sure that the microphone
/// operates.
/// </summary>
/// <remarks>
/// <para>
/// CAUTION: this writes the speech of a person to the disk. It is off, and it
/// operates only if <c>Audio:SaveRecordingsTo</c> gives a directory. Do not
/// give a value on an appliance that a customer uses.
/// </para>
/// <para>
/// The cause for this class: a log line can say "19200 samples, level 0.42",
/// and that does not say if the audio is speech that a person can understand.
/// A file that a person listens to says it. An incorrect rate gives a voice
/// that is too high or too low, and that is not possible to see in a number.
/// </para>
/// </remarks>
public static class WavFile
{
    /// <summary>
    /// Writes 16 kHz mono audio as a WAV file with 16 bits for each sample.
    /// </summary>
    /// <param name="path">The full name of the file to write.</param>
    /// <param name="samples">The audio, in the range -1 to 1.</param>
    /// <param name="sampleRate">The rate of the audio.</param>
    public static void Write(string path, ReadOnlySpan<float> samples, int sampleRate)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        const int channels = 1;
        const int bitsPerSample = 16;

        int dataBytes = samples.Length * sizeof(short);
        int byteRate = sampleRate * channels * (bitsPerSample / 8);

        using FileStream file = File.Create(path);
        using BinaryWriter writer = new(file);

        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);

        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(byteRate);
        writer.Write((short)(channels * (bitsPerSample / 8)));
        writer.Write((short)bitsPerSample);

        writer.Write("data"u8);
        writer.Write(dataBytes);

        foreach (float sample in samples)
        {
            // A value outside the range gives a loud noise if it goes around.
            float limited = Math.Clamp(sample, -1f, 1f);
            writer.Write((short)(limited * short.MaxValue));
        }
    }
}
