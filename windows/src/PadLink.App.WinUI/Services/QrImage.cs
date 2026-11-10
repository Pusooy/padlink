using System;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media.Imaging;

namespace PadLink.App.Services;

/// <summary>把连接地址渲染成二维码位图（用于概览页扫码连接）。</summary>
public static class QrImage
{
    /// <summary>二维码四周静区宽度（模块数），ISO/IEC 18004 要求至少 4。</summary>
    private const int QuietZoneModules = 4;

    /// <summary>
    /// 生成二维码 <see cref="WriteableBitmap"/>；文本过长或编码失败时返回 null。
    /// </summary>
    /// <param name="text">要编码的文本（这里是 http://ip:port）。</param>
    /// <param name="pixelSize">位图的物理像素边长。</param>
    /// <param name="dark">深色模块颜色。</param>
    /// <param name="light">浅色模块（背景）颜色。</param>
    public static WriteableBitmap? Create(string text, int pixelSize, Windows.UI.Color dark, Windows.UI.Color light)
    {
        if (string.IsNullOrWhiteSpace(text) || pixelSize <= 0) return null;

        bool[,]? matrix = QrEncoder.Encode(text);
        if (matrix is null) return null;

        int modules = matrix.GetLength(0);
        int total = modules + QuietZoneModules * 2;
        // 每个模块至少占 1 像素，取整后回填实际尺寸，避免非整数缩放带来的模糊
        int scale = Math.Max(1, pixelSize / total);
        int size = total * scale;

        var bitmap = new WriteableBitmap(size, size);
        var pixels = new byte[size * size * 4];
        uint darkValue = ToBgra(dark);
        uint lightValue = ToBgra(light);

        for (int y = 0; y < size; y++)
        {
            int moduleY = y / scale - QuietZoneModules;
            bool rowInRange = moduleY >= 0 && moduleY < modules;
            for (int x = 0; x < size; x++)
            {
                int moduleX = x / scale - QuietZoneModules;
                bool isDark = rowInRange && moduleX >= 0 && moduleX < modules && matrix[moduleY, moduleX];
                uint value = isDark ? darkValue : lightValue;
                int offset = (y * size + x) * 4;
                pixels[offset + 0] = (byte)(value & 0xFF);
                pixels[offset + 1] = (byte)((value >> 8) & 0xFF);
                pixels[offset + 2] = (byte)((value >> 16) & 0xFF);
                pixels[offset + 3] = (byte)((value >> 24) & 0xFF);
            }
        }

        using var stream = bitmap.PixelBuffer.AsStream();
        stream.Write(pixels, 0, pixels.Length);
        bitmap.Invalidate();
        return bitmap;
    }
    /// <summary>把 Windows.UI.Color 打包为 BGRA 小端整数（WriteableBitmap 的像素格式为 BGRA8）。</summary>
    private static uint ToBgra(Windows.UI.Color color)
        => (uint)(color.A << 24 | color.R << 16 | color.G << 8 | color.B);
}
