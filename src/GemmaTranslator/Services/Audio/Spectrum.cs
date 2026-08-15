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
// been modified. It replaces the AnalyserNode of
// frontend/src/hooks/useAudioRecorder.js and the bar heights of
// frontend/src/components/Visualizer.jsx.

using System.Diagnostics;

namespace GemmaTranslator.Services.Audio;

// CAUTION: one caller only. The arrays are scratch and this class takes no
// lock. The frame timer of the view model is that caller.
public sealed class Spectrum
{
    // Upstream gives 256 to AnalyserNode.fftSize and drops the top quarter of
    // the spectrum. At 16 kHz one bin is 62.5 Hz, thus the 96 bins that stay
    // end at 6000 Hz. A sound above them comes through the leakage of the
    // window only: 6000 Hz gives 0.17 on the last bar and 7000 Hz gives 0.
    public const int WindowLength = 256;

    private const int BinCount = WindowLength / 2;

    private const int KeptBins = BinCount * 3 / 4;

    // A sine at full scale, on the centre of a bin, gives 1.0 x 256 x 0.5 / 2
    // through a Hann window. Thus that sine has an energy of 1.0 and 0 dBFS.
    private const double Reference = WindowLength / 4.0;

    // CAUTION: these are the limits of ONE BIN and not of the whole sound.
    // Speech puts its energy in 128 bins, thus each bin is far below the level
    // of the recording: a peak of 0.331 on the appliance, which is -9.6 dBFS,
    // gives bins near -45 dBFS. A measurement on the appliance gives -48 dBFS
    // to -39 dBFS for the largest bin while a person speaks, and the ceiling
    // of -40 brings those to 0.87 and 1.00.
    //
    // CAUTION: do not take the two limits of an AnalyserNode (-100 and -30)
    // and think that they compare. That node divides by fftSize and this code
    // divides by WindowLength / 4, thus a sine at full scale gives about
    // -13.5 dB there and 0 dB here. This code is about 13 dB above the browser
    // for the same sound, and a change to the reference of the browser makes
    // each bar SHORTER.
    //
    // Upstream has no gate and this code has none. A gate is not necessary:
    // the Jabra Speak2 40 removes the noise in its own hardware, on the path
    // that it sends, which is the path that this code reads. Thus a quiet room
    // comes to this code quiet, and the software does not have to make it
    // quiet a second time. See section 4.3 of CLAUDE.md, which says to add no
    // such function in the software.
    //
    // CAUTION: the same hardware can make the speech of a person who is quiet
    // small. If a person who speaks softly gives no bar, the cause is that
    // control of the noise and not these two limits, thus a lower ceiling
    // corrects nothing.
    private const double FloorDecibels = -100;
    private const double CeilingDecibels = -40;

    // Up fast and down slowly, as a meter of a level does. Each frame makes its
    // alpha from the interval that it measured, thus the fall takes the same
    // time of the clock at a tick of 33 ms and at one of 200 ms.
    private const double AttackMilliseconds = 40;
    private const double ReleaseMilliseconds = 220;

    // A tick of 200 ms brings 3200 samples and thus needs 13 windows of 16 ms.
    // The ring of the device holds 16, and a longer stall has no more to give.
    private const int MaxWindows = 16;

    private readonly double[] _hann = new double[WindowLength];
    private readonly double[] _cos = new double[BinCount];
    private readonly double[] _sin = new double[BinCount];
    private readonly int[] _reverse = new int[WindowLength];
    private readonly double[] _re = new double[WindowLength];
    private readonly double[] _im = new double[WindowLength];

    private double[] _target = [];
    private double[] _level = [];
    private long _ticks;
    private int _read;
    private double _loudest;

    public Spectrum()
    {
        for (int n = 0; n < WindowLength; n++)
        {
            _hann[n] = 0.5 - (0.5 * Math.Cos(2 * Math.PI * n / WindowLength));
            _reverse[n] = Reverse(n);
        }

        for (int k = 0; k < BinCount; k++)
        {
            _cos[k] = Math.Cos(2 * Math.PI * k / WindowLength);
            _sin[k] = Math.Sin(2 * Math.PI * k / WindowLength);
        }
    }

    /// <summary>The largest bar since <see cref="Reset"/>.</summary>
    public double Loudest => _loudest;

    public void Reset()
    {
        _ticks = 0;
        _read = 0;
        _loudest = 0;
        Array.Clear(_target);
        Array.Clear(_level);

        // SECURITY CONTROL. See Fill.
        Array.Clear(_re);
        Array.Clear(_im);
    }

    // "written" counts each sample that went in the ring, thus the newest one
    // is at that count modulo the length of the ring.
    public void Fill(ReadOnlySpan<float> ring, int written, Span<double> bars)
    {
        if (bars.IsEmpty)
        {
            return;
        }

        bool fresh = _ticks == 0 || _level.Length != bars.Length;
        double milliseconds = fresh
            ? 0
            : Stopwatch.GetElapsedTime(_ticks).TotalMilliseconds;

        _ticks = Stopwatch.GetTimestamp();

        if (_level.Length != bars.Length)
        {
            _level = new double[bars.Length];
            _target = new double[bars.Length];
        }

        ReadTarget(ring, written);

        // SECURITY CONTROL. Do not delete this to save a memset. Transform
        // leaves the DFT of the last window of a person's speech in these two
        // arrays. A DFT inverts exactly, so those 16 ms are raw recoverable
        // audio, not a level. Nothing else clears them: Reset covers the bar
        // values only, and StopRecording and Dispose clear the ring and never
        // reach inside this object. Without this line that speech stays in the
        // heap after the appliance stops, and it is in any core dump.
        Array.Clear(_re);
        Array.Clear(_im);

        Smooth(fresh, milliseconds);

        _level.CopyTo(bars);
    }

    private static int Reverse(int index)
    {
        int value = 0;

        for (int bit = 1; bit < WindowLength; bit <<= 1)
        {
            value <<= 1;

            if ((index & bit) != 0)
            {
                value |= 1;
            }
        }

        return value;
    }

    // The windows cover each sample that came since the last frame and each bar
    // keeps the largest of them: one window for each frame would show 16 ms of
    // each 200 ms. CAUTION: the count of the new samples gives this work and
    // not the clock, or a microphone that goes away shows its last words again.
    private void ReadTarget(ReadOnlySpan<float> ring, int written)
    {
        Array.Clear(_target);

        int arrived = written - _read;

        _read = written;

        // Keep the oldest window one window behind the writer. Without this a
        // tick that came late makes the span cover the whole ring, and the read
        // then straddles the position that the audio thread writes next.
        int available = Math.Min(written, ring.Length - WindowLength);

        if (arrived <= 0 || available < WindowLength)
        {
            return;
        }

        int span = Math.Clamp(arrived, WindowLength, available);
        int windows = Math.Clamp(
            (span + WindowLength - 1) / WindowLength,
            1,
            MaxWindows);
        int hop = windows > 1 ? (span - WindowLength) / (windows - 1) : 0;

        for (int window = 0; window < windows; window++)
        {
            Load(ring, written - (window * hop));
            Transform();
            TakeLargest();
        }
    }

    private void Load(ReadOnlySpan<float> ring, int end)
    {
        // start is never negative: span <= available <= written, and C# gives a
        // negative result for the % of a negative int.
        int start = end - WindowLength;

        for (int n = 0; n < WindowLength; n++)
        {
            int at = _reverse[n];

            _re[at] = ring[(start + n) % ring.Length] * _hann[n];
            _im[at] = 0;
        }
    }

    private void Transform()
    {
        for (int length = 2; length <= WindowLength; length <<= 1)
        {
            int half = length >> 1;
            int step = WindowLength / length;

            for (int block = 0; block < WindowLength; block += length)
            {
                for (int j = 0; j < half; j++)
                {
                    int k = j * step;
                    double wr = _cos[k];
                    double wi = -_sin[k];
                    int a = block + j;
                    int b = a + half;

                    double tr = (_re[b] * wr) - (_im[b] * wi);
                    double ti = (_re[b] * wi) + (_im[b] * wr);

                    _re[b] = _re[a] - tr;
                    _im[b] = _im[a] - ti;
                    _re[a] += tr;
                    _im[a] += ti;
                }
            }
        }
    }

    private void TakeLargest()
    {
        int bars = _target.Length;

        for (int i = 0; i < bars; i++)
        {
            int start = i * KeptBins / bars;
            int end = (i + 1) * KeptBins / bars;

            if (end <= start)
            {
                end = start + 1;
            }

            double sum = 0;

            for (int bin = start; bin < end; bin++)
            {
                sum += LevelOf(bin);
            }

            double value = sum / (end - start);

            // CAUTION: the > refuses a NaN. Math.Max here would give it back.
            if (value > _target[i])
            {
                _target[i] = value;
            }
        }
    }

    private double LevelOf(int bin)
    {
        double energy =
            ((_re[bin] * _re[bin]) + (_im[bin] * _im[bin])) / (Reference * Reference);

        // A device that gives F32 natively can pass a NaN through. NaN fails
        // each comparison below, thus the gate does not stop it and Math.Clamp
        // gives it back. AudioVisualizer then makes a Rect with a NaN in it.
        if (!double.IsFinite(energy))
        {
            return 0;
        }

        double decibels = 10 * Math.Log10(energy + 1e-12);

        return Math.Clamp(
            (decibels - FloorDecibels) / (CeilingDecibels - FloorDecibels),
            0,
            1);
    }

    private void Smooth(bool fresh, double milliseconds)
    {
        double attack = fresh ? 1 : 1 - Math.Exp(-milliseconds / AttackMilliseconds);
        double release = fresh ? 1 : 1 - Math.Exp(-milliseconds / ReleaseMilliseconds);

        for (int i = 0; i < _level.Length; i++)
        {
            double target = _target[i];

            _level[i] += (target - _level[i]) * (target > _level[i] ? attack : release);

            if (_level[i] > _loudest)
            {
                _loudest = _level[i];
            }
        }
    }
}
