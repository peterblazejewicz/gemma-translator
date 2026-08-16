// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0

namespace GemmaTranslator.Services.Speech.Native;

/// <summary>
/// Owns one handle of the Moonshine library and frees it one time.
/// </summary>
/// <remarks>
/// <para>
/// CAUTION: a handle of this library is an <c>int32</c> and not a pointer, thus
/// nothing about it looks wrong. A handle that a person frees and then uses
/// names a slot that the library can give to the model that comes next, and the
/// call then operates on the wrong model and gives no error. Thus
/// <see cref="Value"/> throws after <see cref="Dispose"/>. That test is not a
/// lock: a caller holds a copy of the number for the length of its call, and
/// the gate of SpeechEngineCache, which puts the eviction and the use under one
/// lock, is what stops a free in that time. The interlocked pair here does not
/// stop such a use. It makes it throw, and not operate on the wrong model.
/// </para>
/// <para>
/// A <c>SafeHandle</c> is the usual type for this work, and it does not fit: it
/// marshals as an <c>IntPtr</c>, but a handle here is an <c>int32</c> slot
/// index. Each call would go through <c>DangerousGetHandle</c> and lose the
/// count of the references that is the one function of the type. The critical
/// finalizer goes away with it: a transcriber that no code disposes holds about
/// 800 MB of native memory until the process stops.
/// </para>
/// </remarks>
internal abstract class MoonshineHandle : IDisposable
{
    private const int Freed = -1;

    private int _handle;

    protected MoonshineHandle(int handle) => _handle = handle;

    public int Value
    {
        get
        {
            int handle = Volatile.Read(ref _handle);

            ObjectDisposedException.ThrowIf(handle == Freed, this);

            return handle;
        }
    }

    public void Dispose()
    {
        // A second free of the same handle is the same defect as a use after
        // free: the slot can belong to another model by then.
        int handle = Interlocked.Exchange(ref _handle, Freed);

        if (handle != Freed)
        {
            Free(handle);
        }
    }

    protected abstract void Free(int handle);
}

internal sealed class TranscriberHandle(int handle) : MoonshineHandle(handle)
{
    protected override void Free(int value) => MoonshineLibrary.FreeTranscriber(value);
}

internal sealed class SynthesizerHandle(int handle) : MoonshineHandle(handle)
{
    protected override void Free(int value) => MoonshineLibrary.FreeTtsSynthesizer(value);
}
