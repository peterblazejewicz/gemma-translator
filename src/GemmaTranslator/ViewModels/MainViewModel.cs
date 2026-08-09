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
// been modified.

using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;

namespace GemmaTranslator.ViewModels;

/// <summary>
/// The data of the main view.
/// </summary>
/// <remarks>
/// This is a skeleton. It holds the two languages and no other data. The
/// speech-to-text part, the translation part, and the text-to-speech part come
/// later.
/// </remarks>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly ILogger<MainViewModel> _logger;

    /// <summary>
    /// The language of lane 1, which is the person on the left.
    /// </summary>
    /// <remarks>
    /// The names are the names of the upstream lanes (<c>laneId</c> 1 and 2 in
    /// <c>TranslatorApp.jsx</c>). They do not say "left" and "right", because
    /// the position of a lane is a property of the layout.
    /// </remarks>
    [ObservableProperty]
    private Language _lane1Language = Languages.FromCode("ja");

    /// <summary>
    /// The language of lane 2, which is the person on the right.
    /// </summary>
    [ObservableProperty]
    private Language _lane2Language = Languages.FromCode("en");

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModel"/> class.
    /// </summary>
    /// <param name="logger">The logger from the container.</param>
    public MainViewModel(ILogger<MainViewModel> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;

        LogStarted(_logger, Lane1Language.Name, Lane2Language.Name);
    }

    /// <summary>
    /// The text that the Moonshine licence conditions make necessary.
    /// </summary>
    public static string Attribution => "Powered by Moonshine AI";

    /// <summary>
    /// Writes the line that shows that the user interface started.
    /// </summary>
    /// <remarks>
    /// The <c>[LoggerMessage]</c> attribute makes the code of this method. It
    /// makes no garbage and it does no work if the level is off.
    /// </remarks>
    /// <param name="logger">The logger.</param>
    /// <param name="lane1Language">The name of the language of lane 1.</param>
    /// <param name="lane2Language">The name of the language of lane 2.</param>
    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The user interface started. Lane 1 is {lane1Language} and lane 2 is {lane2Language}.")]
    private static partial void LogStarted(
        ILogger logger,
        string lane1Language,
        string lane2Language);
}
