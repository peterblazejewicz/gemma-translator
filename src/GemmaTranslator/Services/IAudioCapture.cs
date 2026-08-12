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
// been modified. It replaces frontend/src/hooks/useAudioRecorder.js.

namespace GemmaTranslator.Services;

/// <summary>
/// What one recording gives back.
/// </summary>
/// <param name="Samples">
/// The audio, as 16 kHz mono samples in the range -1 to 1. This is the form
/// that Moonshine needs.
/// </param>
/// <param name="Duration">The time of the recording.</param>
/// <param name="PeakLevel">
/// The largest value in the recording, from 0 to 1. A value near 0 says that
/// the microphone heard nothing.
/// </param>
/// <param name="SampleRate">
/// The rate that the machine gave, which is not always the rate that the
/// software asked for.
/// </param>
/// <param name="ReachedLimit">
/// True if the recording came to
/// <see cref="Configuration.AudioOptions.MaximumRecordingSeconds"/>. Then the
/// button did not come up, and the audio is not complete.
/// </param>
public sealed record Recording(
    float[] Samples,
    TimeSpan Duration,
    float PeakLevel,
    int SampleRate,
    bool ReachedLimit) : IDisposable
{
    /// <summary>
    /// Clears the samples.
    /// </summary>
    /// <remarks>
    /// SECURITY CONTROL. The caller owns this speech. Use a <c>using</c>
    /// statement, and put the work that needs the samples in that block.
    /// Without this, each recording leaves an array of speech in the heap
    /// until a later allocation takes that memory.
    /// </remarks>
    public void Dispose() => Array.Clear(Samples);
}

/// <summary>
/// Records the microphone.
/// </summary>
/// <remarks>
/// The upstream hook makes 16 kHz mono Float32 in the browser and does the
/// change of the rate in JavaScript, at <c>audioHelpers.js:35-52</c>, with no
/// filter against aliasing. miniaudio does this work in native code and it
/// puts a low-pass filter first, thus the C# code asks for the format and
/// writes no resampler.
/// </remarks>
public interface IAudioCapture : IDisposable
{
    /// <summary>
    /// Opens the microphone before the first press.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CAUTION: the Jabra Speak2 40 gives 1.22 s from the start of the device
    /// to the first sample. If the software opens the device at the press,
    /// each person loses the first word.
    /// </para>
    /// <para>
    /// The software calls this at the start, thus the device is ready before
    /// the first press. The line in the log also shows which microphone the
    /// software selected, which is the first thing to read if the appliance
    /// records silence.
    /// </para>
    /// </remarks>
    void Prepare();

    /// <summary>
    /// Starts to record.
    /// </summary>
    /// <exception cref="AudioCaptureException">The microphone did not open.</exception>
    void StartRecording();

    /// <summary>
    /// Stops the recording and gives the audio.
    /// </summary>
    /// <remarks>
    /// The name is not <c>Stop</c>. <c>Stop</c> is a keyword of Visual Basic,
    /// and rule CA1716 does not permit it on an interface.
    /// </remarks>
    /// <returns>The audio, or <c>null</c> if no recording was in operation.</returns>
    Recording? StopRecording();
}

/// <summary>
/// The microphone did not operate.
/// </summary>
public sealed class AudioCaptureException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AudioCaptureException"/> class.
    /// </summary>
    public AudioCaptureException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioCaptureException"/> class.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    public AudioCaptureException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioCaptureException"/> class.
    /// </summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The first error.</param>
    public AudioCaptureException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
