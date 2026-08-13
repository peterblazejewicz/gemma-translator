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
// been modified. It replaces the localStorage calls of
// frontend/src/App.jsx:40 and App.jsx:48.

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace GemmaTranslator.Services;

/// <summary>
/// Keeps the settings of a person in one JSON file.
/// </summary>
public sealed partial class JsonUserSettingsStore : IUserSettingsStore
{
    /// <summary>
    /// The directory below the settings directory of the account.
    /// </summary>
    private const string DirectoryName = "gemma-translator";

    /// <summary>
    /// The name of the file.
    /// </summary>
    /// <remarks>
    /// It is not <c>appsettings.json</c>. That file holds the settings of the
    /// operator, git holds it, and a person cannot change it on the display.
    /// </remarks>
    private const string FileName = "user-settings.json";

    private readonly ILogger<JsonUserSettingsStore> _logger;
    /// <summary>The file, or <c>null</c> if the account has no settings directory.</summary>
    private readonly string? _path;

    private UserSettings _current;

    /// <summary>
    /// Initializes a new instance of the <see cref="JsonUserSettingsStore"/>
    /// class and reads the file.
    /// </summary>
    /// <param name="logger">The logger from the container.</param>
    public JsonUserSettingsStore(ILogger<JsonUserSettingsStore> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;

        // The settings directory of the account, which is %APPDATA% on Windows
        // and $XDG_CONFIG_HOME or ~/.config on Linux. It is not the directory
        // of the binary: a deployment replaces that directory, and the
        // selections of a person must stay.
        string root = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData,
            Environment.SpecialFolderOption.Create);

        // CAUTION: .NET gives an empty text on Linux if the account has no HOME
        // and no XDG_CONFIG_HOME. A systemd unit with ProtectHome or with
        // DynamicUser makes that condition. The path would then be relative and
        // it would go to WorkingDirectory, which a deployment replaces, and the
        // selections of a person would go away with no cause in the journal.
        if (root.Length == 0)
        {
            LogNoSettingsDirectory(_logger);
            _path = null;
            _current = UserSettings.Default;
            return;
        }

        _path = Path.Combine(root, DirectoryName, FileName);
        _current = Read(_path);
    }

    /// <inheritdoc/>
    public UserSettings Current => _current;

    /// <inheritdoc/>
    public void Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        UserSettings next = settings.Sanitized();

        // A record compares by value. The settings screen keeps each touch, and
        // a person who holds the plus of the count of bars makes one write for
        // each step. This appliance writes to an SD card.
        bool unchanged = next == _current;

        _current = next;

        if (_path is null || unchanged)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            // CAUTION: write beside the file and then move it. A move on one
            // file system is one operation, thus a person gets the old file or
            // the new one and never one half of each. The appliance takes its
            // electrical supply from cells, and the guard of the low charge
            // stops the machine while this software operates.
            string temporary = _path + ".tmp";

            File.WriteAllText(temporary, JsonSerializer.Serialize(_current, StoreJson.Default.UserSettings));
            File.Move(temporary, _path, overwrite: true);
        }
#pragma warning disable CA1031 // The appliance must not stop. See the remark.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            // CAUTION: a disk that is full or a directory that is read only
            // must not stop the appliance. The person sees the change that they
            // made, and it goes away at the next start. That is much better
            // than a display that is black.
            LogWriteFailed(_logger, _path, exception);
        }
    }

    /// <summary>
    /// Reads the file, or gives the settings of an appliance that nobody
    /// changed.
    /// </summary>
    /// <param name="path">The full path of the file.</param>
    private UserSettings Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                LogNoFile(_logger, path);
                return UserSettings.Default;
            }

            UserSettings? read = JsonSerializer.Deserialize(
                File.ReadAllText(path),
                StoreJson.Default.UserSettings);

            return (read ?? UserSettings.Default).Sanitized();
        }
#pragma warning disable CA1031 // The appliance must not stop. See the remark.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            // A file that is damaged must not stop the appliance. It has no
            // keyboard and no console: a person could not remove that file.
            LogReadFailed(_logger, path, exception);
            return UserSettings.Default;
        }
    }

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The account has no settings directory. The selections of a person stay in the memory and go away at the next start.")]
    private static partial void LogNoSettingsDirectory(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "There is no file of settings at {path}. The software uses the values that it has.")]
    private static partial void LogNoFile(ILogger logger, string path);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The software did not read {path}. It uses the values that it has.")]
    private static partial void LogReadFailed(ILogger logger, string path, Exception exception);

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "The software did not write {path}. The selection goes away at the next start.")]
    private static partial void LogWriteFailed(ILogger logger, string path, Exception exception);
}

/// <summary>
/// The JSON context of the settings.
/// </summary>
/// <remarks>
/// The software uses a generated context for the LiteRT-LM protocol also.
/// </remarks>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(UserSettings))]
internal sealed partial class StoreJson : JsonSerializerContext;
