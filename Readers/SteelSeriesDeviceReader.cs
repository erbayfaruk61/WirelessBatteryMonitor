using WirelessBatteryMonitor.Constants;
using WirelessBatteryMonitor.Models;

namespace WirelessBatteryMonitor.Readers;

/// <summary>
/// SteelSeries kablosuz fare (Rival 3 Wireless, Aerox, Prime) ve kulaklıkları (Arctis) GG yazılımı olmadan okur.
/// </summary>
public sealed class SteelSeriesDeviceReader : IDeviceReader
{
    public string Name => "SteelSeries HID";

    private static readonly Dictionary<ushort, string> RivalPids = new()
    {
        [0x1830] = "SteelSeries Rival 3 Wireless",
        [0x1872] = "SteelSeries Rival 3 Wireless Gen 2",
        [0x172B] = "SteelSeries Rival 650 Wireless",
        [0x1726] = "SteelSeries Rival 650 Wireless",
    };

    private static readonly Dictionary<ushort, (string Name, bool Wired)> AeroxPrimePids = new()
    {
        [0x1838] = ("SteelSeries Aerox 3 Wireless", false),
        [0x183A] = ("SteelSeries Aerox 3 Wireless", true),
        [0x1852] = ("SteelSeries Aerox 5 Wireless", false),
        [0x1854] = ("SteelSeries Aerox 5 Wireless", true),
        [0x1858] = ("SteelSeries Aerox 9 Wireless", false),
        [0x185A] = ("SteelSeries Aerox 9 Wireless", true),
        [0x1840] = ("SteelSeries Prime Wireless", false),
        [0x1842] = ("SteelSeries Prime Wireless", true),
    };

    public Task<IReadOnlyList<DeviceBatteryInfo>> ReadAsync(CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<DeviceBatteryInfo>>(() =>
        {
            var results = new List<DeviceBatteryInfo>();
            var collections = HidNative.EnumerateDevices(AppConstants.VendorSteelSeries);
            var seenKeys = new HashSet<string>();

            foreach (var c in collections)
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (c.InputReportLength <= 0 || c.OutputReportLength <= 0) continue;

                if (RivalPids.TryGetValue(c.ProductId, out var rivalName))
                {
                    var rival = TryReadRival(c, rivalName);
                    if (rival is not null && seenKeys.Add(rival.Key))
                        results.Add(rival);
                    continue;
                }

                if (AeroxPrimePids.TryGetValue(c.ProductId, out var aeroxInfo))
                {
                    var aerox = TryReadAeroxPrime(c, aeroxInfo.Name, aeroxInfo.Wired);
                    if (aerox is not null && seenKeys.Add(aerox.Key))
                        results.Add(aerox);
                }
            }

            return results;
        }, cancellationToken);
    }

    private DeviceBatteryInfo? TryReadRival(HidCollectionInfo c, string modelName)
    {
        using var handle = HidNative.OpenReadWrite(c.DevicePath);
        if (handle is null) return null;

        HidNative.Flush(handle);
        if (!HidNative.WriteReport(handle, [0x00, 0xAA, 0x01], c.OutputReportLength, 300))
            return null;

        var r = HidNative.ReadReport(handle, c.InputReportLength, 300);
        if (r is null || r.Length < 3) return null;

        int percent = Math.Clamp((int)r[0], 0, 100);
        bool charging = r[2] != 0;

        return new DeviceBatteryInfo
        {
            Key = $"steelseries:{c.ProductId:X4}",
            Name = modelName,
            Kind = DeviceKind.Mouse,
            ConnectionVia = c.ProductId == 0x172B ? AppConstants.ViaUsbCable : AppConstants.ViaReceiver24Ghz,
            Percent = percent,
            IsCharging = charging,
            IsConnected = true,
            SourceReader = Name
        };
    }

    private DeviceBatteryInfo? TryReadAeroxPrime(HidCollectionInfo c, string modelName, bool wired)
    {
        using var handle = HidNative.OpenReadWrite(c.DevicePath);
        if (handle is null) return null;

        byte cmd = wired ? (byte)0x92 : (byte)0xD2;
        HidNative.Flush(handle);
        if (!HidNative.WriteReport(handle, [0x00, cmd], c.OutputReportLength, 300))
            return null;

        var r = HidNative.ReadReport(handle, c.InputReportLength, 300);
        if (r is null || r.Length < 2) return null;

        int raw = r[1] & 0x7F;
        if (raw == 0) return null;

        int? percent = raw <= 21 ? (raw - 1) * 5 : null;
        bool charging = (r[1] & 0x80) != 0;

        return new DeviceBatteryInfo
        {
            Key = $"steelseries:{c.ProductId:X4}",
            Name = modelName,
            Kind = DeviceKind.Mouse,
            ConnectionVia = wired ? AppConstants.ViaUsbCable : AppConstants.ViaReceiver24Ghz,
            Percent = percent,
            IsCharging = charging,
            IsConnected = true,
            SourceReader = Name
        };
    }
}
