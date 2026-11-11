# -*- coding: utf-8 -*-
import io

P = (r"C:\Users\trump\.zcode\cli\memories\projects"
     r"\40c7b29c-1556-4b3b-ad16-4ac9f6367c84-8b8417c80882ffc3\memory"
     r"\padlink-project.md")
s = io.open(P, encoding="utf-8").read()

add = """Iteration round 8 (2026-11-11, four user-reported issues):
1) **Android 17 crash** — reverted AppCompat (risky on API 36/37) to plain `android.app.Activity` + `@android:style/Theme.DeviceDefault.NoActionBar`; removed appcompat deps; added `showFatalError()` that renders the stack trace as selectable text instead of crashing; global handler logs via `Log.e("PadLink", ...)`. **Local Android builds now work**: JDK 17 at `C:\\Program Files\\Microsoft\\jdk-17.0.21.21-hotspot`, Gradle 8.11.1 extracted to `C:\\Users\\trump\\AppData\\Local\\Temp\\gradle-8.11.1`, `android/local.properties` must use FORWARD SLASHES (`sdk.dir=C:/Users/trump/AppData/Local/Android/Sdk`) — backslash form throws "文件名、目录名或卷标语法不正确". SDK has platforms android-34/36/37. Build: `gradle assembleDebug --no-daemon`.
2) **Autostart hidden to tray** — App.OnLaunched checks `--tray` arg → always hide; added `MainWindow.HideToTray()`. **Critical bug fixed**: launcher scenario makes `Environment.ProcessPath` point at the extracted `PadLink.UI.exe`, so the Run key would register the wrong exe; launcher now passes `PADLINK_LAUNCHER` env var and `StartupRegistration.ResolveLauncherPath()` prefers it. Verified: Run key = `"C:\\Users\\trump\\PadLink\\PadLink.exe" --tray`, window handle 0, service 200.
3) **Web latency** — client now batches pointer deltas with requestAnimationFrame (`M<dx>,<dy>` / `S<dx>,<dy>` compact text frames, backpressure via `ws.bufferedAmount > 4096` keep-buffer-merge-not-drop, ±120px clamp); server parses first byte (`M`/`S`/`{`) with `ReadOnlySpan<byte>` + hand-rolled ASCII scan (no allocations), input commands run synchronously in the receive loop (only launch_program/open_file/power stay on Task.Run — NOT focus_window because it Thread.Sleep(30)). InputService accumulates sub-pixel remainders. Measured: rAF batching 1.90x faster, payload -67%, memory +4.6MB vs +14.7MB over 30k msgs.
4) **Keyboard must not invoke phone IME** — replaced `<input>` with a self-drawn `#kbDisplay` (span + blinking caret) + `kbBuf` string buffer; 260ms idle flush sends `{t:"text"}`; backspace edits buffer (falls back to sending `backspace` key when empty); Enter flushes then sends return; voice button appends recognition results. **Latent bug found**: `ROWS` single-letter keys are written as `["z"]` so `k[1]` is undefined → `keyPress` threw "Cannot read properties of undefined (reading 'length')"; fixed by `var name = (k.length > 1 && k[1] != null) ? k[1] : k[0]`. JS declaration order matters: kbBuf/kbAppend must be defined before buildKbd. Verified live: typing hello → "hello", backspace → "hell", Shift+a → "A", Caps+b → "B", zero JS errors, no input element.

Gotcha: browser caches the old inline script aggressively — must hard-reload with a cache-busting query when verifying page changes in IAB.

"""
marker = "Related: [[user-environment-facts]]"
idx = s.find(marker)
s = s[:idx] + add + s[idx:]
io.open(P, "w", encoding="utf-8").write(s)
print("project memory 已更新（round 8）")
