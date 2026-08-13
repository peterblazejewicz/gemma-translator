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
// been modified. It replaces frontend/src/components/LanguageLane.jsx.

using Avalonia;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace GemmaTranslator.ViewModels;

public sealed record DrumItem(string Name, bool IsSelected);

public sealed partial class LaneViewModel : ObservableObject
{
    /// <remarks>
    /// The design gives 44 pixels. The drum is 132 pixels high, thus it shows 3
    /// rows and the window is on the row in the middle.
    /// </remarks>
    public const double RowHeight = 44;

    private readonly Action<LaneViewModel, int> _turn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Items))]
    [NotifyPropertyChangedFor(nameof(DrumOffset))]
    private Language _language;

    [ObservableProperty]
    private bool _isRecording;

    [ObservableProperty]
    private IReadOnlyList<double> _levels = [];

    [ObservableProperty]
    private bool _canTurn = true;

    public LaneViewModel(int number, Language language, Action<LaneViewModel, int> turn)
    {
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(turn);

        Number = number;
        _language = language;
        _turn = turn;
    }

    /// <summary>
    /// 1 for the person on the left, and 2 for the person on the right.
    /// </summary>
    public int Number { get; }

    public bool IsMirrored => Number == 2;

    public int BadgeColumn => IsMirrored ? 4 : 0;

    public Thickness BadgeMargin => IsMirrored
        ? new Thickness(14, 0, 0, 0)
        : new Thickness(0, 0, 14, 0);

    public FlowDirection BadgeFlow => IsMirrored
        ? FlowDirection.RightToLeft
        : FlowDirection.LeftToRight;

    public IReadOnlyList<DrumItem> Items => MakeItems(Language);

    /// <remarks>
    /// CAUTION: the column must carry this height itself. The drum is 132
    /// pixels and it cuts what goes outside. Without an explicit height the
    /// column takes those 132 pixels, it cuts each row after the third one, and
    /// the move then shows an empty window for each language after the second
    /// one.
    /// </remarks>
    public static double DrumHeight => Languages.All.Count * RowHeight;

    public double DrumOffset => -(Languages.IndexOf(Language) - 1) * RowHeight;

    [RelayCommand]
    private void TurnUp() => _turn(this, -1);

    [RelayCommand]
    private void TurnDown() => _turn(this, 1);

    private static IReadOnlyList<DrumItem> MakeItems(Language selected)
    {
        List<DrumItem> items = new(Languages.All.Count);

        foreach (Language language in Languages.All)
        {
            items.Add(new DrumItem(language.Name, language.Code == selected.Code));
        }

        return items;
    }
}
