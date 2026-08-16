// Copyright 2026 Piotr Błażejewicz (Peter Blazejewicz)
// SPDX-License-Identifier: Apache-2.0

using Microsoft.Extensions.Logging;

namespace GemmaTranslator.Services.Speech.Native;

/// <remarks>The directory that <see cref="MoonshineResolver"/> needs. The
/// library comes in a wheel of Python, thus that path belongs to the machine
/// and not to the software.</remarks>
internal sealed partial class MoonshineLocator(
    string? configuredDirectory,
    ILogger<MoonshineLocator> logger)
{
    private const string PackageName = "moonshine_voice";

    /// <summary>
    /// The first directory that holds the library of this platform.
    /// </summary>
    /// <remarks>
    /// The sequence is the setting, then beside the software, then each venv
    /// below the directory of the software. A deploy that puts the file beside
    /// the binary needs no setting and no venv.
    /// </remarks>
    /// <exception cref="DirectoryNotFoundException">No candidate holds it.</exception>
    public string Locate()
    {
        string file = MoonshineResolver.FileName();
        List<string> searched = [];

        if (Configured() is { } configured)
        {
            if (File.Exists(Path.Combine(configured, file)))
            {
                return configured;
            }

            // The walk below continues and it can find a different copy. With
            // no line here the log then names that other directory and nothing
            // says that the setting was wrong. start.sh writes its own value in
            // the same journal, thus the two lines can disagree.
            LogSettingHasNoLibrary(logger, configured, file);

            searched.Add(configured);
        }

        foreach (string candidate in Candidates())
        {
            if (File.Exists(Path.Combine(candidate, file)))
            {
                return candidate;
            }

            searched.Add(candidate);
        }

        // The message names each directory. The appliance shows one line of text
        // for this failure, thus the journal is the only place where a person
        // can see where the software looked.
        throw new DirectoryNotFoundException(
            $"No directory holds {file}. The software looked in {string.Join(", ", searched)}. " +
            $"It looks beside itself, and then in each venv from there to the root of the " +
            $"disk. Give Speech:LibraryDirectory if the file is in another place.");
    }

    private string? Configured() => string.IsNullOrWhiteSpace(configuredDirectory)
        ? null
        : Path.GetFullPath(configuredDirectory.Trim());

    private static IEnumerable<string> Candidates()
    {
        string? root = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);

        yield return root;

        // The walk goes to the root of the disk and stops at no count. The
        // appliance puts the binary one level below the venv and the
        // development host puts it five below, under bin/Debug/net10.0. A limit
        // of four levels reached the appliance and not the host, and the
        // software then did not start on Windows at all.
        while (root is not null)
        {
            foreach (string found in PackagesOf(Path.Combine(root, "venv")))
            {
                yield return found;
            }

            root = Path.GetDirectoryName(root);
        }
    }

    /// <returns>Each directory of the package below one venv, in a fixed order.</returns>
    /// <remarks>
    /// Windows puts the packages in <c>lib\site-packages</c> and Linux one
    /// level deeper, under the name of the version of Python. That name is not
    /// known here, thus the software reads the entries of <c>lib</c> and builds
    /// no path. CAUTION: this is one level and not a walk. A venv holds tens of
    /// thousands of files, and <see cref="SearchOption.AllDirectories"/> reads
    /// all of them and follows each symlink.
    /// </remarks>
    private static IEnumerable<string> PackagesOf(string venv)
    {
        string lib = Path.Combine(venv, "lib");

        if (!Directory.Exists(lib))
        {
            yield break;
        }

        string direct = Path.Combine(lib, "site-packages", PackageName);

        if (Directory.Exists(direct))
        {
            yield return direct;
        }

        // The order of the entries of a directory belongs to the file system.
        // A sort makes the winner the same on each start.
        foreach (string version in Directory.EnumerateDirectories(lib).Order(StringComparer.Ordinal))
        {
            string found = Path.Combine(version, "site-packages", PackageName);

            if (Directory.Exists(found))
            {
                yield return found;
            }
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "Speech:LibraryDirectory is {directory} and that directory holds no {file}. The software looks in the other places.")]
    private static partial void LogSettingHasNoLibrary(
        ILogger logger,
        string directory,
        string file);
}
