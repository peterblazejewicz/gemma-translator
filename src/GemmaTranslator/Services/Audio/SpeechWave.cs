// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0

namespace GemmaTranslator.Services.Audio;

/// <summary>
/// The bars of the strip while the appliance speaks: one wave that travels
/// along the strip, at the height that the level of the speaker gives.
/// </summary>
public static class SpeechWave
{
    // The design: "vizSpeak 1.15s ease-in-out -(i / count) * 1.15s infinite",
    // keyframes 0.22 at 0 % and 100 %, 0.95 at 50 %. The microphone gets a
    // different duration for each bar, and this must not look like that: one
    // wave says playback and a row that jumps says live microphone.
    private const double PeriodSeconds = 1.15;
    private const double Floor = 0.22;
    private const double Peak = 0.95;

    // The synthesis is 1.74 times the sound it makes (deploy/README.md 8.21),
    // thus the space with no sound between two pieces is seconds and the strip
    // must stay alive through it. COMPUTED from three values in two other
    // files: a bar has 56 - 2 x PadY = 44 pixels, thus the peak is
    // 0.15 x 0.95 x 44 = 6.3, just above the IdleHeight of 6.
    private const double MinimumVisibleEnvelope = 0.15;

    /// <param name="bars">Takes one value from 0.0 to 1.0 in each element.</param>
    /// <param name="seconds">The time since the appliance started to speak.</param>
    /// <param name="level">The level of the speaker, from 0.0 to 1.0.</param>
    public static void Fill(Span<double> bars, double seconds, double level)
    {
        // See Spectrum.LevelOf for the NaN.
        double envelope = double.IsFinite(level)
            ? Math.Max(MinimumVisibleEnvelope, Math.Min(level, 1))
            : MinimumVisibleEnvelope;

        int count = bars.Length;

        for (int index = 0; index < count; index++)
        {
            // CAUTION: the delay of the design is NEGATIVE, and a negative
            // delay starts an animation that is already advanced. Thus the
            // offset is POSITIVE here.
            double phase = (seconds / PeriodSeconds) + ((double)index / count);

            phase -= Math.Floor(phase);

            bars[index] = envelope
                * (Floor + ((Peak - Floor) * 0.5 * (1 - Math.Cos(2 * Math.PI * phase))));
        }
    }
}
