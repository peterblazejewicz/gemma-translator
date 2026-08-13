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
// been modified. It replaces the localStorage calls of
// frontend/src/App.jsx:40 and App.jsx:48.

namespace GemmaTranslator.Services;

/// <summary>
/// Keeps what a person selected, so that it stays after a start.
/// </summary>
/// <remarks>
/// Upstream keeps the colour of the theme in <c>localStorage</c>. The appliance
/// has no browser, thus the software writes a small file.
/// </remarks>
public interface IUserSettingsStore
{
    /// <summary>
    /// Gets the settings that the software uses now.
    /// </summary>
    UserSettings Current { get; }

    /// <summary>
    /// Occurs when <see cref="Save"/> keeps a value that is not the value that
    /// the store had.
    /// </summary>
    /// <remarks>
    /// CAUTION: two view models read this store. Without this event a touch on
    /// the settings screen does not reach the primary screen: a person changes
    /// the count of the bars, and the visualizer keeps the count that it had
    /// until the end of the next recording, with no message and no line in the
    /// journal.
    ///
    /// The event comes on the thread that called <see cref="Save"/>, which is
    /// the thread of the user interface.
    /// </remarks>
    event EventHandler<UserSettings>? Changed;

    /// <summary>
    /// Keeps new settings and writes them to the disk.
    /// </summary>
    /// <remarks>
    /// A write that does not occur is not an error that stops the software. The
    /// appliance continues with the value in the memory, and the person sees
    /// the change that they made.
    /// </remarks>
    /// <param name="settings">The settings to keep.</param>
    void Save(UserSettings settings);
}
