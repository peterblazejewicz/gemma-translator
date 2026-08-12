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

using Avalonia;
using Avalonia.LinuxFramebuffer;
using Avalonia.Platform;
using GemmaTranslator.Fonts;

namespace GemmaTranslator;

/// <summary>
/// The start of the software. It selects one of the two heads.
/// </summary>
internal static class Program
{
    /// <summary>
    /// Starts the software.
    /// </summary>
    /// <param name="args">
    /// The arguments of the command. Give <c>--drm</c> on the Raspberry Pi.
    /// </param>
    /// <returns>The exit code.</returns>
    [STAThread]
    public static int Main(string[] args)
    {
        AppBuilder builder = BuildAvaloniaApp();

        if (args.Contains("--drm"))
        {
            SilenceConsole();

            // card: null lets Avalonia find the card. Give "/dev/dri/card1" to
            // select one card.
            //
            // The panel is native portrait at 720 x 1280. The appliance
            // operates in landscape, as upstream does, thus Avalonia turns the
            // output and the software gets a surface of 1280 x 720. Avalonia
            // adjusts the touch coordinates with it, which is necessary
            // because the touchscreen gives the coordinates of the panel.
            //
            // The value comes from the console of the machine. The console is
            // correct with "video=DSI-1:720x1280@60,rotate=270" in
            // cmdline.txt. Raspberry Pi gives that value in degrees clockwise,
            // and SurfaceOrientation is also degrees clockwise, thus the two
            // agree at 270.
            //
            // CAUTION: this comes from the console and not from a test of this
            // software on the panel. The DRM backend makes its own plane, thus
            // the rotation of the console does not apply to it. If the display
            // shows the user interface inverted, change this one value to
            // Rotation90.
            return builder.StartLinuxDrm(args, card: null, options: new DrmOutputOptions
            {
                Scaling = 1.0,
                Orientation = SurfaceOrientation.Rotation270,
            });
        }

        return builder.StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Makes the Avalonia application.
    /// </summary>
    /// <remarks>
    /// The Avalonia previewer and the XAML tools also call this method, thus it
    /// is public and it has no other work in it.
    /// </remarks>
    /// <returns>The builder of the application.</returns>
    // The software supplies its own fonts and does not use a font of the
    // operating system. See Fonts/AppFonts.cs.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .ConfigureFonts(static fontManager => fontManager.AddFontCollection(new GemmaFontCollection()))
            .With(AppFonts.MakeOptions())
            .LogToTrace();

    /// <summary>
    /// Stops the cursor of the console that blinks on top of the user
    /// interface on the Raspberry Pi.
    /// </summary>
    /// <remarks>
    /// <para>
    /// CAUTION: the Avalonia documents give this method without a guard. The
    /// method that they give kills the software under systemd. There is no
    /// keyboard on stdin in a service, <c>Console.ReadKey</c> throws
    /// <see cref="InvalidOperationException"/>, and an exception on a
    /// background thread stops the process.
    /// </para>
    /// <para>
    /// Thus this method makes no thread if the input is redirected, and it
    /// catches each exception in the thread.
    /// </para>
    /// </remarks>
    private static void SilenceConsole()
    {
        if (Console.IsInputRedirected)
        {
            return;
        }

        Thread thread = new(() =>
        {
            try
            {
                Console.CursorVisible = false;

                while (true)
                {
                    Console.ReadKey(true);
                }
            }
            catch (Exception)
            {
                // There is no console. The blinking cursor is a small problem.
                // A stop of the software is a large one.
            }
        })
        {
            IsBackground = true,
        };

        thread.Start();
    }
}
