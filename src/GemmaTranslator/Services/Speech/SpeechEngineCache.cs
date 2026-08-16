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
// modified. It replaces the two OrderedDict caches, the two RLocks, the
// eviction and the two lines of the log of backend/server.py lines 57 to 123.

using System.Diagnostics;
using GemmaTranslator.Services.Speech.Native;
using Microsoft.Extensions.Logging;

namespace GemmaTranslator.Services.Speech;

/// <remarks>
/// <para>
/// A model belongs to one language, and it costs about 800 MB and 2.7 s to
/// 8.1 s to make, thus the software keeps a few of them.
/// </para>
/// <para>
/// CAUTION: the lock covers the making of the model AND the work that follows
/// it, the same as upstream. Thus one transcription and one synthesis happen at
/// a time. This is not only for the eviction, which cannot take a model that
/// another thread is in: the library is before 1.0 and it gives no rule for the
/// threads, and this is the rule that this software makes for it. The two parts
/// have their own lock, and a caller must not hold the two at the same time.
/// Nothing limits how long one caller holds a lock: the timeout that did this
/// belonged to the HTTP client that this branch deleted, and a call into the
/// library gives back when it gives back.
/// </para>
/// </remarks>
internal sealed partial class SpeechEngineCache : IDisposable
{
    /// <summary>The value of upstream, at server.py:58.</summary>
    public const int DefaultCapacity = 2;

    private static readonly TimeSpan StopWait = TimeSpan.FromSeconds(1);

    private readonly string _cacheRoot;
    private readonly Lru<MoonshineTranscriber> _transcribers;
    private readonly Lru<MoonshineSynthesizer> _synthesizers;

    // These two gates are never disposed, and that is on purpose. A thread can
    // still be inside the library when the process stops, and the `finally` of
    // that thread must be able to release its gate; Release on a disposed
    // SemaphoreSlim throws ObjectDisposedException, and a throw out of Dispose
    // stops the rest of provider.Dispose(), which is where the buffers of the
    // audio get wiped. Nothing leaks either: SemaphoreSlim takes an unmanaged
    // handle only when a caller reads AvailableWaitHandle, and no code reads it.
    private readonly SemaphoreSlim _transcriberGate = new(1, 1);
    private readonly SemaphoreSlim _synthesizerGate = new(1, 1);

    private int _disposed;

    public SpeechEngineCache(int capacity, string cacheRoot, ILogger<SpeechEngineCache> logger)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);

        _cacheRoot = cacheRoot;
        _transcribers = new Lru<MoonshineTranscriber>(capacity, "STT", logger);
        _synthesizers = new Lru<MoonshineSynthesizer>(capacity, "TTS", logger);

        // A model that the disk does not hold gives an error code and no path,
        // and appsettings.json is optional: a deploy that loses it operates at
        // DefaultCapacity and looks the same.
        LogCache(logger, cacheRoot, capacity);
    }

    private bool IsDisposed => Volatile.Read(ref _disposed) != 0;

    /// <remarks>
    /// CAUTION: <paramref name="cancellationToken"/> stops the wait for the
    /// lock and it does not stop the work. A call into the library cannot be
    /// stopped, thus a caller that gives up still pays for the full call. The
    /// software depends on this: a press of a button during a synthesis lets
    /// the sound go away and does not make the appliance wait.
    /// </remarks>
    public Task<TResult> UseTranscriberAsync<TResult>(
        Language language,
        Func<MoonshineTranscriber, TResult> work,
        CancellationToken cancellationToken = default) =>
        UseAsync(
            _transcriberGate,
            _transcribers,
            language,
            static request => new MoonshineTranscriber(request.Directory, request.Architecture),
            work,
            cancellationToken);

    /// <inheritdoc cref="UseTranscriberAsync{TResult}"/>
    public Task<TResult> UseSynthesizerAsync<TResult>(
        Language language,
        Func<MoonshineSynthesizer, TResult> work,
        CancellationToken cancellationToken = default) =>
        UseAsync(
            _synthesizerGate,
            _synthesizers,
            language,
            static request => new MoonshineSynthesizer(
                request.Model.TtsLanguage, request.Model.Voice, request.AssetRoot),
            work,
            cancellationToken);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        // A gate that does not come free holds a call into the library, which
        // cannot be stopped and which measures up to 8.1 s of load plus 5.5 s
        // of work. Thus the wait is 1 s: it takes the gate of an idle appliance,
        // where nothing holds it and it comes free in microseconds, and a longer
        // wait would only stall the stop and then fail in the same way.
        //
        // A cache that keeps its models costs nothing here. moonshine_free_*
        // gives the blocks back to the allocator and it does not zero them, thus
        // the speech stays in this process either way until the process dies and
        // the kernel takes the pages, and the kernel zeroes a page before
        // another process can get it. A free under a call that is in flight, in
        // contrast, can give that slot to the model that comes next, or fault.
        // The wipes that protect the speech of a person are explicit and they
        // are not in this file.
        DisposeUnderGate(_transcriberGate, _transcribers);
        DisposeUnderGate(_synthesizerGate, _synthesizers);
    }

    private static void DisposeUnderGate<TEngine>(SemaphoreSlim gate, Lru<TEngine> cache)
        where TEngine : IDisposable
    {
        if (!gate.Wait(StopWait))
        {
            return;
        }

        try
        {
            cache.Dispose();
        }
#pragma warning disable CA1031 // A stop must not fail because a free failed.
        catch (Exception)
#pragma warning restore CA1031
        {
            // Dispose must not throw. Each free below is a P/Invoke, and a
            // library that is missing or of the wrong ABI throws from it. This
            // runs inside provider.Dispose() at SIGTERM, where a throw stops
            // every disposal that comes after it. App.axaml.cs wipes the
            // recorded audio BEFORE it calls provider.Dispose(), for the same
            // reason, and this catch is the other half of that pair. Nothing
            // is written: the process is stopping and the logger can be gone.
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<TResult> UseAsync<TEngine, TResult>(
        SemaphoreSlim gate,
        Lru<TEngine> cache,
        Language language,
        Func<EngineRequest, TEngine> make,
        Func<TEngine, TResult> work,
        CancellationToken cancellationToken)
        where TEngine : IDisposable
    {
        // Not a duplicate of the test below: Dispose gives up after 1 s, thus a
        // gate can stay held by a call of seconds, and a press then waits out
        // that whole call to get this same exception.
        ObjectDisposedException.ThrowIf(IsDisposed, this);

        SpeechModel model = SpeechModels.For(language);

        EngineRequest request = new(
            model,
            Path.Combine(_cacheRoot, model.ModelDirectory.Replace('/', Path.DirectorySeparatorChar)),
            Path.Combine(_cacheRoot, SpeechModels.TtsAssetDirectory));

        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Dispose takes this same gate and empties the cache, thus it can
            // run between the test above and this line. Without a second test
            // the software would start a load of seconds while the process
            // stops, and no code would ever free the handle that it makes.
            ObjectDisposedException.ThrowIf(IsDisposed, this);

            // The library holds the thread for seconds, and the user interface
            // has an animation and a meter of the sound on its own thread.
            return await Task.Run(
                () =>
                {
                    TEngine engine = cache.GetOrAdd(
                        language.Code, () => make(request), out bool loaded);

                    // After the load, and not before it: the new model takes what
                    // it can from the blocks that the eviction freed, and this
                    // gives the rest back to the system. See
                    // NativeHeap.ReleaseFreeMemory for the measurement. A load
                    // costs seconds and this costs nothing against it; an
                    // exchange that finds its model must not pay for it at all.
                    if (loaded)
                    {
                        NativeHeap.ReleaseFreeMemory();
                    }

                    return work(engine);
                },
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Loaded the {part} model of {language} in {seconds:F2} s.")]
    private static partial void LogLoaded(
        ILogger logger,
        string part,
        string language,
        double seconds);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Evicted the {part} model of {language}. Its next use waits for a load.")]
    private static partial void LogEvicted(ILogger logger, string part, string language);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The models are below {root}. Each cache holds {capacity} of them.")]
    private static partial void LogCache(ILogger logger, string root, int capacity);

    private readonly record struct EngineRequest(
        SpeechModel Model,
        string Directory,
        string AssetRoot)
    {
        /// <summary>The C value, for the last step before the library.</summary>
        public uint Architecture => (uint)Model.Architecture;
    }

    /// <remarks>
    /// CAUTION: this holds no lock of its own. Each caller comes through
    /// <see cref="UseAsync"/>, which has one already. Upstream needs a
    /// reentrant <c>RLock</c>; <see cref="SemaphoreSlim"/> is not reentrant and
    /// would stop for ever, thus the lock is in one place only. The flag of the
    /// dispose is a backstop for the same reason: <see cref="UseAsync"/> tests
    /// the state of the cache under that lock, and this makes a load that starts
    /// after the stop impossible and not only improbable.
    /// </remarks>
    private sealed class Lru<TEngine>(int capacity, string part, ILogger logger) : IDisposable
        where TEngine : IDisposable
    {
        private readonly int _capacity = capacity;
        private readonly LinkedList<string> _order = new();
        private readonly Dictionary<string, (TEngine Engine, LinkedListNode<string> Node)> _items =
            new(StringComparer.Ordinal);

        private bool _disposed;

        public TEngine GetOrAdd(string key, Func<TEngine> make, out bool loaded)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_items.TryGetValue(key, out (TEngine Engine, LinkedListNode<string> Node) found))
            {
                _order.Remove(found.Node);
                _order.AddLast(found.Node);

                loaded = false;

                return found.Engine;
            }

            // The oldest goes out BEFORE the new one comes in, thus the count
            // never stands at one above the limit. At 800 MB for each model,
            // that one extra is the difference between operating and swapping.
            while (_items.Count >= _capacity && _order.First is { } oldest)
            {
                _order.RemoveFirst();

                if (_items.Remove(oldest.Value, out (TEngine Engine, LinkedListNode<string> Node) gone))
                {
                    gone.Engine.Dispose();

                    LogEvicted(logger, part, oldest.Value);
                }
            }

            long startTicks = Stopwatch.GetTimestamp();

            TEngine engine = make();

            TimeSpan duration = Stopwatch.GetElapsedTime(startTicks);

            LogLoaded(logger, part, key, duration.TotalSeconds);

            _items[key] = (engine, _order.AddLast(key));

            loaded = true;

            return engine;
        }

        public void Dispose()
        {
            _disposed = true;

            foreach ((TEngine engine, _) in _items.Values)
            {
                engine.Dispose();
            }

            _items.Clear();
            _order.Clear();
        }
    }
}
