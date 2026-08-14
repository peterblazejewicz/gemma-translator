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
// been modified. It replaces the record keys of handleKeyDown and
// handleKeyUp, in upstream/main:frontend/src/TranslatorApp.jsx.

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.Extensions.Logging;

namespace GemmaTranslator.Services.PushToTalk;

/// <remarks>
/// This class operates on the Windows development host. The keys are Z and X,
/// which are the keys of the "vertical" mode of upstream, and F13 and F14,
/// which are the keys that the buttons of the appliance make.
/// </remarks>
public sealed partial class KeyboardPushToTalk : IPushToTalk
{
    private readonly ILogger<KeyboardPushToTalk> _logger;

    // Avalonia repeats KeyDown while a key stays down. This set makes one
    // press one event.
    private readonly HashSet<Key> _down = [];

    private TopLevel? _topLevel;

    public KeyboardPushToTalk(ILogger<KeyboardPushToTalk> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public event EventHandler<PushToTalkChange>? Changed;

    /// <remarks>
    /// CAUTION: the handler goes on the top level and it tunnels, and it is not
    /// on a control. Avalonia sends a key to the control that has the focus,
    /// and this view has text and one button only, thus a key can go to no
    /// location.
    /// </remarks>
    public void Start(TopLevel? topLevel)
    {
        if (topLevel is null)
        {
            LogNoTopLevel(_logger);
            return;
        }

        _topLevel = topLevel;

        topLevel.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);
        topLevel.AddHandler(InputElement.KeyUpEvent, OnKeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);

        LogAttached(_logger);
    }

    public void Dispose()
    {
        if (_topLevel is not null)
        {
            _topLevel.RemoveHandler(InputElement.KeyDownEvent, OnKeyDown);
            _topLevel.RemoveHandler(InputElement.KeyUpEvent, OnKeyUp);
            _topLevel = null;
        }

        _down.Clear();
    }

    private static int LaneOf(Key key) => key switch
    {
        Key.Z or Key.F13 => 1,
        Key.X or Key.F14 => 2,
        _ => 0,
    };

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (IsTextControl(e.Source))
        {
            return;
        }

        int lane = LaneOf(e.Key);
        if (lane == 0)
        {
            return;
        }

        e.Handled = true;

        if (!_down.Add(e.Key))
        {
            return;
        }

        Changed?.Invoke(this, new PushToTalkChange(lane, IsPressed: true));
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        if (IsTextControl(e.Source))
        {
            return;
        }

        int lane = LaneOf(e.Key);
        if (lane == 0)
        {
            return;
        }

        e.Handled = true;

        if (!_down.Remove(e.Key))
        {
            return;
        }

        Changed?.Invoke(this, new PushToTalkChange(lane, IsPressed: false));
    }

    /// <remarks>
    /// The handler tunnels on the top level, thus it sees each key before a
    /// control. With no test, a person could not type Z or X in any field of
    /// this software.
    /// </remarks>
    private static bool IsTextControl(object? source)
        => source is TextBox or AutoCompleteBox;

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "The buttons have no top level, thus no key arrives.")]
    private static partial void LogNoTopLevel(ILogger logger);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "The buttons come from the keyboard. Lane 1 is Z or F13, and lane 2 is X or F14.")]
    private static partial void LogAttached(ILogger logger);
}
