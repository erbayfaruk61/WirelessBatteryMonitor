using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using WirelessBatteryMonitor.Constants;

namespace WirelessBatteryMonitor.Services;

/// <summary>
/// Hangi marka/protokol okuyucularının aktif olduğunu yönetir.
/// </summary>
public sealed class EnabledReadersSettings
{
    public bool Corsair { get; set; } = true;
    public bool SteelSeries { get; set; } = true;
    public bool Logitech { get; set; } = true;
    public bool Razer { get; set; } = true;
    public bool HyperX { get; set; } = true;
    public bool PlayStation { get; set; } = true;
    public bool Xbox { get; set; } = true;
    public bool Bluetooth { get; set; } = true;
    public bool BleGatt { get; set; } = true;
}

/// <summary>
/// Kullanıcı tercihleri modeli.
/// </summary>
public sealed class AppSettings
{
    public int PollIntervalSeconds { get; set; } = AppConstants.DefaultPollIntervalSeconds;
    public int LowBatteryThreshold { get; set; } = AppConstants.DefaultLowBatteryThreshold;
    public bool EnableLowBatteryNotifications { get; set; } = true;
    public bool ShowTaskbarWidget { get; set; } = true;
    public bool ShowOnAllMonitors { get; set; } = true;
    public bool ShowDisconnectedDevices { get; set; } = true;
    public bool RememberLastCharge { get; set; } = true;
    public bool ShowSubBatteries { get; set; } = true;

    public EnabledReadersSettings EnabledReaders { get; set; } = new();
    public HashSet<string> HiddenDeviceKeys { get; set; } = [];
    public HashSet<string> HiddenFromPanelKeys { get; set; } = [];
    public Dictionary<string, string> CustomDeviceNames { get; set; } = [];
    public Dictionary<string, int> CustomLowThresholds { get; set; } = [];
    public Dictionary<string, CachedDeviceState> CachedDevices { get; set; } = [];

    public int GetEffectiveLowThreshold(string deviceKey) =>
        CustomLowThresholds.TryGetValue(deviceKey, out int custom) ? custom : LowBatteryThreshold;

    public string GetEffectiveName(string deviceKey, string rawName) =>
        CustomDeviceNames.TryGetValue(deviceKey, out var custom) && !string.IsNullOrWhiteSpace(custom)
            ? custom
            : rawName;
}

public sealed class CachedDeviceState
{
    public string Name { get; set; } = string.Empty;
    public Models.DeviceKind Kind { get; set; }
    public string ConnectionVia { get; set; } = string.Empty;
    public int? Percent { get; set; }
    public DateTime LastUpdated { get; set; }
}

/// <summary>
/// Ayarları yerel diskte saklar ve Windows başlangıç kaydını yönetir.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly object _saveLock = new();
    private readonly string _settingsPath;

    public AppSettings Current { get; private set; } = new();

    public SettingsService()
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppConstants.AppId);
        Directory.CreateDirectory(folder);
        _settingsPath = Path.Combine(folder, AppConstants.SettingsFileName);
        Load();
    }

    public void Load()
    {
        try
        {
            if (File.Exists(_settingsPath))
            {
                var json = File.ReadAllText(_settingsPath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                if (loaded is not null)
                {
                    loaded.PollIntervalSeconds = Math.Clamp(
                        loaded.PollIntervalSeconds,
                        AppConstants.MinPollIntervalSeconds,
                        AppConstants.MaxPollIntervalSeconds);
                    loaded.LowBatteryThreshold = Math.Clamp(
                        loaded.LowBatteryThreshold,
                        AppConstants.MinLowBatteryThreshold,
                        AppConstants.MaxLowBatteryThreshold);
                    loaded.EnabledReaders ??= new EnabledReadersSettings();
                    loaded.HiddenDeviceKeys ??= [];
                    loaded.HiddenFromPanelKeys ??= [];
                    loaded.CustomDeviceNames ??= [];
                    loaded.CustomLowThresholds ??= [];
                    loaded.CachedDevices ??= [];
                    Current = loaded;
                }
            }
        }
        catch
        {
            Current = new AppSettings();
        }
    }

    public void Save()
    {
        lock (_saveLock)
        {
            try
            {
                var json = JsonSerializer.Serialize(Current, JsonOptions);
                File.WriteAllText(_settingsPath, json);
            }
            catch
            {
                // Disk yazma hatası uygulamayı çökertmemeli
            }
        }
    }

    public bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AppConstants.StartupRegistryPath, false);
            return key?.GetValue(AppConstants.AppId) is string;
        }
        catch
        {
            return false;
        }
    }

    public void SetStartupEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(AppConstants.StartupRegistryPath, true);
            if (key is null) return;

            if (enabled)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exePath))
                    key.SetValue(AppConstants.AppId, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(AppConstants.AppId, false);
            }
        }
        catch
        {
            // Kayıt defteri erişim hatası yoksayılır
        }
    }
}
