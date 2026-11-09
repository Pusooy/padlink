# -*- coding: utf-8 -*-
"""
PadLink 网页版服务端 —— 一个端口同时提供触控板网页 + WebSocket 控制通道。

用法：双击 启动服务端.bat，或命令行运行 python server.py
然后手机浏览器打开控制台显示的网址（或扫终端里的二维码）。
首次运行 Windows 防火墙弹窗时，勾选"专用网络"并允许访问。
"""
import asyncio
import json
import socket
import sys
from pathlib import Path

try:
    sys.stdout.reconfigure(encoding="utf-8")
except Exception:
    pass

import qrcode
from aiohttp import WSMsgType, web
from pynput.mouse import Button, Controller as MouseController
from pynput.keyboard import Controller as KeyboardController

HOST = "0.0.0.0"
PORT = 8765
WEB_DIR = Path(__file__).parent / "web"

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
        # 特殊按键：backspace / return / tab / esc / up / down / left / right / ctrl+c 等
        name = data.get("value", "")
        if "+" in name:
            from pynput.keyboard import Key

            mod_map = {"ctrl": Key.ctrl, "alt": Key.alt, "shift": Key.shift, "win": Key.cmd}
            mods, _, main = name.rpartition("+")
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


async def ws_handler(request: web.Request) -> web.WebSocketResponse:
    ws = web.WebSocketResponse()
    await ws.prepare(request)
    peer = request.remote
    print(f"[连接] 手机已连入：{peer}")
    try:
        async for msg in ws:
            if msg.type == WSMsgType.TEXT:
                try:
                    data = json.loads(msg.data)
                except (ValueError, TypeError):
                    continue
                if data.get("t") == "ping":
                    await ws.send_json({"t": "pong", "ts": data.get("ts", 0)})
                    continue
                # pynput 的 win32 后端是同步阻塞调用，丢到线程池避免卡住事件循环
                await asyncio.to_thread(handle_event, data)
            elif msg.type == WSMsgType.ERROR:
                break
    finally:
        print(f"[断开] 手机已断开：{peer}")
    return ws


async def index_handler(request: web.Request) -> web.FileResponse:
    return web.FileResponse(WEB_DIR / "index.html")


def make_app() -> web.Application:
    app = web.Application()
    app.router.add_get("/", index_handler)
    app.router.add_get("/ws", ws_handler)
    app.router.add_static("/static/", WEB_DIR)
    return app


def print_banner(ip: str) -> None:
    url = f"http://{ip}:{PORT}"
    print("=" * 52)
    print("  PadLink 网页版服务端已启动")
    print()
    print(f"  手机浏览器打开：  {url}")
    print("  （手机需与电脑连同一个 WiFi）")
    print("  打开后建议：分享 → 添加到主屏幕")
    print()
    print("  扫码直达：")
    qr = qrcode.QRCode(border=1)
    qr.add_data(url)
    qr.print_ascii(invert=True)
    print("  按 Ctrl+C 退出")
    print("=" * 52)


async def main() -> None:
    ip = get_local_ip()
    print_banner(ip)
    runner = web.AppRunner(make_app())
    await runner.setup()
    site = web.TCPSite(runner, HOST, PORT)
    await site.start()
    await asyncio.Future()  # 运行到被 Ctrl+C 中断


if __name__ == "__main__":
    try:
        asyncio.run(main())
    except KeyboardInterrupt:
        print("\n已退出")
