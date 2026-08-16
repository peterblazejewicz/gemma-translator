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
/// one call. Thus the appliance speaks the first sentence while the synthesis
/// makes the next one, and no piece is longer than the limit.
/// </summary>
public static class SpeechChunks
{
    /// <summary>The longest piece, in characters of UTF-16.</summary>
    /// <remarks>
    /// CAUTION: the cause of this number is gone and the number stays. It came
    /// from backend/server.py, which read the request line with readline(65537)
    /// and answered 414 above 65536 bytes; the text of a call went in the
    /// address, and one Japanese character became 9 bytes there. There is no
    /// address now, thus no length of the text is too long for the library.
    /// <para>
    /// The value stays because a change to it changes what a person hears: it
    /// decides how many pieces an answer has, and thus the holds that
    /// <see cref="HoldToStart"/> computes. Measure before you move it.
    /// </para>
    /// </remarks>
    public const int DefaultLimit = 180;

    // A cut costs 0.9 s of EXTRA AUDIO, measured: the same Japanese sentence
    // gives 4.10 s of sound whole and 5.00 s in two pieces, because each piece
    // carries its own lead-in and tail. At 1.8 times realtime that is 1.6 s of
    // synthesis for nothing. Thus a short answer must stay whole: below this
    // length the cut costs more than it saves.
    //
    // Japanese gives about 0.15 s of sound for one character and English about
    // 0.115 s, thus 40 characters is about 6 s of sound and 4.6 s to make.
    public const int MinimumSplitLength = 40;

    // The target for a text that is long enough to cut. A piece of 40 gives
    // about 4.6 s to the first word, against 14.6 s for a whole answer of 51
    // characters. HoldToStart keeps the joins without a space; a smaller value
    // here gives the first word sooner and makes the hold longer.
    public const int PieceLength = 40;

    // The synthesis makes 1 s of sound in about 1.8 s, measured on the
    // appliance: 1.76, 1.84, 1.82, 1.82, 1.82 for whole answers and 1.74, 1.77,
    // 1.78, 1.78, 1.79 for the five pieces of one long answer.
    //
    // CAUTION: the value here is 1.9 and not 1.8, and the margin is not
    // timidity. HoldToStart divides the sound by the characters on the two
    // sides of its comparison, and that is only correct while one character
    // gives the same sound in each piece. It does not: each piece carries its
    // own lead-in and tail, thus a SHORT piece gives more sound for one
    // character than a long one. A measurement of one answer of five pieces
    // gives 0.157 s for one character in the piece of 26 and 0.224 s in the
    // piece of 14, which is 43 % more. With 1.7 that answer held two pieces,
    // and the journal then shows spaces with no sound of 1.61 s and 0.62 s at
    // the last two joins. With 1.9 it holds three and it has none.
    //
    // A fraction and not a double, because this decides a join.
    private const int RealtimeNumerator = 19;
    private const int RealtimeDenominator = 10;

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

    /// <summary>
    /// The pieces that the appliance speaks, at the length that the
    /// measurements of the appliance give.
    /// </summary>
    /// <remarks>
    /// A short answer goes whole, because a cut costs more sound than it saves.
    /// A long answer goes in pieces of the same length: a greedy fill gives one
    /// full piece and a short remainder, and that remainder pays the same
    /// lead-in and tail as a full one for almost no words.
    /// </remarks>
    public static IReadOnlyList<string> Plan(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length < MinimumSplitLength)
        {
            return Split(text);
        }

        int count = (text.Length + PieceLength - 1) / PieceLength;
        IReadOnlyList<string> pieces = Split(text, (text.Length + count - 1) / count);

        // A cut that gives no word sooner is a cut that costs sound for
        // nothing. See HoldToStart: with two pieces the appliance must hold
        // both, thus the first word comes at the same moment as with no cut.
        return HoldToStart(pieces) >= pieces.Count ? Split(text) : pieces;
    }

    /// <summary>
    /// The count of pieces that must be complete before the speaker starts, so
    /// that no space with no sound comes at a join.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The synthesis makes sound more slowly than the speaker plays it, thus a
    /// speaker that starts at the first piece catches the synthesis up and
    /// waits. The appliance holds the first pieces until what it has can last
    /// until the last piece is complete.
    /// </para>
    /// <para>
    /// For each piece k after the hold, the synthesis of the pieces from the
    /// hold to k must be complete before the speaker has played the pieces in
    /// front of k. The seconds for one character stand on the two sides of that
    /// comparison and go away, thus this is arithmetic on the COUNT OF
    /// CHARACTERS: it needs no measurement of the sound and no value for a
    /// language.
    /// </para>
    /// <para>
    /// CAUTION: that cancellation is an APPROXIMATION, and the margin in
    /// RealtimeNumerator pays for it. One character does not give the same
    /// sound in each piece, because a short piece carries the same lead-in and
    /// tail as a long one.
    /// </para>
    /// </remarks>
    public static int HoldToStart(IReadOnlyList<string> pieces)
    {
        ArgumentNullException.ThrowIfNull(pieces);

        int count = pieces.Count;
        int[] through = new int[count + 1];

        for (int i = 0; i < count; i++)
        {
            through[i + 1] = through[i] + pieces[i].Length;
        }

        for (int hold = 1; hold <= count; hold++)
        {
            bool lasts = true;

            for (int k = hold + 1; k <= count; k++)
            {
                // A fraction, so that no rounding of a double decides a join.
                if (RealtimeNumerator * (through[k] - through[hold])
                    > RealtimeDenominator * through[k - 1])
                {
                    lasts = false;
                    break;
                }
            }

            if (lasts)
            {
                return hold;
            }
        }

        return count;
    }

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
        // cluster has no limit of its own, and a piece must not go past the
        // limit that DefaultLimit sets. One character that is not correct is
        // less than the loss of all the sound.
        return hard > 0
            ? hard
            : Math.Min(StringInfo.GetNextTextElementLength(piece), limit);
    }
}
