using WirelessBatteryMonitor.Constants;
using WirelessBatteryMonitor.Models;

namespace WirelessBatteryMonitor.Readers;

/// <summary>
/// Sony PlayStation DualSense, DualSense Edge ve DualShock 4 oyun kollarını HID üzerinden okur.
/// </summary>
public sealed class PlayStationDeviceReader : IDeviceReader
{
    public string Name => "PlayStation HID";

    private static readonly Dictionary<ushort, (string Name, bool IsDualSense)> KnownControllers = new()
    {
        [0x0CE6] = ("Sony DualSense", true),
        [0x0DF2] = ("Sony DualSense Edge", true),
        [0x05C4] = ("Sony DualShock 4", false),
        [0x09CC] = ("Sony DualShock 4 v2", false),
    };

    public Task<IReadOnlyList<DeviceBatteryInfo>> ReadAsync(CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<DeviceBatteryInfo>>(() =>
        {
            var results = new List<DeviceBatteryInfo>();
            var collections = HidNative.EnumerateDevices(AppConstants.VendorSony);
            var seenKeys = new HashSet<string>();

            foreach (var c in collections)
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (!KnownControllers.TryGetValue(c.ProductId, out var model)) continue;
                if (c.UsagePage != 0x0001 || c.InputReportLength < 32) continue;

                var info = TryReadController(c, model.Name, model.IsDualSense);
                if (info is not null && seenKeys.Add(info.Key))
                    results.Add(info);
            }

            return results;
        }, cancellationToken);
    }

    private DeviceBatteryInfo? TryReadController(HidCollectionInfo c, string modelName, bool isDualSense)
    {
        using var handle = HidNative.OpenReadWrite(c.DevicePath);
        if (handle is null) return null;

        var r = HidNative.ReadReport(handle, c.InputReportLength, 250);
        if (r is null || r.Length < 32) return null;

        bool isBluetooth = c.InputReportLength >= 78 && r[0] == 0x31;
        int percent;
        bool charging;

        if (isDualSense)
        {
            int idx = isBluetooth ? 54 : 53;
            if (r.Length <= idx) return null;
            byte battByte = r[idx];
            int level = battByte & 0x0F;
            int state = (battByte >> 4) & 0x0F;
            charging = state is 1 or 2;
            percent = Math.Clamp(level * 10 + 5, 0, 100);
        }
        else
        {
            int idx = isBluetooth ? 32 : 30;
            if (r.Length <= idx) return null;
            byte battByte = r[idx];
            int level = battByte & 0x0F;
            charging = (battByte & 0x10) != 0;
            percent = Math.Clamp(level * 10, 0, 100);
        }

        return new DeviceBatteryInfo
        {
            Key = $"ps:{c.ProductId:X4}",
            Name = modelName,
            Kind = DeviceKind.Gamepad,
            ConnectionVia = isBluetooth ? AppConstants.ViaBluetooth : AppConstants.ViaUsbCable,
            Percent = percent,
            IsCharging = charging,
            IsConnected = true,
            SourceReader = Name
        };
    }
}
