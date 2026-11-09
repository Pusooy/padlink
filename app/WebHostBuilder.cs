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
