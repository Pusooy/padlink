# -*- coding: utf-8 -*-
"""
PadLink 键盘输入确定性测试 v2：
- 全程先打开记事本并聚焦，注入的文字全部落在记事本里，不干扰用户
- WH_KEYBOARD_LL 底层钩子只捕获 INJECTED 事件，验证字节级顺序
- 含延迟测量
"""
import asyncio
import ctypes
import ctypes.wintypes
import json
import time
import websockets

user32 = ctypes.windll.user32

WH_KEYBOARD_LL = 13
WM_KEYDOWN, WM_SYSKEYDOWN = 0x100, 0x104
LLKHF_INJECTED = 0x10
VK_PACKET = 0xE7


class KBDLLHOOKSTRUCT(ctypes.Structure):
    _fields_ = [("vkCode", ctypes.c_uint), ("scanCode", ctypes.c_uint),
                ("flags", ctypes.c_uint), ("time", ctypes.c_uint),
                ("dwExtraInfo", ctypes.c_void_p)]


received = []
HOOK = ctypes.WINFUNCTYPE(ctypes.c_long, ctypes.c_int, ctypes.c_uint, ctypes.POINTER(KBDLLHOOKSTRUCT))


def hook_proc(n, wparam, lparam):
    kb = lparam.contents
    if kb.flags & LLKHF_INJECTED and wparam in (WM_KEYDOWN, WM_SYSKEYDOWN):
        if kb.vkCode == VK_PACKET:
            received.append((time.monotonic(), "char", chr(kb.scanCode)))
        else:
            received.append((time.monotonic(), "vk", kb.vkCode))
    return user32.CallNextHookEx(None, n, wparam, lparam)


def install_hook():
    proc = HOOK(hook_proc)
    ok = user32.SetWindowsHookExW(WH_KEYBOARD_LL, proc, None, 0)
    assert ok, "SetWindowsHookEx failed"
    msg = ctypes.wintypes.MSG()
    while user32.GetMessageW(ctypes.byref(msg), None, 0, 0):
        pass


async def send_and_focus(ws, app="记事本"):
    """打开并聚焦目标窗口，确保注入有安全落点。"""
    await ws.send(json.dumps({"t": "open_app", "name": app}))
    await asyncio.sleep(2.2)
    await ws.send(json.dumps({"t": "list_windows"}))
    r = json.loads(await asyncio.wait_for(ws.recv(), 5))
    win = next((w for w in r["list"] if app in w["title"]), None)
    assert win, f"未找到 {app} 窗口"
    await ws.send(json.dumps({"t": "focus_window", "id": win["id"]}))
    await asyncio.sleep(0.6)
    return win


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
        # 连通性：立即验证 pong
        await ws.send(json.dumps({"t": "ping", "ts": 0}))
        r = json.loads(await asyncio.wait_for(ws.recv(), 3))
        results.append(("pong 连通", r["t"] == "pong"))

        notepad = await send_and_focus(ws)

        # 1. 混合文本字节级顺序
        text1 = "你好，PadLink！abc XYZ 123 ～·「测试」"
        await ws.send(json.dumps({"t": "text", "value": text1}))
        await asyncio.sleep(1.2)
        chars = "".join(v for (_, k, v) in received if k == "char")
        results.append(("混合文本顺序", chars == text1))

        # 2. 特殊键
        received.clear()
        await ws.send(json.dumps({"t": "key", "value": "enter"}))
        await ws.send(json.dumps({"t": "key", "value": "backspace", "count": 3}))
        await asyncio.sleep(1.0)
        vks = [v for (_, k, v) in received if k == "vk"]
        results.append(("enter/backspace", vks.count(0x0D) == 1 and vks.count(0x08) == 3))

        # 3. 组合键
        received.clear()
        await ws.send(json.dumps({"t": "key", "value": "ctrl+shift+s"}))
        await asyncio.sleep(1.0)
        vks = [v for (_, k, v) in received if k == "vk"]
        ok_combo = (0x11 in vks or 0xA2 in vks) and (0x10 in vks or 0xA0 in vks) and 0x53 in vks
        results.append((f"组合键 ctrl+shift+s vks={sorted(set(vks))}", ok_combo))

        # 4. 大小写
        received.clear()
        await ws.send(json.dumps({"t": "text", "value": "AaBb"}))
        await asyncio.sleep(0.8)
        chars = "".join(v for (_, k, v) in received if k == "char")
        results.append(("大小写", chars == "AaBb"))

        # 5. 延迟
        rtts = []
        for i in range(20):
            ts = time.time() * 1000
            await ws.send(json.dumps({"t": "ping", "ts": ts}))
            r = json.loads(await asyncio.wait_for(ws.recv(), 5))
            rtts.append(time.time() * 1000 - ts)
        avg_rtt = sum(rtts) / len(rtts)
        results.append((f"ping RTT 平均 {avg_rtt:.1f}ms (max {max(rtts):.1f})", avg_rtt < 30))

        received.clear()
        t0 = time.monotonic()
        await ws.send(json.dumps({"t": "text", "value": "x" * 50}))
        await asyncio.sleep(1.0)
        elapsed = (received[-1][0] - t0) * 1000 if received else 9999
        n_chars = len([1 for x in received if x[1] == 'char'])
        results.append((f"50字符注入 {elapsed:.0f}ms", elapsed < 800 and n_chars == 50))

        # 6. 长文本
        received.clear()
        long_text = "输入法跳变测试，" * 20
        await ws.send(json.dumps({"t": "text", "value": long_text}))
        await asyncio.sleep(2.5)
        chars = "".join(v for (_, k, v) in received if k == "char")
        results.append(("长文本 160 字", chars == long_text))

        # 清理：关闭测试记事本
        await ws.send(json.dumps({"t": "close_window", "id": notepad["id"]}))
        await asyncio.sleep(1.0)

    print("\n".join(f"{'✅' if ok else '❌'} {n}" for n, ok in results))
    assert all(ok for _, ok in results), "有测试失败！"
    print("=== 键盘钩子全面验证通过 ===")

asyncio.run(main())
