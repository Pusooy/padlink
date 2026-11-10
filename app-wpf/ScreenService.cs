using System.IO;
using System.Linq;
using System.Threading;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Encoder = System.Drawing.Imaging.Encoder;
using System.Windows.Forms;

namespace PadLink;

/// <summary>屏幕捕获（供手机看屏幕 + 点击定位）。</summary>
internal static class ScreenService
{
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    public static (int w, int h) VirtualScreen()
    {
        // SM_XVIRTUALSCREEN=76, SM_YVIRTUALSCREEN=77, SM_CXVIRTUALSCREEN=78, SM_CYVIRTUALSCREEN=79
        int x = GetSystemMetrics(76), y = GetSystemMetrics(77);
        int w = GetSystemMetrics(78), h = GetSystemMetrics(79);
        _ = x; _ = y;
        return (w > 0 ? w : 1920, h > 0 ? h : 1080);
    }

    /// <summary>截取主屏，缩放到指定宽度，JPEG 编码（质量适中，适合手机流量）。</summary>
    public static byte[]? CaptureJpeg(int targetWidth = 1280, int quality = 72)
    {
        try
        {
            var (vw, vh) = VirtualScreen();
            int x = GetSystemMetrics(76), y = GetSystemMetrics(77);
            using var full = new Bitmap(vw, vh, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(full))
            {
                g.CopyFromScreen(x, y, 0, 0, new Size(vw, vh), CopyPixelOperation.SourceCopy);
            }

            int tw = Math.Min(targetWidth, vw);
            int th = (int)Math.Round(vh * (tw / (double)vw));
            using var resized = new Bitmap(tw, th, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(resized))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bilinear;
                g.DrawImage(full, 0, 0, tw, th);
            }

            using var ms = new MemoryStream();
            var codec = ImageCodecInfo.GetImageEncoders()
                .FirstOrDefault(c => c.FormatID == ImageFormat.Jpeg.Guid);
            var ep = new EncoderParameters(1);
            ep.Param[0] = new EncoderParameter(Encoder.Quality, (long)quality);
            if (codec != null) resized.Save(ms, codec, ep);
            else resized.Save(ms, ImageFormat.Jpeg);
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            DebugLog.Write($"截屏失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>把归一化坐标 (0..1) 映射到屏幕并移动/点击。</summary>
    public static void MoveToNormalized(double nx, double ny)
    {
        var (vw, vh) = VirtualScreen();
        int x = GetSystemMetrics(76), y = GetSystemMetrics(77);
        int px = x + (int)Math.Round(Math.Clamp(nx, 0, 1) * (vw - 1));
        int py = y + (int)Math.Round(Math.Clamp(ny, 0, 1) * (vh - 1));
        SetCursorPos(px, py);
    }

    public static (double nx, double ny) CursorNormalized()
    {
        var (vw, vh) = VirtualScreen();
        int x = GetSystemMetrics(76), y = GetSystemMetrics(77);
        if (!GetCursorPos(out var p)) return (0, 0);
        return ((p.X - x) / (double)Math.Max(1, vw - 1),
                (p.Y - y) / (double)Math.Max(1, vh - 1));
    }
}

/// <summary>剪贴板双向同步（WinForms Clipboard 必须在 STA 线程访问）。</summary>
internal static class ClipboardService
{
    private static T? OnStaThread<T>(Func<T> action, T? fallback = default)
    {
        T? result = fallback;
        Exception? error = null;
        var t = new Thread(() =>
        {
            try { result = action(); }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.IsBackground = true;
        t.Start();
        if (!t.Join(2500)) return fallback;
        if (error != null) DebugLog.Write($"剪贴板操作失败: {error.Message}");
        return result;
    }

    public static string GetText()
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            var text = OnStaThread(() =>
            {
                var data = Clipboard.GetDataObject();
                if (data == null) return "";
                if (data.GetDataPresent(DataFormats.UnicodeText))
                    return data.GetData(DataFormats.UnicodeText) as string ?? "";
                if (data.GetDataPresent(DataFormats.Text))
                    return data.GetData(DataFormats.Text) as string ?? "";
                return "";
            }, "");
            if (!string.IsNullOrEmpty(text)) return text;
            Thread.Sleep(80);
        }
        return "";
    }

    public static bool SetText(string text)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            var ok = OnStaThread(() =>
            {
                try
                {
                    Clipboard.SetDataObject(text ?? "", true, 5, 120);
                    return true;
                }
                catch
                {
                    try { Clipboard.SetText(text ?? ""); return true; } catch { return false; }
                }
            }, false);
            if (ok) return true;
            Thread.Sleep(100);
        }
        DebugLog.Write("写剪贴板重试 3 次仍失败");
        return false;
    }
}

/// <summary>电源与系统操作。</summary>
internal static class PowerService
{
    public enum Action { Lock, Sleep, Hibernate, SignOut, Restart, Shutdown }

    public static bool Perform(string action)
    {
        try
        {
            switch (action)
            {
                case "lock":
                    // 锁定工作站
                    if (!NativeMethods.LockWorkStation())
                        Process.Start("rundll32.exe", "user32.dll,LockWorkStation");
                    return true;
                case "sleep":
                    Run("powershell", "-NoProfile -Command \"Add-Type -AssemblyName System.Windows.Forms; " +
                        "[System.Windows.Forms.Application]::SetSuspendState('Suspend',$false,$false)\"");
                    return true;
                case "hibernate":
                    Run("shutdown", "/h");
                    return true;
                case "signout":
                    Run("shutdown", "/l");
                    return true;
                case "restart":
                    Run("shutdown", "/r /t 3");
                    return true;
                case "shutdown":
                    Run("shutdown", "/s /t 3");
                    return true;
                case "cancel":
                    Run("shutdown", "/a");
                    return true;
                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Write($"电源操作 {action} 失败: {ex.Message}");
            return false;
        }
    }

    private static void Run(string file, string args) =>
        Process.Start(new ProcessStartInfo(file, args) { CreateNoWindow = true, UseShellExecute = false });

    private static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern bool LockWorkStation();
    }
}

/// <summary>系统信息（CPU/RAM/电量/运行时长），让手机端了解主机状态。</summary>
internal static class SystemInfoService
{
    public static object Snapshot()
    {
        string cpu = "—", ram = "—", uptime = "—", battery = "—";
        try
        {
            double cpuLoad = PerfCpu();
            cpu = cpuLoad >= 0 ? $"{cpuLoad:0}%" : "—";
            var mem = GC.GetGCMemoryInfo();
            long total = mem.TotalAvailableMemoryBytes;
            if (total > 0)
            {
                var status = new Microsoft.VisualBasic.Devices.ComputerInfo();
                ulong availMb = status.AvailablePhysicalMemory / 1024 / 1024;
                ulong totalMb = status.TotalPhysicalMemory / 1024 / 1024;
                ram = $"{(totalMb - availMb) * 100 / Math.Max(1, totalMb)}%";
            }
            var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
            uptime = $"{(int)up.TotalHours}h{up.Minutes:00}m";
            var ps = SystemInformation.PowerStatus;
            if (ps.BatteryChargeStatus != BatteryChargeStatus.NoSystemBattery)
                battery = $"{ps.BatteryLifePercent * 100:0}%";
        }
        catch { }
        return new { cpu, ram, uptime, battery, host = Environment.MachineName, os = Environment.OSVersion.VersionString };
    }

    private static double PerfCpu()
    {
        try
        {
            using var pc = new System.Diagnostics.PerformanceCounter("Processor", "% Processor Time", "_Total");
            pc.NextValue();
            Thread.Sleep(120);
            return pc.NextValue();
        }
        catch { return -1; }
    }
}
