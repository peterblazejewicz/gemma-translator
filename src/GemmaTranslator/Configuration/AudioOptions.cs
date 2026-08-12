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
// been modified.

namespace GemmaTranslator.Configuration;

/// <summary>
/// The settings of the microphone and of the buttons.
/// </summary>
public sealed class AudioOptions
{
    /// <summary>
    /// The name of the section in <c>appsettings.json</c>.
    /// </summary>
    public const string SectionName = "Audio";

    /// <summary>
    /// A part of the name of the audio device to select.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CAUTION: this value stops the most probable failure of the appliance.
    /// systemd starts the software before udev completes the enumeration of
    /// the USB devices. The default device is then the audio of the HDMI
    /// output or the <c>bcm2835</c> device. The software looks correct and
    /// <b>records silence</b>, with no error and no line in the log.
    /// </para>
    /// <para>
    /// One appsettings.json goes to the two machines, thus Windows gets this
    /// value also. To get the device that a person selected on Windows, give
    /// an empty value in GEMMA_Audio__PreferredDeviceName, which the software
    /// reads after the file.
    /// </para>
    /// </remarks>
    public string PreferredDeviceName { get; set; } = string.Empty;

    /// <summary>
    /// The rate of the capture, in samples each second.
    /// </summary>
    /// <remarks>
    /// Moonshine needs 16 kHz. The microphone band of the Jabra Speak2 40
    /// stops at 7000 Hz, thus 16 kHz is sufficient.
    /// </remarks>
    public int SampleRate { get; set; } = 16000;

    /// <summary>
    /// The shortest press that starts a recording, in milliseconds.
    /// </summary>
    /// <remarks>
    /// A physical button in a public location gets an accidental touch. A
    /// press that is shorter than this value does nothing.
    /// </remarks>
    public int MinimumPressMilliseconds { get; set; } = 250;

    /// <summary>
    /// The longest recording, in seconds.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CAUTION: this value is the one protection against a button that stays
    /// down. A release can go away: a thread that reads the device can stop,
    /// or a button can be mechanically down. The software then records the
    /// room and the memory increases at 64 KB each second, which is 5.5 GB
    /// each day on a machine with 8 GB that holds a model of 2.4 GB.
    /// </para>
    /// <para>
    /// The buffer has this dimension and the software makes it one time at the
    /// start. Thus the memory of the software does not increase, and a defect
    /// in the buttons cannot stop the appliance. 120 s is much longer than one
    /// person speaks, thus the limit does not operate in usual conditions.
    /// </para>
    /// </remarks>
    public int MaximumRecordingSeconds { get; set; } = 120;

}
