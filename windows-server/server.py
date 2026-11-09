# -*- coding: utf-8 -*-
"""
PadLink 服务端 —— 跑在 Windows 上，接收 iPhone 触控板 App 的指令控制鼠标键盘。

用法：双击 启动服务端.bat，或命令行运行 python server.py
首次运行 Windows 防火墙弹窗时，勾选"专用网络"并允许访问。
"""
import asyncio
import json
import socket
import sys

try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass

from websockets.asyncio.server import serve
from pynput.mouse import Button, Controller as MouseController
from pynput.keyboard import Controller as KeyboardController

HOST = "0.0.0.0"
PORT = 8765

mouse = MouseController()
keyboard = KeyboardController()

BUTTONS = {"left": Button.left, "right": Button.right, "middle": Button.middle}


def get_local_ip():
    """获取局域网 IP，供手机连接用。"""
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        s.connect(("8.8.8.8", 80))
        return s.getsockname()[0]
    except Exception:
        return "127.0.0.1"
    finally:
        s.close()


def handle_event(data: dict) -> None:
    t = data.get("t")

    if t == "move":
        mouse.move(float(data.get("dx", 0)), float(data.get("dy", 0)))

    elif t == "click":
        btn = BUTTONS.get(data.get("btn", "left"), Button.left)
        mouse.click(btn, int(data.get("count", 1)))

    elif t in ("down", "up"):
        btn = BUTTONS.get(data.get("btn", "left"), Button.left)
        (mouse.press if t == "down" else mouse.release)(btn)

    elif t == "scroll":
        # 自然滚动：手指下滑 (dy>0) → 内容下移（滚轮向上），与触控板习惯一致
        mouse.scroll(-float(data.get("dx", 0)) / 100.0, float(data.get("dy", 0)) / 100.0)

    elif t == "text":
        value = data.get("value", "")
        if value:
            keyboard.type(value)

    elif t == "key":
        # 特殊按键：backspace / enter / tab / esc / up / down / left / right / ctrl+c 等
        name = data.get("value", "")
        if "+" in name:
            mods, _, main = name.rpartition("+")
            from pynput.keyboard import Key, KeyCode
            mod_map = {"ctrl": Key.ctrl, "alt": Key.alt, "shift": Key.shift, "win": Key.cmd}
            with keyboard.pressed(*(mod_map[m] for m in mods.split("+") if m in mod_map)):
                if len(main) == 1:
                    keyboard.tap(main)
                elif hasattr(Key, main):
                    keyboard.tap(getattr(Key, main))
        else:
            from pynput.keyboard import Key
            count = int(data.get("count", 1))
            if hasattr(Key, name):
                for _ in range(count):
                    keyboard.tap(getattr(Key, name))
            elif len(name) == 1:
                keyboard.tap(name)


async def handler(ws) -> None:
    peer = ws.remote_address[0] if ws.remote_address else "?"
    print(f"[连接] 手机已连入：{peer}")
    try:
        async for raw in ws:
            try:
                data = json.loads(raw)
            except (ValueError, TypeError):
                continue
            if data.get("t") == "ping":
                await ws.send(json.dumps({"t": "pong", "ts": data.get("ts", 0)}))
                continue
            # pynput 的 win32 后端是同步阻塞调用，丢到线程池避免卡住事件循环
            await asyncio.to_thread(handle_event, data)
    except Exception as e:
        print(f"[断开] {peer}：{type(e).__name__}")
    print(f"[断开] 手机已断开：{peer}")


async def main():
    ip = get_local_ip()
    print("=" * 46)
    print("  PadLink 服务端已启动")
    print(f"  请在 iPhone App 里填入此地址：{ip}")
    print(f"  端口：{PORT}")
    print("  按 Ctrl+C 退出")
    print("=" * 46)
    async with serve(handler, HOST, PORT, ping_interval=20):
        await asyncio.Future()


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        print("\n已退出")
