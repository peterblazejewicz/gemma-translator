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
            return StartOnPanel(builder, args);
        }

        return builder.StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Starts the software on the panel of the appliance.
    /// </summary>
    /// <param name="builder">The builder from <see cref="BuildAvaloniaApp"/>.</param>
    /// <param name="args">The arguments of the command.</param>
    /// <returns>The exit code.</returns>
    private static int StartOnPanel(AppBuilder builder, string[] args)
    {
        // Avalonia opens each /dev/dri/card[0-9]+ in the sequence that the
        // directory gives and takes the first one that opens. This account can
        // open each card of this machine, thus that sequence would decide which
        // card gets the user interface. The udev rule of
        // deploy/99-gemma-translator.rules gives this name to the card of the
        // panel, as it gives a name to the buttons and to the touchscreen.
        const string panelCard = "/dev/dri/appliance-panel";

        // This test comes before Avalonia, because Avalonia has no logger here
        // and the appliance has no console. Without it a machine with no rule
        // gives a native error with no cause in it.
        if (!File.Exists(panelCard))
        {
            throw new InvalidOperationException(
                $"There is no {panelCard}. Put in deploy/99-gemma-translator.rules, "
                + "then examine the result with: ls -l /dev/dri/");
        }

        // The panel is native portrait at 720 x 1280 and the appliance operates
        // in landscape, thus Avalonia turns the output and the software gets a
        // surface of 1280 x 720. It moves the touch coordinates with the image,
        // which is necessary because the touchscreen gives the coordinates of
        // the panel.
        //
        // CAUTION: this value and the `rotate=` of cmdline.txt are 180 degrees
        // apart. A person who makes the two the same gets an image that is
        // upside down. The DRM backend makes its own plane, thus what turns the
        // console does not apply to it.
        //
        // TO BE UNDERSTOOD: this value agrees with the console and with no
        // other thing, and a person selected the value of the console by an
        // examination of the display. Thus the two can be upside down together.
        // See section 8.15 of deploy/README.md.
        return builder.StartLinuxDrm(args, card: panelCard, options: new DrmOutputOptions
        {
            Scaling = 1.0,
            Orientation = SurfaceOrientation.Rotation90,
        });
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
