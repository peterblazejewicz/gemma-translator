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
// modified. It replaces get_stt_recognizer and the transcribe call of
// backend/server.py lines 108 to 123 and line 216.

using System.Runtime.InteropServices;

namespace GemmaTranslator.Services.Speech.Native;

/// <remarks>
/// The language is fixed when the library makes the model, thus there is one of
/// these for each language and <see cref="SpeechEngineCache"/> holds them.
/// </remarks>
internal sealed class MoonshineTranscriber : IDisposable
{
    private readonly TranscriberHandle _handle;

    public MoonshineTranscriber(string modelDirectory, uint architecture)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelDirectory);

        using MoonshineOptions options = new([]);

        _handle = new TranscriberHandle(MoonshineException.Handle(
            MoonshineLibrary.LoadTranscriberFromFiles(
                modelDirectory,
                architecture,
                options.Array,
                options.Count,
                MoonshineLibrary.HeaderVersion),
            "The load of the transcriber"));
    }

    /// <summary>
    /// The words of <paramref name="samples"/>, which are mono and in the range
    /// -1 to 1. An empty text is not an error, the same as server.py:217.
    /// </summary>
    public string Transcribe(ReadOnlySpan<float> samples, int sampleRate)
    {
        if (samples.IsEmpty)
        {
            return string.Empty;
        }

        // The generated interop pins the buffer for the call, thus the samples
        // do not move and no copy of the speech is made here.
        MoonshineException.Check(
            MoonshineLibrary.TranscribeWithoutStreaming(
                _handle.Value,
                ref MemoryMarshal.GetReference(samples),
                (ulong)samples.Length,
                sampleRate,
                flags: 0,
                out nint transcript),
            "The transcription");

        // The transcript belongs to the library and this code frees nothing.
        return MoonshineTranscript.Read(transcript);
    }

    public void Dispose() => _handle.Dispose();
}
