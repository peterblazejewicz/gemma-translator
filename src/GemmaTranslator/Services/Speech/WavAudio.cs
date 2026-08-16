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
// This file is part of a fork of google-gemma/gemma-translator and has been
// modified. It replaces the numpy conversion and the wave writer of
// backend/server.py lines 164 to 175.

using System.Buffers.Binary;

namespace GemmaTranslator.Services.Speech;

/// <remarks>
/// The decoder of miniaudio takes a WAV file, thus the software makes one. The
/// header is the canonical 44 bytes that the <c>wave</c> module of Python
/// wrote, and each part after this one stays as it was.
/// </remarks>
internal static class WavAudio
{
    private const int HeaderBytes = 44;

    private const short PcmFormat = 1;
    private const short Channels = 1;
    private const short BitsPerSample = 16;
    private const int BytesPerSample = BitsPerSample / 8;

    /// <summary>One channel of 16-bit PCM at <paramref name="sampleRate"/>.</summary>
    /// <remarks>
    /// CAUTION: the numbers here are the numbers of upstream and they are not
    /// free to change. The multiplier is 32767 and not 32768: with 32768 a
    /// sample of 1.0 becomes 32768, which does not fit in a short and wraps to
    /// the loudest negative value, which is a click in the sound. The
    /// conversion cuts toward zero, as <c>astype</c> of numpy does, and
    /// <c>Math.Round</c> gives a different sample. The multiply stays in
    /// <c>float</c>: a Python float is a weak scalar, thus numpy keeps
    /// <c>samples * 32767.0</c> in float32, and a widening to <c>double</c>
    /// here is what would make a finite sample differ. A NaN diverges:
    /// <c>Math.Clamp</c> keeps it and the conversion gives 0, where
    /// <c>astype</c> of numpy usually gives the most negative value, which is a
    /// click. The engine must give no NaN, and 0 is the better of the two.
    /// </remarks>
    public static byte[] FromSamples(ReadOnlySpan<float> samples, int sampleRate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleRate);

        int dataBytes = samples.Length * BytesPerSample;
        byte[] wav = new byte[HeaderBytes + dataBytes];
        Span<byte> file = wav;

        "RIFF"u8.CopyTo(file);
        BinaryPrimitives.WriteInt32LittleEndian(file[4..], HeaderBytes - 8 + dataBytes);
        "WAVE"u8.CopyTo(file[8..]);

        "fmt "u8.CopyTo(file[12..]);

        BinaryPrimitives.WriteInt32LittleEndian(file[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(file[20..], PcmFormat);
        BinaryPrimitives.WriteInt16LittleEndian(file[22..], Channels);
        BinaryPrimitives.WriteInt32LittleEndian(file[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(file[28..], sampleRate * Channels * BytesPerSample);
        BinaryPrimitives.WriteInt16LittleEndian(file[32..], Channels * BytesPerSample);
        BinaryPrimitives.WriteInt16LittleEndian(file[34..], BitsPerSample);

        "data"u8.CopyTo(file[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(file[40..], dataBytes);

        Span<byte> data = file[HeaderBytes..];

        for (int index = 0; index < samples.Length; index++)
        {
            float sample = Math.Clamp(samples[index], -1f, 1f);

            BinaryPrimitives.WriteInt16LittleEndian(
                data[(index * BytesPerSample)..],
                (short)(sample * 32767f));
        }

        return wav;
    }
}
