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
    /// A part of the name of the microphone to select.
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
    /// The appliance gives "Jabra" here. The value is empty on Windows, thus
    /// the operating system continues to select the device that a person
    /// selected.
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
}
