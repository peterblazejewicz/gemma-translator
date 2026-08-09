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

using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GemmaTranslator.Configuration;
using GemmaTranslator.Fonts;
using GemmaTranslator.Services;
using GemmaTranslator.ViewModels;
using GemmaTranslator.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GemmaTranslator;

/// <summary>
/// The Avalonia application.
/// </summary>
public partial class App : Application
{
    // A static field, because the registration must not go away.
    private static PosixSignalRegistration? _stopSignal;

    /// <inheritdoc/>
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    /// <inheritdoc/>
    /// <remarks>
    /// The two heads put the same <see cref="Views.MainView"/> in a different
    /// container. Windows has a window manager and gets a window. The
    /// Raspberry Pi has no window manager and gets one view that fills the
    /// display.
    /// </remarks>
    public override void OnFrameworkInitializationCompleted()
    {
        IConfiguration configuration = ServiceRegistration.BuildConfiguration();

        ServiceCollection services = new();
        services.AddGemmaTranslator(configuration);

        ServiceProvider provider = services.BuildServiceProvider();

        // Get the settings now. This is the moment that the validator
        // operates. An incorrect value must stop the software here, with a
        // clear message in the log, and not at the first translation. The
        // appliance has no settings screen that can correct it later.
        LiteRtOptions liteRt = provider.GetRequiredService<IOptions<LiteRtOptions>>().Value;

        // The same reason: a rate of 0 or a minimum press of 99999 ms gives an
        // appliance that does nothing and shows no cause.
        _ = provider.GetRequiredService<IOptions<AudioOptions>>().Value;

        // The values go in a local first. CA1873 does not permit a call in the
        // argument list of a log method, because the call operates also if the
        // level is off.
        ILogger<App> logger = provider.GetRequiredService<ILogger<App>>();

        // The safe form, because an address can hold a user and a password.
        string endpoint = liteRt.GetSafeDisplayUrl();
        bool hasApiKey = liteRt.ApiKey.Length != 0;

        LogSettings(logger, endpoint, liteRt.ModelName, hasApiKey);

        // The fonts are the largest risk at the start on Raspberry Pi OS Lite.
        // This line puts the condition of each font in the journal, because
        // the appliance has no console. See Fonts/FontCheck.cs.
        FontCheck.Run(logger);

        // Open the microphone before the first press. A test measured 1.22 s
        // from the start of the device to the first sample with a Jabra
        // Speak2 40, thus a device that opens at the press loses the first
        // word. The line in the log also names the microphone that the
        // software selected.
        try
        {
            provider.GetRequiredService<IAudioCapture>().Prepare();
        }
        catch (AudioCaptureException exception)
        {
            // The software continues. A person can connect the microphone
            // after the start, and the first press opens it again.
            LogNoMicrophoneAtStart(logger, exception);
        }

        MainViewModel viewModel = provider.GetRequiredService<MainViewModel>();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow window = new()
            {
                DataContext = viewModel,
            };

            // The keys of the two people arrive at the top level, because
            // Avalonia sends a key to the control that has the focus and this
            // view has no control that takes the focus.
            provider.GetRequiredService<IPushToTalk>().Start(window);

            desktop.MainWindow = window;

            // The container holds each service that must be disposed, for
            // example the audio device. Windows can stop the software, thus
            // the container must go with it.
            desktop.Exit += (_, _) => provider.Dispose();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            MainSingleView view = new()
            {
                DataContext = viewModel,
            };

            singleView.MainView = view;

            // The Raspberry Pi reads /dev/input, thus it needs no top level.
            provider.GetRequiredService<IPushToTalk>().Start(null);

            // The Raspberry Pi has no exit of the application. systemd sends
            // SIGTERM, thus this is the one location that can stop the
            // microphone in an orderly manner.
            _stopSignal = PosixSignalRegistration.Create(
                PosixSignal.SIGTERM,
                _ => provider.Dispose());
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Writes the settings of the LiteRT-LM server to the log.
    /// </summary>
    /// <remarks>
    /// CAUTION: the key is a secret. This method writes only if a key is
    /// there. Do not put the key in the message.
    /// </remarks>
    /// <param name="logger">The logger.</param>
    /// <param name="endpoint">The address of the server.</param>
    /// <param name="model">The name of the model.</param>
    /// <param name="hasApiKey">True if a key is supplied.</param>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The LiteRT-LM endpoint is {endpoint} and the model is {model}. A key is supplied: {hasApiKey}.")]
    private static partial void LogSettings(
        ILogger logger,
        string endpoint,
        string model,
        bool hasApiKey);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The microphone did not open at the start. The software tries again at the first press.")]
    private static partial void LogNoMicrophoneAtStart(ILogger logger, Exception exception);
}
