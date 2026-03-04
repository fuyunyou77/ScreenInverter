根据你提供的 `change.md` 中的分析与指令，以及现有的代码结构，我已经完成了代码的审查和优化。

你所提供的 `change.md` 准确地指出了问题的核心：
1.  **缩放伪影 (Artifacts)**：由 `NearestNeighbor` 导致，解决方案是改为 `HighQuality`。
2.  **ClearType 冲突 (Jagged Edges)**：由简单的颜色/亮度判定导致，解决方案是引入“亮度感知”逻辑，**强制反转暗色高饱和度像素**（即 ClearType 的边缘）。

现有的代码（特别是 `Inverter.cs` 和 `InverterOverlayWindow.xaml`）已经基本体现了这些修复。为了确保问题彻底解决，我整理了最终的定稿代码。请确保你的项目完全使用以下代码。

### 1. 核心算法修复 (`Inverter.cs`)

这是最关键的修改。原有的逻辑可能会把“暗红色的文字边缘”误判为“红色图片”而保留原色，导致黑底上出现杂乱的红边。
新的逻辑通过 `isBrightImage = (sat > 60) && (luma > 90)` 确保只有****明亮且鲜艳**的区域才保留原色，其余（包括暗色的 ClearType 边缘）一律进行亮度反转。

```csharp
using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenInverter;

/// <summary>
/// 屏幕颜色反转器 - 核心算法库
/// </summary>
public class Inverter
{
    private static readonly byte[] _softInvertTable = new byte[256];
    private static bool _isTableInitialized = false;

    /// <summary>
    /// 初始化柔和反转 LUT (避免纯黑纯白刺眼)
    /// </summary>
    private static void InitializeTable()
    {
        if (_isTableInitialized) return;

        for (int i = 0; i < 256; i++)
        {
            // 输入 255 (白) -> 输出 25 (深灰)
            // 输入 0 (黑)   -> 输出 215 (灰白)
            double normalized = i / 255.0;
            double inverted = 1.0 - normalized;
            byte val = (byte)(25 + (inverted * (215 - 25)));
            _softInvertTable[i] = val;
        }
        _isTableInitialized = true;
    }

    /// <summary>
    /// 智能文档模式 (Smart Invert) - 修复 ClearType 锯齿问题
    /// </summary>
    public static void ProcessSmartInvert(byte[] pixelData, int width, int height)
    {
        if (!_isTableInitialized) InitializeTable();

        Parallel.For(0, pixelData.Length / 4, i =>
        {
            int idx = i * 4;
            byte b = pixelData[idx];
            byte g = pixelData[idx + 1];
            byte r = pixelData[idx + 2];

            // 1. 计算亮度 (Luma) 和 饱和度 (Saturation)
            // Luma = 0.299R + 0.587G + 0.114B (近似整数运算)
            int luma = (r * 77 + g * 150 + b * 29) >> 8;
            
            byte max = r > g ? (r > b ? r : b) : (g > b ? g : b);
            byte min = r < g ? (r < b ? r : b) : (g < b ? g : b);
            int sat = max - min;

            // 2. 核心判定：解决 ClearType 边缘问题的关键
            // ClearType 边缘通常是 "高饱和度" 但 "低亮度" (如深红/深蓝边缘)
            // 旧算法只看饱和度会导致边缘不反转，形成锯齿。
            // 新算法要求必须是 "既鲜艳 又 明亮" 才保留原色。
            
            bool isBrightImage = (sat > 60) && (luma > 90);

            if (isBrightImage)
            {
                // [保留原色]：适用于高亮图表、图片
```             // 稍微压暗 (x0.9) 避免黑底上过于刺眼
                pixelData[idx] = (byte)((b * 230) >> 8);
                pixelData[idx + 1] = (byte)((g * 230) >> 8);
                pixelData[idx + 2] = (byte)((r * 230) >> 8);
            }
            else
            {
                // [亮度反转]：适用于文字、背景、以及 ClearType 边缘
                // 这样 ClearType 的暗色边缘会被反转为亮色边缘，与白色文字完美融合
                
                byte targetLuma = _softInvertTable[luma];
                int diff = targetLuma - luma;

                // 应用亮度差值，保留色相
                int nb = b + diff;
                int ng = g + diff;
                int nr = r + diff;

                // Clamp 防止溢出
                pixelData[idx] = (byte)(nb < 0 ? 0 : (nb > 255 ? 255 : nb));
                pixelData[idx + 1] = (byte)(ng < 0 ? 0 : (ng > 255 ? 255 : ng));
                pixelData[idx + 2] = (byte)(nr < 0 ? 0 : (nr > 255 ? 255 : nr));
            }
        });
    }

    // ... 其他方法 (InvertColors, InvertLightnessOnly, CreateBitmapSource) 保持不变 ...
    // 为了完整性，这里包含它们：
    public static void InvertColors(byte[] pixelData, int width, int height)
    {
        Parallel.For(0, pixelData.Length / 4, i =>
        {
            int idx = i * 4;
            pixelData[idx] = (byte)(255 - pixelData[idx]);
            pixelData[idx + 1] = (byte)(255 - pixelData[idx + 1]);
            pixelData[idx + 2] = (byte)(255 - pixelData[idx + 2]);
        });
    }

    public static void InvertLightnessOnly(byte[] pixelData, int width, int height)
    {
        Parallel.For(0, pixelData.Length / 4, i =>
        {
            int idx = i * 4;
            byte b = pixelData[idx];
            byte g = pixelData[idx + 1];
            byte r = pixelData[idx + 2];
            int luma = (r * 77 + g * 150 + b * 29) >> 8;
            int invertedLuma = 255 - luma;
            int diff = invertedLuma - luma;
            int nb = b + diff;
            int ng = g + diff;
            int nr = r + diff;
            pixelData[idx] = (byte)(nb < 0 ? 0 : (nb > 255 ? 255 : nb));
            pixelData[idx + 1] = (byte)(ng < 0 ? 0 : (ng > 255 ? 255 : ng));
            pixelData[idx + 2] = (byte)(nr < 0 ? 0 : (nr > 255 ? 255 : nr));
        });
    }
}
```

### 2\. 显示层修复 (`InverterOverlayWindow.xaml`)

解决“小字体像素缺失”的关键在于 `BitmapScalingMode="HighQuality"`。
如果不设置这个属性，WPF 默认使用线性插值或临近采样，当屏幕截图的分辨率与显示区域哪怕只有 0.5 像素的错位，都会导致细小的笔画（如 i 的点，或者 1 像素宽的表格线）直接消失。

```xml
<Window x:Class="ScreenInverter.InverterOverlayWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Screen Inverter"
        Width="400" Height="300"
        MinWidth="100" MinHeight="100"
        WindowStyle="None"
        AllowsTransparency="True"
        Background="Transparent"
        Topmost="True"
        ShowInTaskbar="False"
        WindowStartupLocation="Manual"
        ResizeMode="NoResize"
        UseLayoutRounding="True">

    <Border x:Name="MainBorder"
            BorderBrush="#40FFFFFF"
            BorderThickness="2"
            Background="Transparent"
            SnapsToDevicePixels="True">
        <Grid x:Name="MainGrid">
            <Image x:Name="CapturedImage"
                   Stretch="Fill"
                   IsHitTestVisible="False"
                   RenderOptions.BitmapScalingMode="HighQuality"
                   SnapsToDevicePixels="True"/>

            <TextBlock x:Name="StatusText"
                       Text="已锁定"
                       Foreground="#00FF00"
                       FontSize="24"
                       FontWeight="Bold"
                       HorizontalAlignment="Center"
                       VerticalAlignment="Center"
                       Visibility="Collapsed"
                       IsHitTestVisible="False">
                <TextBlock.Effect>
                    <DropShadowEffect Color="Black" BlurRadius="4" ShadowDepth="0"/>
                </TextBlock.Effect>
            </TextBlock>

            <Border x:Name="ControlBar"
                    VerticalAlignment="Top"
                    HorizontalAlignment="Stretch"
                    Background="#80000000"
                    Height="30"
                    Visibility="Visible">
                <Grid>
                    <Grid.ColumnDefinitions>
                        <ColumnDefinition Width="*"/>
                        <ColumnDefinition Width="Auto"/>
                        <ColumnDefinition Width="Auto"/>
                    </Grid.ColumnDefinitions>

                    <TextBlock Grid.Column="0"
                               Text="Ctrl+L 锁定/解锁 (穿透模式)"
                               VerticalAlignment="Center"
                               Foreground="White"
                               Margin="10,0,0,0"
                               FontSize="12"/>

                    <Button Grid.Column="1"
                            x:Name="ModeButton"
                            Content="模式：智能文档"
                            Click="ModeButton_Click"
                            Foreground="White"
                            Background="#40FFFFFF"
                            BorderThickness="0"
                            Margin="5,2,5,2"
                            FontSize="11"/>

                    <Button Grid.Column="2"
                            Content="✕"
                            Click="CloseButton_Click"
                            Foreground="White"
                            Background="#C03030"
                            BorderThickness="0"
                            Margin="5,2,5,2"
                            Width="24"
                            FontSize="14"/>
                </Grid>
            </Border>

            <Thumb Width="50" Height="50" HorizontalAlignment="Left" VerticalAlignment="Top"
                   Margin="-15,-15,0,0" Cursor="SizeNWSE"
                   DragStarted="OnResizeDragStarted" DragDelta="OnTopLeftCornerResize" DragCompleted="OnResizeDragCompleted">
                <Thumb.Template><ControlTemplate TargetType="Thumb"><Rectangle Fill="Transparent"/></ControlTemplate></Thumb.Template>
            </Thumb>
            <Thumb Width="50" Height="50" HorizontalAlignment="Left" VerticalAlignment="Bottom"
                   Margin="-15,0,0,-15" Cursor="SizeNESW"
                   DragStarted="OnResizeDragStarted" DragDelta="OnBottomLeftCornerResize" DragCompleted="OnResizeDragCompleted">
                <Thumb.Template><ControlTemplate TargetType="Thumb"><Rectangle Fill="Transparent"/></ControlTemplate></Thumb.Template>
            </Thumb>
            <Thumb Width="50" Height="50" HorizontalAlignment="Right" VerticalAlignment="Bottom"
                   Margin="0,0,-15,-15" Cursor="SizeNWSE"
                   DragStarted="OnResizeDragStarted" DragDelta="OnBottomRightCornerResize" Drag
```leted="OnResizeDragCompleted">
                <Thumb.Template><ControlTemplate TargetType="Thumb"><Rectangle Fill="Transparent"/></ControlTemplate></Thumb.Template>
            </Thumb>
             <Thumb Height="15" HorizontalAlignment="Stretch" VerticalAlignment="Top"
                   Margin="0,-15,-150,0" Cursor="SizeNS"
                   DragStarted="OnResizeDragStarted" DragDelta="OnTopEdgeResize" DragCompleted="OnResizeDragCompleted">
                <Thumb.Template><ControlTemplate TargetType="Thumb"><Rectangle Fill="Transparent"/></ControlTemplate></Thumb.Template>
            </Thumb>
            <Thumb Height="15" HorizontalAlignment="Stretch" VerticalAlignment="Bottom"
                   Margin="0,0,0,-15" Cursor="SizeNS"
                   DragStarted="OnResizeDragStarted" DragDelta="OnBottomEdgeResize" DragCompleted="OnResizeDragCompleted">
                <Thumb.Template><ControlTemplate TargetType="Thumb"><Rectangle Fill="Transparent"/></ControlTemplate></Thumb.Template>
            </Thumb>
            <Thumb Width="15" HorizontalAlignment="Left" VerticalAlignment="Stretch"
                   Margin="-15,0,0,0" Cursor="SizeWE"
                   DragStarted="OnResizeDragStarted" DragDelta="OnLeftEdgeResize" DragCompleted="OnResizeDragCompleted">
                <Thumb.Template><ControlTemplate TargetType="Thumb"><Rectangle Fill="Transparent"/></ControlTemplate></Thumb.Template>
            </Thumb>
            <Thumb Width="15" HorizontalAlignment="Right" VerticalAlignment="Stretch"
                   Margin="0,30,0,-15" Cursor="SizeWE"
                   DragStarted="OnResizeDragStarted" DragDelta="OnRightEdgeResize" DragCompleted="OnResizeDragCompleted">
                <Thumb.Template><ControlTemplate TargetType="Thumb"><Rectangle Fill="Transparent"/></ControlTemplate></Thumb.Template>
            </Thumb>

        </Grid>
    </Border>
</Window>
```

### 3\. 验证要点

应用上述两份代码后，你应该能观察到以下变化：

1.  **字体清晰度**：即使在窗口尺寸不完美匹配像素时，文字也不再断裂（归功于 XAML 中的 `BitmapScalingMode="HighQuality"`）。
2.  **边缘平滑**：白底黑字的网页，放大的时候边缘不再有红/蓝色的杂色噪点。ClearType 的彩色子像素现在被正确地反转亮度，变成了极淡的色彩，融入了白色的文字笔画中（归功于 C\# 中的 `isBrightImage` 亮度+饱和度双重判定）。
3.  **深色控件可见**：原本深蓝色的超链接，现在会变成浅蓝色，在黑背景上清晰可见（不再因为是“彩色”而被保留为深蓝色，导致看不清）。

其他文件（如 `InverterOverlayWindow.xaml.cs` 和 `ScreenCapture.cs`）你提供的版本已经包含了 DPI 修正和 `SetWindowDisplayAffinity`，这些是正确的，无需更改。