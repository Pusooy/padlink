using System.Runtime.InteropServices;
using PadLink.Core;

namespace PadLink.App.Services;

/// <summary>开机自启（HKCU Run）。</summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "PadLink";

    /// <summary>
    /// 解析用于自启的可执行路径。
    /// launcher 场景下 Environment.ProcessPath 指向解压目录里的 PadLink.UI.exe，
    /// 但自启必须指向用户手上的单文件 PadLink.exe（launcher 通过环境变量告知）。
    /// </summary>
    private static string ResolveLauncherPath()
    {
        var fromEnv = Environment.GetEnvironmentVariable("PADLINK_LAUNCHER");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
            return fromEnv;

        // 非 launcher 场景（直接运行 PadLink.UI.exe）：用它自己
        return Environment.ProcessPath ?? "";
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(ValueName) != null;
        }
        catch { return false; }
    }

    public static void Apply(bool enable)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
            if (enable)
            {
                var exe = ResolveLauncherPath();
                key.SetValue(ValueName, $"\"{exe}\" --tray");
                Log.Info($"已启用自启：{exe}");
            }
            else
            {
                key.DeleteValue(ValueName, false);
                Log.Info("已关闭自启");
            }
        }
        catch (Exception ex) { Log.Error("设置自启失败", ex); }
    }
}

/// <summary>Win32 托盘图标（Shell_NotifyIcon），HiDPI 适配。</summary>
public sealed class TrayIcon : IDisposable
{
    private const int WM_USER = 0x0400;
    private const int WM_TRAYICON = WM_USER + 1;
    private const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    private const int NIF_MESSAGE = 0x01, NIF_ICON = 0x02, NIF_TIP = 0x04;
    private const int WM_LBUTTONUP = 0x0202, WM_RBUTTONUP = 0x0205;
    private const int WM_COMMAND = 0x0111;

    private const int IDI_SHOW = 1001;
    private const int IDI_EXIT = 1002;

    private IntPtr _hwnd;
    private IntPtr _hIcon;
    private WndProcDelegate? _wndProc;
    private string _className = "PadLinkTrayWnd_" + Guid.NewGuid().ToString("N")[..8];

    public event Action? ShowRequested;
    public event Action? ExitRequested;

    public void Create()
    {
        try
        {
            _wndProc = WndProc;
            var wc = new WNDCLASS
            {
                lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
                hInstance = GetModuleHandle(null),
                lpszClassName = _className,
            };
            RegisterClass(ref wc);
            _hwnd = CreateWindowEx(0, _className, "PadLink Tray", 0, 0, 0, 0, 0,
                IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);

            // 从 exe 提取图标，按 SM_CXSMICON 缩放（HiDPI）
            var exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                var count = ExtractIconEx(exe, -1, null, null, 0);
                if (count > 0)
                {
                    var icons = new IntPtr[1];
                    ExtractIconEx(exe, 0, icons, null, 1);
                    _hIcon = icons[0];
                }
            }
            if (_hIcon == IntPtr.Zero) _hIcon = LoadIcon(IntPtr.Zero, IDI_APPLICATION);

            var data = NewData();
            data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
            data.uCallbackMessage = WM_TRAYICON;
            data.hIcon = _hIcon;
            data.szTip = "PadLink 服务";
            Shell_NotifyIcon(NIM_ADD, ref data);
            Log.Info("托盘图标已创建");
        }
        catch (Exception ex) { Log.Error("创建托盘失败", ex); }
    }

    private NOTIFYICONDATA NewData()
    {
        return new NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
            hWnd = _hwnd,
            uID = 1,
        };
    }

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (msg == WM_TRAYICON)
            {
                var evt = lParam.ToInt32() & 0xFFFF;
                if (evt == WM_LBUTTONUP) ShowRequested?.Invoke();
                else if (evt == WM_RBUTTONUP) ShowContextMenu();
            }
            else if (msg == WM_COMMAND)
            {
                var id = wParam.ToInt32() & 0xFFFF;
                if (id == IDI_SHOW) ShowRequested?.Invoke();
                else if (id == IDI_EXIT) ExitRequested?.Invoke();
            }
        }
        catch (Exception ex) { Log.Error("托盘消息处理失败", ex); }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        AppendMenu(menu, 0, IDI_SHOW, "打开控制台");
        AppendMenu(menu, 0x800, 0, null);          // MF_SEPARATOR
        AppendMenu(menu, 0, IDI_EXIT, "退出");

        GetCursorPos(out var pt);
        SetForegroundWindow(_hwnd);
        TrackPopupMenuEx(menu, 0x0100 | 0x0002, pt.X, pt.Y, _hwnd, IntPtr.Zero);
        PostMessage(_hwnd, 0, IntPtr.Zero, IntPtr.Zero);
        DestroyMenu(menu);
    }

    public void Dispose()
    {
        try
        {
            var data = NewData();
            Shell_NotifyIcon(NIM_DELETE, ref data);
        }
        catch { }
        if (_hIcon != IntPtr.Zero) { DestroyIcon(_hIcon); _hIcon = IntPtr.Zero; }
        if (_hwnd != IntPtr.Zero) { DestroyWindow(_hwnd); _hwnd = IntPtr.Zero; }
    }

    // ---------- P/Invoke ----------
    private delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASS
    {
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszMenuName;
        [MarshalAs(UnmanagedType.LPWStr)] public string lpszClassName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public uint uID, uFlags, uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState, dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClass(ref WNDCLASS wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint ex, string cls, string name, uint style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr h, uint m, IntPtr w, IntPtr l);

    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(IntPtr h);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? name);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIcon(uint msg, ref NOTIFYICONDATA data);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[]? small, int count);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIcon(IntPtr inst, IntPtr name);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr h);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, int id, string? text);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT pt);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr h);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr h, IntPtr p);

    [DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);

    private static readonly IntPtr IDI_APPLICATION = 32512;
}
