using WirelessBatteryMonitor.Models;

namespace WirelessBatteryMonitor.Readers;

/// <summary>
/// Yeni cihaz protokolleri eklemek için uygulanan ortak okuyucu arayüzü.
/// </summary>
public interface IDeviceReader
{
    string Name { get; }
    Task<IReadOnlyList<DeviceBatteryInfo>> ReadAsync(CancellationToken cancellationToken);
}
