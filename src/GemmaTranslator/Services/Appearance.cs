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
/// <para>
/// Upstream writes one CSS variable and the browser does the remainder. This
/// class is the same operation: it writes the variant and the accent, and each
/// <c>DynamicResource</c> of the user interface follows.
/// </para>
/// <para>
/// This class has no interface. Section 5.2 of CLAUDE.md gives one cause for an
/// interface in this fork, which is a part that is not the same on Windows and
/// on the Raspberry Pi. This code is the same on the two machines.
/// </para>
/// </remarks>
public sealed partial class Appearance
{
    /// <summary>The key that each accent surface reads.</summary>
    public const string AccentKey = "AccentBrush";

    /// <summary>The key of the ink that goes on top of the accent.</summary>
    public const string AccentInkKey = "AccentInkBrush";

    /// <summary>The key of the accent that stays legible on a light surface.</summary>
    public const string AccentDeepKey = "AccentDeepBrush";

    private readonly ILogger<Appearance> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="Appearance"/> class.
    /// </summary>
    /// <param name="logger">The logger from the container.</param>
    public Appearance(ILogger<Appearance> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <summary>
    /// Applies the variant and the accent of these settings.
    /// </summary>
    /// <remarks>
    /// CAUTION: a write to the resources of the application raises
    /// <c>ResourcesChanged</c> and Avalonia then walks the tree. Off the thread
    /// of the user interface that gives no error at all: it gives a frame that
    /// is torn. Thus this method makes sure of the thread and does not trust a
    /// comment. <see cref="IPowerMonitor.Changed"/> comes on a thread of the
    /// pool and is one call away from here.
    /// </remarks>
    /// <param name="settings">The settings of the person.</param>
    public void Apply(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Dispatcher.UIThread.VerifyAccess();

        if (Application.Current is not { } application)
        {
            LogNoApplication(_logger);
            return;
        }

        // The store gives a value that is inside its limits, and this method is
        // public. A settings screen that shows a colour before it keeps the
        // selection would else give a value that Color.Parse refuses, and the
        // panel would go black.
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
