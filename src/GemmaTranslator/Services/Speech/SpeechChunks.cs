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
// been modified. It replaces splitTextIntoSpeechChunks of
// frontend/src/utils/api.js.

using System.Buffers;
using System.Globalization;

namespace GemmaTranslator.Services.Speech;

/// <summary>
/// Cuts a translation into the pieces of the text-to-speech part. One piece is
/// one call. Thus the appliance speaks the first sentence while the server
/// makes the next one, and no piece is longer than the limit.
/// </summary>
public static class SpeechChunks
{
    /// <summary>The longest piece, in characters of UTF-16.</summary>
    /// <remarks>
    /// backend/server.py reads the request line with readline(65537) and
    /// answers 414 above 65536 bytes. Uri.EscapeDataString gives ASCII, thus
    /// one Japanese character of the address is 9 bytes. A larger value here
    /// must keep the address of one call below that limit of the server.
    /// </remarks>
    public const int DefaultLimit = 180;

    // CAUTION: a port of the upstream rule gives nothing for one half of the
    // languages here. Upstream cuts on white space, and Chinese, Japanese, and
    // Korean put no space between the words. Its loop then gives one piece that
    // holds all the text, with no error and no signal. A mark of the end of a
    // sentence is a cut that each of the six languages has.
    private static readonly SearchValues<char> SentenceMarks =
        SearchValues.Create("!?。！？؟");

    private static readonly SearchValues<char> CloseMarks =
        SearchValues.Create("\"')]}»”’」』】）");

    // A cut makes a step from the last sample to 0, thus the click is as loud
    // as that sample and a cut where the sound decayed is quiet. The fullwidth
    // marks are here because CJK gives no white space to cut at.
    private static readonly SearchValues<char> SoftMarks = SearchValues.Create("、，,");

    /// <returns>The pieces, in sequence. White space only gives none.</returns>
    public static IReadOnlyList<string> Split(string? text, int limit = DefaultLimit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(limit, 1);

        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        List<string> pieces = [];
        int start = 0;
        int lastBreak = 0;
        int at = 0;

        while (at < text.Length)
        {
            int length = StringInfo.GetNextTextElementLength(text.AsSpan(at));

            if (length == 1 && EndsSentence(text, at))
            {
                int after = AfterMarks(text, at + 1);

                // A mark is the position of a cut and not a command to cut.
                // Upstream fills each piece to the limit, and a cut at each
                // mark gives 0.31 s of silence at each join, which a person
                // hears.
                if (after - start > limit && lastBreak > start)
                {
                    Add(pieces, text.AsSpan(start, lastBreak - start), limit);
                    start = lastBreak;
                }

                lastBreak = after;
                at = after;

                continue;
            }

            at += length;
        }

        Add(pieces, text.AsSpan(start), limit);

        return pieces;
    }

    private static bool EndsSentence(string text, int at)
    {
        char mark = text[at];

        if (mark != '.')
        {
            return SentenceMarks.Contains(mark);
        }

        // "3.5" and "gemma.local" keep their period. A full stop of Latin
        // script cuts only in front of white space, the end of the text, or a
        // mark of CloseMarks. A mark of CJK or Arabic needs no space after it,
        // thus it gets no such test.
        int next = at + 1;

        return next == text.Length
            || char.IsWhiteSpace(text[next])
            || CloseMarks.Contains(text[next]);
    }

    private static int AfterMarks(string text, int at)
    {
        while (at < text.Length
            && StringInfo.GetNextTextElementLength(text.AsSpan(at)) == 1
            && (text[at] == '.' || SentenceMarks.Contains(text[at]) || CloseMarks.Contains(text[at])))
        {
            at++;
        }

        return at;
    }

    private static void Add(List<string> pieces, ReadOnlySpan<char> piece, int limit)
    {
        piece = piece.Trim();

        while (piece.Length > limit)
        {
            int cut = CutAt(piece, limit);
            ReadOnlySpan<char> head = piece[..cut].TrimEnd();

            if (!head.IsEmpty)
            {
                pieces.Add(head.ToString());
            }

            piece = piece[cut..].TrimStart();
        }

        if (!piece.IsEmpty)
        {
            pieces.Add(piece.ToString());
        }
    }

    // The length that comes back is always more than 0, thus the caller moves.
    private static int CutAt(ReadOnlySpan<char> piece, int limit)
    {
        int soft = 0;
        int hard = 0;
        int at = 0;

        while (at < piece.Length)
        {
            int length = StringInfo.GetNextTextElementLength(piece[at..]);
            int next = at + length;

            if (next > limit)
            {
                break;
            }

            hard = next;

            if (length == 1 && (char.IsWhiteSpace(piece[at]) || SoftMarks.Contains(piece[at])))
            {
                soft = next;
            }

            at = next;
        }

        if (soft > 0)
        {
            return soft;
        }

        // A cut in a grapheme cluster makes a character that no font can show.
        // Thus a cluster keeps its full length, but not past the limit: a
        // cluster has no limit of its own, and a piece that is longer makes an
        // address that the server answers with 414. One character that is not
        // correct is less than the loss of all the sound.
        return hard > 0
            ? hard
            : Math.Min(StringInfo.GetNextTextElementLength(piece), limit);
    }
}
