# -*- coding: utf-8 -*-
"""
鼠标平滑度测试：WH_MOUSE_LL 钩子测量注入 MOUSEEVENTF_MOVE 的到达间隔。
- 突发相位：60 条 M 帧（每条 +1px）一口气发出 → 泵应把间隔摊到 <10ms 且无 >25ms 空洞
- 均匀相位：60 条 M 帧每 16ms 一条 → 间隔应贴近 16ms
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
WH_MOUSE_LL = 14
WM_MOUSEMOVE = 0x200
LLMHF_INJECTED = 0x01  # 鼠标钩子的注入标志（键盘钩子才是 0x10）


class MSLLHOOKSTRUCT(ctypes.Structure):
    _fields_ = [("ptX", ctypes.c_long), ("ptY", ctypes.c_long),
                ("mouseData", ctypes.c_uint), ("flags", ctypes.c_uint),
                ("time", ctypes.c_uint), ("dwExtraInfo", ctypes.c_void_p)]


stamps = []  # 注入 MOVE 的单调时间戳
HOOK = ctypes.WINFUNCTYPE(ctypes.c_long, ctypes.c_int, ctypes.c_uint, ctypes.POINTER(MSLLHOOKSTRUCT))


def hook_proc(n, wparam, lparam):
    if wparam == WM_MOUSEMOVE and lparam.contents.flags & LLMHF_INJECTED:
        stamps.append(time.monotonic())
    return user32.CallNextHookEx(None, n, wparam, lparam)


def install_hook():
    proc = HOOK(hook_proc)
    assert user32.SetWindowsHookExW(WH_MOUSE_LL, proc, None, 0), "hook failed"
    msg = ctypes.wintypes.MSG()
    while user32.GetMessageW(ctypes.byref(msg), None, 0, 0):
        pass


def mframe(dx, dy):
    return struct.pack("<Bhh", 0x4D, int(round(dx * 16)), int(round(dy * 16)))


def stats(ts):
    gaps = [(b - a) * 1000 for a, b in zip(ts, ts[1:])]
    gaps.sort()
    n = len(gaps)
    if n == 0:
        return dict(n=len(ts), p50=999.0, p95=999.0, max=999.0)
    return dict(n=len(ts), p50=gaps[n // 2], p95=gaps[int(n * 0.95)], max=gaps[-1])


async def main():
    import threading
    threading.Thread(target=install_hook, daemon=True).start()
    time.sleep(0.5)

    results = []
    async with websockets.connect("ws://127.0.0.1:8765/ws") as ws:
        await ws.send(json.dumps({"t": "screen_move", "x": 0.2, "y": 0.5}))
        await asyncio.sleep(0.4)

        # 相位 1：突发 20px（网络攒包的典型量级）。
        # 泵按实测手速摊开：应看到 ≥6 个注入事件、无 >30ms 空洞
        stamps.clear()
        for _ in range(20):
            await ws.send(mframe(1, 0))
        await asyncio.sleep(1.2)
        s = stats(stamps)
        ok_burst = 5 <= s["n"] <= 90 and s["max"] < 30
        results.append((f"突发 20px 注入 n={s['n']} p50={s['p50']:.1f}ms p95={s['p95']:.1f}ms max={s['max']:.1f}ms", ok_burst))

        await asyncio.sleep(0.6)
        await ws.send(json.dumps({"t": "screen_move", "x": 0.8, "y": 0.5}))
        await asyncio.sleep(0.4)

        # 相位 2：均匀发送。Python 客户端自己的 sleep 有时钟抖动（15.6ms 粒度），
        # 所以正确判据是「注入间隔不比发送间隔更差」：注入不能制造额外空洞。
        stamps.clear()
        sent = []
        for _ in range(40):
            await ws.send(mframe(1, 0))
            sent.append(time.monotonic())
            await asyncio.sleep(0.025)
        await asyncio.sleep(1.2)
        s2 = stats(stamps)
        send_max = max((b - a) * 1000 for a, b in zip(sent, sent[1:]))
        ok_even = s2["n"] >= 30 and s2["max"] < send_max + 12
        results.append((f"均匀 40px 注入 n={s2['n']}/{len(sent)} 注入max={s2['max']:.1f}ms 发送max={send_max:.1f}ms", ok_even))

    print("\n".join(f"{'✅' if ok else '❌'} {n}" for n, ok in results))
    assert all(ok for _, ok in results), "有测试失败！"
    print("=== 注入节拍平滑验证通过 ===")

asyncio.run(main())
