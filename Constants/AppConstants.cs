namespace WirelessBatteryMonitor.Constants;

/// <summary>
/// Uygulama genelinde kullanılan sabitler, donanım kimlikleri ve arayüz metinleri.
/// </summary>
public static class AppConstants
{
    public const string AppName = "Kablosuz Pil İzleyici";
    public const string AppId = "WirelessBatteryMonitor";
    public const string SettingsFileName = "settings.json";
    public const string StartupRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public const int DefaultPollIntervalSeconds = 30;
    public const int MinPollIntervalSeconds = 5;
    public const int MaxPollIntervalSeconds = 120;

    public const int DefaultLowBatteryThreshold = 30;
    public const int MinLowBatteryThreshold = 5;
    public const int MaxLowBatteryThreshold = 50;

    public const int MaxHistoryPoints = 30;
    public const int HidTimeoutMs = 450;

    // Üretici (VID) Kimlikleri
    public const ushort VendorCorsair = 0x1B1C;
    public const ushort VendorSteelSeries = 0x1038;
    public const ushort VendorLogitech = 0x046D;
    public const ushort VendorRazer = 0x1532;
    public const ushort VendorHyperXHp = 0x03F0;
    public const ushort VendorHyperXKingston = 0x0951;
    public const ushort VendorSony = 0x054C;
    public const ushort VendorMicrosoft = 0x045E;
    public const ushort VendorGameSir = 0x3537;

    // Bluetooth Pil Özelliği GUID'i (DEVPKEY_Bluetooth_Battery)
    public const string BluetoothBatteryPropertyKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    public const string DeviceConnectedPropertyKey = "{83DA6326-97A6-4088-9453-A1923F573B29} 15";
    public const string BluetoothAepContainerPrefix = "System.Devices.ContainerId";

    // Bağlantı Türü Metinleri
    public const string ViaReceiver24Ghz = "2.4 GHz Alıcı";
    public const string ViaBluetooth = "Bluetooth";
    public const string ViaXboxWireless = "Xbox Wireless / Bluetooth";
    public const string ViaUsbCable = "USB Kablo";
    public const string ViaHid = "USB HID";

    // Durum Metinleri
    public const string StatusCharging = "Şarj oluyor";
    public const string StatusFull = "Tam dolu";
    public const string StatusConnected = "Bağlı";
    public const string StatusDisconnected = "Bağlı değil (Son bilinen)";
    public const string StatusNoDevices = "Kablosuz cihaz bulunamadı";
}
