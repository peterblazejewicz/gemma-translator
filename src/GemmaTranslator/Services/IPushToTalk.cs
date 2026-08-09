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
// been modified. It replaces the keydown and keyup handlers of
// TranslatorApp.jsx, lines 250 to 312.

namespace GemmaTranslator.Services;

/// <summary>
/// The button of one person went down or came up.
/// </summary>
/// <param name="Lane">The lane of the person: 1 or 2.</param>
/// <param name="IsPressed">True if the button went down.</param>
public sealed record PushToTalkChange(int Lane, bool IsPressed);

/// <summary>
/// The two buttons that start and stop the recording.
/// </summary>
/// <remarks>
/// <para>
/// This interface gives the raw condition of the two buttons only. It does not
/// know the minimum time of a press, and it does not know if the software is
/// occupied. Those are rules of the operation, and they are in the view model.
/// </para>
/// <para>
/// CAUTION: this is a true difference of the platform, thus it obeys section
/// 5.2 of CLAUDE.md. Avalonia gives no key event on the Raspberry Pi: the DRM
/// backend of <c>Avalonia.LinuxFramebuffer</c> 12.1.1 can raise a pointer
/// event and a touch event only, and <c>RawKeyEventArgs</c> is not in that
/// assembly. Thus Windows uses the keys of Avalonia and the Raspberry Pi reads
/// the input device of Linux.
/// </para>
/// </remarks>
public interface IPushToTalk : IDisposable
{
    /// <summary>
    /// Occurs when a button goes down or comes up.
    /// </summary>
    /// <remarks>
    /// The event can come on a thread that is not the thread of the user
    /// interface. The Raspberry Pi reads the device on its own thread.
    /// </remarks>
    event EventHandler<PushToTalkChange>? Changed;

    /// <summary>
    /// Starts to listen to the buttons.
    /// </summary>
    void Start();
}
