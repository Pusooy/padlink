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
                    app.Map("/ws", branch => branch.Run(async ctx => await wsHandler(ctx)));
                    // 文件浏览: /files?path=...
                    app.Map("/files", branch => branch.Run(async ctx =>
                    {
                        var path = ctx.Request.Query["path"].ToString();
                        var result = FileService.List(path);
                        ctx.Response.ContentType = "application/json; charset=utf-8";
                        await ctx.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(result));
                    }));

                    // 文件预览/下载: /file?path=...&raw=1
                    app.Map("/file", branch => branch.Run(async ctx =>
                    {
                        var path = ctx.Request.Query["path"].ToString();
                        var full = FileService.ResolveForDownload(path);
                        if (full == null) { ctx.Response.StatusCode = 404; return; }
                        var raw = ctx.Request.Query["raw"] == "1";
                        var isText = FileService.IsText(full);
                        if (!raw && isText)
                        {
                            ctx.Response.ContentType = "application/json; charset=utf-8";
                            // 预览接口同时回传文本内容，便于前端渲染
                            var payload = new { text = FileService.ReadText(full), kind = "text", name = Path.GetFileName(full) };
                            await ctx.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(payload));
                            return;
                        }
                        ctx.Response.ContentType = FileService.MimeOf(full);
                        ctx.Response.Headers.ContentDisposition =
                            $"inline; filename*=UTF-8''{Uri.EscapeDataString(Path.GetFileName(full))}";
                        await ctx.Response.SendFileAsync(full);
                    }));

                    // 文件上传: POST /upload?dir=...
                    app.Map("/upload", branch => branch.Run(async ctx =>
                    {
                        if (!HttpMethods.IsPost(ctx.Request.Method)) { ctx.Response.StatusCode = 405; return; }
                        var dir = ctx.Request.Query["dir"].ToString();
                        if (!ctx.Request.HasFormContentType) { ctx.Response.StatusCode = 400; return; }
                        var form = await ctx.Request.ReadFormAsync();
                        var file = form.Files.FirstOrDefault();
                        if (file == null) { ctx.Response.StatusCode = 400; return; }
                        var saved = FileService.Save(dir,
                            string.IsNullOrWhiteSpace(file.FileName) ? "upload.bin" : file.FileName,
                            file.OpenReadStream());
                        ctx.Response.ContentType = "application/json; charset=utf-8";
                        await ctx.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(
                            saved == null ? (object)new { ok = false } : new { ok = true, path = saved }));
                    }));

                    // 屏幕截图（手机看屏幕）: /screen?w=1280&q=70
                    app.Map("/screen", branch => branch.Run(async ctx =>
                    {
                        int w = 1280, q = 70;
                        if (int.TryParse(ctx.Request.Query["w"], out var wv)) w = Math.Clamp(wv, 320, 2560);
                        if (int.TryParse(ctx.Request.Query["q"], out var qv)) q = Math.Clamp(qv, 30, 92);
                        var jpg = ScreenService.CaptureJpeg(w, q);
                        if (jpg == null) { ctx.Response.StatusCode = 500; return; }
                        ctx.Response.ContentType = "image/jpeg";
                        ctx.Response.Headers.CacheControl = "no-store";
                        await ctx.Response.Body.WriteAsync(jpg);
                    }));

                    // 应用/窗口真实图标: /icon?pid=123 或 /icon?app=记事本
                    app.Map("/icon", branch => branch.Run(async ctx =>
                    {
                        var q = ctx.Request.Query;
                        byte[]? png = null;
                        if (q["pid"].Count > 0 && uint.TryParse(q["pid"], out var pid))
                            png = IconService.GetForPid(pid);
                        else if (q["app"].Count > 0)
                        {
                            var path = AppLauncher.ResolvePath(q["app"].ToString());
                            if (path != null) png = IconService.GetForExe(path);
                        }
                        if (png == null) { ctx.Response.StatusCode = 404; return; }
                        ctx.Response.ContentType = "image/png";
                        ctx.Response.Headers.CacheControl = "public, max-age=86400";
                        await ctx.Response.Body.WriteAsync(png);
                    }));
                    // 静态文件（index.html / manifest.json / icon）
                    var wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
                    app.Run(async ctx =>
                    {
                        var path = ctx.Request.Path == "/" ? "/index.html" : ctx.Request.Path.Value!;
                        // /static/* 统一映射到 wwwroot 根
                        if (path.StartsWith("/static/", StringComparison.OrdinalIgnoreCase))
                            path = path["/static/".Length..];
                        var file = Path.Combine(wwwroot, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                        // 防目录穿越
                        if (!file.StartsWith(wwwroot, StringComparison.Ordinal) || !File.Exists(file))
                        {
                            ctx.Response.StatusCode = 404;
                            return;
                        }
                        ctx.Response.ContentType = Path.GetExtension(file) switch
                        {
                            ".html" => "text/html; charset=utf-8",
                            ".json" => "application/json; charset=utf-8",
                            ".png" => "image/png",
                            ".js" => "application/javascript",
                            _ => "application/octet-stream",
                        };
                        await ctx.Response.SendFileAsync(file);
                    });
                });
            })
            .Build();
    }
}
