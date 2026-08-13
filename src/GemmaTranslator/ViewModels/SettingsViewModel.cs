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

public sealed record AccentSwatch(string Color, bool IsSelected, ICommand Pick);

/// <remarks>
/// <para>
/// This screen holds what a person at the appliance can change, and no more. It
/// has no text field, because the appliance has no keyboard. The endpoint, the
/// name of the model, and the key are settings of the operator: they are in
/// <c>appsettings.json</c> and in the <c>GEMMA_</c> variables of the
/// environment.
/// </para>
/// <para>
/// There is no control of the volume. A control of that kind needs
/// <c>wpctl</c>, <c>pactl</c>, or <c>amixer</c>, which are Linux only, and
/// Raspberry Pi OS Lite has no PipeWire. The speakerphone has its own buttons.
/// </para>
/// </remarks>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IUserSettingsStore _store;
    private readonly Appearance _appearance;

    [ObservableProperty]
    private string _batteryAbout = string.Empty;

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

    public string ModelName { get; }

    /// <remarks>
    /// CAUTION: the text stops at the plus. SourceLink puts the hash of the
    /// commit after that character, which is 41 characters more and wider than
    /// the panel. The journal holds the full text at the start.
    /// </remarks>
    public static string Version { get; } = ShortVersion();

    public IReadOnlyList<AccentSwatch> Swatches => MakeSwatches();

    public bool IsDark => _store.Current.IsDark;

    public bool SpeakTranslations => _store.Current.SpeakTranslations;

    public int VisualizerBars => _store.Current.VisualizerBars;

    public bool CanAddBars => VisualizerBars < UserSettings.MaximumBars;

    public bool CanRemoveBars => VisualizerBars > UserSettings.MinimumBars;

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
