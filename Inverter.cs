using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenInverter;

/// <summary>
/// 屏幕颜色反转器 - 处理颜色反转逻辑
/// </summary>
public class Inverter
{
    /// <summary>
    /// 反转颜色 (RGB -> 255-R, 255-G, 255-B)
    /// </summary>
    public static void InvertColors(byte[] pixelData, int width, int height)
    {
        // BGRA 格式，每 4 字节一个像素
        for (int i = 0; i < pixelData.Length; i += 4)
        {
            // B 通道
            pixelData[i] = (byte)(255 - pixelData[i]);
            // G 通道
            pixelData[i + 1] = (byte)(255 - pixelData[i + 1]);
            // R 通道
            pixelData[i + 2] = (byte)(255 - pixelData[i + 2]);
            // Alpha 通道保持不变
        }
    }

    /// <summary>
    /// 部分反转 - 只反转亮度，保留色相
    /// </summary>
    public static void InvertLightnessOnly(byte[] pixelData, int width, int height)
    {
        for (int i = 0; i < pixelData.Length; i += 4)
        {
            byte b = pixelData[i];
            byte g = pixelData[i + 1];
            byte r = pixelData[i + 2];

            // 计算亮度 (简单的平均)
            byte lightness = (byte)((r + g + b) / 3);

            // 反转亮度
            byte invertedLightness = (byte)(255 - lightness);

            // 应用到各通道
            pixelData[i] = (byte)(b - lightness + invertedLightness);
            pixelData[i + 1] = (byte)(g - lightness + invertedLightness);
            pixelData[i + 2] = (byte)(r - lightness + invertedLightness);
        }
    }

    /// <summary>
    /// 创建 WPF BitmapSource 从 byte 数组
    /// </summary>
    public static BitmapSource CreateBitmapSource(byte[] pixelData, int width, int height)
    {
        return BitmapSource.Create(
            width,
            height,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            pixelData,
            width * 4);
    }
}
