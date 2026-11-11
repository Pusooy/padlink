# -*- coding: utf-8 -*-
"""
二进制输入协议 v2 端到端测试：
- 二进制 K 帧（VK 逐键 down/up）→ WH_KEYBOARD_LL 钩子应捕获注入的 VK
- 修饰键 hold 语义（shift+a → 大写 A 路径）
- B 帧（鼠标键）、M 帧（移动，get_cursor 验证）、R 帧（释放全部）
- 注入延迟测量：send → hook 收到
"""
import asyncio
import ctypes
import ctypes.wintypes
import json
import struct
import sys
import time
import websockets

try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass

user32 = ctypes.windll.user32
WH_KEYBOARD_LL = 13
WM_KEYDOWN, WM_SYSKEYDOWN, WM_KEYUP = 0x100, 0x104, 0x101
LLKHF_INJECTED = 0x10
VK_PACKET = 0xE7


class KBDLLHOOKSTRUCT(ctypes.Structure):
    _fields_ = [("vkCode", ctypes.c_uint), ("scanCode", ctypes.c_uint),
                ("flags", ctypes.c_uint), ("time", ctypes.c_uint),
                ("dwExtraInfo", ctypes.c_void_p)]


received = []  # (t, "down"/"up", vk)
HOOK = ctypes.WINFUNCTYPE(ctypes.c_long, ctypes.c_int, ctypes.c_uint, ctypes.POINTER(KBDLLHOOKSTRUCT))


def hook_proc(n, wparam, lparam):
    kb = lparam.contents
    if kb.flags & LLKHF_INJECTED and kb.vkCode != VK_PACKET:
        if wparam in (WM_KEYDOWN, WM_SYSKEYDOWN):
            received.append((time.monotonic(), "down", kb.vkCode))
        elif wparam == WM_KEYUP:
            received.append((time.monotonic(), "up", kb.vkCode))
    return user32.CallNextHookEx(None, n, wparam, lparam)


def install_hook():
    proc = HOOK(hook_proc)
    ok = user32.SetWindowsHookExW(WH_KEYBOARD_LL, proc, None, 0)
    assert ok, "SetWindowsHookEx failed"
    msg = ctypes.wintypes.MSG()
    while user32.GetMessageW(ctypes.byref(msg), None, 0, 0):
        pass


def frame(op, *args):
    if op == 0x4D:
        return struct.pack("<Bhh", op, *args)
    if op == 0x53:
        return struct.pack("<Bh", op, *args)
    if op == 0x52:
        return struct.pack("<B", op)
    return struct.pack("<BBB", op, *args)


async def recv_until(ws, want_t, timeout=5):
    while True:
        r = json.loads(await asyncio.wait_for(ws.recv(), timeout))
        if r.get("t") == want_t:
            return r


async def main():
    import threading
    t = threading.Thread(target=install_hook, daemon=True)
    t.start()
    time.sleep(0.5)

    results = []
    async with websockets.connect("ws://127.0.0.1:8765/ws") as ws:
        await ws.send(json.dumps({"t": "ping", "ts": 0}))
        r = json.loads(await asyncio.wait_for(ws.recv(), 3))
        results.append(("pong 连通", r["t"] == "pong"))

        # 1. 二进制 K 帧：h i 逐键
        received.clear()
        for vk in (0x48, 0x49):
            await ws.send(frame(0x4B, vk, 1))
            await ws.send(frame(0x4B, vk, 0))
        await asyncio.sleep(0.8)
        downs = [v for (_, k, v) in received if k == "down"]
        ups = [v for (_, k, v) in received if k == "up"]
        results.append((f"K帧 h/i downs={downs} ups={ups}",
                        downs == [0x48, 0x49] and ups == [0x48, 0x49]))

        # 2. shift hold + a → 修饰键 hold 语义（VK 顺序）
        received.clear()
        await ws.send(frame(0x4B, 0xA0, 1))   # shift down
        await ws.send(frame(0x4B, 0x41, 1))   # a down
        await ws.send(frame(0x4B, 0x41, 0))   # a up
        await ws.send(frame(0x4B, 0xA0, 0))   # shift up
        await asyncio.sleep(0.8)
        seq = [(k, v) for (_, k, v) in received]
        ok_seq = seq and seq[0][1] == 0xA0 and seq[0][0] == "down" and seq[-1][1] == 0xA0
        results.append((f"shift+a hold 序列={seq[:2]}...{seq[-2:]}", ok_seq and 0x41 in [v for _, v in seq]))

        # 3. R 帧释放全部：shift down → R（应收到 shift up）
        received.clear()
        await ws.send(frame(0x4B, 0xA0, 1))
        await asyncio.sleep(0.15)
        await ws.send(frame(0x52))
        await asyncio.sleep(0.8)
        ups = [v for (_, k, v) in received if k == "up"]
        results.append((f"R帧释放 shift ups={ups}", 0xA0 in ups))

        # 4. M 帧：移动光标（先归位屏幕中心，避免撞边缘；get_cursor 前后对比）
        await ws.send(json.dumps({"t": "screen_move", "x": 0.5, "y": 0.5}))
        await asyncio.sleep(0.4)
        await ws.send(json.dumps({"t": "get_cursor"}))
        c0 = await recv_until(ws, "cursor")
        for _ in range(30):
            await ws.send(frame(0x4D, 16, 0))  # 每次 +1px（16 定点）
        await asyncio.sleep(0.4)
        await ws.send(json.dumps({"t": "get_cursor"}))
        c1 = await recv_until(ws, "cursor")
        dx = c1["x"] - c0["x"]
        results.append((f"M帧移动 dx_norm={dx:.4f}（≈{dx*1920:.0f}px）", abs(dx) > 0.005))

        # 5. B 帧：left down/up（钩子看不到鼠标，验证不炸 + get_cursor 仍通）
        await ws.send(frame(0x42, 0, 1))
        await ws.send(frame(0x42, 0, 0))
        await ws.send(json.dumps({"t": "ping", "ts": 0}))
        r = json.loads(await asyncio.wait_for(ws.recv(), 3))
        results.append(("B帧发送后连接存活", r["t"] == "pong"))

        # 6. 注入延迟：K down 发出 → 钩子收到
        lats = []
        for _ in range(20):
            received.clear()
            t0 = time.monotonic()
            await ws.send(frame(0x4B, 0x44, 1))  # d down
            await ws.send(frame(0x4B, 0x44, 0))
            while not any(v == 0x44 for (_, k, v) in received if k == "down"):
                await asyncio.sleep(0.002)
            lats.append((received[0][0] - t0) * 1000)
            await asyncio.sleep(0.05)
        avg = sum(lats) / len(lats)
        results.append((f"K帧注入延迟 平均 {avg:.1f}ms (max {max(lats):.1f})", avg < 30))

    print("\n".join(f"{'✅' if ok else '❌'} {n}" for n, ok in results))
    assert all(ok for _, ok in results), "有测试失败！"
    print("=== 二进制协议 v2 验证通过 ===")

asyncio.run(main())
