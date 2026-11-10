# -*- coding: utf-8 -*-
"""重排 WebHostBuilder 的路由：把 /icon /screen /file /files /upload 统一放在管道末尾判定，
避免 app.Map 的 PathBase 与静态文件 app.Run 冲突。"""
import io

P = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\app-wpf\WebHostBuilder.cs"
s = io.open(P, encoding="utf-8").read()

# 找到 configure 块内的全部内容，重写为单一分发器
start_marker = "                web.Configure(app =>"
start = s.find(start_marker)
end = s.find("            })\n            .Build();")
assert start > 0 and end > start

body = r'''                web.Configure(app =>
                {
                    app.UseWebSockets();

                    // 所有请求统一在末端分发，避免 Map 的 PathBase 干扰
                    app.Run(async ctx =>
                    {
                        var path = ctx.Request.Path.Value ?? "/";

                        // --- WebSocket ---
                        if (path.Equals("/ws", StringComparison.OrdinalIgnoreCase))
                        {
                            await wsHandler(ctx);
                            return;
                        }

                        // --- 图标 ---
                        if (path.Equals("/icon", StringComparison.OrdinalIgnoreCase))
                        {
                            var q = ctx.Request.Query;
                            byte[]? png = null;
                            if (q["pid"].Count > 0 && uint.TryParse(q["pid"], out var pid))
                                png = IconService.GetForPid(pid);
                            else if (q["app"].Count > 0)
                            {
                                var resolved = AppLauncher.ResolvePath(q["app"].ToString());
                                if (resolved != null) png = IconService.GetForExe(resolved);
                            }
                            if (png == null)
                            {
                                ctx.Response.StatusCode = 404;
                                return;
                            }
                            ctx.Response.ContentType = "image/png";
                            ctx.Response.Headers.CacheControl = "public, max-age=86400";
                            await ctx.Response.Body.WriteAsync(png);
                            return;
                        }

                        // --- 屏幕截图 ---
                        if (path.Equals("/screen", StringComparison.OrdinalIgnoreCase))
                        {
                            int w = 1280, q2 = 70;
                            if (int.TryParse(ctx.Request.Query["w"], out var wv)) w = Math.Clamp(wv, 320, 2560);
                            if (int.TryParse(ctx.Request.Query["q"], out var qv)) q2 = Math.Clamp(qv, 30, 92);
                            var jpg = ScreenService.CaptureJpeg(w, q2);
                            if (jpg == null) { ctx.Response.StatusCode = 500; return; }
                            ctx.Response.ContentType = "image/jpeg";
                            ctx.Response.Headers.CacheControl = "no-store";
                            await ctx.Response.Body.WriteAsync(jpg);
                            return;
                        }

                        // --- 文件浏览 ---
                        if (path.Equals("/files", StringComparison.OrdinalIgnoreCase))
                        {
                            var dirPath = ctx.Request.Query["path"].ToString();
                            var result = FileService.List(dirPath);
                            ctx.Response.ContentType = "application/json; charset=utf-8";
                            await ctx.Response.WriteAsync(
                                System.Text.Json.JsonSerializer.Serialize(result));
                            return;
                        }

                        // --- 文件预览 / 下载 ---
                        if (path.Equals("/file", StringComparison.OrdinalIgnoreCase))
                        {
                            var filePath = ctx.Request.Query["path"].ToString();
                            var full = FileService.ResolveForDownload(filePath);
                            if (full == null) { ctx.Response.StatusCode = 404; return; }
                            var raw = ctx.Request.Query["raw"] == "1";
                            if (!raw && FileService.IsText(full))
                            {
                                ctx.Response.ContentType = "application/json; charset=utf-8";
                                var payload = new
                                {
                                    text = FileService.ReadText(full),
                                    kind = "text",
                                    name = Path.GetFileName(full),
                                };
                                await ctx.Response.WriteAsync(
                                    System.Text.Json.JsonSerializer.Serialize(payload));
                                return;
                            }
                            ctx.Response.ContentType = FileService.MimeOf(full);
                            ctx.Response.Headers.ContentDisposition =
                                $"inline; filename*=UTF-8''{Uri.EscapeDataString(Path.GetFileName(full))}";
                            await ctx.Response.SendFileAsync(full);
                            return;
                        }

                        // --- 文件上传 ---
                        if (path.Equals("/upload", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!HttpMethods.IsPost(ctx.Request.Method))
                            {
                                ctx.Response.StatusCode = 405;
                                return;
                            }
                            var targetDir = ctx.Request.Query["dir"].ToString();
                            if (!ctx.Request.HasFormContentType)
                            {
                                ctx.Response.StatusCode = 400;
                                return;
                            }
                            var form = await ctx.Request.ReadFormAsync();
                            var file = form.Files.FirstOrDefault();
                            if (file == null) { ctx.Response.StatusCode = 400; return; }
                            var saved = FileService.Save(targetDir,
                                string.IsNullOrWhiteSpace(file.FileName) ? "upload.bin" : file.FileName,
                                file.OpenReadStream());
                            ctx.Response.ContentType = "application/json; charset=utf-8";
                            var payload = saved == null
                                ? (object)new { ok = false }
                                : new { ok = true, path = saved };
                            await ctx.Response.WriteAsync(
                                System.Text.Json.JsonSerializer.Serialize(payload));
                            return;
                        }

                        // --- 静态文件 ---
                        var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
                        var rel = path == "/" ? "/index.html" : path;
                        if (rel.StartsWith("/static/", StringComparison.OrdinalIgnoreCase))
                            rel = rel["/static/".Length..];
                        var fileOnDisk = Path.Combine(wwwroot,
                            rel.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                        if (!fileOnDisk.StartsWith(wwwroot, StringComparison.Ordinal) ||
                            !File.Exists(fileOnDisk))
                        {
                            ctx.Response.StatusCode = 404;
                            return;
                        }
                        ctx.Response.ContentType = Path.GetExtension(fileOnDisk).ToLowerInvariant() switch
                        {
                            ".html" => "text/html; charset=utf-8",
                            ".json" => "application/json; charset=utf-8",
                            ".png" => "image/png",
                            ".jpg" or ".jpeg" => "image/jpeg",
                            ".js" => "application/javascript; charset=utf-8",
                            ".css" => "text/css; charset=utf-8",
                            ".svg" => "image/svg+xml",
                            _ => "application/octet-stream",
                        };
                        await ctx.Response.SendFileAsync(fileOnDisk);
                    });
                });
'''

s = s[:start] + body + s[end:]
io.open(P, "w", encoding="utf-8").write(s)
print("WebHostBuilder 路由已重排")
