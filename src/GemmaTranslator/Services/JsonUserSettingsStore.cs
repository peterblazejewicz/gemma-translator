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

public sealed partial class JsonUserSettingsStore : IUserSettingsStore
{
    private const string DirectoryName = "gemma-translator";

    /// <remarks>
    /// It is not <c>appsettings.json</c>. That file holds the settings of the
    /// operator, git holds it, and a person cannot change it on the display.
    /// </remarks>
    private const string FileName = "user-settings.json";

    private readonly ILogger<JsonUserSettingsStore> _logger;
    /// <summary>The file, or <c>null</c> if the account has no settings directory.</summary>
    private readonly string? _path;

    private UserSettings _current;

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

    public UserSettings Current => _current;

    public event EventHandler<UserSettings>? Changed;

    public void Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        UserSettings next = settings.Sanitized();

        // The settings screen keeps each touch, and a person who holds the plus
        // of the count of bars makes one write for each step. This appliance
        // writes to an SD card.
        bool unchanged = next == _current;

        _current = next;

        if (unchanged)
        {
            return;
        }

        Changed?.Invoke(this, next);

        if (_path is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            // CAUTION: write beside the file and then move it. A move on one
            // file system is one operation, thus a reader gets the old file or
            // the new one and never one half of each. The appliance takes its
            // electrical supply from cells, and the guard of the low charge
            // stops the machine while this software operates.
            string temporary = _path + ".tmp";

            using (FileStream stream = File.Create(temporary))
            {
                JsonSerializer.Serialize(stream, UserSettingsFile.From(_current), StoreJson.Default.UserSettingsFile);

                // The flush goes to the disk and not to the cache of the
                // operating system. Without it the rename can reach the journal
                // of the file system before the bytes do, and a machine that
                // stops between the two finds a file of zero length.
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, _path, overwrite: true);
        }
#pragma warning disable CA1031 // The appliance must not stop. See the remark.
        catch (Exception exception)
#pragma warning restore CA1031
        {
            // CAUTION: a disk that is full or a directory that is read only
            // must not stop the appliance. The person sees the change that they
            // made, and it goes away at the next start.
            LogWriteFailed(_logger, _path, exception);
        }
    }

    private UserSettings Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                LogNoFile(_logger, path);
                return UserSettings.Default;
            }

            UserSettingsFile? read = JsonSerializer.Deserialize(
                File.ReadAllText(path),
                StoreJson.Default.UserSettingsFile);

            return (read?.ToSettings() ?? UserSettings.Default).Sanitized();
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

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(UserSettingsFile))]
internal sealed partial class StoreJson : JsonSerializerContext;
