using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace PadLink;

/// <summary>Kestrel 构建器：静态页 + WebSocket。</summary>
internal static class WebHostBuilder
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
                                DebugLog.Write($"icon: app='{appName}' resolved='{resolved ?? "null"}'");
                                if (resolved != null) png = IconService.GetForExe(resolved);
                                DebugLog.Write($"icon: png={(png == null ? "null" : png.Length + "B")}");
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
            })
            .Build();
    }
}
