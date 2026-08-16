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
using GemmaTranslator.Services.Audio;
using GemmaTranslator.Services.Power;
using GemmaTranslator.Services.PushToTalk;
using GemmaTranslator.Services.Settings;
using GemmaTranslator.Services.Speakerphone;
using GemmaTranslator.Theming;
using GemmaTranslator.ViewModels;
using GemmaTranslator.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Runtime.InteropServices;

namespace GemmaTranslator;

public partial class App : Application
{
    // The registrations go in a field. A local one becomes garbage, and the
    // finalizer of that object removes the handler again.
    private static readonly List<PosixSignalRegistration> Signals = [];

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

        // The same reason, without the security part: the loopback check went
        // away with the server that took the voice of a person on a socket.
        // The read stays, because an incorrect count of the models or an
        // incorrect timeout must give a message at the start.
        _ = provider.GetRequiredService<IOptions<SpeechOptions>>().Value;

        // The same reason: a rate of 0 or a minimum press of 99999 ms gives an
        // appliance that does nothing and shows no cause.
        _ = provider.GetRequiredService<IOptions<AudioOptions>>().Value;

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

        // SECURITY CONTROL. Do not delete this, and do not change the order of
        // the two lines in Stop. The appliance takes the single view lifetime
        // below, and that lifetime raises no Exit event. Thus provider.Dispose()
        // ran on Windows only, and on the Raspberry Pi nothing disposed
        // SoundFlowAudioDevice. Its Dispose is what clears the audio buffer
        // when the software stops while a person holds a button.
        //
        // The device goes first and the container second. The container made
        // this device before MainViewModel and the speech part, and it disposes
        // in the reverse of the order it made them, so provider.Dispose() on
        // its own reaches the wipe LAST — after the view model, the speech
        // service and the cache of the models — and it catches nothing. One
        // throw from any of them, managed or from the native library, and the
        // wipe never runs. That order is not even a promise: the documents of
        // .NET do not state the order of disposal. A control that holds the
        // speech of a person must not rest on it. Dispose here is safe to call
        // two times, thus the call that the container makes after this one
        // does nothing.
        //
        // Each way that stops this appliance sends SIGTERM: systemctl stop,
        // the pkill of the notes, and the cleanup trap of start.sh. SIGINT is
        // for a person who starts the software by hand.
        SoundFlowAudioDevice audio = provider.GetRequiredService<SoundFlowAudioDevice>();

        void Stop()
        {
            audio.Dispose();
            provider.Dispose();
        }

        foreach (PosixSignal signal in (PosixSignal[])[PosixSignal.SIGTERM, PosixSignal.SIGINT])
        {
            Signals.Add(PosixSignalRegistration.Create(signal, _ => Stop()));
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

            desktop.Exit += (_, _) => Stop();
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

        // The ring opens its device here and not at the first push, for the
        // same cause as the microphone above.
        provider.GetRequiredService<ICallIndicator>().Start();

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
