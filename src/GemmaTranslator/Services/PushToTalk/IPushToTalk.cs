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
// been modified. It replaces the record keys of handleKeyDown and
// handleKeyUp, in upstream/main:frontend/src/TranslatorApp.jsx.

using Avalonia.Controls;

namespace GemmaTranslator.Services.PushToTalk;

/// <param name="Lane">The lane of the person: 1 or 2.</param>
/// <param name="IsPressed">True if the button went down.</param>
public sealed record PushToTalkChange(int Lane, bool IsPressed);

/// <remarks>
/// Avalonia gives no key event on the Raspberry Pi: the DRM backend of
/// <c>Avalonia.LinuxFramebuffer</c> 12.1.1 can raise a pointer event and a
/// touch event only, and <c>RawKeyEventArgs</c> is not in that assembly. Thus
/// Windows uses the keys of Avalonia and the Raspberry Pi reads the input
/// device of Linux.
/// </remarks>
public interface IPushToTalk : IDisposable
{
    /// <remarks>
    /// The event can come on a thread that is not the thread of the user
    /// interface. The Raspberry Pi reads the device on its own thread.
    /// </remarks>
    event EventHandler<PushToTalkChange>? Changed;

    /// <remarks>
    /// CAUTION: the top level is a part of the contract, and it is not a
    /// property of one platform. Windows needs it. The Raspberry Pi does not
    /// use it.
    /// </remarks>
    /// <param name="topLevel">
    /// The window or the single view, or <c>null</c> if there is none.
    /// </param>
    void Start(TopLevel? topLevel);
}
