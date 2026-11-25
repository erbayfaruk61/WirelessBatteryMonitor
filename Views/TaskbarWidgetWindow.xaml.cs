using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using WirelessBatteryMonitor.Models;
using WirelessBatteryMonitor.Services;

namespace WirelessBatteryMonitor.Views;

public sealed class TaskbarDeviceItemVm
{
    public required string Glyph { get; init; }
    public required string PercentText { get; init; }
    public required string TooltipText { get; init; }
    public required System.Windows.Media.Brush AccentBrush { get; init; }
    public required System.Windows.Media.Brush TextBrush { get; init; }
    public required string ChargingIcon { get; init; }
    public required Visibility ChargingVisibility { get; init; }
}

public partial class TaskbarWidgetWindow : Window
{
    private const int GwlExStyle = -20;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint FindWindowEx(nint hwndParent, nint hwndChildAfter, string lpszClass, string? lpszWindow);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(nint hWnd, out NativeRect lpRect);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(nint hWnd, int nIndex, int dwNewLong);

    private nint _targetTaskbarHwnd;
    private bool _isPrimaryTaskbar = true;

    public event Action? LeftClicked;
    public event Action? RightClicked;

    public TaskbarWidgetWindow()
    {
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        SizeChanged += (_, _) => PositionForAssignedTaskbar();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = GetWindowLong(hwnd, GwlExStyle);
        SetWindowLong(hwnd, GwlExStyle, exStyle | WsExToolWindow | WsExNoActivate);
        PositionForAssignedTaskbar();
    }

    public void AssignTaskbar(nint taskbarHwnd, bool isPrimary)
    {
        _targetTaskbarHwnd = taskbarHwnd;
        _isPrimaryTaskbar = isPrimary;
        PositionForAssignedTaskbar();
    }

    public void UpdateDevices(IReadOnlyList<DeviceBatteryInfo> devices, AppSettings settings)
    {
        var visible = devices
            .Where(d => !settings.HiddenDeviceKeys.Contains(d.Key) &&
                        !settings.HiddenFromPanelKeys.Contains(d.Key) &&
                        (settings.ShowDisconnectedDevices || d.IsConnected))
            .ToList();

        if (visible.Count == 0)
        {
            DevicesItemsControl.ItemsSource = new[]
            {
                new TaskbarDeviceItemVm
                {
                    Glyph = "\uE839",
                    PercentText = "--",
                    TooltipText = "Görünür kablosuz cihaz yok",
                    AccentBrush = System.Windows.Media.Brushes.Gray,
                    TextBrush = System.Windows.Media.Brushes.Gray,
                    ChargingIcon = string.Empty,
                    ChargingVisibility = Visibility.Collapsed
                }
            };
        }
        else
        {
            DevicesItemsControl.ItemsSource = visible.Select(d =>
            {
                string displayName = settings.GetEffectiveName(d.Key, d.Name);
                int threshold = settings.GetEffectiveLowThreshold(d.Key);
                var color = GetStatusColor(d, threshold);
                var brush = new SolidColorBrush(color);

                string subLine = settings.ShowSubBatteries && d.SubBatteries.Count > 0
                    ? $"\n{d.SubBatteriesText}"
                    : string.Empty;

                return new TaskbarDeviceItemVm
                {
                    Glyph = d.KindGlyph,
                    PercentText = d.DisplayPercentText,
                    TooltipText = $"{displayName}\n{d.DisplayPercentText} · {d.StatusSummary}{subLine}\nBağlantı: {d.ConnectionVia}\nSon okuma: {d.LastUpdated:HH:mm:ss}",
                    AccentBrush = brush,
                    TextBrush = d.IsConnected ? System.Windows.Media.Brushes.White : System.Windows.Media.Brushes.DarkGray,
                    ChargingIcon = "⚡",
                    ChargingVisibility = d.IsCharging ? Visibility.Visible : Visibility.Collapsed
                };
            }).ToList();
        }

        PositionForAssignedTaskbar();
    }

    public void PositionForAssignedTaskbar()
    {
        try
        {
            if (_targetTaskbarHwnd == nint.Zero || !GetWindowRect(_targetTaskbarHwnd, out var tbRect))
                return;

            var source = PresentationSource.FromVisual(this);
            double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            double taskbarTop = tbRect.Top / dpiY;
            double taskbarLeft = tbRect.Left / dpiX;
            double taskbarRight = tbRect.Right / dpiX;
            double taskbarWidth = taskbarRight - taskbarLeft;
            double taskbarHeight = (tbRect.Bottom - tbRect.Top) / dpiY;

            double w = ActualWidth > 0 ? ActualWidth : 100;
            double h = ActualHeight > 0 ? ActualHeight : 28;

            double anchorRight = taskbarRight - 96;
            if (_isPrimaryTaskbar)
            {
                nint trayNotify = FindWindowEx(_targetTaskbarHwnd, nint.Zero, "TrayNotifyWnd", null);
                if (trayNotify != nint.Zero && GetWindowRect(trayNotify, out var trayRect))
                    anchorRight = trayRect.Left / dpiX;
            }

            if (taskbarWidth > taskbarHeight)
            {
                Left = Math.Max(taskbarLeft + 4, anchorRight - w - 8);
                Top = taskbarTop + (taskbarHeight - h) / 2.0;
            }
            else
            {
                Left = taskbarLeft + (taskbarWidth - w) / 2.0;
                Top = Math.Max(taskbarTop + 4, (tbRect.Bottom / dpiY) - h - 80);
            }
        }
        catch
        {
            // Konumlandırma hatası yoksayılır
        }
    }

    private static System.Windows.Media.Color GetStatusColor(DeviceBatteryInfo d, int lowThreshold)
    {
        if (!d.IsConnected) return System.Windows.Media.Color.FromRgb(140, 145, 155);
        if (d.IsCharging) return System.Windows.Media.Color.FromRgb(96, 165, 250);
        if (d.Percent.HasValue && d.Percent.Value <= lowThreshold)
            return System.Windows.Media.Color.FromRgb(239, 68, 68);
        return System.Windows.Media.Color.FromRgb(52, 211, 153);
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => LeftClicked?.Invoke();
    private void Window_MouseRightButtonUp(object sender, MouseButtonEventArgs e) => RightClicked?.Invoke();
}
