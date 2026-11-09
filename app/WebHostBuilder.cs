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
