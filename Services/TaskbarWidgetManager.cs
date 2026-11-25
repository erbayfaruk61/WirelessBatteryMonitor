using System.Runtime.InteropServices;
using System.Text;
using WirelessBatteryMonitor.Models;
using WirelessBatteryMonitor.Views;

namespace WirelessBatteryMonitor.Services;

/// <summary>
/// Birincil ve ikincil monitörlerdeki görev çubuğu widget pencerelerini yönetir.
/// </summary>
public sealed class TaskbarWidgetManager : IDisposable
{
    private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint hWnd);

    private readonly Dictionary<nint, TaskbarWidgetWindow> _widgets = new();

    public event Action? LeftClicked;
    public event Action? RightClicked;

    public void Sync(IReadOnlyList<DeviceBatteryInfo> devices, AppSettings settings)
    {
        if (!settings.ShowTaskbarWidget)
        {
            HideAll();
            return;
        }

        var targets = FindTaskbars(settings.ShowOnAllMonitors);
        var targetSet = targets.Select(t => t.Hwnd).ToHashSet();

        // Artık olmayan veya devre dışı bırakılan monitör widget'larını kaldır
        foreach (var hwnd in _widgets.Keys.Where(k => !targetSet.Contains(k)).ToList())
        {
            _widgets[hwnd].Close();
            _widgets.Remove(hwnd);
        }

        // Her hedef görev çubuğu için widget oluştur/güncelle
        foreach (var (hwnd, isPrimary) in targets)
        {
            if (!_widgets.TryGetValue(hwnd, out var widget))
            {
                widget = new TaskbarWidgetWindow();
                widget.LeftClicked += () => LeftClicked?.Invoke();
                widget.RightClicked += () => RightClicked?.Invoke();
                _widgets[hwnd] = widget;
            }

            widget.AssignTaskbar(hwnd, isPrimary);
            widget.UpdateDevices(devices, settings);
            if (!widget.IsVisible)
                widget.Show();
            widget.PositionForAssignedTaskbar();
        }
    }

    public void HideAll()
    {
        foreach (var widget in _widgets.Values)
            widget.Hide();
    }

    private static List<(nint Hwnd, bool IsPrimary)> FindTaskbars(bool includeSecondary)
    {
        var list = new List<(nint Hwnd, bool IsPrimary)>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            var sb = new StringBuilder(64);
            GetClassName(hwnd, sb, sb.Capacity);
            string cls = sb.ToString();

            if (cls == "Shell_TrayWnd")
                list.Add((hwnd, true));
            else if (includeSecondary && cls == "Shell_SecondaryTrayWnd")
                list.Add((hwnd, false));

            return true;
        }, nint.Zero);
        return list;
    }

    public void Dispose()
    {
        foreach (var widget in _widgets.Values)
            widget.Close();
        _widgets.Clear();
    }
}
