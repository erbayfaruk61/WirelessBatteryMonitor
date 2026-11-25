using System.Runtime.InteropServices;
using Windows.Gaming.Input;
using Windows.System.Power;
using WirelessBatteryMonitor.Constants;
using WirelessBatteryMonitor.Models;

namespace WirelessBatteryMonitor.Readers;

/// <summary>
/// Xbox kablosuz oyun kollarını Windows.Gaming.Input (gerçek yüzde) ve XInput (kaba seviye) üzerinden okur.
/// </summary>
public sealed class XboxControllerReader : IDeviceReader
{
    public string Name => "Xbox / XInput";

    private const int MaxXInputControllers = 4;
    private const byte BatteryDevTypeGamepad = 0x00;
    private const byte BatteryTypeWired = 0x01;
    private const byte BatteryTypeDisconnected = 0x00;
    private const byte BatteryLevelEmpty = 0x00;
    private const byte BatteryLevelLow = 0x01;
    private const byte BatteryLevelMedium = 0x02;
    private const byte BatteryLevelFull = 0x03;

    [StructLayout(LayoutKind.Sequential)]
    private struct XInputBatteryInformation
    {
        public byte BatteryType;
        public byte BatteryLevel;
    }

    [DllImport("xinput1_4.dll")]
    private static extern uint XInputGetBatteryInformation(uint dwUserIndex, byte devType, out XInputBatteryInformation pBatteryInformation);

    public async Task<IReadOnlyList<DeviceBatteryInfo>> ReadAsync(CancellationToken cancellationToken)
    {
        var results = new List<DeviceBatteryInfo>();
        bool hasExactWgi = false;

        try
        {
            var controllers = RawGameController.RawGameControllers;
            for (int i = 0; i < controllers.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested) break;
                var ctrl = controllers[i];
                if (ctrl.HardwareVendorId != AppConstants.VendorMicrosoft &&
                    ctrl.HardwareVendorId != AppConstants.VendorGameSir)
                {
                    continue;
                }

                var report = ctrl.TryGetBatteryReport();
                if (report is null || report.Status == BatteryStatus.NotPresent) continue;

                int? full = report.FullChargeCapacityInMilliwattHours;
                int? rem = report.RemainingCapacityInMilliwattHours;
                if (full is null or <= 0 || rem is null) continue;

                int percent = Math.Clamp((int)Math.Round(rem.Value * 100.0 / full.Value), 0, 100);
                string displayName = string.IsNullOrWhiteSpace(ctrl.DisplayName)
                    ? "Xbox Kablosuz Oyun Kolu"
                    : ctrl.DisplayName;

                results.Add(new DeviceBatteryInfo
                {
                    Key = $"xbox:wgi:{ctrl.HardwareProductId:X4}:{i}",
                    Name = displayName,
                    Kind = DeviceKind.Gamepad,
                    ConnectionVia = AppConstants.ViaXboxWireless,
                    Percent = percent,
                    IsCharging = report.Status == BatteryStatus.Charging,
                    IsConnected = true,
                    SourceReader = Name
                });
                hasExactWgi = true;
            }
        }
        catch
        {
            // WinRT RawGameController erişilemezse XInput fallback'e düş
        }

        if (!hasExactWgi)
        {
            await Task.Run(() => ReadXInputFallback(results, cancellationToken), cancellationToken);
        }

        return results;
    }

    private void ReadXInputFallback(List<DeviceBatteryInfo> results, CancellationToken ct)
    {
        for (uint i = 0; i < MaxXInputControllers; i++)
        {
            if (ct.IsCancellationRequested) break;
            try
            {
                if (XInputGetBatteryInformation(i, BatteryDevTypeGamepad, out var info) != 0)
                    continue;

                if (info.BatteryType == BatteryTypeDisconnected)
                    continue;

                var (coarseText, approxPercent) = info.BatteryLevel switch
                {
                    BatteryLevelEmpty => ("Boş", 5),
                    BatteryLevelLow => ("Düşük", 25),
                    BatteryLevelMedium => ("Orta", 60),
                    BatteryLevelFull => ("Dolu", 100),
                    _ => ("--", (int?)null)
                };

                results.Add(new DeviceBatteryInfo
                {
                    Key = $"xbox:xinput:{i}",
                    Name = $"Xbox Oyun Kolu #{i + 1}",
                    Kind = DeviceKind.Gamepad,
                    ConnectionVia = info.BatteryType == BatteryTypeWired
                        ? AppConstants.ViaUsbCable
                        : AppConstants.ViaXboxWireless,
                    Percent = approxPercent,
                    CoarseLevelText = coarseText,
                    IsCharging = info.BatteryType == BatteryTypeWired,
                    IsConnected = true,
                    SourceReader = Name
                });
            }
            catch
            {
                break;
            }
        }
    }
}
