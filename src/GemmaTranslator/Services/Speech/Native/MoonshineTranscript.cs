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
// modified. It replaces the transcript reader of backend/server.py line 217.

using System.Runtime.InteropServices;
using System.Text;

namespace GemmaTranslator.Services.Speech.Native;

/// <remarks>
/// <para>
/// CAUTION: <c>moonshine_transcript_to_string</c> looks like the method for
/// this and it is not. It gives a text for a person to read, with the count of
/// the lines and the time of each one, thus
/// <c>"1 lines\n0.1s: The quick brown fox."</c> and not the words. That text
/// went to the translation part and to the display before a measurement found
/// it. A test with silence does not find it, because a transcript of no lines
/// gives an empty text from the two methods.
/// </para>
/// <para>
/// CAUTION: the layout below is written by hand from the header. A field that a
/// later version adds makes the stride wrong, and this code then reads the
/// text of one line out of the middle of another. Nothing falls over.
/// <see cref="MoonshineLibrary.HeaderVersion"/> is the guard: the library
/// refuses a version it does not know, and that check runs before this code.
/// </para>
/// </remarks>
internal static class MoonshineTranscript
{
    /// <summary>The lines, joined with one space, as upstream joins them.</summary>
    public static string Read(nint transcript)
    {
        if (transcript == 0)
        {
            return string.Empty;
        }

        TranscriptStruct header = Marshal.PtrToStructure<TranscriptStruct>(transcript);

        if (header.Lines == 0 || header.LineCount == 0)
        {
            return string.Empty;
        }

        int stride = Marshal.SizeOf<LineStruct>();
        StringBuilder words = new();

        for (ulong index = 0; index < header.LineCount; index++)
        {
            LineStruct line = Marshal.PtrToStructure<LineStruct>(
                header.Lines + (nint)(index * (ulong)stride));

            string? text = Marshal.PtrToStringUTF8(line.Text);

            if (string.IsNullOrEmpty(text))
            {
                continue;
            }

            if (words.Length > 0)
            {
                words.Append(' ');
            }

            words.Append(text);
        }

        return words.ToString();
    }

    /// <remarks><c>transcript_t</c>.</remarks>
    [StructLayout(LayoutKind.Sequential)]
    private struct TranscriptStruct
    {
        public nint Lines;
        public ulong LineCount;
    }

    /// <remarks>
    /// <c>transcript_line_t</c>. Each field is here although this code reads
    /// one: the size of the whole is the step from one line to the next, thus a
    /// field that is missing makes each line after the first incorrect.
    /// </remarks>
    [StructLayout(LayoutKind.Sequential)]
    private struct LineStruct
    {
        public nint Text;
        public nint AudioData;
        public nuint AudioDataCount;
        public float StartTime;
        public float Duration;
        public ulong Id;
        public sbyte IsComplete;
        public sbyte IsUpdated;
        public sbyte IsNew;
        public sbyte HasTextChanged;
        public sbyte HasSpeakerId;
        public ulong SpeakerId;
        public uint SpeakerIndex;
        public uint LastTranscriptionLatencyMilliseconds;
        public nint Words;
        public ulong WordCount;
    }
}
