# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 项目概述

屏幕颜色反转器 (ScreenInverter) - WPF 桌面应用，在屏幕任意区域上覆盖反色滤镜，缓解眼睛疲劳。

## 构建和运行

```bash
dotnet build      # 构建项目
dotnet run        # 运行应用
```

目标框架：.NET 8.0 Windows (WPF)

## 代码结构

```
ScreenInverter/
├── ScreenInverter.csproj      # 项目配置
├── App.xaml / App.xaml.cs     # 应用入口
├── MainWindow.xaml / .cs      # 启动窗口
├── InverterOverlayWindow.xaml / .cs  # 反色覆盖窗口（核心）
├── ScreenCapture.cs           # GDI+ 屏幕捕获
├── Inverter.cs                # 颜色反转算法
└── app.manifest               # DPI 感知配置
```

## 架构要点

### 核心组件

**InverterOverlayWindow** - 主要功能窗口
- 使用 `DispatcherTimer` (200ms 间隔) 轮询位置/大小变化
- 拖动时设置 `_isDragging = true` 暂停更新，避免闪烁
- 捕获流程：`Hide()` → 延迟 16ms → `CaptureRegion()` → 反转像素 → `UpdateBitmap()` → `Show()`
- DPI 适配：通过 `CompositionTarget.TransformToDevice` 获取缩放比，将 WPF DIP 转换为物理像素
- 窗口调整：使用 7 个 `Thumb` 控件实现 (右上角留给关闭按钮)
  - 角落手柄 (50x50, Margin 负偏移 15px)：支持双向拖动
  - 边缘手柄 (15px 厚，Margin 负偏移 15px)：支持单向拖动
  - 所有手柄完全透明，不遮挡内容

**ScreenCapture** - 屏幕捕获
- 封装 `Graphics.CopyFromScreen()`
- 返回 `Bitmap` 对象

**Inverter** - 颜色处理
- `InvertColors()` - 完全反转 RGB 通道
- `InvertLightnessOnly()` - 仅反转亮度，保留色相

### 关键实现细节

1. **避免无限递归**: 捕获前必须 `Hide()` 窗口，否则窗口会捕获到自身
2. **DPI 适配**: 所有坐标/尺寸需乘以 `_dpiScaleX/Y` 转换为物理像素
3. **闪烁控制**: 拖动时禁用更新，仅在拖动结束后更新一次
4. **调整大小手柄布局**: 右上角留空避免与关闭按钮冲突，右边缘手柄从顶部 30px 开始

## 已知问题

### 鼠标点击穿透失败

**现象**: 点击窗口区域时，窗口会短暂闪烁（隐藏后重新显示），无法实现真正的点击穿透。

**原因**: 当前架构使用 `Hide()` -> 捕获 -> `Show()` 流程，导致窗口在捕获期间必须隐藏。

**技术限制**:
- WPF 窗口无法同时实现"显示反色内容"和"鼠标穿透"
- `IsHitTestVisible="False"` 会禁用所有鼠标交互（包括控制按钮）

**可能的解决方向**:
1. 使用 Windows API 设置 `WS_EX_TRANSPARENT` 样式
2. 分离控制窗口和显示窗口
3. 使用 DirectX/OpenGL 覆盖层
4. 探索 Desktop Duplication API

## 依赖

- `System.Drawing.Common` 8.0.0 - GDI+ 屏幕捕获
- `app.manifest` 配置 Per-Monitor V2 DPI 感知