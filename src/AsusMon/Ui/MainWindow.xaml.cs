using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using AsusMon.Monitors;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;

namespace AsusMon.Ui;

/// <summary>
/// Read-mostly summary of the attached monitors, shown when the app is started
/// without a console. GameVisual can also be switched from here.
/// </summary>
public sealed partial class MainWindow : Window, INotifyPropertyChanged
{
    private bool _isIdle;

    public MainWindow()
    {
        GuiFailure.Stage = "window-initialize";
        InitializeComponent();

        Title = "asusmon";
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        ResizeForDpi(620, 780);

        MonitorList.ItemsSource = Monitors;

        _ = RefreshAsync();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Sizes the window in effective (DPI-independent) pixels. AppWindow works
    /// in physical pixels, so on a scaled display a raw size comes out too small.
    /// </summary>
    private void ResizeForDpi(int width, int height)
    {
        double scale = Ddc.NativeMethods.GetDpiForWindow(
            WinRT.Interop.WindowNative.GetWindowHandle(this)) / 96.0;

        if (scale <= 0)
        {
            scale = 1.0;
        }

        AppWindow.Resize(new SizeInt32((int)(width * scale), (int)(height * scale)));
    }

    public ObservableCollection<MonitorViewModel> Monitors { get; } = [];

    public bool IsIdle
    {
        get => _isIdle;
        private set
        {
            if (_isIdle == value)
            {
                return;
            }

            _isIdle = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsIdle)));
        }
    }

    /// <summary>
    /// Collects every monitor summary on a worker thread — DDC/CI transactions
    /// take hundreds of milliseconds each and must never block the UI thread.
    /// </summary>
    private async Task RefreshAsync()
    {
        IsIdle = false;
        Busy.IsActive = true;
        Busy.Visibility = Visibility.Visible;
        EmptyState.Visibility = Visibility.Collapsed;
        StatusText.Text = "Reading monitors over DDC/CI...";

        Stopwatch clock = Stopwatch.StartNew();
        List<DisplaySummary> summaries;

        try
        {
            summaries = await Task.Run(CollectSummaries);
        }
        catch (Exception ex)
        {
            Busy.IsActive = false;
            Busy.Visibility = Visibility.Collapsed;
            EmptyState.Visibility = Visibility.Visible;
            EmptyState.Text = $"Could not query monitors: {ex.Message}";
            StatusText.Text = "Failed";
            IsIdle = true;
            return;
        }

        Monitors.Clear();

        foreach (DisplaySummary summary in summaries)
        {
            Monitors.Add(Build(summary));
        }

        Busy.IsActive = false;
        Busy.Visibility = Visibility.Collapsed;

        if (Monitors.Count == 0)
        {
            EmptyState.Visibility = Visibility.Visible;
            EmptyState.Text =
                "No DDC/CI capable monitors found.\n\n" +
                "Check that DDC/CI is enabled in the monitor OSD, and that the panel is " +
                "connected by DisplayPort or HDMI rather than through an adapter that " +
                "blocks the DDC channel.";
        }

        StatusText.Text = $"{Monitors.Count} monitor(s) - refreshed in {clock.ElapsedMilliseconds} ms";
        IsIdle = true;
    }

    private static List<DisplaySummary> CollectSummaries()
    {
        using DisplaySet displays = DisplaySet.Open();
        List<DisplaySummary> summaries = [];

        foreach (AsusDisplay display in displays.All)
        {
            summaries.Add(display.Summarize());
        }

        return summaries;
    }

    private static MonitorViewModel Build(DisplaySummary summary)
    {
        MonitorViewModel vm = new()
        {
            MonitorIndex = summary.Index,
            Title = string.IsNullOrWhiteSpace(summary.Description) ? "Display" : summary.Description,
            Subtitle = BuildSubtitle(summary),
            IsAsus = summary.IsAsus,
            CanChangeMode = summary.AvailableModes.Count > 0,
            IsPopulating = true,
        };

        int selected = -1;

        foreach (GameVisualMode mode in summary.AvailableModes)
        {
            // A preset only applies while the panel is in the matching pipeline:
            // SDR presets need SDR, HDR/Dolby presets need HDR.
            bool needsOtherPipeline = (mode.Family != GameVisualFamily.Sdr) != summary.IsHdrActive;

            vm.Modes.Add(new ModeViewModel
            {
                Id = mode.Id,
                Label = needsOtherPipeline
                    ? $"{mode.Name}  (needs {(summary.IsHdrActive ? "SDR" : "HDR")})"
                    : mode.Name,
                Mode = mode,
            });

            if (summary.CurrentMode is { } current && current.Id == mode.Id && current.Code == mode.Code)
            {
                selected = vm.Modes.Count - 1;
            }
        }

        vm.SelectedModeIndex = selected;
        vm.IsPopulating = false;

        vm.Settings.Add(new SettingViewModel("Pipeline", summary.IsHdrActive ? "HDR" : "SDR"));
        vm.Settings.Add(new SettingViewModel("Model", summary.Model ?? "unknown"));
        vm.Settings.Add(new SettingViewModel("Family", summary.ProductLine.ToString()));
        vm.Settings.Add(new SettingViewModel("Input", summary.InputSourceName ?? "n/a"));

        AddIfPresent(vm, "Brightness", summary.Brightness);
        AddIfPresent(vm, "Contrast", summary.Contrast);
        AddIfPresent(vm, "Sharpness", summary.Sharpness);
        AddIfPresent(vm, "Volume", summary.Volume);

        vm.Settings.Add(new SettingViewModel("MCCS", summary.MccsVersion ?? "unknown"));

        if (summary.VendorCode is { } vendor)
        {
            vm.Settings.Add(new SettingViewModel("Vendor id", $"{vendor} (0x{vendor:X2})"));
        }

        return vm;
    }

    private static void AddIfPresent(MonitorViewModel vm, string label, Reading? reading)
    {
        if (reading is { } value)
        {
            vm.Settings.Add(new SettingViewModel(label, $"{value.Current} / {value.Maximum}"));
        }
    }

    private static string BuildSubtitle(DisplaySummary summary)
    {
        string device = string.IsNullOrWhiteSpace(summary.DeviceName) ? "unknown device" : summary.DeviceName;
        return summary.IsPrimary ? $"{device} - primary" : device;
    }

    private async void OnRefreshClick(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async void OnModeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { Tag: MonitorViewModel vm } box ||
            vm.IsPopulating ||
            box.SelectedItem is not ModeViewModel selection)
        {
            return;
        }

        IsIdle = false;
        StatusText.Text = $"Switching to {selection.Mode.Name}...";

        (bool applied, uint readBack) = await Task.Run(() =>
        {
            using DisplaySet displays = DisplaySet.Open();
            AsusDisplay? display = displays.Select(vm.MonitorIndex);

            if (display is null)
            {
                return (false, 0u);
            }

            bool ok = display.ApplyMode(selection.Mode, out uint value);
            return (ok, value);
        });

        StatusText.Text = applied
            ? $"GameVisual set to {selection.Mode.Name}"
            : $"Monitor rejected {selection.Mode.Name} (reported 0x{readBack:X2})";

        IsIdle = true;
    }
}
