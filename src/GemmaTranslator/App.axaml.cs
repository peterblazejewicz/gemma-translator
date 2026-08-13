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
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GemmaTranslator.Configuration;
using GemmaTranslator.Services;
using GemmaTranslator.ViewModels;
using GemmaTranslator.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GemmaTranslator;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Avalonia calls this method also when there is no lifetime, which is
        // what SetupWithoutStarting does. The work below opens the microphone,
        // and a process that shows no view cannot release it.
        if (ApplicationLifetime is null)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

        IConfiguration configuration = ServiceRegistration.BuildConfiguration();

        ServiceCollection services = new();
        services.AddGemmaTranslator(configuration);

        ServiceProvider provider = services.BuildServiceProvider();

        // SECURITY CONTROL. Do not delete this line as a redundant read. It is
        // what runs the loopback check in LiteRtOptionsValidator. Options are
        // validated lazily, so without this line the check happens on the
        // first translation instead — which is after somebody has already
        // spoken into the appliance, and after their speech has already been
        // sent to whatever endpoint the settings named.
        LiteRtOptions liteRt = provider.GetRequiredService<IOptions<LiteRtOptions>>().Value;

        // The same reason: a rate of 0 or a minimum press of 99999 ms gives an
        // appliance that does nothing and shows no cause.
        _ = provider.GetRequiredService<IOptions<AudioOptions>>().Value;

        // The values go in a local first. CA1873 does not permit a call in the
        // argument list of a log method, because the call operates also if the
        // level is off.
        ILogger<App> logger = provider.GetRequiredService<ILogger<App>>();

        bool hasApiKey = liteRt.ApiKey.Length != 0;

        LogSettings(logger, liteRt.ModelName, hasApiKey);

        provider.GetRequiredService<Appearance>()
            .Apply(provider.GetRequiredService<IUserSettingsStore>().Current);

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
            LogNoMicrophoneAtStart(logger, exception);
        }

        MainViewModel viewModel = provider.GetRequiredService<MainViewModel>();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            MainWindow window = new()
            {
                DataContext = viewModel,
            };

            provider.GetRequiredService<IPushToTalk>().Start(window);

            desktop.MainWindow = window;

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
        }

        provider.GetRequiredService<IPowerMonitor>().Start();

        base.OnFrameworkInitializationCompleted();
    }

    /// <remarks>
    /// CAUTION: the key is a secret. This method writes only if a key is there.
    /// Do not put the key in the message.
    /// </remarks>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The model is {model}. A key is supplied: {hasApiKey}.")]
    private static partial void LogSettings(ILogger logger, string model, bool hasApiKey);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The microphone did not open at the start. Each press gives an error until a person starts the software again.")]
    private static partial void LogNoMicrophoneAtStart(ILogger logger, Exception exception);
}
