using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenInverter;

/// <summary>
/// 屏幕颜色反转器 - 支持多种反转模式
/// 方案 B+C：分块自适应 + 全强度反转
/// - 分块判断区域亮度（暗区不动，亮区全力反转）
/// - 块间双线性插值 + 双重平滑，消除块边界
/// - 块内所有像素使用同一 blend → 单调映射 → 零锯齿
/// </summary>
public class Inverter
{
    // 分块参数
    private const int BlockSize = 24;         // 较大的块以减少边界数量
    private const int BrightThreshold = 170;  // 块平均亮度 >= 此值 → 全力反转
    private const int DarkThreshold = 90;     // 块平均亮度 <= 此值 → 不反转
    private const int AdaptiveRange = BrightThreshold - DarkThreshold;

    // 预计算的亮度映射表 — 全强度柔和反转 [0,255] → [215, 25]
    private static readonly byte[] _softInvertTable = new byte[256];
    private static bool _isTableInitialized = false;

    private static void InitializeTable()
    {
        if (_isTableInitialized) return;

        for (int i = 0; i < 256; i++)
        {
            double normalized = i / 255.0;
            double inverted = 1.0 - normalized;
            _softInvertTable[i] = (byte)(25 + (inverted * (215 - 25)));
        }
        _isTableInitialized = true;
    }

    /// <summary>
    /// 构建平滑的分块混合图
    /// </summary>
    private static float[] BuildBlendMap(byte[] pixelData, int width, int height, out int blocksX, out int blocksY)
    {
        blocksX = (width + BlockSize - 1) / BlockSize;
        blocksY = (height + BlockSize - 1) / BlockSize;
        int totalBlocks = blocksY * blocksX;
        var rawBlend = new float[totalBlocks];

        // 1. 计算每块平均亮度 → blend
        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                int startX = bx * BlockSize;
                int startY = by * BlockSize;
                int endX = Math.Min(startX + BlockSize, width);
                int endY = Math.Min(startY + BlockSize, height);

                long lumaSum = 0;
                int count = 0;

                for (int y = startY; y < endY; y += 2)
                {
                    for (int x = startX; x < endX; x += 2)
                    {
                        int idx = (y * width + x) * 4;
                        if (idx + 2 >= pixelData.Length) continue;
                        lumaSum += (pixelData[idx + 2] * 77 + pixelData[idx + 1] * 150 + pixelData[idx] * 29) >> 8;
                        count++;
                    }
                }

                int avgLuma = count > 0 ? (int)(lumaSum / count) : 128;
                float blend;
                if (avgLuma >= BrightThreshold) blend = 1.0f;
                else if (avgLuma <= DarkThreshold) blend = 0.0f;
                else blend = (float)(avgLuma - DarkThreshold) / AdaptiveRange;

                rawBlend[by * blocksX + bx] = blend;
            }
        }

        // 2. 双重平滑（两次 3×3 均值模糊）
        var temp = new float[totalBlocks];
        Smooth3x3(rawBlend, temp, blocksX, blocksY);
        Smooth3x3(temp, rawBlend, blocksX, blocksY);

        return rawBlend;
    }

    private static void Smooth3x3(float[] src, float[] dst, int bx, int by)
    {
        for (int y = 0; y < by; y++)
        {
            for (int x = 0; x < bx; x++)
            {
                float sum = 0;
                int n = 0;
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int ny = y + dy, nx = x + dx;
                        if (ny >= 0 && ny < by && nx >= 0 && nx < bx)
                        {
                            sum += src[ny * bx + nx];
                            n++;
                        }
                    }
                }
                dst[y * bx + x] = sum / n;
            }
        }
    }

    /// <summary>
    /// 双线性插值取像素的混合因子
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetPixelBlend(float[] blendMap, int blocksX, int blocksY, int px, int py)
    {
        float fbx = (px + 0.5f) / BlockSize - 0.5f;
        float fby = (py + 0.5f) / BlockSize - 0.5f;

        int bx0 = Math.Max(0, (int)fbx);
        int by0 = Math.Max(0, (int)fby);
        int bx1 = Math.Min(blocksX - 1, bx0 + 1);
        int by1 = Math.Min(blocksY - 1, by0 + 1);

        float fx = Math.Max(0, fbx - bx0);
        float fy = Math.Max(0, fby - by0);

        float top = blendMap[by0 * blocksX + bx0] * (1 - fx) + blendMap[by0 * blocksX + bx1] * fx;
        float bot = blendMap[by1 * blocksX + bx0] * (1 - fx) + blendMap[by1 * blocksX + bx1] * fx;
        float blend = top * (1 - fy) + bot * fy;

        return (int)(blend * 255);
    }

    // ─────────── 三种反转模式 ───────────

    public static void ProcessSmartInvert(byte[] pixelData, int width, int height)
    {
        if (!_isTableInitialized) InitializeTable();
        var blendMap = BuildBlendMap(pixelData, width, height, out int blocksX, out int blocksY);

        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int idx = (y * width + x) * 4;
                int blend = GetPixelBlend(blendMap, blocksX, blocksY, x, y);
                if (blend == 0) continue;

                byte b = pixelData[idx];
                byte g = pixelData[idx + 1];
                byte r = pixelData[idx + 2];

                int luma = (r * 77 + g * 150 + b * 29) >> 8;
                byte max = r > g ? (r > b ? r : b) : (g > b ? g : b);
                byte min = r < g ? (r < b ? r : b) : (g < b ? g : b);
                int sat = max - min;

                byte outB, outG, outR;

                if ((sat > 20) && (luma > 100))
                {
                    outB = (byte)((b * 140) >> 8);
                    outG = (byte)((g * 140) >> 8);
                    outR = (byte)((r * 140) >> 8);
                }
                else
                {
                    byte t = _softInvertTable[luma];
                    outB = t; outG = t; outR = t;
                }

                if (blend < 255)
                {
                    int inv = 255 - blend;
                    outB = (byte)((outB * blend + b * inv) >> 8);
                    outG = (byte)((outG * blend + g * inv) >> 8);
                    outR = (byte)((outR * blend + r * inv) >> 8);
                }

                pixelData[idx] = outB;
                pixelData[idx + 1] = outG;
                pixelData[idx + 2] = outR;
            }
        });
    }

    public static void InvertColors(byte[] pixelData, int width, int height)
    {
        if (!_isTableInitialized) InitializeTable();
        var blendMap = BuildBlendMap(pixelData, width, height, out int blocksX, out int blocksY);

        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int idx = (y * width + x) * 4;
                int blend = GetPixelBlend(blendMap, blocksX, blocksY, x, y);
                if (blend == 0) continue;

                byte b = pixelData[idx];
                byte g = pixelData[idx + 1];
                byte r = pixelData[idx + 2];

                byte outB = _softInvertTable[b];
                byte outG = _softInvertTable[g];
                byte outR = _softInvertTable[r];

                if (blend < 255)
                {
                    int inv = 255 - blend;
                    outB = (byte)((outB * blend + b * inv) >> 8);
                    outG = (byte)((outG * blend + g * inv) >> 8);
                    outR = (byte)((outR * blend + r * inv) >> 8);
                }

                pixelData[idx] = outB;
                pixelData[idx + 1] = outG;
                pixelData[idx + 2] = outR;
            }
        });
    }

    public static void InvertLightnessOnly(byte[] pixelData, int width, int height)
    {
        if (!_isTableInitialized) InitializeTable();
        var blendMap = BuildBlendMap(pixelData, width, height, out int blocksX, out int blocksY);

        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int idx = (y * width + x) * 4;
                int blend = GetPixelBlend(blendMap, blocksX, blocksY, x, y);
                if (blend == 0) continue;

                byte b = pixelData[idx];
                byte g = pixelData[idx + 1];
                byte r = pixelData[idx + 2];

                int luma = (r * 77 + g * 150 + b * 29) >> 8;
                byte targetLuma = _softInvertTable[luma];

                int diff = targetLuma - luma;
                byte outB = (byte)Math.Clamp(b + diff, 0, 255);
                byte outG = (byte)Math.Clamp(g + diff, 0, 255);
                byte outR = (byte)Math.Clamp(r + diff, 0, 255);

                if (blend < 255)
                {
                    int inv = 255 - blend;
                    outB = (byte)((outB * blend + b * inv) >> 8);
                    outG = (byte)((outG * blend + g * inv) >> 8);
                    outR = (byte)((outR * blend + r * inv) >> 8);
                }

                pixelData[idx] = outB;
                pixelData[idx + 1] = outG;
                pixelData[idx + 2] = outR;
            }
        });
    }

    public static BitmapSource CreateBitmapSource(byte[] pixelData, int width, int height)
    {
        return BitmapSource.Create(width, height, 96, 96,
            PixelFormats.Bgra32, null, pixelData, width * 4);
    }
}