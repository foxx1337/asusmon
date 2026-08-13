using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using AsusMon.Ddc;
using AsusMon.Monitors;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Graphics;

namespace AsusMon.Ui;

/// <summary>
/// Read-mostly summary of the attached monitors, shown when the app is started
/// without a console. GameVisual can also be switched from here.
/// </summary>
public sealed partial class MainWindow : Window, INotifyPropertyChanged
{
    private bool _isIdle;

    /// <summary>Debounce tokens, one per monitor and feature.</summary>
    private readonly Dictionary<(int Monitor, byte Code), CancellationTokenSource> _pendingLevels = [];

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
        using DisplaySet displays = DisplaySet.Open(CapabilityCache.Open(enabled: true));
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

        if (summary.Brightness is { } brightness)
        {
            vm.HasBrightness = true;
            vm.BrightnessMaximum = brightness.Maximum;
            vm.AppliedBrightness = brightness.Current;
            vm.Brightness = brightness.Current;
        }

        if (summary.Contrast is { } contrast)
        {
            vm.HasContrast = true;
            vm.ContrastMaximum = contrast.Maximum;
            vm.AppliedContrast = contrast.Current;
            vm.Contrast = contrast.Current;
        }

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
            using DisplaySet displays = DisplaySet.Open(CapabilityCache.Open(enabled: true));
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

    private async void OnBrightnessChanged(object sender, RangeBaseValueChangedEventArgs e) =>
        await OnLevelChangedAsync(sender, e, LevelFeature.Brightness);

    private async void OnContrastChanged(object sender, RangeBaseValueChangedEventArgs e) =>
        await OnLevelChangedAsync(sender, e, LevelFeature.Contrast);

    /// <summary>
    /// Pushes a slider value to the monitor, debounced. A DDC/CI write costs
    /// tens of milliseconds and the channel has no flow control, so writing on
    /// every tick of a drag would flood it; only the value the slider rests on
    /// is sent.
    /// </summary>
    private async Task OnLevelChangedAsync(object sender, RangeBaseValueChangedEventArgs e, LevelFeature feature)
    {
        if (sender is not Slider { Tag: MonitorViewModel vm })
        {
            return;
        }

        bool isBrightness = feature.Code == Vcp.Brightness;
        uint applied = isBrightness ? vm.AppliedBrightness : vm.AppliedContrast;

        // Template realization raises ValueChanged for the loaded value; that
        // must not be echoed back to the panel.
        if ((uint)Math.Round(e.NewValue) == applied)
        {
            return;
        }

        (int, byte) key = (vm.MonitorIndex, feature.Code);

        if (_pendingLevels.TryGetValue(key, out CancellationTokenSource? previous))
        {
            previous.Cancel();
            previous.Dispose();
        }

        CancellationTokenSource cts = new();
        _pendingLevels[key] = cts;

        try
        {
            await Task.Delay(200, cts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        // Two-way binding keeps the view model current, so read the value the
        // slider actually came to rest on rather than the one that queued us.
        uint target = (uint)Math.Round(isBrightness ? vm.Brightness : vm.Contrast);

        StatusText.Text = $"Setting {feature.Name.ToLowerInvariant()} to {target}...";

        uint readBack = await Task.Run(() =>
        {
            using DisplaySet displays = DisplaySet.Open(CapabilityCache.Open(enabled: true));
            AsusDisplay? display = displays.Select(vm.MonitorIndex);

            if (display is null)
            {
                return uint.MaxValue;
            }

            display.ApplyLevel(feature.Code, target, out uint value);
            return value;
        });

        if (_pendingLevels.TryGetValue(key, out CancellationTokenSource? current) && current == cts)
        {
            _pendingLevels.Remove(key);
            cts.Dispose();
        }

        if (readBack == uint.MaxValue)
        {
            StatusText.Text = $"Monitor did not accept {feature.Name.ToLowerInvariant()} {target}";
            return;
        }

        if (isBrightness)
        {
            vm.AppliedBrightness = readBack;
            vm.Brightness = readBack;
        }
        else
        {
            vm.AppliedContrast = readBack;
            vm.Contrast = readBack;
        }

        StatusText.Text = $"{feature.Name} set to {readBack}";
    }
}
