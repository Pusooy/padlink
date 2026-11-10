using PadLink.Core;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PadLink.Interop;

/// <summary>Kestrel 构建器：静态页 + WebSocket。</summary>
public static class WebHostBuilder
{
    public static IHost Build(string listenUrl, Func<HttpContext, Task> wsHandler)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureLogging(l => l.ClearProviders())
            .ConfigureWebHostDefaults(web =>
            {
                web.UseUrls(listenUrl);
                web.Configure(app =>
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
                                var appName = q["app"].ToString();
                                var resolved = AppLauncher.ResolvePath(appName);
                                Log.Info($"icon: app='{appName}' resolved='{resolved ?? "null"}'");
                                if (resolved != null) png = IconService.GetForExe(resolved);
                                Log.Info($"icon: png={(png == null ? "null" : png.Length + "B")}");
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

                        // --- 静态文件（从嵌入资源读取，纯单文件 exe） ---
                        var rel = path == "/" ? "index.html" : path.TrimStart('/');
                        if (rel.StartsWith("static/", StringComparison.OrdinalIgnoreCase))
                            rel = rel["static/".Length..];

                        // 文件名安全校验（禁止穿越）
                        if (rel.Contains("..") || rel.Contains(':'))
                        {
                            ctx.Response.StatusCode = 404;
                            return;
                        }

                        var asm = typeof(WebHostBuilder).Assembly;
                        var resourceName = "web." + rel.Replace('/', '.');
                        Stream? stream = null;
                        // 允许 index.html / manifest.json / icon.png / lucide.min.js
                        try { stream = asm.GetManifestResourceStream(resourceName); }
                        catch { stream = null; }

                        // 兼容旧部署：若 exe 旁存在 wwwroot 且资源缺失，则回退磁盘读取
                        if (stream == null)
                        {
                            var disk = Path.Combine(AppContext.BaseDirectory, "wwwroot",
                                rel.Replace('/', Path.DirectorySeparatorChar));
                            if (File.Exists(disk))
                                stream = File.OpenRead(disk);
                        }

                        if (stream == null)
                        {
                            ctx.Response.StatusCode = 404;
                            await ctx.Response.WriteAsync("not found: " + rel);
                            return;
                        }

                        using (stream)
                        {
                            ctx.Response.ContentType = rel.ToLowerInvariant() switch
                            {
                                var f when f.EndsWith(".html") => "text/html; charset=utf-8",
                                var f when f.EndsWith(".json") => "application/json; charset=utf-8",
                                var f when f.EndsWith(".png") => "image/png",
                                var f when f.EndsWith(".jpg") || f.EndsWith(".jpeg") => "image/jpeg",
                                var f when f.EndsWith(".js") => "application/javascript; charset=utf-8",
                                var f when f.EndsWith(".css") => "text/css; charset=utf-8",
                                var f when f.EndsWith(".svg") => "image/svg+xml",
                                var f when f.EndsWith(".ico") => "image/x-icon",
                                _ => "application/octet-stream",
                            };
                            // HTML 与图标不缓存，便于升级后立即生效；其余长缓存
                            ctx.Response.Headers.CacheControl = rel.EndsWith(".html")
                                ? "no-cache" : "public, max-age=86400";
                            await stream.CopyToAsync(ctx.Response.Body);
                        }
                        return;
                    });
                });
            })
            .Build();
    }
}
