# -*- coding: utf-8 -*-
"""WebHostBuilder: 静态文件从嵌入资源读取（纯单文件 exe，无 wwwroot 目录依赖）。"""
import io

P = r"C:\Users\trump\.zcode\workspace\40c7b29c-1556-4b3b-ad16-4ac9f6367c84\padlink\app-wpf\WebHostBuilder.cs"
s = io.open(P, encoding="utf-8").read()

start = s.find("                        // --- 静态文件 ---")
end = s.find("                    });\n                });")
assert start > 0 and end > start

new = r'''                        // --- 静态文件（从嵌入资源读取，纯单文件 exe） ---
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
'''

s = s[:start] + new + s[end:]
io.open(P, "w", encoding="utf-8").write(s)
print("静态文件已改为嵌入资源读取")
