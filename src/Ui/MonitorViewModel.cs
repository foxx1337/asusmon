using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using AsusMon.Monitors;
using Microsoft.UI.Xaml;

namespace AsusMon.Ui;

/// <summary>One labelled read-only value in the summary card.</summary>
/// <remarks>
/// The XAML type-info generator constructs view-model types through a
/// parameterless constructor and assigns members individually, so these types
/// deliberately avoid <c>required</c> and <c>init</c> accessors.
/// </remarks>
public sealed class SettingViewModel
{
    public SettingViewModel()
    {
    }

    public SettingViewModel(string label, string value)
    {
        Label = label;
        Value = value;
    }

    public string Label { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}

/// <summary>One selectable GameVisual preset.</summary>
public sealed class ModeViewModel
{
    public string Id { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    internal GameVisualMode Mode { get; set; } = null!;
}

/// <summary>Presentation state for a single monitor card.</summary>
public sealed class MonitorViewModel : INotifyPropertyChanged
{
    private int _selectedModeIndex = -1;

    public event PropertyChangedEventHandler? PropertyChanged;

    internal int MonitorIndex { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Subtitle { get; set; } = string.Empty;

    public bool IsAsus { get; set; }

    public Visibility AsusBadgeVisibility => IsAsus ? Visibility.Visible : Visibility.Collapsed;

    public bool CanChangeMode { get; set; }

    public ObservableCollection<ModeViewModel> Modes { get; } = [];

    public ObservableCollection<SettingViewModel> Settings { get; } = [];

    public int SelectedModeIndex
    {
        get => _selectedModeIndex;
        set
        {
            if (_selectedModeIndex == value)
            {
                return;
            }

            _selectedModeIndex = value;
            OnPropertyChanged();
        }
    }

    /// <summary>
    /// Suppresses the selection-changed handler while the view model is being
    /// populated, so that loading state does not write to the monitor.
    /// </summary>
    internal bool IsPopulating { get; set; }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
