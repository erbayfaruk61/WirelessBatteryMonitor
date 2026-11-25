using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using WirelessBatteryMonitor.Constants;
using WirelessBatteryMonitor.Models;

namespace WirelessBatteryMonitor.Services;

/// <summary>
/// Sistem tepsisi ikonunu, sağ tık menüsünü ve düşük pil balon bildirimlerini yönetir.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _toggleWidgetItem;
    private Icon _currentIcon;

    public event Action? OpenFlyoutRequested;
    public event Action? RefreshRequested;
    public event Action? ToggleTaskbarWidgetRequested;
    public event Action? ExitRequested;

    public TrayIconService(bool widgetVisible)
    {
        _currentIcon = CreateBatteryIcon(100, isCharging: false);

        _menu = new ContextMenuStrip();
        var openItem = new ToolStripMenuItem("Paneli Aç", null, (_, _) => OpenFlyoutRequested?.Invoke());
        var refreshItem = new ToolStripMenuItem("Şimdi Yenile", null, (_, _) => RefreshRequested?.Invoke());
        _toggleWidgetItem = new ToolStripMenuItem("Görev Çubuğu Göstergesi")
        {
            Checked = widgetVisible,
            CheckOnClick = false
        };
        _toggleWidgetItem.Click += (_, _) => ToggleTaskbarWidgetRequested?.Invoke();

        var exitItem = new ToolStripMenuItem("Çıkış", null, (_, _) => ExitRequested?.Invoke());

        _menu.Items.Add(openItem);
        _menu.Items.Add(refreshItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(_toggleWidgetItem);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = _currentIcon,
            Text = AppConstants.AppName,
            Visible = true,
            ContextMenuStrip = _menu
        };

        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                OpenFlyoutRequested?.Invoke();
        };
    }

    public void UpdateWidgetChecked(bool isChecked) => _toggleWidgetItem.Checked = isChecked;

    public void SetVisible(bool visible) => _notifyIcon.Visible = visible;

    public void UpdateStatus(IReadOnlyList<DeviceBatteryInfo> devices, int lowThreshold)
    {
        var connected = devices.Where(d => d.IsConnected && d.Percent.HasValue).ToList();
        if (connected.Count == 0)
        {
            _notifyIcon.Text = AppConstants.AppName;
            SwapIcon(CreateBatteryIcon(null, false));
            return;
        }

        var lowest = connected.OrderBy(d => d.Percent!.Value).First();
        string tip = string.Join("\n", connected.Take(3).Select(d => $"{d.Name}: {d.DisplayPercentText}"));
        _notifyIcon.Text = tip.Length > 63 ? tip[..60] + "..." : tip;

        bool isLow = lowest.Percent!.Value <= lowThreshold && !lowest.IsCharging;
        SwapIcon(CreateBatteryIcon(lowest.Percent.Value, lowest.IsCharging, isLow));
    }

    public void ShowLowBatteryAlert(DeviceBatteryInfo device)
    {
        _notifyIcon.ShowBalloonTip(
            5000,
            "Düşük Pil Uyarısı",
            $"{device.Name} pil seviyesi {device.DisplayPercentText} seviyesine düştü.",
            ToolTipIcon.Warning);
    }

    public void ShowCriticalBatteryAlert(DeviceBatteryInfo device)
    {
        _notifyIcon.ShowBalloonTip(
            7000,
            "Kritik Pil Uyarısı!",
            $"{device.Name} kritik pil seviyesinde ({device.DisplayPercentText})! Lütfen şarj edin.",
            ToolTipIcon.Error);
    }

    public void ShowContextMenuAtCursor()
    {
        _menu.Show(Cursor.Position);
    }

    private void SwapIcon(Icon next)
    {
        var old = _currentIcon;
        _currentIcon = next;
        _notifyIcon.Icon = _currentIcon;
        old.Dispose();
    }

    private static Icon CreateBatteryIcon(int? percent, bool isCharging, bool isLow = false)
    {
        using var bmp = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        Color fillColor = isCharging
            ? Color.FromArgb(96, 165, 250)
            : isLow
                ? Color.FromArgb(239, 68, 68)
                : percent.HasValue
                    ? Color.FromArgb(52, 211, 153)
                    : Color.FromArgb(156, 163, 175);

        using var borderPen = new Pen(Color.White, 2f);
        g.DrawRectangle(borderPen, 4, 8, 21, 16);
        g.FillRectangle(Brushes.White, 25, 12, 3, 8);

        if (percent.HasValue)
        {
            int fillWidth = Math.Max(2, (int)Math.Round(17.0 * percent.Value / 100.0));
            using var fillBrush = new SolidBrush(fillColor);
            g.FillRectangle(fillBrush, 6, 10, fillWidth, 12);
        }

        return Icon.FromHandle(bmp.GetHicon());
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _menu.Dispose();
        _currentIcon.Dispose();
    }
}
