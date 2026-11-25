using WirelessBatteryMonitor.Constants;
using WirelessBatteryMonitor.Models;

namespace WirelessBatteryMonitor.Readers;

/// <summary>
/// Logitech HID++ 2.0 (Unifying / Lightspeed / Bolt / Doğrudan) kablosuz cihazları okur.
/// </summary>
public sealed class LogitechDeviceReader : IDeviceReader
{
    public string Name => "Logitech HID++";

    private const byte HidppShortReportId = 0x10;
    private const byte HidppLongReportId = 0x11;
    private const byte SwId = 0x0A;

    public Task<IReadOnlyList<DeviceBatteryInfo>> ReadAsync(CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<DeviceBatteryInfo>>(() =>
        {
            var results = new List<DeviceBatteryInfo>();
            var collections = HidNative.EnumerateDevices(AppConstants.VendorLogitech);
            var seenKeys = new HashSet<string>();

            foreach (var c in collections)
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (c.UsagePage != 0xFF00 && c.UsagePage != 0xFF43) continue;
                if (c.OutputReportLength is not (7 or 20)) continue;

                using var handle = HidNative.OpenReadWrite(c.DevicePath);
                if (handle is null) continue;

                // Doğrudan cihaz (0xFF) ve alıcı arkasındaki eşli hedefler (0x01..0x06)
                byte[] targets = [0x01, 0x02, 0x03, 0xFF];
                foreach (byte target in targets)
                {
                    if (cancellationToken.IsCancellationRequested) break;
                    var info = TryQueryHidppBattery(handle, c, target);
                    if (info is not null && seenKeys.Add(info.Key))
                        results.Add(info);
                }
            }

            return results;
        }, cancellationToken);
    }

    private DeviceBatteryInfo? TryQueryHidppBattery(
        Microsoft.Win32.SafeHandles.SafeFileHandle handle,
        HidCollectionInfo c,
        byte deviceIndex)
    {
        // Feature 0x1004 (Unified Battery) veya 0x1000 (Battery Level Status) indeksini bul
        if (TryGetFeatureIndex(handle, c, deviceIndex, 0x10, 0x04, out byte unifiedIdx))
        {
            var resp = SendHidpp(handle, c, deviceIndex, unifiedIdx, 0x10);
            if (resp is not null && resp.Length >= 7)
            {
                int percent = Math.Clamp((int)resp[4], 0, 100);
                bool charging = resp[6] is 1 or 2 or 3;
                return BuildDevice(c, deviceIndex, percent, charging);
            }
        }

        if (TryGetFeatureIndex(handle, c, deviceIndex, 0x10, 0x00, out byte legacyIdx))
        {
            var resp = SendHidpp(handle, c, deviceIndex, legacyIdx, 0x00);
            if (resp is not null && resp.Length >= 7)
            {
                int percent = Math.Clamp((int)resp[4], 0, 100);
                bool charging = resp[6] is 1 or 2 or 3;
                return BuildDevice(c, deviceIndex, percent, charging);
            }
        }

        return null;
    }

    private static bool TryGetFeatureIndex(
        Microsoft.Win32.SafeHandles.SafeFileHandle handle,
        HidCollectionInfo c,
        byte deviceIndex,
        byte featHi,
        byte featLo,
        out byte featureIndex)
    {
        featureIndex = 0;
        var resp = SendHidpp(handle, c, deviceIndex, 0x00, 0x00, featHi, featLo);
        if (resp is null || resp.Length < 5) return false;
        if (resp[2] == 0x8F || resp[4] == 0x00) return false;
        featureIndex = resp[4];
        return true;
    }

    private static byte[]? SendHidpp(
        Microsoft.Win32.SafeHandles.SafeFileHandle handle,
        HidCollectionInfo c,
        byte deviceIndex,
        byte featureIndex,
        byte functionAndSw,
        byte p0 = 0,
        byte p1 = 0)
    {
        bool useLong = c.OutputReportLength >= 20;
        byte[] req = useLong
            ? [HidppLongReportId, deviceIndex, featureIndex, (byte)(functionAndSw | SwId), p0, p1]
            : [HidppShortReportId, deviceIndex, featureIndex, (byte)(functionAndSw | SwId), p0, p1, 0];

        HidNative.Flush(handle);
        if (!HidNative.WriteReport(handle, req, c.OutputReportLength, 200))
            return null;

        var deadline = Environment.TickCount64 + 250;
        while (Environment.TickCount64 < deadline)
        {
            var r = HidNative.ReadReport(handle, c.InputReportLength, 180);
            if (r is null || r.Length < 5) return null;
            if (r[1] == deviceIndex && (r[2] == featureIndex || r[2] == 0x8F))
                return r;
        }
        return null;
    }

    private DeviceBatteryInfo BuildDevice(HidCollectionInfo c, byte deviceIndex, int percent, bool charging)
    {
        string name = string.IsNullOrWhiteSpace(c.ProductName) ? "Logitech Kablosuz Cihaz" : c.ProductName;
        var kind = name.Contains("Keyboard", StringComparison.OrdinalIgnoreCase)
            ? DeviceKind.Keyboard
            : name.Contains("Headset", StringComparison.OrdinalIgnoreCase) || name.Contains("G73", StringComparison.OrdinalIgnoreCase)
                ? DeviceKind.Headset
                : DeviceKind.Mouse;

        return new DeviceBatteryInfo
        {
            Key = $"logitech:{c.ProductId:X4}:{deviceIndex:X2}",
            Name = name,
            Kind = kind,
            ConnectionVia = deviceIndex == 0xFF ? AppConstants.ViaHid : AppConstants.ViaReceiver24Ghz,
            Percent = percent,
            IsCharging = charging,
            IsConnected = true,
            SourceReader = Name
        };
    }
}
