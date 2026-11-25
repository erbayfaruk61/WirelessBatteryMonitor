using WirelessBatteryMonitor.Constants;

namespace WirelessBatteryMonitor.Models;

/// <summary>
/// Desteklenen kablosuz cihaz kategorileri.
/// </summary>
public enum DeviceKind
{
    Mouse,
    Keyboard,
    Headset,
    Earbuds,
    Gamepad,
    Other
}

/// <summary>
/// Pil geçmişi grafiği (sparkline) için tek bir ölçüm noktası.
/// </summary>
public sealed record BatteryHistoryPoint(DateTime Timestamp, int Percent, bool IsCharging);

/// <summary>
/// Kulaklık/TWS gibi çoklu pil içeren cihazlar için alt pil bilgisi (Sol, Sağ, Kutu).
/// </summary>
public sealed record SubBatteryInfo(string Label, int Percent, bool IsCharging = false);

/// <summary>
/// Tek bir kablosuz cihazın anlık pil ve bağlantı durumunu temsil eder.
/// </summary>
public sealed class DeviceBatteryInfo
{
    public required string Key { get; init; }
    public required string Name { get; set; }
    public DeviceKind Kind { get; set; } = DeviceKind.Other;
    public string ConnectionVia { get; set; } = AppConstants.ViaHid;

    /// <summary>0..100 arası pil yüzdesi. Bilinmiyorsa null.</summary>
    public int? Percent { get; set; }

    /// <summary>Yüzde yerine metin gösterilmesi gereken durumlar (örn. XInput kaba seviye).</summary>
    public string? CoarseLevelText { get; set; }

    public bool IsCharging { get; set; }
    public bool IsConnected { get; set; } = true;
    public int? Millivolts { get; set; }
    public DateTime LastUpdated { get; set; } = DateTime.Now;
    public string SourceReader { get; set; } = string.Empty;

    public List<SubBatteryInfo> SubBatteries { get; } = [];
    public List<BatteryHistoryPoint> History { get; } = [];

    public string DisplayPercentText =>
        Percent.HasValue ? $"%{Percent.Value}" : (CoarseLevelText ?? "--");

    public string SubBatteriesText =>
        SubBatteries.Count == 0
            ? string.Empty
            : string.Join(" · ", SubBatteries.Select(b => $"{b.Label}: %{b.Percent}{(b.IsCharging ? "⚡" : string.Empty)}"));

    public string KindGlyph => Kind switch
    {
        DeviceKind.Mouse => "\uE962",
        DeviceKind.Keyboard => "\uE765",
        DeviceKind.Headset => "\uE7F6",
        DeviceKind.Earbuds => "\uE95B",
        DeviceKind.Gamepad => "\uE7FC",
        _ => "\uE839"
    };

    public string StatusSummary
    {
        get
        {
            if (!IsConnected) return AppConstants.StatusDisconnected;
            if (IsCharging) return Percent == 100 ? AppConstants.StatusFull : AppConstants.StatusCharging;
            return AppConstants.StatusConnected;
        }
    }

    /// <summary>
    /// Geçmiş ölçümlerden saatlik deşarj/şarj hızına göre tahmini kalan süre metni üretir.
    /// </summary>
    public string? EstimatedRemainingText
    {
        get
        {
            if (!IsConnected || !Percent.HasValue || History.Count < 2) return null;
            var first = History[0];
            var last = History[^1];
            var hours = (last.Timestamp - first.Timestamp).TotalHours;
            if (hours < 0.05) return null;

            var delta = last.Percent - first.Percent;
            if (IsCharging && delta > 0)
            {
                var ratePerHour = delta / hours;
                var hoursLeft = (100 - last.Percent) / ratePerHour;
                if (hoursLeft is > 0 and < 24)
                    return $"~{FormatDuration(TimeSpan.FromHours(hoursLeft))} sonra tam dolu";
            }
            else if (!IsCharging && delta < 0)
            {
                var drainPerHour = Math.Abs(delta) / hours;
                var hoursLeft = last.Percent / drainPerHour;
                if (hoursLeft is > 0 and < 200)
                    return $"~{FormatDuration(TimeSpan.FromHours(hoursLeft))} kaldı (-{drainPerHour:F1}%/sa)";
            }
            return null;
        }
    }

    private static string FormatDuration(TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours} sa {ts.Minutes} dk";
        return $"{Math.Max(1, ts.Minutes)} dk";
    }
}
