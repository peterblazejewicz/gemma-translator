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

namespace GemmaTranslator.Services.Settings;

public interface IUserSettingsStore
{
    UserSettings Current { get; }

    /// <summary>
    /// Occurs when <see cref="Save"/> keeps a value that is not the value that
    /// the store had.
    /// </summary>
    /// <remarks>
    /// CAUTION: two view models read this store. Without this event a change on
    /// the settings screen does not reach the primary screen, and nothing says
    /// so in the journal.
    ///
    /// The event comes on the thread that called <see cref="Save"/>, which is
    /// the thread of the user interface.
    /// </remarks>
    event EventHandler<UserSettings>? Changed;

    /// <remarks>
    /// A write that does not occur is not an error that stops the software. The
    /// appliance continues with the value in the memory.
    /// </remarks>
    void Save(UserSettings settings);
}
