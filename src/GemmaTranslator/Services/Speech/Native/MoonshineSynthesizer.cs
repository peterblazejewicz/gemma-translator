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
// modified. It replaces get_tts_engine and the synthesize call of
// backend/server.py lines 87 to 106 and line 162.

using System.Runtime.InteropServices;

namespace GemmaTranslator.Services.Speech.Native;

/// <param name="Samples">Mono, in the range -1 to 1.</param>
/// <param name="SampleRate">
/// The engine gives this. A measurement gives 24000 for each language, and the
/// software does not fix the value: a version that changes it must not play
/// fast.
/// </param>
internal readonly record struct Synthesis(float[] Samples, int SampleRate);

internal sealed class MoonshineSynthesizer : IDisposable
{
    private readonly SynthesizerHandle _handle;

    public MoonshineSynthesizer(string language, string? voice, string assetRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentException.ThrowIfNullOrWhiteSpace(assetRoot);

        // g2p_root names the directory of the models. Without it the library
        // looks in its own default, which is the cache of the account that
        // starts the software, and the systemd unit does not use that account.
        List<KeyValuePair<string, string>> options = [new("g2p_root", assetRoot)];

        if (!string.IsNullOrWhiteSpace(voice))
        {
            options.Add(new KeyValuePair<string, string>("voice", voice));
        }

        using MoonshineOptions native = new(options);

        _handle = new SynthesizerHandle(MoonshineException.Handle(
            MoonshineLibrary.CreateTtsSynthesizerFromFiles(
                language,
                files: 0,
                fileCount: 0,
                native.Array,
                native.Count,
                MoonshineLibrary.HeaderVersion),
            "The making of the synthesizer"));
    }

    /// <remarks>
    /// The call gives back when the sound is complete. The engine makes no part
    /// of it early, thus there is nothing to stream.
    /// </remarks>
    public Synthesis Synthesize(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        using MoonshineOptions none = new([]);

        // CAUTION: the buffer belongs to this code only when the call gives 0.
        // The ABI promises nothing about audio and count on a failure, and a
        // pointer that the ABI does not promise must not go to free: an
        // interior pointer, a static, or one that the library freed already
        // corrupts the heap, and NativeHeap records what that costs. count is
        // no better — it is the length that Erase writes, thus a value of
        // garbage memsets an arbitrary quantity of live memory before the
        // free. The shim of the supplier draws the same line: at
        // moonshine_api.py:400-413 the error path reads neither value and
        // frees nothing, and the free sits in a finally that only the path
        // that succeeded enters. A leak that is bounded, on a path that ends
        // in an exception, is the smaller cost.
        nint audio = 0;
        ulong count = 0;
        bool owned = false;

        try
        {
            int result = MoonshineLibrary.TextToSpeech(
                _handle.Value,
                text,
                none.Array,
                none.Count,
                out audio,
                out count,
                out int rate);

            owned = result == 0;

            MoonshineException.Check(result, "The synthesis");

            if (audio == 0 || count == 0)
            {
                return new Synthesis([], rate);
            }

            float[] samples = new float[count];

            Marshal.Copy(audio, samples, 0, (int)count);

            return new Synthesis(samples, rate);
        }
        finally
        {
            // SECURITY CONTROL. Keep both calls, keep this order, and keep them
            // in the finally. These samples are the translation of what a
            // person said. free() only marks the block reusable; it leaves the
            // sound readable until some later allocation overwrites it, so a
            // core dump, a debugger or a swap file can still recover the
            // speech. Erase closes that window. The library releases nothing
            // itself, so if this block does not run the speech stays for the
            // life of the process.
            //
            // The test of owned is a second control and not a tidy-up. See the
            // CAUTION above: without it a failed call sends a pointer that
            // nothing promised to free, and a length that nothing promised to
            // Erase.
            //
            // NativeHeap picks the C runtime that the library allocated from.
            // Do not replace it with another free. See the comment there.
            if (owned)
            {
                Erase(audio, count);

                NativeHeap.Free(audio);
            }
        }
    }

    public void Dispose() => _handle.Dispose();

    private static unsafe void Erase(nint audio, ulong count)
    {
        if (audio == 0 || count == 0)
        {
            return;
        }

        NativeMemory.Clear((void*)audio, (nuint)count * sizeof(float));
    }
}
