using System;
using System.Drawing;
using System.Drawing.Imaging;

namespace ScreenInverter;

/// <summary>
/// 使用 GDI+ 和 CopyFromScreen 进行屏幕捕获
/// </summary>
public class ScreenCapture : IDisposable
{
    private bool _disposed;

    public void Initialize()
    {
        // 留空即可。已经不再需要缓存 VirtualScreen 尺寸，
        // 传递给 CaptureRegion 的物理坐标已经是最准确的。
    }

    /// <summary>
    /// 捕获指定区域的屏幕内容
    /// 参数 x, y 均为屏幕绝对物理坐标
    /// </summary>
    public Bitmap? CaptureRegion(int x, int y, int width, int height)
    {
        try
        {
            var bitmap = new Bitmap(width, height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(x, y, 0, 0, new Size(width, height));
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
    }
}
