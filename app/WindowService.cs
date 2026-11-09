using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;

namespace PadLink;

/// <summary>从 exe 提取图标并转 PNG（带缓存），供网页显示真实应用图标。</summary>
internal static class IconService
{
    private static readonly object Lock = new();
    private static readonly Dictionary<string, byte[]?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static byte[]? GetForPid(uint pid)
    {
        try
        {
            var path = Process.GetProcessById((int)pid).MainModule?.FileName;
            return path == null ? null : GetForExe(path);
        }
        catch { return null; }
    }

    public static byte[]? GetForExe(string exePath)
    {
        lock (Lock)
        {
            if (Cache.TryGetValue(exePath, out var cached)) return cached;
            byte[]? png = null;
            try
            {
                using var icon = Icon.ExtractAssociatedIcon(exePath);
                if (icon != null)
                {
                    using var bmp = icon.ToBitmap();
                    using var resized = new Bitmap(bmp, new Size(48, 48));
                    using var ms = new MemoryStream();
                    resized.Save(ms, ImageFormat.Png);
                    png = ms.ToArray();
                }
            }
            catch { /* 提取失败（UWP/权限）→ 404 由前端回退 */ }
            Cache[exePath] = png;
            return png;
        }
    }
}

/// <summary>窗口枚举 / 激活 / 关闭 / 最小化。</summary>
internal static class WindowService
{
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr hWnd, int cmdShow);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    private const int SW_RESTORE = 9, SW_MINIMIZE = 6;
    private const uint WM_CLOSE = 0x0010;

    private static readonly HashSet<string> TitleBlacklist = new(StringComparer.OrdinalIgnoreCase)
    {
        "Program Manager", "Windows Input Experience", "Microsoft Text Input Application",
        "WeType Native Candidate", "WeType Candidate Shadow and Emoji",
        "New notification", "Windows Shell Experience Host",
    };

    public static List<object> ListWindows()
    {
        var result = new List<object>();
        EnumWindows((hWnd, _) =>
        {
            if (!IsWindowVisible(hWnd)) return true;
            var sb = new StringBuilder(256);
            GetWindowText(hWnd, sb, 256);
            var title = sb.ToString().Trim();
            if (title.Length == 0 || TitleBlacklist.Contains(title)) return true;
            if (title.StartsWith("PadLink", StringComparison.OrdinalIgnoreCase)) return true;
            GetWindowThreadProcessId(hWnd, out var pid);
            result.Add(new { id = hWnd.ToInt64(), title, pid });
            return true;
        }, IntPtr.Zero);
        return result;
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

    /// <summary>关闭窗口：先 WM_CLOSE，700ms 后仍存在则恢复前台并发送 Alt+F4 兜底。</summary>
    public static bool CloseWindow(long id)
    {
        var hwnd = new IntPtr(id);
        if (!IsWindow(hwnd)) return false;
        PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        _ = Task.Run(async () =>
        {
            await Task.Delay(700);
            if (IsWindow(hwnd))
            {
                try
                {
                    if (IsIconic(hwnd)) ShowWindowAsync(hwnd, SW_RESTORE);
                    FocusWindow(hwnd.ToInt64());
                    Thread.Sleep(120);
                    InputService.KeyTap("alt+f4");
                    DebugLog.Write($"关闭兜底 Alt+F4: hwnd={id}");
                }
                catch (Exception ex) { DebugLog.Write($"关闭兜底失败: {ex.Message}"); }
            }
        });
        return true;
    }

    public static bool MinimizeWindow(long id)
    {
        var hwnd = new IntPtr(id);
        return IsWindow(hwnd) && ShowWindowAsync(hwnd, SW_MINIMIZE);
    }
}
