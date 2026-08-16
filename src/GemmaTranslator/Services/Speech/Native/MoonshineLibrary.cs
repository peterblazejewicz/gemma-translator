// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0

using System.Runtime.InteropServices;

namespace GemmaTranslator.Services.Speech.Native;

/// <remarks>
/// The declarations of the C API of Moonshine, and nothing else. Do not add a
/// method that makes two calls or that decides something, so that a person can
/// compare this against the header of a version that comes later. CAUTION: the
/// ABI is before 1.0. Each load function takes <see cref="HeaderVersion"/> as
/// its last argument, and the library refuses a value that it does not know.
/// </remarks>
internal static partial class MoonshineLibrary
{
    /// <remarks>
    /// A measurement gives 20000 for the aarch64 library of the appliance and
    /// for the win-x64 library of the development host, thus one value serves
    /// the two platforms.
    /// </remarks>
    public const int HeaderVersion = 20000;

    /// <summary>
    /// The name that <see cref="MoonshineResolver"/> answers, and not the name
    /// of a file: the library is in the cache of the person.
    /// </summary>
    public const string LibraryName = "moonshine";

    [LibraryImport(LibraryName, EntryPoint = "moonshine_get_version")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int GetVersion();

    /// <remarks>The text belongs to the library and the caller does not free it.</remarks>
    [LibraryImport(LibraryName, EntryPoint = "moonshine_error_to_string")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial nint ErrorToString(int code);

    /// <returns>
    /// CAUTION: this is not an error code. A value of 0 or more is the handle,
    /// and a value below 0 is the error. See <see cref="MoonshineException"/>.
    /// </returns>
    [LibraryImport(
        LibraryName,
        EntryPoint = "moonshine_load_transcriber_from_files",
        StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int LoadTranscriberFromFiles(
        string modelDirectory,
        uint modelArchitecture,
        nint options,
        ulong optionCount,
        int headerVersion);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_free_transcriber")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void FreeTranscriber(int handle);

    /// <remarks>
    /// <c>flags</c> is 0. <c>transcript</c> comes back as a
    /// <c>transcript_t*</c> that the library owns, thus the caller does not
    /// free it. The result is 0, or an error below 0.
    /// </remarks>
    [LibraryImport(LibraryName, EntryPoint = "moonshine_transcribe_without_streaming")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int TranscribeWithoutStreaming(
        int handle,
        ref float audio,
        ulong sampleCount,
        int sampleRate,
        uint flags,
        out nint transcript);

    // moonshine_transcript_to_string is NOT declared here on purpose. It gives
    // a text for a person to read and not the words, thus the software reads
    // the structure. See MoonshineTranscript.

    /// <remarks>
    /// IMPORTANT: give 0 for <c>files</c> and 0 for <c>fileCount</c>. The
    /// library then finds its own models below the <c>g2p_root</c> option. The
    /// Python package does the same at tts.py:586, thus the name of the
    /// function is misleading. The result is the handle, or an error below 0.
    /// </remarks>
    [LibraryImport(
        LibraryName,
        EntryPoint = "moonshine_create_tts_synthesizer_from_files",
        StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int CreateTtsSynthesizerFromFiles(
        string language,
        nint files,
        ulong fileCount,
        nint options,
        ulong optionCount,
        int headerVersion);

    [LibraryImport(LibraryName, EntryPoint = "moonshine_free_tts_synthesizer")]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial void FreeTtsSynthesizer(int handle);

    /// <remarks>
    /// CAUTION: the caller owns the samples of <c>audio</c> and must give them
    /// to <see cref="NativeHeap"/>. The result is 0, or an error below 0.
    /// </remarks>
    [LibraryImport(
        LibraryName,
        EntryPoint = "moonshine_text_to_speech",
        StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(System.Runtime.CompilerServices.CallConvCdecl)])]
    public static partial int TextToSpeech(
        int handle,
        string text,
        nint options,
        ulong optionCount,
        out nint audio,
        out ulong sampleCount,
        out int sampleRate);
}
