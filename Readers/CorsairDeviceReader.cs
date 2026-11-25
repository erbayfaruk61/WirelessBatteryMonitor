using WirelessBatteryMonitor.Constants;
using WirelessBatteryMonitor.Models;

namespace WirelessBatteryMonitor.Readers;

/// <summary>
/// Corsair kablosuz kulaklık (Void / HS serisi) ve Slipstream/Bragi fare/klavyeleri doğrudan HID ile okur.
/// </summary>
public sealed class CorsairDeviceReader : IDeviceReader
{
    public string Name => "Corsair HID";

    private static readonly HashSet<ushort> VoidFamilyPids =
    [
        0x1B1C, 0x1B27, 0x0A14, 0x0A16, 0x0A17, 0x0A1D, 0x0A1A,
        0x1B2A, 0x1B23, 0x1B29, 0x0A55, 0x0A51, 0x0A52, 0x0A38,
        0x0A4F, 0x0A2B, 0x0A75, 0x0A56
    ];

    private static readonly HashSet<ushort> BragiPids =
    [
        0x2A08, 0x2A02, 0x0A97, 0x0A64, 0x0A62, 0x0A3E, 0x0A3D, 0x2B00, 0x1BDC, 0x1B7E, 0x1B7F
    ];

    private static readonly Dictionary<ushort, (string Name, DeviceKind Kind)> KnownBragiModels = new()
    {
        [0x1B7E] = ("Corsair Dark Core RGB Pro SE", DeviceKind.Mouse),
        [0x1B7F] = ("Corsair Dark Core RGB Pro SE", DeviceKind.Mouse),
        [0x1B80] = ("Corsair Dark Core RGB Pro", DeviceKind.Mouse),
        [0x1B7A] = ("Corsair Harpoon RGB Wireless", DeviceKind.Mouse),
        [0x1B7C] = ("Corsair Ironclaw RGB Wireless", DeviceKind.Mouse),
    };

    private const byte VoidCmdPrefix = 0xC9;
    private const byte VoidCmdBattery = 0x64;

    private const byte BragiPropBattery = 0x0F;
    private const byte BragiPropChargeState = 0x10;
    private const byte BragiPropProductId = 0x12;
    private const byte BragiTargetSelf = 0x08;
    private const byte BragiMaxPairedTarget = 0x0F;

    public Task<IReadOnlyList<DeviceBatteryInfo>> ReadAsync(CancellationToken cancellationToken)
    {
        return Task.Run<IReadOnlyList<DeviceBatteryInfo>>(() =>
        {
            var results = new List<DeviceBatteryInfo>();
            var collections = HidNative.EnumerateDevices(AppConstants.VendorCorsair);
            var seenKeys = new HashSet<string>();

            foreach (var c in collections)
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (c.InputReportLength <= 0 || c.OutputReportLength <= 0) continue;

                // Void / Void Elite / HS70 / HS80 ailesi
                if (VoidFamilyPids.Contains(c.ProductId) && c.UsagePage >= 0xFF00)
                {
                    var voidDevice = TryReadVoidHeadset(c);
                    if (voidDevice is not null && seenKeys.Add(voidDevice.Key))
                    {
                        results.Add(voidDevice);
                        continue;
                    }
                }

                // Bragi / Slipstream protokolü (Dark Core RGB Pro SE, Virtuoso, Slipstream alıcılar)
                if ((BragiPids.Contains(c.ProductId) || c.UsagePage >= 0xFF00) &&
                    (c.OutputReportLength == 64 || c.OutputReportLength == 65) &&
                    (c.InputReportLength == 64 || c.InputReportLength == 65))
                {
                    foreach (var bragiDevice in TryReadBragiCollection(c))
                    {
                        if (seenKeys.Add(bragiDevice.Key))
                            results.Add(bragiDevice);
                    }
                }
            }

            return results;
        }, cancellationToken);
    }

    private DeviceBatteryInfo? TryReadVoidHeadset(HidCollectionInfo c)
    {
        using var handle = HidNative.OpenReadWrite(c.DevicePath);
        if (handle is null) return null;

        HidNative.Flush(handle);
        if (!HidNative.WriteReport(handle, [VoidCmdPrefix, VoidCmdBattery], c.OutputReportLength))
            return null;

        var r = HidNative.ReadReport(handle, c.InputReportLength);
        if (r is null || r.Length < 5) return null;

        byte status = r[4];
        bool connected = status != 0;
        bool charging = status is 4 or 5;
        int? percent = (status is 1 or 2 || charging) ? Math.Clamp(r[2] & 0x7F, 0, 100) : null;

        if (!connected && percent is null) return null;

        return new DeviceBatteryInfo
        {
            Key = $"corsair:void:{c.ProductId:X4}",
            Name = string.IsNullOrWhiteSpace(c.ProductName) ? "Corsair Void Wireless" : c.ProductName,
            Kind = DeviceKind.Headset,
            ConnectionVia = AppConstants.ViaReceiver24Ghz,
            Percent = percent,
            IsCharging = charging,
            IsConnected = connected,
            SourceReader = Name
        };
    }

    private List<DeviceBatteryInfo> TryReadBragiCollection(HidCollectionInfo c)
    {
        var list = new List<DeviceBatteryInfo>();
        using var handle = HidNative.OpenReadWrite(c.DevicePath);
        if (handle is null) return list;

        bool numbered = c.OutputReportLength == 64;

        // Önce alıcı üzerindeki eşli hedefleri (0x09..0x0F), ardından cihazın kendisini (0x08) sorgula
        for (byte target = 0x09; target <= BragiMaxPairedTarget; target++)
        {
            if (TryReadBragiTarget(handle, c, numbered, target, out var pairedDevice) && pairedDevice is not null)
                list.Add(pairedDevice);
        }

        if (list.Count == 0 && TryReadBragiTarget(handle, c, numbered, BragiTargetSelf, out var selfDevice) && selfDevice is not null)
            list.Add(selfDevice);

        return list;
    }

    private bool TryReadBragiTarget(
        Microsoft.Win32.SafeHandles.SafeFileHandle handle,
        HidCollectionInfo c,
        bool numbered,
        byte target,
        out DeviceBatteryInfo? device)
    {
        device = null;
        if (!TryBragiGet(handle, c, numbered, target, BragiPropBattery, out uint rawBattery))
            return false;

        // Fare uyku modundayken alıcı 0 veya >1000 döndürebilir; birkaç kez tekrar dene
        for (int retry = 0; (rawBattery == 0 || rawBattery > 1000) && retry < 2; retry++)
        {
            if (!TryBragiGet(handle, c, numbered, target, BragiPropBattery, out rawBattery))
                return false;
        }

        // Hala 0 veya geçersizse fare uykudadır; %0 olarak raporlama
        if (rawBattery == 0 || rawBattery > 1000)
            return false;

        int percent = Math.Clamp((int)((rawBattery + 5) / 10), 1, 100);
        bool charging = false;
        if (TryBragiGet(handle, c, numbered, target, BragiPropChargeState, out uint chargeState))
            charging = chargeState == 1;

        ushort modelPid = c.ProductId;
        if (TryBragiGet(handle, c, numbered, target, BragiPropProductId, out uint reportedPid) && reportedPid > 0)
            modelPid = (ushort)reportedPid;

        string name = string.IsNullOrWhiteSpace(c.ProductName) ? "Corsair Wireless" : c.ProductName;
        DeviceKind kind = DeviceKind.Mouse;

        if (KnownBragiModels.TryGetValue(modelPid, out var known))
        {
            name = known.Name;
            kind = known.Kind;
        }
        else if (name.Contains("Void", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("Virtuoso", StringComparison.OrdinalIgnoreCase) ||
                 name.Contains("HS", StringComparison.OrdinalIgnoreCase))
        {
            kind = DeviceKind.Headset;
        }

        device = new DeviceBatteryInfo
        {
            Key = $"corsair:bragi:{modelPid:X4}",
            Name = name,
            Kind = kind,
            ConnectionVia = target == BragiTargetSelf ? AppConstants.ViaHid : AppConstants.ViaReceiver24Ghz,
            Percent = percent,
            IsCharging = charging,
            IsConnected = true,
            SourceReader = Name
        };
        return true;
    }

    private static bool TryBragiGet(
        Microsoft.Win32.SafeHandles.SafeFileHandle handle,
        HidCollectionInfo c,
        bool numbered,
        byte target,
        byte prop,
        out uint value)
    {
        value = 0;
        if (TryBragiGetFramed(handle, c, numbered, plain: false, target, prop, out value))
            return true;
        return TryBragiGetFramed(handle, c, numbered, plain: true, target, prop, out value);
    }

    private static bool TryBragiGetFramed(
        Microsoft.Win32.SafeHandles.SafeFileHandle handle,
        HidCollectionInfo c,
        bool numbered,
        bool plain,
        byte target,
        byte prop,
        out uint value)
    {
        value = 0;
        byte[] req = plain
            ? [0x00, target, 0x02, prop]
            : (numbered ? [0x02, target, 0x02, prop] : [0x00, 0x02, target, 0x02, prop]);

        HidNative.Flush(handle);
        if (!HidNative.WriteReport(handle, req, c.OutputReportLength, 250))
            return false;

        byte expectedSource = target == BragiTargetSelf ? (byte)0x00 : (byte)0x01;
        int offset = plain
            ? (c.InputReportLength == 65 ? 1 : 0)
            : (c.InputReportLength == 65 ? 2 : 1);

        var deadline = Environment.TickCount64 + 300;
        while (Environment.TickCount64 < deadline)
        {
            var r = HidNative.ReadReport(handle, c.InputReportLength, 200);
            if (r is null) return false;
            if (r.Length < offset + 7) continue;
            if (r[offset + 1] != 0x02 || r[offset] != expectedSource) continue;
            if (r[offset + 2] != 0x00) return false;

            value = (uint)(r[offset + 3] | (r[offset + 4] << 8) | (r[offset + 5] << 16) | (r[offset + 6] << 24));
            return true;
        }
        return false;
    }
}
