using PadLink.Core;
using System.Runtime.InteropServices;

namespace PadLink.Interop;

/// <summary>
/// Win32 waveOut 音频播放（接收手机麦克风 PCM 并从电脑扬声器播出）。
/// 纯 Win32 API，无第三方依赖。
/// </summary>
public static class AudioPlaybackService
{
    private const int WAVE_MAPPER = -1;
    private const int WAVE_FORMAT_PCM = 1;
    private const uint WHDR_DONE = 0x00000001;
    private const int NUM_BUFFERS = 4;

    private static IntPtr _hWaveOut;
    private static readonly object Lock = new();
    private static bool _running;
    private static WAVEFORMATEX _format;
    private static WAVEHDR[]? _headers;
    private static IntPtr[]? _buffers;
    private static GCHandle[]? _pins;
    private static int _sampleRate = 16000;
    private static int _channels = 1;
    private static int _bitsPerSample = 16;

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEFORMATEX
    {
        public ushort wFormatTag, nChannels;
        public uint nSamplesPerSec, nAvgBytesPerSec;
        public ushort nBlockAlign, wBitsPerSample, cbSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WAVEHDR
    {
        public IntPtr lpData;
        public uint dwBufferLength, dwBytesRecorded, dwUser, dwFlags, dwLoops;
        public IntPtr lpNext, reserved;
    }

    [DllImport("winmm.dll")]
    private static extern int waveOutOpen(out IntPtr h, int devId, ref WAVEFORMATEX fmt,
        IntPtr callback, IntPtr instance, uint flags);

    [DllImport("winmm.dll")]
    private static extern int waveOutClose(IntPtr h);

    [DllImport("winmm.dll")]
    private static extern int waveOutPrepareHeader(IntPtr h, ref WAVEHDR hdr, uint size);

    [DllImport("winmm.dll")]
    private static extern int waveOutUnprepareHeader(IntPtr h, ref WAVEHDR hdr, uint size);

    [DllImport("winmm.dll")]
    private static extern int waveOutWrite(IntPtr h, ref WAVEHDR hdr, uint size);

    [DllImport("winmm.dll")]
    private static extern int waveOutReset(IntPtr h);

    [DllImport("winmm.dll")]
    private static extern int waveOutGetVolume(IntPtr h, out uint volume);

    [DllImport("winmm.dll")]
    private static extern int waveOutSetVolume(IntPtr h, uint volume);

    /// <summary>开始播放（16kHz/16bit/mono PCM）。</summary>
    public static void Start(int sampleRate = 16000)
    {
        lock (Lock)
        {
            if (_running) return;
            _sampleRate = sampleRate;
            _format = new WAVEFORMATEX
            {
                wFormatTag = WAVE_FORMAT_PCM,
                nChannels = (ushort)_channels,
                nSamplesPerSec = (uint)_sampleRate,
                wBitsPerSample = (ushort)_bitsPerSample,
            };
            _format.nBlockAlign = (ushort)(_format.nChannels * _format.wBitsPerSample / 8);
            _format.nAvgBytesPerSec = _format.nSamplesPerSec * _format.nBlockAlign;

            int ret = waveOutOpen(out _hWaveOut, WAVE_MAPPER, ref _format, IntPtr.Zero, IntPtr.Zero, 0);
            if (ret != 0) { Log.Error($"waveOutOpen 失败 ret={ret}"); return; }

            _running = true;
            // 预分配缓冲区
            _headers = new WAVEHDR[NUM_BUFFERS];
            _buffers = new IntPtr[NUM_BUFFERS];
            _pins = new GCHandle[NUM_BUFFERS];
            int bufSize = (int)(_format.nAvgBytesPerSec / 10); // 100ms per buffer
            for (int i = 0; i < NUM_BUFFERS; i++)
            {
                _buffers[i] = Marshal.AllocHGlobal(bufSize);
                _headers[i].lpData = _buffers[i];
                _headers[i].dwBufferLength = (uint)bufSize;
                _headers[i].dwFlags = 0;
                _pins[i] = GCHandle.Alloc(_headers, GCHandleType.Pinned);
            }
            Log.Info($"音频播放已启动 sr={sampleRate}");
        }
    }

    public static void Stop()
    {
        lock (Lock)
        {
            if (!_running) return;
            _running = false;
            waveOutReset(_hWaveOut);
            for (int i = 0; i < NUM_BUFFERS; i++)
            {
                if (_pins?[i].IsAllocated == true) _pins[i].Free();
                if (_buffers?[i] != IntPtr.Zero) Marshal.FreeHGlobal(_buffers[i]);
            }
            waveOutClose(_hWaveOut);
            _hWaveOut = IntPtr.Zero;
            Log.Info("音频播放已停止");
        }
    }

    /// <summary>写入 PCM 数据并播放（非阻塞，缓冲满时丢弃）。</summary>
    public static void WritePcm(byte[] pcm)
    {
        if (!_running || _headers == null) return;
        try
        {
            for (int i = 0; i < NUM_BUFFERS; i++)
            {
                if ((_headers[i].dwFlags & WHDR_DONE) != 0 || _headers[i].dwFlags == 0)
                {
                    int len = (int)Math.Min(pcm.Length, _format.nAvgBytesPerSec / 5);
                    Marshal.Copy(pcm, 0, _buffers![i], len);
                    _headers[i].dwBufferLength = (uint)len;
                    _headers[i].dwFlags &= ~WHDR_DONE;
                    waveOutWrite(_hWaveOut, ref _headers[i], (uint)Marshal.SizeOf<WAVEHDR>());
                    return;
                }
            }
        }
        catch (Exception ex) { Log.Error("音频写入失败: " + ex.Message); }
    }

    public static bool IsRunning => _running;
}

/// <summary>多显示器枚举与选择。</summary>
public static class MonitorService
{
    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc cb, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX info);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

    public static RECT PtrToRect(IntPtr ptr)
    {
        var r = new RECT();
        Marshal.PtrToStructure(ptr, r);
        return r;
    }

    public delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr lprcMonitor, IntPtr data);

    public static readonly List<RECT> _monRects = new();
    public static readonly MonitorEnumProc _monCallback = OnMonitorEnum;

    public static bool OnMonitorEnum(IntPtr hMon, IntPtr hdc, IntPtr lprc, IntPtr data)
    {
        var rect = PtrToRect(lprc);
        lock (_monLock) { _monRects.Add(rect); }
        return true;
    }

    public static readonly object _monLock = new();

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor, rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDeviceName;
    }

    public static List<object> ListMonitors()
    {
        var list = new List<object>();
        _monRects.Clear();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, _monCallback, IntPtr.Zero);
        for (int idx = 0; idx < _monRects.Count; idx++)
        {
            var r = _monRects[idx];
            bool primary = idx == 0; // 第一个视为主显示器
            list.Add(new
            {
                index = idx,
                device = "DISPLAY" + (idx + 1),
                primary,
                left = r.Left, top = r.Top,
                width = r.Right - r.Left,
                height = r.Bottom - r.Top,
            });
        }
        return list;
    }

    /// <summary>截取指定显示器（或全部虚拟屏幕）。</summary>
    public static byte[]? CaptureMonitor(int index = -1, int targetWidth = 1280, int quality = 70)
    {
        try
        {
            _monRects.Clear();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, _monCallback, IntPtr.Zero);
            var monitors = new List<RECT>(_monRects);

            RECT area;
            if (index >= 0 && index < monitors.Count)
                area = monitors[index];
            else
            {
                // 全部虚拟屏幕
                int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
                foreach (var r in monitors)
                {
                    minX = Math.Min(minX, r.Left); minY = Math.Min(minY, r.Top);
                    maxX = Math.Max(maxX, r.Right); maxY = Math.Max(maxY, r.Bottom);
                }
                area = new RECT { Left = minX, Top = minY, Right = maxX, Bottom = maxY };
            }

            int w = area.Right - area.Left;
            int h = area.Bottom - area.Top;
            if (w <= 0 || h <= 0) return null;

            using var bmp = new System.Drawing.Bitmap(w, h);
            using (var g = System.Drawing.Graphics.FromImage(bmp))
            {
                var hdc = g.GetHdc();
                var screenDc = GetDC(IntPtr.Zero);
                BitBlt(hdc, 0, 0, w, h, screenDc, area.Left, area.Top, 0x00CC0020);
                ReleaseDC(IntPtr.Zero, screenDc);
                g.ReleaseHdc(hdc);
            }

            int tw = Math.Min(targetWidth, w);
            int th = (int)Math.Round(h * (tw / (double)w));
            using var resized = new System.Drawing.Bitmap(tw, th);
            using (var g = System.Drawing.Graphics.FromImage(resized))
                g.DrawImage(bmp, 0, 0, tw, th);

            using var ms = new MemoryStream();
            resized.Save(ms, System.Drawing.Imaging.ImageFormat.Jpeg);
            return ms.ToArray();
        }
        catch (Exception ex) { Log.Error($"截取显示器 {index} 失败: {ex.Message}"); return null; }
    }

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hdcDest, int x, int y, int w, int h,
        IntPtr hdcSrc, int sx, int sy, uint rop);

    public static (double nx, double ny) MonitorNormalized(int monitorIndex, double nx, double ny)
    {
        var monitors = new List<RECT>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, _monCallback, IntPtr.Zero);
        if (monitorIndex < 0 || monitorIndex >= monitors.Count)
            return (nx, ny);
        var r = monitors[monitorIndex];
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        if (w <= 0 || h <= 0) return (nx, ny);
        int px = r.Left + (int)(nx * (w - 1));
        int py = r.Top + (int)(ny * (h - 1));
        // 使用 SetCursorPos
        SetCursorPos(px, py);
        return (nx, ny);
    }

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);
}
