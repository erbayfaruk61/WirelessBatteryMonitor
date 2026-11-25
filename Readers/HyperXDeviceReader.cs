using WirelessBatteryMonitor.Constants;
using WirelessBatteryMonitor.Models;

namespace WirelessBatteryMonitor.Readers;

/// <summary>
/// HyperX kablosuz kulaklık ve fareleri (Cloud Alpha / II / III Wireless) okur.
/// </summary>
public sealed class HyperXDeviceReader : IDeviceReader
{
    public string Name => "HyperX HID";

    private static readonly Dictionary<ushort, string> KnownHeadsets = new()
    {
        [0x098D] = "HyperX Cloud Alpha Wireless",
        [0x06BE] = "HyperX Cloud III S Wireless",
        [0x02CC] = "HyperX Cloud III S Wireless",
        [0x0B92] = "HyperX Cloud III Wireless",
        [0x1718] = "HyperX Cloud II Wireless",
        [0x018B] = "HyperX Cloud II Wireless",
    };

    public Task<IReadOnlyList<DeviceBatteryInfo>> ReadAsync(CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<DeviceBatteryInfo>>(() =>
        {
            var results = new List<DeviceBatteryInfo>();
            var collections = HidNative.EnumerateDevices(AppConstants.VendorHyperXHp)
                .Concat(HidNative.EnumerateDevices(AppConstants.VendorHyperXKingston));
            var seenKeys = new HashSet<string>();

            foreach (var c in collections)
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (!KnownHeadsets.TryGetValue(c.ProductId, out var modelName)) continue;
                if (c.OutputReportLength <= 0 || c.InputReportLength <= 0) continue;

                var info = TryReadCloudAlphaOrSeries(c, modelName);
                if (info is not null && seenKeys.Add(info.Key))
                    results.Add(info);
            }

            return results;
        }, cancellationToken);
    }

    private DeviceBatteryInfo? TryReadCloudAlphaOrSeries(HidCollectionInfo c, string modelName)
    {
        using var handle = HidNative.OpenReadWrite(c.DevicePath);
        if (handle is null) return null;

        byte[][] commands =
        [
            [0x21, 0xBB, 0x0B],
            [0x06, 0xFF, 0xBB, 0x02],
            [0x21, 0xFF, 0x05]
        ];

        foreach (var cmd in commands)
        {
            HidNative.Flush(handle);
            if (!HidNative.WriteReport(handle, cmd, c.OutputReportLength, 250))
                continue;

            var r = HidNative.ReadReport(handle, c.InputReportLength, 300);
            if (r is null || r.Length < 5) continue;

            // Cloud Alpha (0x21 0xBB 0x0B -> r[3] %)
            if (r[0] == 0x21 && r[1] == 0xBB && r[2] == 0x0B && r[3] <= 100)
            {
                return new DeviceBatteryInfo
                {
                    Key = $"hyperx:{c.ProductId:X4}",
                    Name = modelName,
                    Kind = DeviceKind.Headset,
                    ConnectionVia = AppConstants.ViaReceiver24Ghz,
                    Percent = r[3],
                    IsCharging = r.Length > 4 && r[4] != 0,
                    IsConnected = true,
                    SourceReader = Name
                };
            }
        }

        return null;
    }
}
