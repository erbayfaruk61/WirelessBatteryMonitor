using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Devices.Enumeration;
using Windows.Storage.Streams;
using WirelessBatteryMonitor.Constants;
using WirelessBatteryMonitor.Models;

namespace WirelessBatteryMonitor.Readers;

/// <summary>
/// Bluetooth LE GATT Pil Servisi (0x180F) üzerinden tekli veya çoklu pil (Sol/Sağ/Kutu) değerlerini okur.
/// </summary>
public sealed class BleGattBatteryReader : IDeviceReader
{
    public string Name => "BLE GATT Pil";

    private static readonly Guid BatteryServiceUuid = new("0000180f-0000-1000-8000-00805f9b34fb");
    private static readonly Guid BatteryLevelCharUuid = new("00002a19-0000-1000-8000-00805f9b34fb");

    public async Task<IReadOnlyList<DeviceBatteryInfo>> ReadAsync(CancellationToken cancellationToken)
    {
        var results = new List<DeviceBatteryInfo>();

        try
        {
            string selector = BluetoothLEDevice.GetDeviceSelectorFromConnectionStatus(BluetoothConnectionStatus.Connected);
            var devices = await DeviceInformation.FindAllAsync(selector).AsTask(cancellationToken);

            foreach (var devInfo in devices)
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    using var ble = await BluetoothLEDevice.FromIdAsync(devInfo.Id).AsTask(cancellationToken);
                    if (ble is null || ble.ConnectionStatus != BluetoothConnectionStatus.Connected) continue;

                    var svcResult = await ble.GetGattServicesForUuidAsync(BatteryServiceUuid, BluetoothCacheMode.Uncached).AsTask(cancellationToken);
                    if (svcResult.Status != GattCommunicationStatus.Success || svcResult.Services.Count == 0)
                        continue;

                    var subBatteries = new List<SubBatteryInfo>();
                    foreach (var svc in svcResult.Services)
                    {
                        using (svc)
                        {
                            var charsResult = await svc.GetCharacteristicsForUuidAsync(BatteryLevelCharUuid, BluetoothCacheMode.Uncached).AsTask(cancellationToken);
                            if (charsResult.Status != GattCommunicationStatus.Success) continue;

                            for (int idx = 0; idx < charsResult.Characteristics.Count; idx++)
                            {
                                var ch = charsResult.Characteristics[idx];
                                var valResult = await ch.ReadValueAsync(BluetoothCacheMode.Uncached).AsTask(cancellationToken);
                                if (valResult.Status != GattCommunicationStatus.Success || valResult.Value is null || valResult.Value.Length == 0)
                                    continue;

                                using var reader = DataReader.FromBuffer(valResult.Value);
                                int pct = Math.Clamp((int)reader.ReadByte(), 0, 100);
                                string label = charsResult.Characteristics.Count > 1
                                    ? idx switch { 0 => "Sol", 1 => "Sağ", 2 => "Kutu", _ => $"Pil {idx + 1}" }
                                    : "Ana";
                                subBatteries.Add(new SubBatteryInfo(label, pct));
                            }
                        }
                    }

                    if (subBatteries.Count == 0) continue;

                    int mainPercent = subBatteries.Min(b => b.Percent);
                    var info = new DeviceBatteryInfo
                    {
                        Key = $"ble:{ble.BluetoothAddress:X12}",
                        Name = string.IsNullOrWhiteSpace(ble.Name) ? devInfo.Name : ble.Name,
                        Kind = DeviceKind.Other,
                        ConnectionVia = AppConstants.ViaBluetooth,
                        Percent = mainPercent,
                        IsConnected = true,
                        SourceReader = Name
                    };

                    if (subBatteries.Count > 1)
                        info.SubBatteries.AddRange(subBatteries);

                    results.Add(info);
                }
                catch
                {
                    // Tek bir BLE cihaz hatası diğerlerini etkilememeli
                }
            }
        }
        catch
        {
            // Bluetooth kapalıysa sessizce boş dön
        }

        return results;
    }
}
