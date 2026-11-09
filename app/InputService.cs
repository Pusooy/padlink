using System.Runtime.InteropServices;

namespace PadLink;

/// <summary>Win32 SendInput 封装：鼠标移动/点击/滚轮 + 键盘（含 Unicode 中文输入、媒体键、组合键）。</summary>
internal static class InputService
{
    // ---- Win32 ----
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

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
        MIDDLEDOWN = 0x0020, MIDDLEUP = 0x0040, WHEEL = 0x0800;
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

    public static void MouseMove(double dx, double dy) => Mouse((int)dx, (int)dy, MOUSEEVENTF_MOVE);
    public static void MouseClick(string btn, int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            switch (btn)
            {
                case "right": Mouse(0, 0, RIGHTDOWN); Mouse(0, 0, RIGHTUP); break;
                case "middle": Mouse(0, 0, MIDDLEDOWN); Mouse(0, 0, MIDDLEUP); break;
                default: Mouse(0, 0, LEFTDOWN); Mouse(0, 0, LEFTUP); break;
            }
        }
    }
    public static void MouseDown(string btn) => Mouse(0, 0, btn == "right" ? RIGHTDOWN : btn == "middle" ? MIDDLEDOWN : LEFTDOWN);
    public static void MouseUp(string btn) => Mouse(0, 0, btn == "right" ? RIGHTUP : btn == "middle" ? MIDDLEUP : LEFTUP);
    public static void Scroll(double dx, double dy)
    {
        // dy>0 = 手指下滑 = 内容向下 = 滚轮向后（负值）
        int wheel = -(int)(dy * WHEEL_DELTA / 100.0);
        if (wheel == 0) wheel = dy > 0 ? -(int)WHEEL_DELTA : (int)WHEEL_DELTA;
        SendMouse(new MOUSEINPUT { mouseData = (uint)wheel, dwFlags = WHEEL });
    }

    private static void Mouse(int dx, int dy, uint flags) =>
        SendMouse(new MOUSEINPUT { dx = dx, dy = dy, dwFlags = flags });

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
    private static void SendVk(ushort vk, uint flags)
    {
        var extra = (vk is 0x25 or 0x26 or 0x27 or 0x28 or 0x21 or 0x22 or 0x23 or 0x24) ? EXTENDEDKEY : 0;
        _ = SendInput(1, new[] { new INPUT { type = INPUT_KEYBOARD, U = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = flags | extra } } } }, INPUT.Size);
    }
}
