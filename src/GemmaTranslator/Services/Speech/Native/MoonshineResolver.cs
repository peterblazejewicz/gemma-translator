// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0

using System.Reflection;
using System.Runtime.InteropServices;

namespace GemmaTranslator.Services.Speech.Native;

/// <remarks>
/// <para>
/// The library is not beside the software. It comes in a wheel of Python and it
/// stays in the directory of that package, thus the default search does not
/// find it.
/// </para>
/// <para>
/// CAUTION: <c>moonshine</c> imports <c>onnxruntime</c>, which is beside it in
/// that same directory. Give an absolute path to
/// <see cref="NativeLibrary.Load(string)"/>: the loader of Windows then looks
/// in the directory of the library for its dependencies. A relative name makes
/// the second library not load, and the message names the first one.
/// </para>
/// </remarks>
internal static class MoonshineResolver
{
    private static readonly Lock Gate = new();
    private static string? _directory;
    private static bool _registered;

    /// <summary>
    /// The first call decides the directory and each call after it does
    /// nothing.
    /// </summary>
    public static void Register(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        lock (Gate)
        {
            if (_registered)
            {
                return;
            }

            string file = Path.Combine(directory, FileName());

            if (!File.Exists(file))
            {
                throw new DirectoryNotFoundException(
                    $"The Moonshine library is not at {file}.");
            }

            _directory = Path.GetFullPath(directory);

            NativeLibrary.SetDllImportResolver(
                Assembly.GetExecutingAssembly(),
                Resolve);

            _registered = true;
        }
    }

    public static string FileName() => OperatingSystem.IsWindows()
        ? "moonshine.dll"
        : "libmoonshine.so";

    private static nint Resolve(string library, Assembly assembly, DllImportSearchPath? path)
    {
        if (!string.Equals(library, MoonshineLibrary.LibraryName, StringComparison.Ordinal))
        {
            // The C runtime of NativeHeap comes through here also. Give it back
            // to the default search, which is where those names belong.
            return 0;
        }

        return NativeLibrary.Load(Path.Combine(_directory!, FileName()));
    }
}
