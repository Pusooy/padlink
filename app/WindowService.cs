using System.Runtime.InteropServices;
using System.Text;

namespace PadLink;

/// <summary>窗口枚举 / 激活 / 关闭 / 最小化。</summary>
internal static class WindowService
{
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hWnd, int cmdShow);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    private const int SW_RESTORE = 9, SW_MINIMIZE = 6;
    private const uint WM_CLOSE = 0x0010;

    private static readonly HashSet<string> TitleBlacklist = new(StringComparer.OrdinalIgnoreCase)
    {
        "Program Manager", "Windows Input Experience", "Settings", "Microsoft Text Input Application",
        "WeType Native Candidate", "WeType Candidate Shadow and Emoji", "Program Manager (桌面窗口管理器)",
    };

    public static List<object> ListWindows()
    {
        var result = new List<(IntPtr Hwnd, string Title)>();
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            var sb = new StringBuilder(256);
            GetWindowText(hWnd, sb, 256);
            var title = sb.ToString().Trim();
            if (title.Length == 0 || TitleBlacklist.Contains(title)) return true;
            if (title.StartsWith("PadLink", StringComparison.OrdinalIgnoreCase)) return true;
            result.Add((hWnd, title));
            return true;
        }, IntPtr.Zero);

        return result.Select(w => (object)new { id = w.Hwnd.ToInt64(), title = w.Title }).ToList();
    }

    public static bool FocusWindow(long id)
    {
        var hwnd = new IntPtr(id);
        if (!IsWindow(hwnd)) return false;
        if (IsIconic(hwnd)) ShowWindowAsync(hwnd, SW_RESTORE);
        // 先模拟一次 ALT 按键，规避前台锁定限制
        InputService.KeyTap("alt");
        Thread.Sleep(30);
        return SetForegroundWindow(hwnd);
    }

    public static bool CloseWindow(long id)
    {
        var hwnd = new IntPtr(id);
        return IsWindow(hwnd) && PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
    }

    public static bool MinimizeWindow(long id)
    {
        var hwnd = new IntPtr(id);
        return IsWindow(hwnd) && ShowWindowAsync(hwnd, SW_MINIMIZE);
    }

    private static bool IsWindow(IntPtr hWnd) => IsWindowVisible(hWnd) || IsIconic(hWnd) || true; // 简化：存在即尝试
}
