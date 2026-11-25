using Windows.Devices.Enumeration;
using WirelessBatteryMonitor.Constants;
using WirelessBatteryMonitor.Models;

namespace WirelessBatteryMonitor.Readers;

/// <summary>
/// Windows PnP Bluetooth pil özelliği üzerinden FreeBuds 5i, kulaklık, fare ve klavyeleri okur.
/// </summary>
public sealed class BluetoothDeviceReader : IDeviceReader
{
    public string Name => "Windows Bluetooth";

    private static readonly string[] RequestedProperties =
    [
        AppConstants.BluetoothBatteryPropertyKey,
        AppConstants.DeviceConnectedPropertyKey,
        AppConstants.BluetoothAepContainerPrefix,
        "System.ItemNameDisplay"
    ];

    public async Task<IReadOnlyList<DeviceBatteryInfo>> ReadAsync(CancellationToken cancellationToken)
    {
        var results = new List<DeviceBatteryInfo>();
        var connectedContainers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenByContainer = new Dictionary<string, DeviceBatteryInfo>(StringComparer.OrdinalIgnoreCase);

        try
        {
            string selector = "System.Devices.Present:=System.StructuredQueryType.Boolean#True";
            var devices = await DeviceInformation
                .FindAllAsync(selector, RequestedProperties, DeviceInformationKind.Device)
                .AsTask(cancellationToken);

            // 1. Geçiş: Aynı ContainerId altında bağlı (Connected=True) herhangi bir düğüm var mı topla
            foreach (var dev in devices)
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (dev.Properties.TryGetValue(AppConstants.DeviceConnectedPropertyKey, out var connVal) &&
                    connVal is bool isConn && isConn &&
                    dev.Properties.TryGetValue(AppConstants.BluetoothAepContainerPrefix, out var cid) &&
                    cid is Guid g)
                {
                    connectedContainers.Add(g.ToString("N"));
                }
            }

            // 2. Geçiş: Pil özelliği taşıyan Bluetooth düğümlerini oku
            foreach (var dev in devices)
            {
                if (cancellationToken.IsCancellationRequested) break;

                if (!dev.Properties.TryGetValue(AppConstants.BluetoothBatteryPropertyKey, out var rawBattery) ||
                    rawBattery is null)
                {
                    continue;
                }

                int percent;
                try
                {
                    percent = Math.Clamp(Convert.ToInt32(rawBattery), 0, 100);
                }
                catch
                {
                    continue;
                }

                string name = CleanDeviceName(dev.Name);
                if (string.IsNullOrWhiteSpace(name)) continue;

                string? containerId = dev.Properties.TryGetValue(AppConstants.BluetoothAepContainerPrefix, out var cidObj) &&
                                      cidObj is Guid g2
                    ? g2.ToString("N")
                    : null;

                bool isConnected = false;
                if (containerId is not null && connectedContainers.Contains(containerId))
                {
                    isConnected = true;
                }
                else if (dev.Properties.TryGetValue(AppConstants.DeviceConnectedPropertyKey, out var connVal) &&
                         connVal is bool b)
                {
                    isConnected = b;
                }

                string dedupKey = containerId ?? name;
                if (seenByContainer.TryGetValue(dedupKey, out var existing))
                {
                    existing.IsConnected |= isConnected;
                    continue;
                }

                var info = new DeviceBatteryInfo
                {
                    Key = $"bt:{dedupKey}",
                    Name = name,
                    Kind = InferKind(name),
                    ConnectionVia = AppConstants.ViaBluetooth,
                    Percent = percent,
                    IsCharging = false,
                    IsConnected = isConnected,
                    SourceReader = Name
                };

                seenByContainer[dedupKey] = info;
                results.Add(info);
            }
        }
        catch
        {
            // Bluetooth servisi kapalıysa veya erişim yoksa sessizce boş dön
        }

        return results;
    }

    private static string CleanDeviceName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        string cleaned = raw.Trim();
        string[] suffixes = [" Hands-Free AG Audio", " Hands-Free AG", " Stereo", " Avrcp Transport", " LE"];
        foreach (var suffix in suffixes)
        {
            if (cleaned.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                cleaned = cleaned[..^suffix.Length].Trim();
        }
        return cleaned;
    }

    private static DeviceKind InferKind(string name)
    {
        if (name.Contains("FreeBuds", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("AirPods", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Buds", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Earbud", StringComparison.OrdinalIgnoreCase))
            return DeviceKind.Earbuds;

        if (name.Contains("Mouse", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Rival", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Dark Core", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Viper", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("DeathAdder", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("LIFT", StringComparison.OrdinalIgnoreCase))
            return DeviceKind.Mouse;

        if (name.Contains("Keyboard", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Klavye", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Keychron", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("AULA", StringComparison.OrdinalIgnoreCase))
            return DeviceKind.Keyboard;

        if (name.Contains("Xbox", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("DualSense", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Controller", StringComparison.OrdinalIgnoreCase))
            return DeviceKind.Gamepad;

        if (name.Contains("Headset", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Void", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Arctis", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Cloud", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("Headphone", StringComparison.OrdinalIgnoreCase))
            return DeviceKind.Headset;

        return DeviceKind.Other;
    }
}
