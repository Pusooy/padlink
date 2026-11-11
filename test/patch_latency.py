# -*- coding: utf-8 -*-
"""
WebSocketHandler 低延迟重构：
1. 输入类指令（move/click/scroll/key）同步执行，消除 Task.Run 线程池切换延迟
2. 新增二进制 WS 消息支持（音频 PCM 流直接走二进制帧）
3. TCP_NODELAY 已由 Kestrel 默认启用（确认无需要额外设置）
4. 高优先级：输入事件在收包循环内联处理，微秒级响应
"""
import io

P = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\windows\src\PadLink.Interop\WebSocketHandler.cs"
s = io.open(P, encoding="utf-8").read()

# 1) 找到 JSON Dispatch 调用并改为同步（消除 Task.Run 延迟）
old_task_run = """                    // 输入类指令可能高频，丢到线程池执行，避免阻塞收包
                    await Task.Run(() => Dispatch(type, root));"""
new_inline = """                    // 输入指令直接同步执行：SendInput 微秒级，
                    // Task.Run 线程池切换反而引入 1-5ms 延迟
                    Dispatch(type, root);"""
if old_task_run in s:
    s = s.replace(old_task_run, new_inline)
    print("✓ Task.Run → 同步执行（消除线程池切换延迟）")

# 2) 二进制消息支持（音频 PCM 流）
if "result.MessageType == WebSocketMessageType.Binary" not in s:
    # 在收到消息后，如果是二进制帧，直接给音频服务
    old_recv = """                if (result.MessageType == WebSocketMessageType.Close) { closed = true; break; }
                var text = Encoding.UTF8.GetString(buffer, 0, result.Count);"""
    new_recv = """                if (result.MessageType == WebSocketMessageType.Close) { closed = true; break; }
                // 二进制帧 = 音频 PCM 数据（手机麦克风 → 电脑扬声器）
                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    var pcm = new byte[result.Count];
                    Array.Copy(buffer, pcm, result.Count);
                    AudioPlaybackService.WritePcm(pcm);
                    continue;
                }
                var text = Encoding.UTF8.GetString(buffer, 0, result.Count);"""
    assert old_recv in s, "recv block not found"
    s = s.replace(old_recv, new_recv)
    print("✓ 二进制 WS 消息 → 音频播放")

io.open(P, "w", encoding="utf-8").write(s)
print("WebSocketHandler 低延迟优化完成")
