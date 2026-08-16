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
// This file is part of a fork of google-gemma/gemma-translator and has been
// modified. It replaces transcribeAudio of frontend/src/utils/api.js, the
// /api/tts calls of frontend/src/TranslatorApp.jsx, and the three handlers of
// backend/server.py.

using System.Diagnostics;
using GemmaTranslator.Services.Speech.Native;
using Microsoft.Extensions.Logging;

namespace GemmaTranslator.Services.Speech;

/// <summary>
/// The speech-to-text part and the text-to-speech part, in this process.
/// </summary>
/// <remarks>
/// There is no server and no port: upstream put an HTTP server of Python
/// between the user interface and this same library, and that server did the
/// base64, the JSON and the WAV that this code now does not need. The time that
/// this saves is small against a synthesis of seconds. What it removes is a
/// service with no authentication that took the recorded voice of a person on a
/// socket.
/// </remarks>
public sealed partial class MoonshineSpeechService : ISpeechService
{
    /// <remarks>
    /// The rate that <see cref="ISpeechService"/> makes a condition of its
    /// samples. Upstream gives the same number to the same call at
    /// server.py:216, and neither side checks it.
    /// </remarks>
    private const int TranscribeSampleRate = 16000;

    private readonly SpeechEngineCache _engines;
    private readonly MoonshineLocator _locator;
    private readonly Lock _libraryGate = new();
    private readonly ILogger<MoonshineSpeechService> _logger;

    private volatile bool _libraryOpen;

    internal MoonshineSpeechService(
        SpeechEngineCache engines,
        MoonshineLocator locator,
        ILogger<MoonshineSpeechService> logger)
    {
        ArgumentNullException.ThrowIfNull(engines);
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(logger);

        _engines = engines;
        _locator = locator;
        _logger = logger;
    }

    public async Task<string> TranscribeAsync(
        ReadOnlyMemory<float> samples,
        Language language,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(language);

        if (samples.IsEmpty)
        {
            throw new SpeechException("There is no audio to transcribe.");
        }

        long startTicks = Stopwatch.GetTimestamp();

        // The samples go to the library as they are. Upstream made a base64
        // copy of them for the wire, and that copy is the reason the old code
        // had a buffer to wipe here. Recording.Dispose wipes the one buffer
        // that now holds this audio.
        string text = await CallAsync(
            () => _engines.UseTranscriberAsync(
                language,
                engine => engine.Transcribe(samples.Span, TranscribeSampleRate),
                cancellationToken),
            "The transcription").ConfigureAwait(false);

        TimeSpan duration = Stopwatch.GetElapsedTime(startTicks);

        // An empty text is not an error. Moonshine gives no words when the
        // microphone heard no speech, and upstream puts that empty text on the
        // display. The translation part is not the same.
        LogTranscribed(_logger, language.Code, samples.Length, duration.TotalSeconds, text.Length);

        return text;
    }

    public async Task<SpokenAudio> SynthesizeAsync(
        string text,
        Language language,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(language);

        if (string.IsNullOrWhiteSpace(text))
        {
            throw new SpeechException("There is no text to speak.");
        }

        long startTicks = Stopwatch.GetTimestamp();

        Synthesis synthesis = await CallAsync(
            () => _engines.UseSynthesizerAsync(
                language,
                engine => engine.Synthesize(text),
                cancellationToken),
            "The synthesis").ConfigureAwait(false);

        if (synthesis.Samples.Length == 0)
        {
            throw new SpeechException("The synthesizer made no audio.");
        }

        byte[] wav;

        try
        {
            wav = WavAudio.FromSamples(synthesis.Samples, synthesis.SampleRate);
        }
        finally
        {
            // SECURITY CONTROL. Do not delete this line. These samples are the
            // translation of what a person said. The WAV above is a second copy
            // of the same sound, and the player wipes that one; without this
            // line the first copy stays readable in a memory dump of the process
            // until a later allocation takes the memory.
            Array.Clear(synthesis.Samples);
        }

        TimeSpan duration = Stopwatch.GetElapsedTime(startTicks);

        LogSpoke(_logger, language.Code, text.Length, duration.TotalSeconds, wav.Length);

        return new SpokenAudio(wav);
    }

    public async Task WarmAsync(Language language, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(language);

        long startTicks = Stopwatch.GetTimestamp();

        // The two parts one after the other, and never the two locks together.
        // handle_warm of upstream makes the same promise at server.py:254.
        await CallAsync(
            () => _engines.UseTranscriberAsync(language, static _ => true, cancellationToken),
            "The load of the transcriber").ConfigureAwait(false);

        await CallAsync(
            () => _engines.UseSynthesizerAsync(language, static _ => true, cancellationToken),
            "The load of the synthesizer").ConfigureAwait(false);

        double seconds = Stopwatch.GetElapsedTime(startTicks).TotalSeconds;

        LogWarmed(_logger, language.Code, seconds);
    }

    // One time and at the first call, and not at the start: upstream keeps its
    // user interface when backend/server.py is dead, and it fails at each press.
    // Only a success stays, the same as the import in each handler of upstream:
    // the appliance has no keyboard, thus a failure that stayed makes it deaf.
    private void OpenLibrary()
    {
        lock (_libraryGate)
        {
            if (_libraryOpen)
            {
                return;
            }

            string directory = _locator.Locate();

            MoonshineResolver.Register(directory);

            int version = MoonshineLibrary.GetVersion();

            LogLibrary(_logger, directory, version);

            if (version != MoonshineLibrary.HeaderVersion)
            {
                throw new SpeechException(
                    $"The Moonshine library at {directory} gives version {version}, and this " +
                    $"software reads the structures of version {MoonshineLibrary.HeaderVersion}.");
            }

            _libraryOpen = true;
        }
    }

    private async Task<TResult> CallAsync<TResult>(
        Func<Task<TResult>> work,
        string operation)
    {
        try
        {
            // In the try, thus a library that does not open gives the message of
            // a press and not an exception that no caller expects. Task.Run keeps
            // the walk of the venv and the first P/Invoke off the thread of the
            // press, which is the thread of the user interface.
            if (!_libraryOpen)
            {
                await Task.Run(OpenLibrary).ConfigureAwait(false);
            }

            return await work().ConfigureAwait(false);
        }
        catch (MoonshineException exception)
        {
            LogEngineFailed(_logger, operation, exception.Code, exception);

            throw new SpeechException($"{operation} did not succeed.", exception);
        }
        catch (KeyNotFoundException exception)
        {
            throw new SpeechException(exception.Message, exception);
        }
        catch (DllNotFoundException exception)
        {
            LogNoLibrary(_logger, exception);

            throw new SpeechException("The speech library is not installed.", exception);
        }
        catch (DirectoryNotFoundException exception)
        {
            LogNoLibrary(_logger, exception);

            throw new SpeechException("The speech library is not on this machine.", exception);
        }
        catch (EntryPointNotFoundException exception)
        {
            LogNoLibrary(_logger, exception);

            throw new SpeechException(
                "The speech library does not hold the functions that this software calls.",
                exception);
        }
        catch (BadImageFormatException exception)
        {
            LogNoLibrary(_logger, exception);

            throw new SpeechException(
                "The speech library is not for the processor of this machine.",
                exception);
        }
        catch (ObjectDisposedException exception)
        {
            LogStopping(_logger, operation, exception);

            throw new SpeechException($"{operation} stopped with the software.", exception);
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Transcribed {samples} samples of {language} in {seconds:F2} s. The text has {characters} characters.")]
    private static partial void LogTranscribed(
        ILogger logger,
        string language,
        int samples,
        double seconds,
        int characters);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The models of {language} are ready. The call took {seconds:F2} s; a value near 0 says that the software held them already.")]
    private static partial void LogWarmed(ILogger logger, string language, double seconds);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Spoke {characters} characters of {language} in {seconds:F2} s. The audio is {bytes} bytes.")]
    private static partial void LogSpoke(
        ILogger logger,
        string language,
        int characters,
        double seconds,
        int bytes);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "{operation} gave the Moonshine error {code}.")]
    private static partial void LogEngineFailed(
        ILogger logger,
        string operation,
        int code,
        Exception exception);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The Moonshine library is at {directory}, and it gives version {version}.")]
    private static partial void LogLibrary(ILogger logger, string directory, int version);

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The Moonshine library did not load. The software cannot hear and cannot speak.")]
    private static partial void LogNoLibrary(ILogger logger, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "{operation} stopped because the software is closing.")]
    private static partial void LogStopping(ILogger logger, string operation, Exception exception);
}
