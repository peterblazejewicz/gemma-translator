// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.InteropServices;

namespace GemmaTranslator.Services.Speech.Native;

/// <remarks>
/// <para>
/// The <c>moonshine_option_t</c> that each call of the library takes is two
/// pointers to UTF-8 text:
/// <c>struct { const char* name; const char* value; }</c>. The library reads
/// the array during the call and keeps no pointer into it, thus
/// <see cref="Dispose"/> may run as soon as the call gives back.
/// </para>
/// <para>
/// CAUTION: this memory comes from <see cref="Marshal"/> and it goes back to
/// <see cref="Marshal"/>. It is OUR memory. <see cref="NativeHeap"/> is for the
/// memory of the library, which is a different heap, and the two must not
/// cross.
/// </para>
/// </remarks>
internal sealed class MoonshineOptions : IDisposable
{
    private static readonly int EntrySize = IntPtr.Size * 2;

    private readonly List<nint> _strings = [];
    private nint _array;

    public MoonshineOptions(IReadOnlyList<KeyValuePair<string, string>> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        Count = (ulong)options.Count;

        if (options.Count == 0)
        {
            return;
        }

        try
        {
            _strings.Capacity = options.Count * 2;
            _array = Marshal.AllocHGlobal(options.Count * EntrySize);

            for (int index = 0; index < options.Count; index++)
            {
                nint name = Marshal.StringToCoTaskMemUTF8(options[index].Key);

                _strings.Add(name);

                nint value = Marshal.StringToCoTaskMemUTF8(options[index].Value);

                _strings.Add(value);

                Marshal.WriteIntPtr(_array, (index * EntrySize) + 0, name);
                Marshal.WriteIntPtr(_array, (index * EntrySize) + IntPtr.Size, value);
            }
        }
        catch
        {
            // A `using` binds after the constructor gives back, thus nothing
            // else frees what the loop took before a throw.
            Dispose();

            throw;
        }
    }

    /// <summary>0 when there is no option, which the library accepts.</summary>
    public nint Array => _array;

    public ulong Count { get; }

    public void Dispose()
    {
        foreach (nint text in _strings)
        {
            Marshal.FreeCoTaskMem(text);
        }

        _strings.Clear();

        if (_array != 0)
        {
            Marshal.FreeHGlobal(_array);
            _array = 0;
        }
    }
}
