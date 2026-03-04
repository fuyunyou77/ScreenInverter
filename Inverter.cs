using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenInverter;

/// <summary>
/// 屏幕颜色反转器 - 支持多种反转模式
/// </summary>
public class Inverter
{
    // 预计算的亮度映射表 (256长度)
    private static readonly byte[] _softInvertTable = new byte[256];
    private static bool _isTableInitialized = false;

    /// <summary>
    /// 初始化柔和反转的查找表 (LUT)
    /// 目的：将线性反转 (255-x) 变成柔和的 S 型曲线，避免纯黑和纯白
    /// </summary>
    private static void InitializeTable()
    {
        if (_isTableInitialized) return;

        for (int i = 0; i < 256; i++)
        {
            // 柔和逻辑：
            // 输入 255 (白背景) -> 输出 25 (深灰，不刺眼)
            // 输入 0 (黑文字)   -> 输出 215 (灰白，对比度适中)
            // 输入 128 (中灰)   -> 输出 120 (维持)

            double normalized = i / 255.0;
            double inverted = 1.0 - normalized;

            byte val = (byte)(25 + (inverted * (215 - 25)));
            _softInvertTable[i] = val;
        }
        _isTableInitialized = true;
    }

    /// <summary>
    /// 智能文档模式处理 (针对 ClearType 优化版)
    /// - 白底黑字 -> 深灰底灰白字 (护眼)
    /// - ClearType 边缘 -> 亮度反转，保留色相 (消除锯齿)
    /// - 明亮彩色图片 -> 保留原色，略微压暗
    /// </summary>
    public static void ProcessSmartInvert(byte[] pixelData, int width, int height)
    {
        if (!_isTableInitialized) InitializeTable();

        // 并行处理，利用多核 CPU 加速
        Parallel.For(0, pixelData.Length / 4, i =>
        {
            int idx = i * 4;
            byte b = pixelData[idx];
            byte g = pixelData[idx + 1];
            byte r = pixelData[idx + 2];

            // 1. 计算亮度 (Luma) 和 饱和度 (Saturation)
            // 使用整数运算优化：L = 0.299R + 0.587G + 0.114B
            int luma = (r * 77 + g * 150 + b * 29) >> 8;
            byte max = r > g ? (r > b ? r : b) : (g > b ? g : b);
            byte min = r < g ? (r < b ? r : b) : (g < b ? g : b);
            int sat = max - min;

            // 2. 严格的图片/高亮判定
            // 提高判定门槛，防止 ClearType 边缘(通常 Luma<100, Sat>50) 被误判为图片
            // 只有 "既很亮 又 很鲜艳" 的像素（如黄色高亮笔、浅色图表）才保留原色
            bool isBrightImage = (sat > 40) && (luma > 140);

            if (isBrightImage)
            {
                // --- 图片/高亮模式 ---
                // 保留原色，但稍微压暗一点以防刺眼 (原色 x 0.9)
                pixelData[idx] = (byte)((b * 230) >> 8);
                pixelData[idx + 1] = (byte)((g * 230) >> 8);
                pixelData[idx + 2] = (byte)((r * 230) >> 8);
            }
            else
            {
                // --- 文本/背景/ClearType 模式 ---
                // [核心修复] 强制去色 (Grayscale)
                // 不要试图保留文字的色相 (b + diff)，那会放大 ClearType 的红/蓝噪点。
                // 直接将 R/G/B 都设置为目标亮度。
                // 效果：ClearType 的彩色边缘 -> 变为平滑的灰色边缘 -> 完美抗锯齿。

                byte targetLuma = _softInvertTable[luma];

                pixelData[idx] = targetLuma;
                pixelData[idx + 1] = targetLuma;
                pixelData[idx + 2] = targetLuma;
            }
        });
    }

    /// <summary>
    /// 强力全反模式 (柔和版)
    /// 使用查找表实现柔和反色，避免纯黑纯白
    /// </summary>
    public static void InvertColors(byte[] pixelData, int width, int height)
    {
        if (!_isTableInitialized) InitializeTable();

        Parallel.For(0, pixelData.Length / 4, i =>
        {
            int idx = i * 4;
            pixelData[idx] = _softInvertTable[pixelData[idx]];     // B
            pixelData[idx + 1] = _softInvertTable[pixelData[idx + 1]]; // G
            pixelData[idx + 2] = _softInvertTable[pixelData[idx + 2]]; // R
        });
    }

    /// <summary>
    /// 仅反转亮度 (柔和版，保留颜色)
    /// 使用查找表实现柔和亮度反转
    /// </summary>
    public static void InvertLightnessOnly(byte[] pixelData, int width, int height)
    {
        if (!_isTableInitialized) InitializeTable();

        Parallel.For(0, pixelData.Length / 4, i =>
        {
            int idx = i * 4;
            byte b = pixelData[idx];
            byte g = pixelData[idx + 1];
            byte r = pixelData[idx + 2];

            // 计算亮度 (Rec.601)
            int luma = (r * 77 + g * 150 + b * 29) >> 8;

            // 使用柔和反转查找表
            byte targetLuma = _softInvertTable[luma];

            // 应用亮度差值，保留色相
            int diff = targetLuma - luma;
            int nb = b + diff;
            int ng = g + diff;
            int nr = r + diff;

            // Clamp 防溢出
            pixelData[idx] = (byte)(nb < 0 ? 0 : (nb > 255 ? 255 : nb));
            pixelData[idx + 1] = (byte)(ng < 0 ? 0 : (ng > 255 ? 255 : ng));
            pixelData[idx + 2] = (byte)(nr < 0 ? 0 : (nr > 255 ? 255 : nr));
        });
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