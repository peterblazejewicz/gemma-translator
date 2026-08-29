// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0

using System.Diagnostics;
using SoundFlow.Abstracts;
using SoundFlow.Structs;

namespace GemmaTranslator.Services.Audio;

/// <summary>
/// The largest RMS of one callback in DECIBELS, and the largest magnitude of
/// one sample. A magnitude of 1.0 or more says that the speaker clips.
/// </summary>
internal readonly record struct PlaybackLoudness(double Decibels, float Magnitude);

/// <remarks>
/// <para>
/// This class keeps five numbers and no audio: no ring, no window, and no
/// transform, thus it needs none of the wipes of the microphone path.
/// </para>
/// <para>
/// CAUTION: <see cref="Analyze"/> operates on the playback thread of miniaudio.
/// It takes no lock and it makes no memory, as the remark on
/// <see cref="SoundFlowAudioDevice"/> makes necessary. SoundFlow calls it at
/// each period, also while the mixer holds no player: the buffer is then all
/// zeros, thus the level falls by itself at the end of a sentence.
/// </para>
/// <para>
/// CAUTION: the smoothing is in <see cref="Analyze"/> and not in
/// <see cref="Read"/>, which is the opposite of <see cref="Spectrum"/>. That
/// class has a ring to look back at and this one has none, thus a reader at the
/// 200 ms tick would see one callback of twenty and lose each other one.
/// </para>
/// </remarks>
internal sealed class PlaybackMeter(AudioFormat format) : AudioAnalyzer(format)
{
    // The two values of Spectrum. The two strips must fall at the same speed,
    // or the display shows two meters that do not agree.
    private const double AttackMilliseconds = 40;
    private const double ReleaseMilliseconds = 220;

    // MEASURED on the appliance. The journal gives -8.3 dBFS for the largest
    // sound of one sentence that the synthesis makes, thus a ceiling of -6 puts
    // that sentence at 0.96 and keeps a little room for a louder one. The first
    // value here was -15, and each sentence then stood at 1.00: the bars filled
    // the strip and followed nothing. The floor gives 0.37 at -40 dBFS.
    private const double FloorDecibels = -60;
    private const double CeilingDecibels = -6;

    private volatile int _levelBits;
    private volatile int _frames;

    private static readonly int QuietBits =
        BitConverter.SingleToInt32Bits((float)FloorDecibels);

    private int _loudestBits = QuietBits;
    private int _peakSampleBits;

    private long _ticks;

    /// <summary>The samples of one channel that the last callback gave.</summary>
    public int Frames => _frames;

    /// <summary>
    /// The level now, from 0.0 to 1.0. It continues the release curve from the
    /// last callback, thus a speakerphone that a person disconnects makes the
    /// strip fall and not hold its height.
    /// </summary>
    public double Read()
    {
        long ticks = Volatile.Read(ref _ticks);

        if (ticks == 0)
        {
            return 0;
        }

        double level = BitConverter.Int32BitsToSingle(_levelBits);
        double milliseconds = Stopwatch.GetElapsedTime(ticks).TotalMilliseconds;

        return level * Math.Exp(-milliseconds / ReleaseMilliseconds);
    }

    /// <summary>
    /// The two largest values since the call before this one, which it puts
    /// back to their floors. One call gives both, thus a caller cannot take one
    /// and leave the other at the value of the piece that ended.
    /// </summary>
    /// <remarks>
    /// CAUTION: the decibels go back to the FLOOR and not to 0. Speech gives a
    /// negative value of decibels, thus a reset to 0 would make each test of
    /// the raise below false and the value would never move again. The
    /// magnitude goes back to 0, which is its own floor.
    /// </remarks>
    public PlaybackLoudness TakeLoudest() => new(
        BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref _loudestBits, QuietBits)),
        BitConverter.Int32BitsToSingle(Interlocked.Exchange(ref _peakSampleBits, 0)));

    /// <remarks>CAUTION: the caller must stop the device first.</remarks>
    public void Reset()
    {
        Volatile.Write(ref _ticks, 0);
        _levelBits = 0;
        _frames = 0;
        Volatile.Write(ref _loudestBits, QuietBits);
        Volatile.Write(ref _peakSampleBits, 0);
    }

    protected override void Analyze(ReadOnlySpan<float> buffer, int channels)
    {
        if (buffer.IsEmpty)
        {
            return;
        }

        double square = 0;
        float peak = 0;

        foreach (float sample in buffer)
        {
            square += (double)sample * sample;

            // This needs no test for a NaN: each comparison with a NaN is
            // false, thus the test below refuses it. DecibelsOf needs one
            // because Math.Clamp passes a NaN through and a comparison does
            // not.
            float magnitude = Math.Abs(sample);

            if (magnitude > peak)
            {
                peak = magnitude;
            }
        }

        int frames = buffer.Length / Math.Max(channels, 1);
        double level = BitConverter.Int32BitsToSingle(_levelBits);
        double decibels = DecibelsOf(square / buffer.Length);
        double target = LevelOf(decibels);
        double tau = target > level ? AttackMilliseconds : ReleaseMilliseconds;

        // The sample clock gives the interval, thus this needs no Stopwatch.
        double milliseconds = 1000.0 * frames / Format.SampleRate;

        level += (target - level) * (1 - Math.Exp(-milliseconds / tau));

        int bits = BitConverter.SingleToInt32Bits((float)level);

        _levelBits = bits;
        _frames = frames;

        // In DECIBELS and not in the level, because the level stops at 1.00
        // and a value that stops says only that the ceiling is too low. The
        // journal of the appliance sites CeilingDecibels from this number.
        //
        // The magnitude goes with it, because an RMS of one callback cannot
        // show one sample that clipped, and it is NOT clamped. This meter sits
        // on the master mixer, thus a value above 1.0 says that the speaker
        // clips and not which part made it: the synthesis, the resample of
        // miniaudio, and the sum of the mixer are each sufficient.
        //
        // Each raise and the Exchange of TakeLoudest can cross and lose the
        // reset. Do NOT make a CAS loop of it: a retry with no limit is the one
        // thing this thread must not do. The caller takes the value when the
        // piece is complete and the level is falling, thus the test is false.
        if (decibels > BitConverter.Int32BitsToSingle(Volatile.Read(ref _loudestBits)))
        {
            Volatile.Write(ref _loudestBits, BitConverter.SingleToInt32Bits((float)decibels));
        }

        if (peak > BitConverter.Int32BitsToSingle(Volatile.Read(ref _peakSampleBits)))
        {
            Volatile.Write(ref _peakSampleBits, BitConverter.SingleToInt32Bits(peak));
        }

        // The timestamp goes last. A reader that sees it then sees the level
        // that goes with it.
        Volatile.Write(ref _ticks, Stopwatch.GetTimestamp());
    }

    private static double DecibelsOf(double square)
    {
        // A device that gives F32 natively can pass a NaN through. NaN fails
        // each comparison, thus Math.Clamp gives it back and AudioVisualizer
        // then makes a Rect with a NaN in it.
        if (!double.IsFinite(square))
        {
            return FloorDecibels;
        }

        return 10 * Math.Log10(square + 1e-12);
    }

    private static double LevelOf(double decibels) => Math.Clamp(
        (decibels - FloorDecibels) / (CeilingDecibels - FloorDecibels),
        0,
        1);
}
