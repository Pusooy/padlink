# -*- coding: utf-8 -*-
"""PadLink WebSocket 延迟对比测试。

测量三件事：
  1) 端到端：紧凑协议 M<dx>,<dy> vs 原 JSON {"t":"move",...}
  2) 纯协议开销：用 0 位移把 Win32 SendInput 排除掉（它单次就要 ~100-200us），
     只比较解析+传输成本
  3) 批处理收益：模拟 rAF 合并——每帧 2 次 touchmove 分开发 vs 合并成 1 条

计时方法：连续发 N 条消息后立刻发一条 ping。服务端在同一个收包循环里按顺序
处理（先跑完所有 move，再回 pong），所以
    首条发出 → 收到 pong = 单程网络 + N 条处理 + pong 回程
再减去空闲 RTT 就是这 N 条消息的服务端处理+传输成本。

用法:
    python latency_test.py [ws://127.0.0.1:8765/ws] [--n 200] [--rounds 3]
"""

import argparse
import asyncio
import json
import statistics
import sys
import time

from websockets.asyncio.client import connect


async def recv_type(ws, want, timeout=8.0):
    """读到指定类型的消息为止（忽略服务端主动推送）。"""
    deadline = time.perf_counter() + timeout
    while True:
        left = deadline - time.perf_counter()
        if left <= 0:
            raise TimeoutError(f"等待 {want} 超时")
        raw = await asyncio.wait_for(ws.recv(), timeout=left)
        try:
            msg = json.loads(raw)
        except Exception:
            continue
        if msg.get("t") == want:
            return msg


async def ping_rtt(ws, samples=15):
    """空闲 RTT 中位数。"""
    vals = []
    for _ in range(samples):
        t0 = time.perf_counter()
        await ws.send(json.dumps({"t": "ping", "ts": int(time.time() * 1000)}))
        await recv_type(ws, "pong")
        vals.append((time.perf_counter() - t0) * 1000.0)
    return statistics.median(vals)


async def run_batch(ws, payloads, idle_rtt_ms):
    t0 = time.perf_counter()
    for p in payloads:
        await ws.send(p)
    send_ms = (time.perf_counter() - t0) * 1000.0

    t1 = time.perf_counter()
    await ws.send(json.dumps({"t": "ping", "ts": int(time.time() * 1000)}))
    await recv_type(ws, "pong")
    barrier_ms = (time.perf_counter() - t1) * 1000.0
    total_ms = (time.perf_counter() - t0) * 1000.0

    n = len(payloads)
    # 单程网络基线：idle RTT 的中点（loopback 下 <0.1ms，可忽略）
    server_ms = max(total_ms - idle_rtt_ms, 0.0)
    return {
        "n": n,
        "send_ms": send_ms,
        "barrier_ms": barrier_ms,
        "total_ms": total_ms,
        "per_msg_total_us": total_ms / n * 1000.0,
        "per_msg_server_us": server_ms / n * 1000.0,
    }


def summarize(label, results, payload_len):
    # 取各轮最小值：Windows 输入队列会被系统负载干扰，最小值最接近内在成本
    best = {k: min(r[k] for r in results) for k in results[0]}
    med = {k: statistics.median(r[k] for r in results) for k in results[0]}
    print(f"\n[{label}] 单条 {payload_len}B")
    print(f"  客户端发送入栈:  {best['send_ms']:7.2f} ms  "
          f"({best['send_ms'] / best['n'] * 1000:5.1f} us/条)")
    print(f"  首条→pong 总计:  {best['total_ms']:7.2f} ms (最快轮) / "
          f"{med['total_ms']:7.2f} ms (中位)  "
          f"({best['per_msg_total_us']:5.1f} us/条)")
    print(f"  服务端处理+传输:{best['per_msg_server_us']:7.1f} us/条")
    return best


async def measure(ws, n, rounds, label, make_payload, idle_rtt, payload_len):
    results = []
    for _ in range(rounds):
        payloads = [make_payload(i) for i in range(n)]
        results.append(await run_batch(ws, payloads, idle_rtt))
        await asyncio.sleep(0.15)  # 让服务端收尾
    return summarize(label, results, payload_len)


async def measure_pair(ws, n, rounds, idle_rtt, spec_a, spec_b):
    """交替测量两个协议：Windows 输入队列的成本会随系统负载漂移，
    交替采样能把漂移平均到两边，避免某一方总赶上"坏时段"。"""
    label_a, make_a, len_a = spec_a
    label_b, make_b, len_b = spec_b
    ra, rb = [], []
    for r in range(rounds):
        pa = [make_a(i) for i in range(n)]
        pb = [make_b(i) for i in range(n)]
        if r % 2 == 0:
            ra.append(await run_batch(ws, pa, idle_rtt))
            rb.append(await run_batch(ws, pb, idle_rtt))
        else:
            rb.append(await run_batch(ws, pb, idle_rtt))
            ra.append(await run_batch(ws, pa, idle_rtt))
        await asyncio.sleep(0.1)
    return (summarize(label_a, ra, len_a), summarize(label_b, rb, len_b))


def cursor_px(x, y):
    """归一化光标坐标 → 像素（虚拟桌面 5120×1152）。"""
    return x * 5119.0, y * 1151.0


async def cursor(ws):
    await ws.send(json.dumps({"t": "get_cursor"}))
    m = await recv_type(ws, "cursor")
    return cursor_px(float(m["x"]), float(m["y"]))


async def functional_check(ws):
    """确认紧凑协议真的被服务端解析：M 移动光标，S 滚轮不报错。"""
    await ws.send(json.dumps({"t": "screen_move", "x": 0.5, "y": 0.5}))
    await asyncio.sleep(0.05)
    x0, y0 = await cursor(ws)

    await ws.send("M120,60")
    await asyncio.sleep(0.08)
    x1, y1 = await cursor(ws)

    await ws.send("M-120,-60")
    await asyncio.sleep(0.08)
    x2, y2 = await cursor(ws)

    dx, dy = x1 - x0, y1 - y0
    bx, by = x2 - x0, y2 - y0
    ok = dx > 100 and dy > 50 and abs(bx) <= 5 and abs(by) <= 5
    print("\n[功能校验] 紧凑消息 M120,60")
    print(f"  光标位移: {dx:+.1f}px, {dy:+.1f}px  (期望约 +120/+60)")
    print(f"  反向 M-120,-60 残差: {bx:+.1f}px, {by:+.1f}px  (系统取整，允许 ≤5px)")
    print(f"  => {'PASS，紧凑协议已生效' if ok else 'FAIL，请检查服务端解析'}")

    t0 = time.perf_counter()
    await ws.send("S0,30")
    await ws.send(json.dumps({"t": "ping", "ts": int(time.time() * 1000)}))
    await recv_type(ws, "pong")
    print(f"  滚轮 S0,30 后 ping 往返: {(time.perf_counter() - t0) * 1000:.2f} ms")

    # 亚像素/余量：连续发 100 条 0.5px 位移，累积后应正好走 50px
    await ws.send(json.dumps({"t": "screen_move", "x": 0.5, "y": 0.5}))
    await asyncio.sleep(0.05)
    a0, _ = await cursor(ws)
    for _ in range(100):
        await ws.send("M0.50,0.00")
    await asyncio.sleep(0.1)
    a1, _ = await cursor(ws)
    print(f"  100 × 0.5px 余量累积位移: {a1 - a0:+.1f}px (期望 +50)")
    return ok


MOVE_COMPACT = "M1.50,1.50"
MOVE_JSON = json.dumps({"t": "move", "dx": 1.5, "dy": 1.5}, separators=(",", ":"))
ZERO_COMPACT = "M0.00,0.00"
ZERO_JSON = json.dumps({"t": "move", "dx": 0, "dy": 0}, separators=(",", ":"))


async def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("url", nargs="?", default="ws://127.0.0.1:8765/ws")
    ap.add_argument("--n", type=int, default=200)
    ap.add_argument("--rounds", type=int, default=3)
    args = ap.parse_args()

    async with connect(args.url, max_size=None, open_timeout=5) as ws:
        idle = await ping_rtt(ws)
        print(f"已连接 {args.url}")
        print(f"空闲 ping 往返中位数: {idle:.2f} ms（网络基线）")

        # 预热：让 JIT / 连接缓冲 / 服务端缓存进入稳态，否则第一批数据会明显偏高
        for _ in range(2):
            await run_batch(ws, ["M0.00,0.00"] * 50, idle)
            await run_batch(ws, [ZERO_JSON] * 50, idle)
        await asyncio.sleep(0.2)

        ok = await functional_check(ws)

        # 1) 真实路径（含 SendInput）。交替采样：SendInput 单次 100-250us 且随系统负载漂移。
        c1, j1 = await measure_pair(
            ws, 200, 6, idle,
            ("紧凑协议 · 真实注入", lambda i: MOVE_COMPACT, len(MOVE_COMPACT)),
            ("JSON 协议 · 真实注入", lambda i: MOVE_JSON, len(MOVE_JSON)))

        # 2) 纯协议对比：0 位移，SendInput 被余量逻辑跳过，只剩 收包+解析+分发。
        #    N 放大到 2000 让固定开销摊薄，才能看出解析方式的差距。
        c2, j2 = await measure_pair(
            ws, 2000, 5, idle,
            ("紧凑协议 · 纯解析", lambda i: ZERO_COMPACT, len(ZERO_COMPACT)),
            ("JSON 协议 · 纯解析", lambda i: ZERO_JSON, len(ZERO_JSON)))

        # 3) rAF 批处理：400 条 1.5px vs 200 条 3px（总位移相同）
        batched = await measure(ws, 200, 5, "rAF 合并 · 每帧 1 条 M3,3",
                                lambda i: "M3.00,3.00", idle, len("M3.00,3.00"))
        split = await measure(ws, 400, 5, "未合并 · 每帧 2 条 M1.5,1.5",
                              lambda i: MOVE_COMPACT, idle, len(MOVE_COMPACT))

    pct = lambda a, b: (1 - a / b) * 100 if b else 0.0
    ratio = lambda a, b: b / a if a else float("inf")

    print("\n" + "=" * 68)
    print("对比结果")
    print("=" * 68)
    print(f"  载荷大小:      紧凑 {len(MOVE_COMPACT)}B  vs  JSON {len(MOVE_JSON)}B"
          f"   → 省 {pct(len(MOVE_COMPACT), len(MOVE_JSON)):.0f}%")
    print(f"  客户端发送:    {c1['send_ms']:.2f} ms  vs  {j1['send_ms']:.2f} ms"
          f"   → {ratio(c1['send_ms'], j1['send_ms']):.2f}× 更快")
    print(f"  端到端 {args.n} 条:  {c1['total_ms']:.2f} ms  vs  {j1['total_ms']:.2f} ms"
          f"   → {ratio(c1['total_ms'], j1['total_ms']):.2f}× 更快")
    print(f"  纯协议开销/条:  {c2['per_msg_server_us']:.1f} us  vs  {j2['per_msg_server_us']:.1f} us"
          f"   → 省 {pct(c2['per_msg_server_us'], j2['per_msg_server_us']):.0f}%")
    print(f"  rAF 合并(同总位移): {batched['total_ms']:.2f} ms  vs  {split['total_ms']:.2f} ms"
          f"   → {ratio(batched['total_ms'], split['total_ms']):.2f}× 更快"
          f"（{split['n']} 条 → {batched['n']} 条）")
    print(f"\n  功能校验: {'PASS' if ok else 'FAIL'}")
    return 0 if ok else 1


if __name__ == "__main__":
    try:
        sys.exit(asyncio.run(main()))
    except Exception as e:
        print(f"测试失败: {type(e).__name__}: {e}", file=sys.stderr)
        sys.exit(1)
