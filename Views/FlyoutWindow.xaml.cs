using System.Text;
using System.Windows;
using System.Windows.Media;
using WirelessBatteryMonitor.Constants;
using WirelessBatteryMonitor.Models;
using WirelessBatteryMonitor.Readers;
using WirelessBatteryMonitor.Services;

namespace WirelessBatteryMonitor.Views;

public sealed class FlyoutDeviceCardVm
{
    public required string Key { get; init; }
    public required string Name { get; init; }
    public required string Glyph { get; init; }
    public required string MetaText { get; init; }
    public required string PercentText { get; init; }
    public required double ProgressValue { get; init; }
    public required System.Windows.Media.Brush AccentBrush { get; init; }
    public required Visibility ChargingVisibility { get; init; }
    public required string VisibilityGlyph { get; init; }
    public required string SubBatteriesText { get; init; }
    public required Visibility SubBatteriesVisibility { get; init; }
    public required string FooterText { get; init; }
    public required PointCollection SparklinePoints { get; init; }
    public required Visibility SparklineVisibility { get; init; }
}

public sealed class SettingsDeviceVisibilityVm
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public required bool IsVisible { get; init; }
}

public partial class FlyoutWindow : Window
{
    private readonly SettingsService _settings;
    private readonly BatteryMonitorService _monitor;
    private bool _isLoadingSettings;
    private bool _showingSettings;
    private string? _editingDeviceKey;

    public event Action? SettingsChanged;

    public FlyoutWindow(SettingsService settings, BatteryMonitorService monitor)
    {
        _settings = settings;
        _monitor = monitor;
        InitializeComponent();
        LoadSettingsUi();
        SizeChanged += (_, _) =>
        {
            if (IsVisible) PositionAboveTaskbar();
        };
    }

    public void ToggleOpen()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        ShowPanel(devices: true);
        LoadSettingsUi();
        UpdateDevices(_monitor.CurrentDevices);
        Show();
        UpdateLayout();
        PositionAboveTaskbar();
        Activate();
    }

    public void UpdateDevices(IReadOnlyList<DeviceBatteryInfo> devices)
    {
        int connectedCount = devices.Count(d => d.IsConnected);
        SubtitleText.Text = connectedCount > 0
            ? $"{connectedCount} aktif kablosuz cihaz"
            : AppConstants.StatusNoDevices;

        var hidden = _settings.Current.HiddenDeviceKeys;
        var hiddenFromPanel = _settings.Current.HiddenFromPanelKeys;
        bool showDisconnected = _settings.Current.ShowDisconnectedDevices;
        bool showSubBatteries = _settings.Current.ShowSubBatteries;

        var filtered = devices
            .Where(d => !hiddenFromPanel.Contains(d.Key) && (showDisconnected || d.IsConnected))
            .ToList();

        EmptyStateText.Visibility = filtered.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        DeviceCardsControl.ItemsSource = filtered.Select(d =>
        {
            int lowThreshold = _settings.Current.GetEffectiveLowThreshold(d.Key);
            string displayName = _settings.Current.GetEffectiveName(d.Key, d.Name);
            var color = GetStatusColor(d, lowThreshold);
            var brush = new SolidColorBrush(color);
            bool isHidden = hidden.Contains(d.Key);
            bool hasSubs = showSubBatteries && d.SubBatteries.Count > 0;

            string eta = d.EstimatedRemainingText ?? $"Son okuma: {d.LastUpdated:HH:mm:ss}";

            return new FlyoutDeviceCardVm
            {
                Key = d.Key,
                Name = displayName,
                Glyph = d.KindGlyph,
                MetaText = $"{d.ConnectionVia} · {d.StatusSummary}",
                PercentText = d.DisplayPercentText,
                ProgressValue = d.Percent ?? 0,
                AccentBrush = brush,
                ChargingVisibility = d.IsCharging ? Visibility.Visible : Visibility.Collapsed,
                VisibilityGlyph = isHidden ? "\uED1A" : "\uE7B3",
                SubBatteriesText = d.SubBatteriesText,
                SubBatteriesVisibility = hasSubs ? Visibility.Visible : Visibility.Collapsed,
                FooterText = eta,
                SparklinePoints = BuildSparkline(d.History),
                SparklineVisibility = d.History.Count >= 2 ? Visibility.Visible : Visibility.Collapsed
            };
        }).ToList();
    }

    private void ShowPanel(bool devices = false, bool settings = false, bool edit = false)
    {
        _showingSettings = settings;
        DevicesPanel.Visibility = devices ? Visibility.Visible : Visibility.Collapsed;
        SettingsPanel.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        DeviceEditPanel.Visibility = edit ? Visibility.Visible : Visibility.Collapsed;
        PositionAboveTaskbar();
    }

    private void PositionAboveTaskbar()
    {
        UpdateLayout();
        var work = SystemParameters.WorkArea;
        double h = ActualHeight > 0 ? ActualHeight : 320;
        Left = work.Right - Width - 12;
        Top = work.Bottom - h - 10;
    }

    private void LoadSettingsUi()
    {
        _isLoadingSettings = true;
        var s = _settings.Current;
        PollIntervalSlider.Value = s.PollIntervalSeconds;
        PollIntervalLabel.Text = $"Yenileme Sıklığı: {s.PollIntervalSeconds} sn";

        LowThresholdSlider.Value = s.LowBatteryThreshold;
        LowThresholdLabel.Text = $"Düşük Pil Uyarı Eşiği: %{s.LowBatteryThreshold}";

        NotificationsCheck.IsChecked = s.EnableLowBatteryNotifications;
        TaskbarWidgetCheck.IsChecked = s.ShowTaskbarWidget;
        AllMonitorsCheck.IsChecked = s.ShowOnAllMonitors;
        ShowDisconnectedCheck.IsChecked = s.ShowDisconnectedDevices;
        RememberChargeCheck.IsChecked = s.RememberLastCharge;
        SubBatteriesCheck.IsChecked = s.ShowSubBatteries;
        StartupCheck.IsChecked = _settings.IsStartupEnabled();

        var r = s.EnabledReaders;
        ReaderCorsairCheck.IsChecked = r.Corsair;
        ReaderSteelSeriesCheck.IsChecked = r.SteelSeries;
        ReaderLogitechCheck.IsChecked = r.Logitech;
        ReaderRazerCheck.IsChecked = r.Razer;
        ReaderHyperXCheck.IsChecked = r.HyperX;
        ReaderPlayStationCheck.IsChecked = r.PlayStation;
        ReaderXboxCheck.IsChecked = r.Xbox;
        ReaderBluetoothCheck.IsChecked = r.Bluetooth;
        ReaderBleGattCheck.IsChecked = r.BleGatt;

        RefreshDeviceVisibilityList();
        _isLoadingSettings = false;
    }

    private void RefreshDeviceVisibilityList()
    {
        var hiddenFromPanel = _settings.Current.HiddenFromPanelKeys;
        DeviceVisibilityList.ItemsSource = _monitor.CurrentDevices
            .Select(d => new SettingsDeviceVisibilityVm
            {
                Key = d.Key,
                DisplayName = $"{_settings.Current.GetEffectiveName(d.Key, d.Name)} ({d.ConnectionVia})",
                IsVisible = !hiddenFromPanel.Contains(d.Key)
            })
            .ToList();
    }

    private void DevicePanelVisibility_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings) return;
        if (sender is System.Windows.Controls.CheckBox { Tag: string key } cb)
        {
            if (cb.IsChecked == true)
                _settings.Current.HiddenFromPanelKeys.Remove(key);
            else
                _settings.Current.HiddenFromPanelKeys.Add(key);

            _settings.Save();
            UpdateDevices(_monitor.CurrentDevices);
            SettingsChanged?.Invoke();
        }
    }

    private static PointCollection BuildSparkline(IReadOnlyList<BatteryHistoryPoint> history)
    {
        var points = new PointCollection();
        if (history.Count < 2) return points;

        const double width = 70.0;
        const double height = 16.0;
        for (int i = 0; i < history.Count; i++)
        {
            double x = i * width / (history.Count - 1);
            double y = height - (history[i].Percent / 100.0 * height);
            points.Add(new System.Windows.Point(x, y));
        }
        return points;
    }

    private static System.Windows.Media.Color GetStatusColor(DeviceBatteryInfo d, int lowThreshold)
    {
        if (!d.IsConnected) return System.Windows.Media.Color.FromRgb(140, 145, 155);
        if (d.IsCharging) return System.Windows.Media.Color.FromRgb(96, 165, 250);
        if (d.Percent.HasValue && d.Percent.Value <= lowThreshold)
            return System.Windows.Media.Color.FromRgb(239, 68, 68);
        return System.Windows.Media.Color.FromRgb(52, 211, 153);
    }

    private void Window_Deactivated(object? sender, EventArgs e)
    {
        if (IsVisible) Hide();
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshButton.IsEnabled = false;
        await _monitor.RefreshNowAsync();
        RefreshButton.IsEnabled = true;
    }

    private void SettingsToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_showingSettings) ShowPanel(devices: true);
        else
        {
            LoadSettingsUi();
            ShowPanel(settings: true);
        }
    }

    private void EditDevice_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: string key }) return;
        _editingDeviceKey = key;

        _settings.Current.CustomDeviceNames.TryGetValue(key, out var customName);
        EditCustomNameBox.Text = customName ?? string.Empty;

        bool hasCustomThreshold = _settings.Current.CustomLowThresholds.TryGetValue(key, out int threshold);
        EditUseCustomThresholdCheck.IsChecked = hasCustomThreshold;
        EditThresholdSlider.IsEnabled = hasCustomThreshold;
        EditThresholdSlider.Value = hasCustomThreshold ? threshold : _settings.Current.LowBatteryThreshold;
        EditThresholdLabel.Text = $"Özel Eşik: %{(int)EditThresholdSlider.Value}";

        ShowPanel(edit: true);
    }

    private void EditCustomThresholdCheck_Changed(object sender, RoutedEventArgs e)
    {
        EditThresholdSlider.IsEnabled = EditUseCustomThresholdCheck.IsChecked == true;
    }

    private void EditThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (EditThresholdLabel is not null)
            EditThresholdLabel.Text = $"Özel Eşik: %{(int)e.NewValue}";
    }

    private void CancelEditDevice_Click(object sender, RoutedEventArgs e)
    {
        _editingDeviceKey = null;
        ShowPanel(devices: true);
    }

    private void SaveEditDevice_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_editingDeviceKey))
        {
            string name = EditCustomNameBox.Text.Trim();
            if (string.IsNullOrEmpty(name))
                _settings.Current.CustomDeviceNames.Remove(_editingDeviceKey);
            else
                _settings.Current.CustomDeviceNames[_editingDeviceKey] = name;

            if (EditUseCustomThresholdCheck.IsChecked == true)
                _settings.Current.CustomLowThresholds[_editingDeviceKey] = (int)EditThresholdSlider.Value;
            else
                _settings.Current.CustomLowThresholds.Remove(_editingDeviceKey);

            _settings.Save();
            UpdateDevices(_monitor.CurrentDevices);
            SettingsChanged?.Invoke();
        }
        _editingDeviceKey = null;
        ShowPanel(devices: true);
    }

    private void ToggleDeviceVisibility_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string key })
        {
            if (!_settings.Current.HiddenDeviceKeys.Add(key))
                _settings.Current.HiddenDeviceKeys.Remove(key);

            _settings.Save();
            UpdateDevices(_monitor.CurrentDevices);
            SettingsChanged?.Invoke();
        }
    }

    private void PollIntervalSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoadingSettings) return;
        int val = (int)e.NewValue;
        PollIntervalLabel.Text = $"Yenileme Sıklığı: {val} sn";
        _settings.Current.PollIntervalSeconds = val;
        _settings.Save();
        _monitor.RestartTimer();
    }

    private void LowThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isLoadingSettings) return;
        int val = (int)e.NewValue;
        LowThresholdLabel.Text = $"Düşük Pil Uyarı Eşiği: %{val}";
        _settings.Current.LowBatteryThreshold = val;
        _settings.Save();
        UpdateDevices(_monitor.CurrentDevices);
        SettingsChanged?.Invoke();
    }

    private void SettingCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings) return;
        var s = _settings.Current;
        s.EnableLowBatteryNotifications = NotificationsCheck.IsChecked == true;
        s.ShowTaskbarWidget = TaskbarWidgetCheck.IsChecked == true;
        s.ShowOnAllMonitors = AllMonitorsCheck.IsChecked == true;
        s.ShowDisconnectedDevices = ShowDisconnectedCheck.IsChecked == true;
        s.RememberLastCharge = RememberChargeCheck.IsChecked == true;
        s.ShowSubBatteries = SubBatteriesCheck.IsChecked == true;
        _settings.Save();
        UpdateDevices(_monitor.CurrentDevices);
        SettingsChanged?.Invoke();
    }

    private async void ReaderCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings) return;
        var r = _settings.Current.EnabledReaders;
        r.Corsair = ReaderCorsairCheck.IsChecked == true;
        r.SteelSeries = ReaderSteelSeriesCheck.IsChecked == true;
        r.Logitech = ReaderLogitechCheck.IsChecked == true;
        r.Razer = ReaderRazerCheck.IsChecked == true;
        r.HyperX = ReaderHyperXCheck.IsChecked == true;
        r.PlayStation = ReaderPlayStationCheck.IsChecked == true;
        r.Xbox = ReaderXboxCheck.IsChecked == true;
        r.Bluetooth = ReaderBluetoothCheck.IsChecked == true;
        r.BleGatt = ReaderBleGattCheck.IsChecked == true;
        _settings.Save();
        await _monitor.RefreshNowAsync();
    }

    private void StartupCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_isLoadingSettings) return;
        _settings.SetStartupEnabled(StartupCheck.IsChecked == true);
    }

    private void CopyReportButton_Click(object sender, RoutedEventArgs e)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== Bağlı HID Cihazları ===");
        foreach (var d in HidNative.EnumerateDevices())
        {
            sb.AppendLine($"VID:{d.VendorId:X4} PID:{d.ProductId:X4} UP:{d.UsagePage:X4} U:{d.Usage:X4} In:{d.InputReportLength} Out:{d.OutputReportLength} | {d.ProductName}");
        }
        System.Windows.Clipboard.SetText(sb.ToString());
        CopyReportButton.Content = "Panoya Kopyalandı ✓";
    }
}
