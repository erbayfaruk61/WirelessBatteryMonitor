using System.Windows;
using WirelessBatteryMonitor.Constants;
using WirelessBatteryMonitor.Services;
using WirelessBatteryMonitor.Views;

namespace WirelessBatteryMonitor;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private SettingsService? _settings;
    private BatteryMonitorService? _monitor;
    private TaskbarWidgetManager? _widgetManager;
    private FlyoutWindow? _flyout;
    private TrayIconService? _trayIcon;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--probe", StringComparer.OrdinalIgnoreCase))
        {
            var probeSettings = new SettingsService();
            using var probeMonitor = new BatteryMonitorService(probeSettings);
            await probeMonitor.RefreshNowAsync();
            foreach (var d in probeMonitor.CurrentDevices)
            {
                Console.WriteLine($"{d.Name} | {d.DisplayPercentText} | {d.ConnectionVia} | Connected={d.IsConnected} | Reader={d.SourceReader}");
            }
            Shutdown();
            return;
        }

        _singleInstanceMutex = new Mutex(true, AppConstants.AppId, out bool createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        _settings = new SettingsService();
        _monitor = new BatteryMonitorService(_settings);
        _flyout = new FlyoutWindow(_settings, _monitor);
        _widgetManager = new TaskbarWidgetManager();
        _trayIcon = new TrayIconService(_settings.Current.ShowTaskbarWidget);

        _widgetManager.LeftClicked += () => _flyout.ToggleOpen();
        _widgetManager.RightClicked += () => _trayIcon.ShowContextMenuAtCursor();

        _trayIcon.OpenFlyoutRequested += () => _flyout.ToggleOpen();
        _trayIcon.RefreshRequested += () => _ = _monitor.RefreshNowAsync();
        _trayIcon.ToggleTaskbarWidgetRequested += ToggleTaskbarWidget;
        _trayIcon.ExitRequested += ShutdownApp;

        _flyout.SettingsChanged += ApplySettingsVisibility;

        _monitor.DevicesUpdated += devices => Dispatcher.Invoke(() =>
        {
            _widgetManager.Sync(devices, _settings.Current);
            _trayIcon.UpdateStatus(devices, _settings.Current.LowBatteryThreshold);
            if (_flyout.IsVisible)
                _flyout.UpdateDevices(devices);
        });

        _monitor.LowBatteryDetected += device => Dispatcher.Invoke(() =>
        {
            _trayIcon.ShowLowBatteryAlert(device);
        });

        ApplySettingsVisibility();
        _monitor.Start();
    }

    private void ToggleTaskbarWidget()
    {
        if (_settings is null) return;
        _settings.Current.ShowTaskbarWidget = !_settings.Current.ShowTaskbarWidget;
        _settings.Save();
        ApplySettingsVisibility();
    }

    private void ApplySettingsVisibility()
    {
        if (_settings is null || _widgetManager is null || _trayIcon is null || _monitor is null) return;

        bool show = _settings.Current.ShowTaskbarWidget;
        _trayIcon.UpdateWidgetChecked(show);
        _widgetManager.Sync(_monitor.CurrentDevices, _settings.Current);
    }

    private void ShutdownApp()
    {
        _monitor?.Dispose();
        _trayIcon?.Dispose();
        _widgetManager?.Dispose();
        _flyout?.Close();
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        Shutdown();
    }
}
