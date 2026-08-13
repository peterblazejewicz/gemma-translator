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
// been modified. It replaces frontend/src/components/SettingsOverlay.jsx.

using System.Reflection;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GemmaTranslator.Configuration;
using GemmaTranslator.Services;
using Microsoft.Extensions.Options;

namespace GemmaTranslator.ViewModels;

/// <summary>
/// One accent of the settings screen.
/// </summary>
/// <param name="Color">The value of the colour, for example <c>#FFD100</c>.</param>
/// <param name="IsSelected">
/// <c>true</c> for the accent that the appliance uses. The display gives it a
/// ring.
/// </param>
/// <param name="Pick">What a touch on this swatch does.</param>
/// <remarks>
/// Each swatch holds its own command. A command with the colour as its
/// parameter needs a binding to the parent of the item, and a compiled binding
/// of that shape is not clear.
/// </remarks>
public sealed record AccentSwatch(string Color, bool IsSelected, ICommand Pick);

/// <summary>
/// The settings screen.
/// </summary>
/// <remarks>
/// <para>
/// Upstream has the endpoint, the name of the model, the key, the prompt, a
/// mode of the keyboard, and a checkbox for the proxy. Each one of those is
/// gone: the appliance has no keyboard, thus a person cannot type. Those
/// values are in <c>appsettings.json</c> and in the <c>GEMMA_</c> variables of
/// the environment.
/// </para>
/// <para>
/// The volume of upstream is also gone. It calls <c>wpctl</c>, <c>pactl</c>,
/// and <c>amixer</c>, which are Linux only, and Raspberry Pi OS Lite has no
/// PipeWire. The speakerphone has its own buttons.
/// </para>
/// </remarks>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IUserSettingsStore _store;
    private readonly Appearance _appearance;

    /// <summary>
    /// The line of the ABOUT panel that shows the charge.
    /// </summary>
    /// <remarks>
    /// <see cref="MainViewModel"/> writes this. The electrical supply has one
    /// reader in the software, and this screen shows what that reader gives.
    /// </remarks>
    [ObservableProperty]
    private string _batteryAbout = string.Empty;

    /// <summary>
    /// Initializes a new instance of the <see cref="SettingsViewModel"/> class.
    /// </summary>
    /// <param name="store">The settings of a person, from the container.</param>
    /// <param name="appearance">The surface, from the container.</param>
    /// <param name="liteRt">The settings of the LiteRT-LM server.</param>
    public SettingsViewModel(
        IUserSettingsStore store,
        Appearance appearance,
        IOptions<LiteRtOptions> liteRt)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(appearance);
        ArgumentNullException.ThrowIfNull(liteRt);

        _store = store;
        _appearance = appearance;

        ModelName = liteRt.Value.ModelName;
    }

    /// <summary>The name of the model, from the settings of the operator.</summary>
    public string ModelName { get; }

    /// <summary>
    /// The version of the software.
    /// </summary>
    /// <remarks>
    /// CAUTION: the text stops at the plus. SourceLink puts the hash of the
    /// commit after that character, which is 41 characters more and wider than
    /// the panel. The journal holds the full text at the start.
    /// </remarks>
    public static string Version { get; } = ShortVersion();

    /// <summary>The 6 accents, and which one the appliance uses.</summary>
    public IReadOnlyList<AccentSwatch> Swatches => MakeSwatches();

    /// <summary><c>true</c> if the surface uses the dark variant.</summary>
    public bool IsDark => _store.Current.IsDark;

    /// <summary><c>true</c> if the appliance speaks the translation.</summary>
    public bool SpeakTranslations => _store.Current.SpeakTranslations;

    /// <summary>The count of the bars of the visualizer.</summary>
    public int VisualizerBars => _store.Current.VisualizerBars;

    /// <summary>
    /// <c>true</c> while a person can make the count of the bars larger.
    /// </summary>
    public bool CanAddBars => VisualizerBars < UserSettings.MaximumBars;

    /// <summary>
    /// <c>true</c> while a person can make the count of the bars smaller.
    /// </summary>
    public bool CanRemoveBars => VisualizerBars > UserSettings.MinimumBars;

    /// <summary>
    /// Keeps new settings, writes them to the disk, and puts them on the
    /// surface.
    /// </summary>
    /// <remarks>
    /// The surface changes at the moment of the touch. A person on a display
    /// with no keyboard must see that the appliance received the touch.
    /// </remarks>
    /// <param name="settings">The settings that the person made.</param>
    private void Apply(UserSettings settings)
    {
        _store.Save(settings);
        _appearance.Apply(_store.Current);

        OnPropertyChanged(nameof(Swatches));
        OnPropertyChanged(nameof(IsDark));
        OnPropertyChanged(nameof(SpeakTranslations));
        OnPropertyChanged(nameof(VisualizerBars));
        OnPropertyChanged(nameof(CanAddBars));
        OnPropertyChanged(nameof(CanRemoveBars));

        // CAUTION: OnPropertyChanged does not re-read CanExecute. A RelayCommand
        // of the toolkit reads it again only after this call, and
        // [NotifyCanExecuteChangedFor] cannot help, because it operates on an
        // [ObservableProperty] field and CanAddBars is a property that we write.
        // Without these two lines the target at 64 bars stays bright, a person
        // pushes it, and nothing occurs and nothing says why.
        AddBarsCommand.NotifyCanExecuteChanged();
        RemoveBarsCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void UseLight() => Apply(_store.Current with { IsDark = false });

    [RelayCommand]
    private void UseDark() => Apply(_store.Current with { IsDark = true });

    [RelayCommand]
    private void ToggleSpeech()
        => Apply(_store.Current with { SpeakTranslations = !_store.Current.SpeakTranslations });

    [RelayCommand(CanExecute = nameof(CanAddBars))]
    private void AddBars()
        => Apply(_store.Current with
        {
            VisualizerBars = _store.Current.VisualizerBars + UserSettings.BarStep,
        });

    [RelayCommand(CanExecute = nameof(CanRemoveBars))]
    private void RemoveBars()
        => Apply(_store.Current with
        {
            VisualizerBars = _store.Current.VisualizerBars - UserSettings.BarStep,
        });

    private static string ShortVersion()
    {
        string full = typeof(SettingsViewModel).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "0.0.0";

        int plus = full.IndexOf('+', StringComparison.Ordinal);

        return plus < 0 ? full : full[..plus];
    }

    private IReadOnlyList<AccentSwatch> MakeSwatches()
    {
        List<AccentSwatch> swatches = new(UserSettings.AccentColors.Count);

        foreach (string color in UserSettings.AccentColors)
        {
            string value = color;

            swatches.Add(new AccentSwatch(
                value,
                string.Equals(value, _store.Current.AccentColor, StringComparison.OrdinalIgnoreCase),
                new RelayCommand(() => Apply(_store.Current with { AccentColor = value }))));
        }

        return swatches;
    }
}
