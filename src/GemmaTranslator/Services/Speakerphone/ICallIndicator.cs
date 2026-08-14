// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0

namespace GemmaTranslator.Services.Speakerphone;

/// <remarks>
/// <para>
/// A speakerphone with call control shows the state of a call on its own body.
/// The Jabra Speak2 40 lights a green ring while it is off hook. The appliance
/// stands in a public location, thus that ring tells each person in the room
/// when the microphone is live.
/// </para>
/// <para>
/// CAUTION: this is an indicator and it is not a control. It does not start
/// the microphone and it does not stop it. A measurement on the appliance
/// gives sound with the device on hook and with the device off hook. See
/// <c>IAudioCapture</c> for the condition that does control the microphone.
/// </para>
/// </remarks>
public interface ICallIndicator : IDisposable
{
    /// <summary>
    /// Opens the device before the first push, and puts the ring in a known
    /// state.
    /// </summary>
    void Start();

    /// <summary>
    /// Puts the device off hook and takes the mute away, in one report.
    /// </summary>
    /// <remarks>
    /// A device that a person muted becomes not muted.
    /// </remarks>
    void StartCall();

    void EndCall();
}
