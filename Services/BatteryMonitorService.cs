using WirelessBatteryMonitor.Constants;
using WirelessBatteryMonitor.Models;
using WirelessBatteryMonitor.Readers;

namespace WirelessBatteryMonitor.Services;

/// <summary>
/// Tüm donanım okuyucularını ayarlara göre çalıştırır, geçmişi ve düşük pil uyarılarını yönetir.
/// </summary>
public sealed class BatteryMonitorService : IDisposable
{
    private readonly SettingsService _settings;
    private readonly CorsairDeviceReader _corsair = new();
    private readonly SteelSeriesDeviceReader _steelSeries = new();
    private readonly LogitechDeviceReader _logitech = new();
    private readonly RazerDeviceReader _razer = new();
    private readonly HyperXDeviceReader _hyperX = new();
    private readonly PlayStationDeviceReader _playStation = new();
    private readonly XboxControllerReader _xbox = new();
    private readonly BluetoothDeviceReader _bluetooth = new();
    private readonly BleGattBatteryReader _bleGatt = new();

    private readonly Dictionary<string, DeviceBatteryInfo> _trackedDevices = new();
    private readonly HashSet<string> _notifiedLowKeys = [];
    private readonly SemaphoreSlim _pollLock = new(1, 1);

    private CancellationTokenSource? _cts;
    private PeriodicTimer? _timer;

    public event Action<IReadOnlyList<DeviceBatteryInfo>>? DevicesUpdated;
    public event Action<DeviceBatteryInfo>? LowBatteryDetected;

    public IReadOnlyList<DeviceBatteryInfo> CurrentDevices =>
        _trackedDevices.Values
            .OrderByDescending(d => d.IsConnected)
            .ThenBy(d => d.Kind)
            .ThenBy(d => _settings.Current.GetEffectiveName(d.Key, d.Name))
            .ToList();

    public BatteryMonitorService(SettingsService settings)
    {
        _settings = settings;
        LoadCachedDevices();
    }

    public void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        var interval = TimeSpan.FromSeconds(_settings.Current.PollIntervalSeconds);
        _timer = new PeriodicTimer(interval);
        _ = RunLoopAsync(_cts.Token);
    }

    public void RestartTimer()
    {
        if (_cts is null) return;
        Start();
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _timer?.Dispose();
        _timer = null;
    }

    public async Task RefreshNowAsync()
    {
        await PollAllAsync(CancellationToken.None);
    }

    private IEnumerable<IDeviceReader> GetActiveReaders()
    {
        var r = _settings.Current.EnabledReaders;
        if (r.Corsair) yield return _corsair;
        if (r.SteelSeries) yield return _steelSeries;
        if (r.Logitech) yield return _logitech;
        if (r.Razer) yield return _razer;
        if (r.HyperX) yield return _hyperX;
        if (r.PlayStation) yield return _playStation;
        if (r.Xbox) yield return _xbox;
        if (r.Bluetooth) yield return _bluetooth;
        if (r.BleGatt) yield return _bleGatt;
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        await PollAllAsync(ct);

        while (_timer is not null && await _timer.WaitForNextTickAsync(ct))
        {
            await PollAllAsync(ct);
        }
    }

    private async Task PollAllAsync(CancellationToken ct)
    {
        if (!await _pollLock.WaitAsync(0, ct)) return;
        try
        {
            var tasks = GetActiveReaders().Select(r => SafeReadAsync(r, ct));
            var batches = await Task.WhenAll(tasks);

            var liveByKey = new Dictionary<string, DeviceBatteryInfo>();
            var bleByName = new Dictionary<string, DeviceBatteryInfo>(StringComparer.OrdinalIgnoreCase);

            foreach (var batch in batches)
            {
                foreach (var item in batch)
                {
                    if (item.SourceReader == _bleGatt.Name)
                    {
                        bleByName[item.Name] = item;
                    }
                    liveByKey[item.Key] = item;
                }
            }

            // Aynı cihaz hem Windows Bluetooth hem BLE GATT'tan geldiyse tekilleştir
            foreach (var btDevice in liveByKey.Values.Where(d => d.SourceReader == _bluetooth.Name).ToList())
            {
                if (bleByName.TryGetValue(btDevice.Name, out var bleMatch))
                {
                    if (bleMatch.SubBatteries.Count > 0 && btDevice.SubBatteries.Count == 0)
                        btDevice.SubBatteries.AddRange(bleMatch.SubBatteries);
                    liveByKey.Remove(bleMatch.Key);
                }
            }

            var now = DateTime.Now;

            foreach (var (key, live) in liveByKey)
            {
                // Eski sürüm key'lerinden kalan aynı isimli çevrimdışı önbellek kopyalarını temizle
                foreach (var staleKey in _trackedDevices
                             .Where(kv => kv.Key != key &&
                                          !liveByKey.ContainsKey(kv.Key) &&
                                          string.Equals(kv.Value.Name, live.Name, StringComparison.OrdinalIgnoreCase))
                             .Select(kv => kv.Key)
                             .ToList())
                {
                    _trackedDevices.Remove(staleKey);
                    _settings.Current.CachedDevices.Remove(staleKey);
                }

                if (_trackedDevices.TryGetValue(key, out var existing))
                {
                    existing.Name = live.Name;
                    existing.Kind = live.Kind;
                    existing.ConnectionVia = live.ConnectionVia;
                    if (live.Percent.HasValue)
                        existing.Percent = live.Percent;
                    existing.CoarseLevelText = live.CoarseLevelText;
                    existing.IsCharging = live.IsCharging;
                    existing.IsConnected = live.IsConnected;
                    existing.Millivolts = live.Millivolts;
                    existing.LastUpdated = now;
                    existing.SourceReader = live.SourceReader;
                    existing.SubBatteries.Clear();
                    existing.SubBatteries.AddRange(live.SubBatteries);
                    AppendHistory(existing, now);
                }
                else
                {
                    live.LastUpdated = now;
                    AppendHistory(live, now);
                    _trackedDevices[key] = live;
                }

                var current = _trackedDevices[key];
                _settings.Current.CachedDevices[key] = new CachedDeviceState
                {
                    Name = current.Name,
                    Kind = current.Kind,
                    ConnectionVia = current.ConnectionVia,
                    Percent = current.Percent,
                    LastUpdated = current.LastUpdated
                };

                int threshold = _settings.Current.GetEffectiveLowThreshold(key);
                if (_settings.Current.EnableLowBatteryNotifications &&
                    current.IsConnected &&
                    !current.IsCharging &&
                    current.Percent.HasValue &&
                    current.Percent.Value <= threshold)
                {
                    if (_notifiedLowKeys.Add(key))
                        LowBatteryDetected?.Invoke(current);
                }
                else if (current.IsCharging || (current.Percent.HasValue && current.Percent.Value > threshold + 5))
                {
                    _notifiedLowKeys.Remove(key);
                }
            }

            foreach (var key in _trackedDevices.Keys.ToList())
            {
                if (liveByKey.ContainsKey(key)) continue;

                if (_settings.Current.RememberLastCharge)
                {
                    _trackedDevices[key].IsConnected = false;
                    _trackedDevices[key].IsCharging = false;
                }
                else
                {
                    _trackedDevices.Remove(key);
                }
            }

            _settings.Save();
            DevicesUpdated?.Invoke(CurrentDevices);
        }
        catch (OperationCanceledException)
        {
            // Durdurma sırasında beklenen iptal
        }
        finally
        {
            _pollLock.Release();
        }
    }

    private static async Task<IReadOnlyList<DeviceBatteryInfo>> SafeReadAsync(IDeviceReader reader, CancellationToken ct)
    {
        try
        {
            return await reader.ReadAsync(ct);
        }
        catch
        {
            return [];
        }
    }

    private static void AppendHistory(DeviceBatteryInfo device, DateTime timestamp)
    {
        if (!device.Percent.HasValue) return;
        device.History.Add(new BatteryHistoryPoint(timestamp, device.Percent.Value, device.IsCharging));
        if (device.History.Count > AppConstants.MaxHistoryPoints)
            device.History.RemoveAt(0);
    }

    private void LoadCachedDevices()
    {
        if (!_settings.Current.RememberLastCharge) return;

        foreach (var (key, cached) in _settings.Current.CachedDevices)
        {
            _trackedDevices[key] = new DeviceBatteryInfo
            {
                Key = key,
                Name = cached.Name,
                Kind = cached.Kind,
                ConnectionVia = cached.ConnectionVia,
                Percent = cached.Percent,
                IsConnected = false,
                LastUpdated = cached.LastUpdated
            };
        }
    }

    public void Dispose()
    {
        Stop();
        _pollLock.Dispose();
    }
}
