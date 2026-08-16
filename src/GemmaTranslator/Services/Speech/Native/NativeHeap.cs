// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.InteropServices;

namespace GemmaTranslator.Services.Speech.Native;

/// <summary>
/// Frees a block that the Moonshine library allocated. The library exports no
/// deallocator of its own.
/// </summary>
internal static partial class NativeHeap
{
    // DO NOT REPLACE THIS WITH Marshal.FreeHGlobal, NativeMemory.Free, OR A
    // SINGLE CROSS-PLATFORM CALL. Read this before you change it.
    //
    // moonshine.dll is linked against the shared Universal C Runtime: its
    // import table names api-ms-win-crt-heap-l1-1-0.dll, which forwards to
    // ucrtbase.dll. Memory it returns therefore belongs to the ucrtbase heap
    // and only ucrtbase's free() can release it. Freeing that memory from any
    // other allocator corrupts the process heap: Windows raises
    // STATUS_HEAP_CORRUPTION (0xC0000374) and kills the process immediately -
    // there is no exception to catch and no stack unwind. On the appliance that
    // is the whole user interface gone.
    //
    // This is not a theory. The Python package moonshine-voice 0.0.65 has this
    // exact defect: moonshine_api.py _load_libc() returns CDLL("msvcrt"), the
    // legacy runtime, which is a different heap. Constructing TextToSpeech on
    // Windows kills the interpreter every time. Freeing through ucrtbase makes
    // the same call succeed.
    //
    // Linux has one shared libc, so the appliance never showed this and any
    // test that runs only on the Raspberry Pi will pass with the wrong free.
    private const string WindowsRuntime = "ucrtbase";
    private const string UnixRuntime = "libc";

    /// <summary>A block of 0 does nothing, as free does with a null pointer.</summary>
    public static void Free(nint block)
    {
        if (block == 0)
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            WindowsFree(block);
        }
        else
        {
            UnixFree(block);
        }
    }

    [LibraryImport(WindowsRuntime, EntryPoint = "free")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial void WindowsFree(nint block);

    [LibraryImport(UnixRuntime, EntryPoint = "free")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial void UnixFree(nint block);

    /// <summary>
    /// Asks glibc to give the free memory of the heap back to the system. It
    /// does nothing on a platform that is not Linux.
    /// </summary>
    /// <remarks>
    /// MEASURED on the appliance, and this is why the call is here. A load of a
    /// model and the free that follows it leave about 380 MB of free blocks in
    /// the arenas of glibc, which keeps them for the next allocation and does
    /// not give them back. A loop of 12 loads of the SAME model holds a level
    /// of 382 MB to 415 MB and <c>mallinfo2().uordblks</c> stays at 12 MB, thus
    /// nothing leaks. But a load of a DIFFERENT model does not fit those blocks
    /// and takes new memory, thus the resident memory of the appliance grew to
    /// 6.8 GB across the twelve models of six languages.
    /// <para>
    /// One call of this method gave 411 MB back to 30 MB. The appliance has
    /// 16 KB pages (<c>getconf PAGESIZE</c>), and a page stays resident while
    /// one byte of it is in use, thus the waste here is about four times what
    /// the same code makes on a machine with 4 KB pages.
    /// </para>
    /// <para>
    /// The cost is some milliseconds against a load of 2.7 s to 8.1 s. Call it
    /// after a load and not in the path of an exchange.
    /// </para>
    /// </remarks>
    public static void ReleaseFreeMemory()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        try
        {
            // The result says whether a block went back. Nothing acts on it:
            // 0 means the arenas held nothing to give, which is not an error.
            _ = UnixMallocTrim(0);
        }
        catch (EntryPointNotFoundException)
        {
            // malloc_trim is of glibc. A Linux that uses musl has no such
            // function, and the memory then stays with the allocator. That is
            // the condition before this method existed, thus it is not an
            // error.
        }
    }

    [LibraryImport(UnixRuntime, EntryPoint = "malloc_trim")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    private static partial int UnixMallocTrim(nuint padding);
}
