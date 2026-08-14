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

namespace GemmaTranslator.Services.Audio;

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
    /// CAUTION: the Jabra Speak2 40 gives 1.22 s from the start of the device
    /// to the first sample. If the software opens the device at the press,
    /// each person loses the first word.
    /// </remarks>
    void Prepare();

    /// <exception cref="AudioCaptureException">The microphone did not open.</exception>
    void StartRecording();

    /// <remarks>
    /// The name is not <c>Stop</c>. <c>Stop</c> is a keyword of Visual Basic,
    /// and rule CA1716 does not permit it on an interface.
    /// </remarks>
    /// <returns>The audio, or <c>null</c> if no recording was in operation.</returns>
    Recording? StopRecording();

    /// <summary>
    /// Gets <c>true</c> while the machine gives the device that
    /// <see cref="Configuration.AudioOptions.PreferredDeviceName"/> names, or
    /// <c>null</c> if this machine cannot answer the question.
    /// </summary>
    /// <remarks>
    /// The value is <c>null</c> while the settings give no name. The software
    /// then takes the default device, and the name of that device says nothing
    /// about the speakerphone.
    /// </remarks>
    bool? IsDevicePresent { get; }

    /// <remarks>
    /// CAUTION: this event comes on the thread that reads the list of the
    /// devices, and not on the thread of the user interface. A listener that
    /// writes a property must go to the correct thread first, or Avalonia
    /// throws.
    /// </remarks>
    event EventHandler<bool?>? DevicePresenceChanged;
}
