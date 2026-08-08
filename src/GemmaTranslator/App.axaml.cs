// Copyright 2026 Google LLC
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

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using GemmaTranslator.Configuration;
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

        // The values go in a local first. CA1873 does not permit a call in the
        // argument list of a log method, because the call operates also if the
        // level is off.
        ILogger<App> logger = provider.GetRequiredService<ILogger<App>>();
        string endpoint = liteRt.GetBaseUrl();
        bool hasApiKey = liteRt.ApiKey.Length != 0;

        LogSettings(logger, endpoint, liteRt.ModelName, hasApiKey);

        MainViewModel viewModel = provider.GetRequiredService<MainViewModel>();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };

            // The container holds each service that must be disposed, for
            // example the audio device. Windows can stop the software, thus
            // the container must go with it.
            desktop.Exit += (_, _) => provider.Dispose();
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = new MainSingleView
            {
                DataContext = viewModel,
            };

            // The Raspberry Pi has no exit. systemd stops the process.
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
}
