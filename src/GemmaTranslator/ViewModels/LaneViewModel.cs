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

/// <summary>
/// One row of the drum of a lane.
/// </summary>
/// <param name="Name">The name of the language, in English.</param>
/// <param name="IsSelected">
/// <c>true</c> for the row in the window of the drum.
/// </param>
/// <remarks>
/// The display gives the selected row a larger dimension and the ink of the
/// lane, and the other rows a smaller dimension and the muted ink. A row is
/// thus not only a text, and this record holds the two values that the style
/// selector needs.
/// </remarks>
public sealed record DrumItem(string Name, bool IsSelected);

/// <summary>
/// One person of the conversation, and the language of that person.
/// </summary>
/// <remarks>
/// <para>
/// Upstream calls this a lane and gives it <c>laneId</c> 1 and 2. The two
/// lanes are beside each other in one strip, and they are not two half
/// screens.
/// </para>
/// <para>
/// A lane does not know the other lane. The rule that the two lanes cannot
/// hold the same language is a rule about the pair, thus
/// <see cref="MainViewModel"/> keeps it and this class asks it to turn the
/// drum.
/// </para>
/// </remarks>
public sealed partial class LaneViewModel : ObservableObject
{
    /// <summary>
    /// The height of one row of the drum, in pixels.
    /// </summary>
    /// <remarks>
    /// The design gives 44 pixels. The drum is 132 pixels high, thus it shows 3
    /// rows and the window is on the row in the middle.
    /// </remarks>
    public const double RowHeight = 44;

    private readonly Action<LaneViewModel, int> _turn;

    /// <summary>
    /// The language of this person.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Items))]
    [NotifyPropertyChangedFor(nameof(DrumOffset))]
    private Language _language;

    /// <summary>
    /// <c>true</c> while the software hears this person.
    /// </summary>
    /// <remarks>
    /// The lane inverts its ground. A person who is not the speaker must see
    /// from a distance which person the appliance hears, thus the signal is the
    /// full ground of the lane and not a border.
    /// </remarks>
    [ObservableProperty]
    private bool _isRecording;

    /// <summary>
    /// The height of each bar of the visualizer of this lane, from 0 to 1.
    /// </summary>
    /// <remarks>
    /// An empty list gives the bars of a lane that is quiet. See
    /// <see cref="VisualizerLevels"/>, which says where these values come from
    /// and why they are not the level of the microphone.
    /// </remarks>
    [ObservableProperty]
    private IReadOnlyList<double> _levels = [];

    /// <summary>
    /// <c>true</c> when a touch on an arrow turns the drum.
    /// </summary>
    /// <remarks>
    /// The two arrows go to opacity 0.35 and take no touch while any lane
    /// records. A person must not change the language of a sentence that the
    /// appliance is hearing.
    /// </remarks>
    [ObservableProperty]
    private bool _canTurn = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="LaneViewModel"/> class.
    /// </summary>
    /// <param name="number">1 for the person on the left, 2 for the right.</param>
    /// <param name="language">The language at the start.</param>
    /// <param name="turn">
    /// What <see cref="MainViewModel"/> does when a person touches an arrow.
    /// The second argument is -1 for the arrow above and 1 for the arrow below.
    /// </param>
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

    /// <summary>
    /// <c>true</c> for lane 2, where the badge goes to the right edge.
    /// </summary>
    public bool IsMirrored => Number == 2;

    /// <summary>
    /// The column of the badge of the person.
    /// </summary>
    /// <remarks>
    /// CAUTION: the two lanes are not a mirror of each other, although they
    /// look like one. The badge goes to the outer edge of each lane, and the
    /// arrow that goes up stays on the left in the two lanes. A mirror of the
    /// full lane moves the two arrows also, and a person then finds the arrow
    /// that goes up on the right of one lane and on the left of the other.
    /// </remarks>
    public int BadgeColumn => IsMirrored ? 4 : 0;

    /// <summary>The space between the badge and the arrow beside it.</summary>
    public Thickness BadgeMargin => IsMirrored
        ? new Thickness(14, 0, 0, 0)
        : new Thickness(0, 0, 14, 0);

    /// <summary>
    /// The direction of the contents of the badge.
    /// </summary>
    /// <remarks>
    /// The badge is the one part that the display mirrors. The glyph of the
    /// person then looks at the lane and not away from it, and the numeral goes
    /// to the outer side.
    /// </remarks>
    public FlowDirection BadgeFlow => IsMirrored
        ? FlowDirection.RightToLeft
        : FlowDirection.LeftToRight;

    /// <summary>
    /// Each language, and which one is in the window of the drum.
    /// </summary>
    public IReadOnlyList<DrumItem> Items => MakeItems(Language);

    /// <summary>
    /// The full height of the column of the drum, in pixels.
    /// </summary>
    /// <remarks>
    /// CAUTION: the column must carry this height itself. The drum is 132
    /// pixels and it cuts what goes outside. Without an explicit height the
    /// column takes those 132 pixels, it cuts each row after the third one, and
    /// the move then shows an empty window for each language after the second
    /// one.
    /// </remarks>
    public static double DrumHeight => Languages.All.Count * RowHeight;

    /// <summary>
    /// How far the column of the drum moves, in pixels.
    /// </summary>
    /// <remarks>
    /// The window is the second of the 3 rows that a person sees. Thus the
    /// column moves up by one row for each step after the first one, and the
    /// first language moves down by one row.
    /// </remarks>
    public double DrumOffset => -(IndexOf(Language) - 1) * RowHeight;

    /// <summary>
    /// Turns the drum to the language above.
    /// </summary>
    [RelayCommand]
    private void TurnUp() => _turn(this, -1);

    /// <summary>
    /// Turns the drum to the language below.
    /// </summary>
    [RelayCommand]
    private void TurnDown() => _turn(this, 1);

    private static int IndexOf(Language language)
    {
        for (int index = 0; index < Languages.All.Count; index++)
        {
            if (Languages.All[index].Code == language.Code)
            {
                return index;
            }
        }

        return 0;
    }

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
