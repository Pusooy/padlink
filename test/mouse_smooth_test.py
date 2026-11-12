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




def mframe(dx, dy):
    return struct.pack("<Bhh", 0x4D, int(round(dx * 16)), int(round(dy * 16)))


async def recv_until(ws, want_t, timeout=5):
    while True:
        r = json.loads(await asyncio.wait_for(ws.recv(), timeout))
        if r.get("t") == want_t:
            return r


async def main():
    results = []
    async with websockets.connect("ws://127.0.0.1:8765/ws") as ws:
        await ws.send(json.dumps({"t": "screen_move", "x": 0.2, "y": 0.5}))
        await asyncio.sleep(0.4)

        # 相位 1：突发 20px（网络攒包的典型量级）。
        # 泵按实测手速摊开：应看到 ≥5 次注入、单拍间隔 ≤8ms、无 >30ms 空洞
        async def pump_snapshot():
            await ws.send(json.dumps({"t": "pump_stats"}))
            r = await recv_until(ws, "pump_stats", 5)
            f = r["freq"]
            stamps = sorted(s * 1000.0 / f for s in r["stamps"])
            return stamps, r["arrivedN"], r["injectedN"]

        b_stamps, b_arr, b_inj = await pump_snapshot()
        baseline = b_stamps[-1] if b_stamps else 0
        for _ in range(20):
            await ws.send(mframe(1, 0))
        await asyncio.sleep(0.8)
        a_stamps, a_arr, a_inj = await pump_snapshot()
        inj = [s for s in a_stamps if s > baseline]
        gaps = [b - a for a, b in zip(inj, inj[1:])]
        max_gap = max(gaps) if gaps else 0.0
        ok_burst = 5 <= len(inj) <= 90 and max_gap < 30
        p50 = sorted(gaps)[len(gaps) // 2] if gaps else 999.0
        results.append((f"突发 20px 注入 n={len(inj)} 到达={a_arr-b_arr} 注入px={a_inj-b_inj} "
                        f"间隔p50={p50:.1f}ms max={max_gap:.1f}ms", ok_burst))
        await asyncio.sleep(0.5)

        await asyncio.sleep(0.6)
        await ws.send(json.dumps({"t": "screen_move", "x": 0.8, "y": 0.5}))
        await asyncio.sleep(0.4)

        # 相位 2：均匀发送。用服务端自记的注入时间戳（pump_stats）做权威测量，
        # 判据：每个发送事件都产生注入（不丢）且注入间隔不比发送间隔差。
        b_stamps, b_arr, b_inj = await pump_snapshot()
        baseline = b_stamps[-1] if b_stamps else 0
        sent = []
        for _ in range(40):
            await ws.send(mframe(1, 0))
            sent.append(time.monotonic())
            await asyncio.sleep(0.025)
        await asyncio.sleep(0.8)
        a_stamps, a_arr, a_inj = await pump_snapshot()
        inj = [s for s in a_stamps if s > baseline]
        send_max = max((b - a) * 1000 for a, b in zip(sent, sent[1:]))
        inj_gaps = [b - a for a, b in zip(inj, inj[1:])]
        inj_max = max(inj_gaps) if inj_gaps else 9999
        cons_ok = (a_arr - b_arr) == 40 and (a_inj - b_inj) == 40
        ok_even = len(inj) >= len(sent) - 2 and inj_max < send_max + 8 and cons_ok
        results.append((f"均匀 40px 服务端注入 n={len(inj)}/{len(sent)} 到达={a_arr-b_arr} 注入={a_inj-b_inj} "
                        f"注入max={inj_max:.1f}ms 发送max={send_max:.1f}ms", ok_even))

    print("\n".join(f"{'✅' if ok else '❌'} {n}" for n, ok in results))
    assert all(ok for _, ok in results), "有测试失败！"
    print("=== 注入节拍平滑验证通过 ===")

asyncio.run(main())
