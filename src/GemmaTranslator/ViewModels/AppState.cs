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
// been modified. It replaces the status values of
// frontend/src/TranslatorApp.jsx.

namespace GemmaTranslator.ViewModels;

/// <summary>
/// What the appliance does now.
/// </summary>
/// <remarks>
/// <para>
/// The DRM backend makes no window and no popup, thus each one of these fills
/// the surface. Two conditions are not in this list, because they go on top of
/// a state and do not replace it: the settings screen and the warning of the
/// very low charge.
/// </para>
/// <para>
/// The image that the system shows before the software starts is also not
/// here. The software does not draw it. See section 6 of the plan and
/// deploy/README.md.
/// </para>
/// </remarks>
public enum AppState
{
    /// <summary>
    /// The model comes into the memory. This takes tens of seconds.
    /// </summary>
    WarmUp,

    /// <summary>
    /// The appliance waits for a person to hold a button.
    /// </summary>
    Idle,

    /// <summary>
    /// The software hears one person.
    /// </summary>
    Recording,

    /// <summary>
    /// The software makes the text and then the translation.
    /// </summary>
    /// <remarks>
    /// <see cref="WorkStage"/> says which of the two operations is in
    /// operation.
    /// </remarks>
    Working,

    /// <summary>
    /// The two texts are on the display.
    /// </summary>
    Result,

    /// <summary>
    /// The display is dark, to use less electrical supply on the cells.
    /// </summary>
    Screensaver,
}

/// <summary>
/// The two operations of <see cref="AppState.Working"/>.
/// </summary>
/// <remarks>
/// The design shows a different text for each one, thus the two are not one
/// state. Upstream also shows two texts at
/// <c>TranslatorApp.jsx</c>: it says "Listening" and then it says
/// "Translating".
/// </remarks>
public enum WorkStage
{
    /// <summary>The speech-to-text part makes the text of the speech.</summary>
    Listening,

    /// <summary>The model makes the text in the other language.</summary>
    Translating,
}
