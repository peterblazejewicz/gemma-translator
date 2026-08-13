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

using GemmaTranslator.Configuration;
using GemmaTranslator.Services;
using GemmaTranslator.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GemmaTranslator;

public static class ServiceRegistration
{
    /// <remarks>
    /// The base path is <see cref="AppContext.BaseDirectory"/> and not the
    /// current directory. systemd starts the software with a current directory of
    /// <c>/</c>, where there is no file.
    /// </remarks>
    public static IConfiguration BuildConfiguration()
        => new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables("GEMMA_")
            .Build();

    public static IServiceCollection AddGemmaTranslator(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton(configuration);

        services.AddLogging(logging =>
        {
            logging.AddConfiguration(configuration.GetSection("Logging"));

            // systemd puts the console in the journal. A WinExe has no
            // console, thus Windows needs the debug output.
            logging.AddConsole();
            logging.AddDebug();
        });

        services.AddOptions<LiteRtOptions>()
            .Bind(configuration.GetSection(LiteRtOptions.SectionName));

        services.AddSingleton<IValidateOptions<LiteRtOptions>, LiteRtOptionsValidator>();

        // CAUTION: MainViewModel is a singleton and it keeps this client for
        // the life of the process. Thus the handler never rotates, and the
        // lifetime is infinite on purpose. With the default of 2 minutes the
        // factory puts each expired handler in a queue and wakes a timer every
        // 10 seconds to collect it. The handler is not collectable while the
        // singleton holds the client, so that timer never stops. The appliance
        // operates for days on a battery.
        //
        // One endpoint on the local machine has no DNS that can change, thus
        // one handler for the life of the process is correct here.
        services.AddHttpClient<ITranslator, LiteRtTranslator>((provider, client) =>
        {
            LiteRtOptions options = provider
                .GetRequiredService<IOptions<LiteRtOptions>>().Value;

            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

            // SendAsync holds the full answer in memory before it gives the
            // response. 2 GB is the default, and the Raspberry Pi has 8 GB
            // with Gemma already in it.
            client.MaxResponseContentBufferSize = 1024 * 1024;
        })
            .SetHandlerLifetime(Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                // An OpenAI endpoint does not redirect. With the default,
                // status 307 sends the text that the person spoke to a machine
                // that the operator did not select. .NET removes the
                // Authorization header on a redirect, and it sends the body.
                AllowAutoRedirect = false,
            });

        services.AddOptions<AudioOptions>()
            .Bind(configuration.GetSection(AudioOptions.SectionName));

        services.AddSingleton<IValidateOptions<AudioOptions>, AudioOptionsValidator>();

        services.AddSingleton<IAudioCapture, SoundFlowAudioCapture>();

        // The store reads the file in its constructor, thus it is a singleton
        // and the disk gets one read.
        services.AddSingleton<IUserSettingsStore, JsonUserSettingsStore>();
        services.AddSingleton<Appearance>();

        // The buttons: Avalonia gives no key event on the Raspberry Pi,
        // because the DRM backend raises a pointer event and a touch event
        // only. Thus Linux reads /dev/input and Windows uses the keys of
        // Avalonia.
        //
        // The electrical supply: the appliance has an X1201 UPS and the
        // development host has none.
        if (OperatingSystem.IsLinux())
        {
            services.AddSingleton<IPushToTalk, EvdevPushToTalk>();
            services.AddSingleton<IPowerMonitor, SysfsPowerMonitor>();
        }
        else
        {
            services.AddSingleton<IPushToTalk, KeyboardPushToTalk>();
            services.AddSingleton<IPowerMonitor, NoPowerMonitor>();
        }

        // The settings screen is a singleton because the selections of a
        // person must stay while the screen opens and closes.
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();

        return services;
    }
}
