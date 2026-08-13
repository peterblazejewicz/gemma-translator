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
// been modified. It replaces the useEffect of frontend/src/App.jsx:45-51,
// which writes the colour of the theme into the --bg-black CSS variable.

using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;

namespace GemmaTranslator.Services;

/// <summary>
/// Puts the selections of a person on the surface.
/// </summary>
/// <remarks>
/// The surface is the same on Windows and on the Raspberry Pi, thus this class
/// needs no interface.
/// </remarks>
public sealed partial class Appearance
{
    public const string AccentKey = "AccentBrush";

    public const string AccentInkKey = "AccentInkBrush";

    /// <summary>The key of the accent that stays legible on a light surface.</summary>
    public const string AccentDeepKey = "AccentDeepBrush";

    private readonly ILogger<Appearance> _logger;

    public Appearance(ILogger<Appearance> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <remarks>
    /// CAUTION: a write to the resources of the application raises
    /// <c>ResourcesChanged</c> and Avalonia then walks the tree. Off the thread
    /// of the user interface that gives no error at all: it gives a frame that
    /// is torn. <see cref="IPowerMonitor.Changed"/> comes on a thread of the
    /// pool and is one call away from here.
    /// </remarks>
    public void Apply(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Dispatcher.UIThread.VerifyAccess();

        if (Application.Current is not { } application)
        {
            LogNoApplication(_logger);
            return;
        }

        // This method is public and a caller can give a value that the store
        // did not make safe. Color.Parse then refuses it and the panel goes
        // black.
        UserSettings safe = settings.Sanitized();

        application.RequestedThemeVariant = safe.IsDark ? ThemeVariant.Dark : ThemeVariant.Light;

        application.Resources[AccentKey] = new SolidColorBrush(safe.Accent);
        application.Resources[AccentInkKey] = new SolidColorBrush(safe.Ink);
        application.Resources[AccentDeepKey] = new SolidColorBrush(safe.DeepAccent);

        LogApplied(_logger, safe.IsDark ? "dark" : "light", safe.AccentColor);
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The surface is {variant} with the accent {accent}.")]
    private static partial void LogApplied(ILogger logger, string variant, string accent);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "There is no application, thus the variant and the accent did not go on the surface.")]
    private static partial void LogNoApplication(ILogger logger);
}
