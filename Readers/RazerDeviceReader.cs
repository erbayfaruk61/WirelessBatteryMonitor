using WirelessBatteryMonitor.Constants;
using WirelessBatteryMonitor.Models;

namespace WirelessBatteryMonitor.Readers;

/// <summary>
/// Razer kablosuz kulaklık ve fareleri (BlackShark / Viper / DeathAdder) HID üzerinden okur.
/// </summary>
public sealed class RazerDeviceReader : IDeviceReader
{
    public string Name => "Razer HID";

    private static readonly Dictionary<ushort, (string Name, DeviceKind Kind)> KnownDevices = new()
    {
        [0x0577] = ("Razer BlackShark V3 Pro", DeviceKind.Headset),
        [0x0555] = ("Razer BlackShark V2 Pro", DeviceKind.Headset),
        [0x00A6] = ("Razer Viper V2 Pro", DeviceKind.Mouse),
        [0x00C1] = ("Razer Viper V3 Pro", DeviceKind.Mouse),
        [0x00B7] = ("Razer DeathAdder V3 Pro", DeviceKind.Mouse),
    };

    public Task<IReadOnlyList<DeviceBatteryInfo>> ReadAsync(CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<DeviceBatteryInfo>>(() =>
        {
            var results = new List<DeviceBatteryInfo>();
            var collections = HidNative.EnumerateDevices(AppConstants.VendorRazer);
            var seenKeys = new HashSet<string>();

            foreach (var c in collections)
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (c.UsagePage < 0xFF00 || c.OutputReportLength <= 0 || c.InputReportLength <= 0) continue;

                // BlackShark V3 Pro (1532:0577) doğrudan HID sorgusu
                if (c.ProductId == 0x0577)
                {
                    var bs = TryReadBlackSharkV3(c);
                    if (bs is not null && seenKeys.Add(bs.Key))
                        results.Add(bs);
                }
            }

            return results;
        }, cancellationToken);
    }

    private DeviceBatteryInfo? TryReadBlackSharkV3(HidCollectionInfo c)
    {
        using var handle = HidNative.OpenReadWrite(c.DevicePath);
        if (handle is null) return null;

        HidNative.Flush(handle);
        if (!HidNative.WriteReport(handle, [0x00, 0x02, 0x00, 0x83], c.OutputReportLength, 250))
            return null;

        var r = HidNative.ReadReport(handle, c.InputReportLength, 300);
        if (r is null || r.Length < 6) return null;

        int percent = Math.Clamp((int)r[4], 0, 100);
        bool charging = r[5] != 0;

        return new DeviceBatteryInfo
        {
            Key = $"razer:{c.ProductId:X4}",
            Name = KnownDevices[0x0577].Name,
            Kind = DeviceKind.Headset,
            ConnectionVia = AppConstants.ViaReceiver24Ghz,
            Percent = percent,
            IsCharging = charging,
            IsConnected = true,
            SourceReader = Name
        };
    }
}
