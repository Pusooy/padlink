using PadLink.Core;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Runtime.InteropServices;

namespace PadLink.Interop;

/// <summary>Win32 SendInput 封装：鼠标移动/点击/滚轮 + 键盘（含 Unicode 中文输入、媒体键、组合键）。</summary>
internal static class InputService
{
    // ---- Win32 ----
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKeyW(uint uCode, uint uMapType);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
        public static int Size => Marshal.SizeOf<INPUT>();
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk, wScan;
        public uint dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    private const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_MOVE = 0x0001, LEFTDOWN = 0x0002, LEFTUP = 0x0004,
        RIGHTDOWN = 0x0008, RIGHTUP = 0x0010,
        MIDDLEDOWN = 0x0020, MIDDLEUP = 0x0040, WHEEL = 0x0800,
        XDOWN = 0x0080, XUP = 0x0100;
    private const uint XBUTTON1 = 0x0001, XBUTTON2 = 0x0002;
    private const uint KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_UNICODE = 0x0004, EXTENDEDKEY = 0x0001;
    private const uint WHEEL_DELTA = 120;

    // ---- 虚拟键码 ----
    private static readonly Dictionary<string, ushort> VkMap = new()
    {
        ["backspace"] = 0x08, ["tab"] = 0x09, ["return"] = 0x0D, ["enter"] = 0x0D,
        ["esc"] = 0x1B, ["space"] = 0x20, ["caps_lock"] = 0x14, ["pageup"] = 0x21,
        ["pagedown"] = 0x22, ["end"] = 0x23, ["home"] = 0x24,
        ["left"] = 0x25, ["up"] = 0x26, ["right"] = 0x27, ["down"] = 0x28,
        ["insert"] = 0x2D, ["delete"] = 0x2E,
        ["win"] = 0x5B, ["shift"] = 0x10, ["ctrl"] = 0x11, ["alt"] = 0x12, ["menu"] = 0x12,
        ["volume_mute"] = 0xAD, ["volume_down"] = 0xAE, ["volume_up"] = 0xAF,
        ["media_next"] = 0xB0, ["media_previous"] = 0xB1, ["media_stop"] = 0xB2,
        ["media_play_pause"] = 0xB3,
        ["printscreen"] = 0x2C, ["scroll_lock"] = 0x91, ["num_lock"] = 0x90,
    };
    static InputService()
    {
        for (ushort i = 0; i < 12; i++) VkMap[$"f{i + 1}"] = (ushort)(0x70 + i);
        for (char c = 'A'; c <= 'Z'; c++) VkMap[char.ToLower(c).ToString()] = c;
        for (char c = '0'; c <= '9'; c++) VkMap[c.ToString()] = c;
    }

    // ==== 移动注入泵：流畅度的关键 ====
    // 客户端的移动事件到达服务端时常是「突发成批」的（网络抖动 / 输入栈批量投递），
    // 逐事件 SendInput 会把一批位移在同一次刷屏里打完、随后停一拍 → 光标一顿一顿。
    // 泵把突发位移摊到 ~3ms 的均匀节拍上（≈250Hz，与真实鼠标报告率同级）；
    // 稀疏事件（距上次注入超过一个节拍）则立即直出，不附加任何延迟。
    // 亚像素余量照旧累积，慢速拖动不丢像素。
    private static readonly object _pumpLock = new();
    private static double _pendX, _pendY;   // 待注入位移（像素，浮点累积）
    private static double _remX, _remY;     // 亚像素余量
    private static long _lastInject;        // 上次注入的 Stopwatch 时间戳
    private static long _lastArrival;       // 上次事件到达时间（算实时手速用）
    private static double _inRate;          // 实时手速 EWMA（px/ms，按主轴幅度）
    private static Thread? _pump;
    private static volatile bool _pumpRun;
    private const int PumpPeriodMs = 1;

    [DllImport("winmm.dll")]
    private static extern uint timeBeginPeriod(uint ms);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateWaitableTimerExW(IntPtr attrs, string? name, uint flags, uint access);

    [DllImport("kernel32.dll")]
    private static extern bool SetWaitableTimer(IntPtr h, ref long due, uint period, IntPtr comp, IntPtr arg, bool resume);

    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr h, uint ms);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr h);

    private const uint CREATE_WAITABLE_TIMER_HIGH_RESOLUTION = 0x00000002;
    private const uint TIMER_ALL_ACCESS = 0x1F0003;

    public static void MouseMove(double dx, double dy)
    {
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) return;
        lock (_pumpLock)
        {
            var now = Stopwatch.GetTimestamp();
            // 手速估计：dt 下限 4ms——网络把多个事件攒成一包时 dt≈0，
            // 若不设下限会把突发误判成「超高速甩动」而一次性放行（正是卡顿的来源）
            var dtMs = _lastArrival == 0
                ? 4.0
                : Math.Max((now - _lastArrival) * 1000.0 / Stopwatch.Frequency, 4.0);
            _lastArrival = now;
            var mag = Math.Max(Math.Abs(dx), Math.Abs(dy));
            var inst = mag / dtMs;
            _inRate = _inRate <= 0 ? inst : _inRate * 0.65 + inst * 0.35;
            _pendX += dx; _pendY += dy;
            AuditArrivedPx += Math.Max(Math.Abs(dx), Math.Abs(dy));
            AuditArrivedCount++;
            DbgPendX = _pendX;
        }
        ArriveStamps.Enqueue(Stopwatch.GetTimestamp());
        while (ArriveStamps.Count > 256) ArriveStamps.TryDequeue(out _);
        StartPump();
        // 稀疏事件零延迟路径：距离上次注入已超过一个节拍，直接出
        if ((Stopwatch.GetTimestamp() - Volatile.Read(ref _lastInject)) * 1000.0 / Stopwatch.Frequency >= PumpPeriodMs)
            PumpTick();
    }

    private static void StartPump()
    {
        if (_pump != null) return;
        lock (_pumpLock)
        {
            if (_pump != null) return;
            try { timeBeginPeriod(1); } catch { } // Sleep(3) 才真能睡 3ms（默认 15.6ms 粒度）
            _pumpRun = true;
            _pump = new Thread(PumpLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.Highest,
                Name = "PadLinkMousePump",
            };
            _pump.Start();
        }
    }

    private static void PumpLoop()
    {
        // 高分辨率等待定时器驱动节拍。不能用 Thread.Sleep：
        // Win11 对后台/托盘进程会忽略 timeBeginPeriod，Sleep(1) 实际睡 15.6ms，
        // 泵节拍劣化 10 倍（实测 1250ms 只 tick 81 次）——这就是残余卡顿的元凶。
        var h = CreateWaitableTimerExW(IntPtr.Zero, null, CREATE_WAITABLE_TIMER_HIGH_RESOLUTION, TIMER_ALL_ACCESS);
        if (h == IntPtr.Zero)
            h = CreateWaitableTimerExW(IntPtr.Zero, null, 0, TIMER_ALL_ACCESS);
        if (h != IntPtr.Zero)
        {
            long due = -1; // 负值 = 相对时间，100ns 单位 → 立即首次到期
            if (!SetWaitableTimer(h, ref due, (uint)PumpPeriodMs, IntPtr.Zero, IntPtr.Zero, false))
            {
                CloseHandle(h);
                h = IntPtr.Zero;
            }
        }
        try
        {
            while (_pumpRun)
            {
                if (h != IntPtr.Zero)
                {
                    WaitForSingleObject(h, 100); // 周期 1ms；100ms 仅作兜底
                }
                else
                {
                    Thread.Sleep(PumpPeriodMs); // 兜底：老系统没有高分辨率定时器
                }
                PumpTick();
            }
        }
        finally
        {
            if (h != IntPtr.Zero) CloseHandle(h);
        }
    }

    /// <summary>移动注入时间戳（Stopwatch ticks，诊断用，环形保留 256 条）。</summary>
    internal static readonly System.Collections.Concurrent.ConcurrentQueue<long> InjectStamps = new();

    /// <summary>移动事件到达时间戳（诊断用，与 InjectStamps 对比定位丢拍侧）。</summary>
    internal static readonly System.Collections.Concurrent.ConcurrentQueue<long> ArriveStamps = new();

    /// <summary>泵循环心跳（每拍更新，无论是否有待注入）——用于确认线程是否被卡。</summary>
    internal static long PumpHeartbeat;

    /// <summary>像素守恒审计：进入泵的 px 总量 / 实际注入的 px 总量（主轴）。</summary>
    internal static double AuditArrivedPx;
    internal static double AuditInjectedPx;
    internal static long AuditArrivedCount;
    internal static long AuditInjectedCount;

    /// <summary>泵内部状态快照（诊断用，每次 Tick 前后写）。</summary>
    internal static double DbgPendX, DbgRemX, DbgInRate, DbgBudget, DbgLastTickMs;

    /// <summary>与 InjectStamps 对齐的注入量级（|dx| 或 |dy| 主轴 px）。</summary>
    internal static readonly System.Collections.Concurrent.ConcurrentQueue<int> InjectMag = new();

    private static void PumpTick()
    {
        int ix, iy;
        lock (_pumpLock)
        {
            var now = Stopwatch.GetTimestamp();
            var dtMs = _lastInject == 0
                ? PumpPeriodMs
                : (now - _lastInject) * 1000.0 / Stopwatch.Frequency;
            _lastInject = now;
            DbgLastTickMs = dtMs;
            // 提前返回必须同时看 pend 和 rem：突发缩放后余量全存在 rem 里（pend=0），
            // 若只查 pend，余量会被困住永不注入（实测 20px 突发只出 2px、18px 被困）。
            if (_pendX == 0 && _pendY == 0 && _remX == 0 && _remY == 0)
            {
                Volatile.Write(ref PumpHeartbeat, now);
                return;
            }

            // 每拍预算：按「实时手速 × 节拍时长」放行。
            // 慢速拖动（0.04px/ms）→ 每拍 ~1px，突发被摊成均匀小步；
            // 快速甩动（6px/ms）→ 每拍 ~25px，光标跟得上手，不拖尾。
            // 下限必须 > 1.0：缩放后 totalX 若落进 [0,1) 区间会被截断成 0，
            // 该拍不注入、余量挂在 rem 上 → 偶发「隔一个事件才注入」的空洞。
            var budget = Math.Clamp(_inRate * dtMs * 1.8 + 1.5, 1.5, 40.0);
            // 记账不变式（像素守恒）：
            //   total = pend + rem → 消费 consumed = min(total, budget)
            //   注入 ix = truncate(consumed)，小数部分 rem = consumed - ix，
            //   未消费的「原始尺度」余量 (total - consumed) 留回 pend。
            // 三个历史 bug 都出在这段记账：负 pend（像素债）、判空漏掉 rem（余量被困）、
            // 缩放后直接清 pend（未消费像素被销毁，实测 20px 突发只出 2px）。
            var totalX = _pendX + _remX;
            var totalY = _pendY + _remY;
            var mag = Math.Max(Math.Abs(totalX), Math.Abs(totalY));
            var consumedX = totalX;
            var consumedY = totalY;
            if (mag > budget)
            {
                var k = budget / mag;
                consumedX = totalX * k; consumedY = totalY * k;
            }
            ix = (int)Math.Truncate(consumedX);
            iy = (int)Math.Truncate(consumedY);
            _remX = consumedX - ix; _remY = consumedY - iy;
            _pendX = totalX - consumedX; _pendY = totalY - consumedY;
            DbgPendX = _pendX; DbgRemX = _remX; DbgInRate = _inRate; DbgBudget = budget;
            Volatile.Write(ref PumpHeartbeat, now);
        }
        if (ix != 0 || iy != 0)
        {
            Mouse(ix, iy, MOUSEEVENTF_MOVE);
            AuditInjectedPx += Math.Max(Math.Abs(ix), Math.Abs(iy));
            AuditInjectedCount++;
            InjectStamps.Enqueue(Stopwatch.GetTimestamp());
            InjectMag.Enqueue(Math.Max(Math.Abs(ix), Math.Abs(iy)));
            while (InjectStamps.Count > 256) { InjectStamps.TryDequeue(out _); InjectMag.TryDequeue(out _); }
        }
    }

    public static void MouseClick(string btn, int count = 1)
    {
        for (int i = 0; i < count; i++) { MouseDown(btn); MouseUp(btn); }
    }

    /// <summary>支持五键：left / right / middle / back(X1) / forward(X2)。</summary>
    public static void MouseDown(string btn) => XBtn(btn, down: true);
    public static void MouseUp(string btn) => XBtn(btn, down: false);

    private static void XBtn(string btn, bool down)
    {
        switch (btn)
        {
            case "right": Mouse(0, 0, down ? RIGHTDOWN : RIGHTUP); break;
            case "middle": Mouse(0, 0, down ? MIDDLEDOWN : MIDDLEUP); break;
            case "back": Mouse(0, 0, down ? XDOWN : XUP, XBUTTON1); break;
            case "forward": Mouse(0, 0, down ? XDOWN : XUP, XBUTTON2); break;
            default: Mouse(0, 0, down ? LEFTDOWN : LEFTUP); break;
        }
    }
    // 滚轮同样累积余量：客户端按帧合并后单帧位移可能不足一格，
    // 直接截断会丢滚动量，而 old 的「不足一格补一格」会在每帧补发，产生过冲。
    private static readonly object _scrollLock = new();
    private static double _scrollRem;

    public static void Scroll(double dx, double dy)
    {
        if (!double.IsFinite(dy)) return;
        int wheel;
        lock (_scrollLock)
        {
            // dy>0 = 手指下滑 = 内容向下 = 滚轮向后（负值）
            _scrollRem += -dy * WHEEL_DELTA / 100.0;
            wheel = (int)_scrollRem;
            if (wheel == 0) return;       // 不足一格：留到下次累积
            _scrollRem -= wheel;
        }
        SendMouse(new MOUSEINPUT { mouseData = (uint)wheel, dwFlags = WHEEL });
    }

    private static void Mouse(int dx, int dy, uint flags, uint mouseData = 0) =>
        SendMouse(new MOUSEINPUT { dx = dx, dy = dy, dwFlags = flags, mouseData = mouseData });

    private static void SendMouse(MOUSEINPUT mi) =>
        _ = SendInput(1, new[] { new INPUT { type = INPUT_MOUSE, U = new InputUnion { mi = mi } } }, INPUT.Size);

    /// <summary>发送按键：单键名 / 组合键 "ctrl+shift+s" / 字符。</summary>
    public static void KeyTap(string name, int count = 1)
    {
        for (int i = 0; i < count; i++) KeyTapOnce(name);
    }

    private static void KeyTapOnce(string name)
    {
        if (name.Contains('+'))
        {
            var parts = name.Split('+');
            var main = parts[^1];
            var mods = parts[..^1].Select(p => VkMap.TryGetValue(p, out var vk) ? vk : VkMap[p.ToLowerInvariant()]).ToArray();
            foreach (var vk in mods) KeyDownVk(vk);
            try { TapMain(main); }
            finally { foreach (var vk in mods.Reverse()) KeyUpVk(vk); }
            return;
        }
        TapMain(name);
    }

    private static void TapMain(string main)
    {
        // 单字符：优先走 Unicode（支持中文及全键盘符号），避免布局问题
        if (main.Length == 1 && !VkMap.ContainsKey(main.ToLowerInvariant()) && main != " ")
        {
            TypeText(main);
            return;
        }
        if (!VkMap.TryGetValue(main.ToLowerInvariant(), out var vk))
        {
            if (main.Length == 1) { TypeText(main); return; }
            throw new ArgumentException($"unknown key: {main}");
        }
        KeyDownVk(vk); KeyUpVk(vk);
    }

    /// <summary>Unicode 逐字符输入（支持中文）。</summary>
    public static void TypeText(string text)
    {
        foreach (var ch in text)
        {
            var down = new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE } } };
            var up = new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP } } };
            _ = SendInput(1, new[] { down }, INPUT.Size);
            _ = SendInput(1, new[] { up }, INPUT.Size);
        }
    }

    private static void KeyDownVk(ushort vk) => SendVk(vk, 0);
    private static void KeyUpVk(ushort vk) => SendVk(vk, KEYEVENTF_KEYUP);

    /// <summary>逐键发送（蓝牙键盘语义）：按下/抬起各发一次，由 PC 自己的输入法/应用处理。</summary>
    internal static void KeyVk(byte vk, bool down) => SendVk(vk, down ? 0 : KEYEVENTF_KEYUP);

    /// <summary>鼠标键逐次发送：0 左 / 1 右 / 2 中 / 3 后退 / 4 前进。</summary>
    internal static void ButtonVk(byte btn, bool down)
    {
        switch (btn)
        {
            case 0: Mouse(0, 0, down ? LEFTDOWN : LEFTUP); break;
            case 1: Mouse(0, 0, down ? RIGHTDOWN : RIGHTUP); break;
            case 2: Mouse(0, 0, down ? MIDDLEDOWN : MIDDLEUP); break;
            case 3: Mouse(0, 0, down ? XDOWN : XUP, XBUTTON1); break;
            case 4: Mouse(0, 0, down ? XDOWN : XUP, XBUTTON2); break;
        }
    }

    private static void SendVk(ushort vk, uint flags)
    {
        var extra = IsExtendedVk(vk) ? EXTENDEDKEY : 0;
        // 同时带上扫描码：只认 scancode 的程序（游戏、部分输入法）才能收到正确按键
        var scan = (ushort)MapVirtualKeyW(vk, 0);
        _ = SendInput(1, new[] { new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags | extra } } } }, INPUT.Size);
    }

    private static bool IsExtendedVk(ushort vk) => vk is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28
        or 0x2C or 0x2D or 0x2E or 0x5B or 0x5C or 0x5D or 0x90 or 0xA3 or 0xA5;
}

/// <summary>单个手机连接的按键/按钮状态。连接断开或收到 R 指令时统一抬起，防止「卡键」。</summary>
internal sealed class InputSession
{
    private readonly bool[] _keys = new bool[256];
    private readonly bool[] _buttons = new bool[5];

    public void Key(byte vk, bool down)
    {
        if (vk == 0) return;
        _keys[vk] = down;
        InputService.KeyVk(vk, down);
    }

    public void Button(byte btn, bool down)
    {
        if (btn >= _buttons.Length) return;
        _buttons[btn] = down;
        InputService.ButtonVk(btn, down);
    }

    public void ReleaseAll()
    {
        for (int i = 0; i < _keys.Length; i++)
            if (_keys[i]) { _keys[i] = false; InputService.KeyVk((byte)i, false); }
        for (int i = 0; i < _buttons.Length; i++)
            if (_buttons[i]) { _buttons[i] = false; InputService.ButtonVk((byte)i, false); }
    }
}
