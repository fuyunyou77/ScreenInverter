using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenInverter;

/// <summary>
/// 屏幕颜色反转器 - 仅保留 BitmapSource 创建工具方法
/// 实际反色处理已完全迁移到 GPU Shader (InvertEffect)
/// </summary>
public static class Inverter
{
    public static BitmapSource CreateBitmapSource(byte[] pixelData, int width, int height)
    {
        return BitmapSource.Create(width, height, 96, 96,
            PixelFormats.Bgra32, null, pixelData, width * 4);
    }
}