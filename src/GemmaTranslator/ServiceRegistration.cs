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
using GemmaTranslator.Services.Audio;
using GemmaTranslator.Services.Branding;
using GemmaTranslator.Services.Power;
using GemmaTranslator.Services.PushToTalk;
using GemmaTranslator.Services.Settings;
using GemmaTranslator.Services.Speakerphone;
using GemmaTranslator.Services.Speech;
using GemmaTranslator.Services.Speech.Native;
using GemmaTranslator.Services.Translation;
using GemmaTranslator.Theming;
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
            // response. 2 GB is the default, and a measurement gives 4 GB on
            // the appliance with Gemma already in it.
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

                // SECURITY CONTROL. Do not delete this line. Without it the
                // loopback check in LiteRtOptionsValidator can be bypassed by
                // an environment variable. On Linux, .NET builds its default
                // proxy from HTTP_PROXY / HTTPS_PROXY / ALL_PROXY, and it
                // applies that proxy to 127.0.0.1 as well — there is no
                // automatic exception for loopback, only the NO_PROXY list.
                // So with ALL_PROXY set, a request the validator has approved
                // as "local" is still sent to whatever host that variable
                // names, carrying the words the person spoke. Setting the
                // address to a loopback address is not on its own enough to
                // keep the speech on this machine; this line is the other half.
                UseProxy = false,
            });

        services.AddOptions<SpeechOptions>()
            .Bind(configuration.GetSection(SpeechOptions.SectionName));

        services.AddSingleton<IValidateOptions<SpeechOptions>, SpeechOptionsValidator>();

        // The speech has no client and no handler. It was an HTTP call to a
        // server of Python on port 3000, and it is a call into a library in
        // this process. The two controls that stood here, the loopback address
        // and UseProxy, protected a socket that no longer exists.
        //
        // One cache holds the models, thus it is a singleton: a model is about
        // 800 MB and a second container of them would take the memory two
        // times.
        services.AddSingleton(provider =>
        {
            SpeechOptions options = provider
                .GetRequiredService<IOptions<SpeechOptions>>().Value;

            return new SpeechEngineCache(
                options.MaxModels,
                options.ResolveModelCacheRoot(),
                provider.GetRequiredService<ILogger<SpeechEngineCache>>());
        });

        services.AddSingleton(provider => new MoonshineLocator(
            provider.GetRequiredService<IOptions<SpeechOptions>>().Value.LibraryDirectory,
            provider.GetRequiredService<ILogger<MoonshineLocator>>()));

        // The library is not opened here. MoonshineSpeechService finds it at the
        // first press: a machine with no library must show the user interface and
        // give its message on that press, and not a black display at the start.
        services.AddSingleton<ISpeechService>(provider => new MoonshineSpeechService(
            provider.GetRequiredService<SpeechEngineCache>(),
            provider.GetRequiredService<MoonshineLocator>(),
            provider.GetRequiredService<ILogger<MoonshineSpeechService>>()));

        services.AddOptions<AudioOptions>()
            .Bind(configuration.GetSection(AudioOptions.SectionName));

        services.AddSingleton<IValidateOptions<AudioOptions>, AudioOptionsValidator>();

        // One object opens the speakerphone, and the two interfaces are the two
        // directions of it. Two objects open the same USB device two times, and
        // the microphone needs the playback interface of that same device. See
        // Services/Audio/SoundFlowAudioDevice.cs.
        services.AddSingleton<SoundFlowAudioDevice>();
        services.AddSingleton<IAudioCapture>(
            static provider => provider.GetRequiredService<SoundFlowAudioDevice>());
        services.AddSingleton<IAudioPlayback>(
            static provider => provider.GetRequiredService<SoundFlowAudioDevice>());

        // The store reads the file in its constructor, thus it is a singleton
        // and the disk gets one read.
        services.AddSingleton<IUserSettingsStore, JsonUserSettingsStore>();
        services.AddSingleton<Appearance>();

        // The mark reads its file and rasterizes it in the constructor, thus it
        // is a singleton and that work happens one time.
        services.AddSingleton<IBrandMark, SvgBrandMark>();

        // The buttons: Avalonia gives no key event on the Raspberry Pi,
        // because the DRM backend raises a pointer event and a touch event
        // only. Thus Linux reads /dev/input and Windows uses the keys of
        // Avalonia.
        //
        // The electrical supply: the appliance has an X1201 UPS and the
        // development host has none.
        // The ring of the speakerphone: Linux writes the report to
        // /dev/hidraw, and Windows gives that device to its own driver stack.
        if (OperatingSystem.IsLinux())
        {
            services.AddSingleton<IPushToTalk, EvdevPushToTalk>();
            services.AddSingleton<IPowerMonitor, SysfsPowerMonitor>();
            services.AddSingleton<ICallIndicator, HidRawCallIndicator>();
        }
        else
        {
            services.AddSingleton<IPushToTalk, KeyboardPushToTalk>();
            services.AddSingleton<IPowerMonitor, NoPowerMonitor>();
            services.AddSingleton<ICallIndicator, NoCallIndicator>();
        }

        // The settings screen is a singleton because the selections of a
        // person must stay while the screen opens and closes.
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();

        return services;
    }
}
