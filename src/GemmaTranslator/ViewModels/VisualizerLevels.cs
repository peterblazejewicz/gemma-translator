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
// been modified. It replaces the bar heights of
// frontend/src/components/Visualizer.jsx.

namespace GemmaTranslator.ViewModels;

/// <summary>
/// The height of each bar of the visualizer at one moment.
/// </summary>
/// <remarks>
/// <para>
/// CAUTION: these values do not come from the microphone. Upstream reads the
/// Web Audio AnalyserNode, and the C# software has no such part: an FFT is
/// work that nobody has done. Thus the bars show that the appliance hears a
/// person, and they do not show what it hears.
/// </para>
/// <para>
/// When the speech-to-text slice gives a level for each frame, give those
/// values to the lane and remove this class.
/// </para>
/// <para>
/// The function has no condition of its own: the same time gives the same
/// values. Thus a screenshot of the harness is the same at each start.
/// </para>
/// </remarks>
public static class VisualizerLevels
{
    /// <summary>The lowest bar, as a part of the tallest one.</summary>
    /// <remarks>A bar of 0 looks like a bar that stopped.</remarks>
    private const double Floor = 0.12;

    /// <summary>
    /// Makes the height of each bar.
    /// </summary>
    /// <remarks>
    /// The period and the phase of each bar come from the index of that bar.
    /// Two bars beside each other then move at a different rate, which looks
    /// like a voice. The three constants 37, 13, and 53 are the constants of
    /// the design.
    /// </remarks>
    /// <param name="count">The count of the bars, from the settings.</param>
    /// <param name="seconds">The time since the recording started.</param>
    /// <returns>One value from 0 to 1 for each bar.</returns>
    public static double[] At(int count, double seconds)
    {
        if (count <= 0)
        {
            return [];
        }

        double[] levels = new double[count];

        for (int index = 0; index < count; index++)
        {
            double period = 0.5 + (((index * 37) % 13) / 13.0 * 1.1);
            double phase = ((index * 53) % 17) / 17.0;

            // A cosine from 0 to 1. The design uses a CSS animation that goes
            // to the end and comes back, which is the same shape.
            double wave = 0.5 - (0.5 * Math.Cos(2 * Math.PI * ((seconds / period) + phase)));

            levels[index] = Floor + ((1 - Floor) * wave);
        }

        return levels;
    }
}
