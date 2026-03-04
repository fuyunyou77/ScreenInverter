# ScreenInverter 项目进展报告

## 项目概述

屏幕颜色反转器 (ScreenInverter) 是一个 WPF 桌面应用，旨在屏幕任意区域上覆盖反色滤镜，缓解眼睛疲劳。

---

## 已完成的功能

### 1. 基础架构
- [x] WPF 项目框架 (.NET 8.0 Windows)
- [x] DPI 感知配置 (Per-Monitor V2)
- [x] 多显示器支持 (使用 VirtualScreen)

### 2. 屏幕捕获
- [x] GDI+ 屏幕捕获 (`Graphics.CopyFromScreen`)
- [x] 指定区域捕获
- [x] DPI 缩放适配

### 3. 颜色反转
- [x] 全反转模式 (RGB 各通道取反)
- [x] 仅亮度反转模式 (保留色相)
- [x] 实时像素处理

### 4. 窗口交互
- [x] 透明顶层窗口
- [x] 拖动移动窗口
- [x] 7 个方向的调整大小手柄 (Thumb 控件)
- [x] 控制栏 (模式切换、关闭按钮)

---

## 当前存在的问题

### 问题 1: 鼠标点击穿透失败 (核心问题)

**现象**：要求穿透的区域不能鼠标穿透，鼠标左键点击界面时，窗口会短暂消失又出现。

**原因分析**：
当前实现使用 `Hide()` -> 捕获屏幕 -> `Show()` 的流程来避免窗口捕获到自身。这导致：
1. 点击窗口时触发捕获更新
2. 窗口短暂隐藏（约 16ms 延迟 + 捕获时间）
3. 在隐藏期间可以操作下方界面
4. 窗口重新显示后，视觉上表现为闪烁

**技术限制**：
- WPF 窗口无法同时实现"显示反色内容"和"鼠标穿透"
- `IsHitTestVisible="False"` 会禁用所有鼠标交互（包括控制按钮）
- 屏幕捕获必须先隐藏窗口，否则会捕获到自身（无限递归）

**可能的解决方向**（未实现）：
1. 使用 Windows API 设置窗口层级和点击穿透 (`WS_EX_TRANSPARENT`)
2. 分离控制窗口和显示窗口
3. 使用 DirectX/OpenGL 覆盖层
4. 探索其他屏幕捕获方式（如 Desktop Duplication API）

### 问题 2: 屏幕捕获闪烁

**现象**：每次捕获时窗口会闪烁一次。

**原因**：`Hide()` / `Show()` 切换的副作用。

**已采取的缓解措施**：
- 拖动/调整大小时暂停捕获更新
- 使用 200ms 定时器减少捕获频率

---

## 技术架构

```
┌─────────────────────────────────────────────────────────┐
│                    InverterOverlayWindow                │
│  ┌─────────────────────────────────────────────────┐   │
│  │  DispatcherTimer (200ms)                        │   │
│  │  └─> CaptureLoopAsync()                         │   │
│  │       └─> CaptureAndUpdateAsync()               │   │
│  │            ├─> Hide()                           │   │
│  │            ├─> Task.Delay(16ms)                 │   │
│  │            ├─> ScreenCapture.CaptureRegion()    │   │
│  │            ├─> Inverter.InvertColors()          │   │
│  │            ├─> UpdateBitmap()                   │   │
│  │            └─> Show()                           │   │
│  └─────────────────────────────────────────────────┘   │
└─────────────────────────────────────────────────────────┘
```

---

## 文件结构

| 文件 | 用途 |
|------|------|
| `ScreenInverter.csproj` | 项目配置，依赖 |
| `App.xaml / .cs` | 应用入口 |
| `MainWindow.xaml / .cs` | 启动窗口 |
| `InverterOverlayWindow.xaml` | 反色窗口 UI 定义 |
| `InverterOverlayWindow.xaml.cs` | 核心逻辑：捕获、反转、交互 |
| `ScreenCapture.cs` | GDI+ 屏幕捕获封装 |
| `Inverter.cs` | 颜色反转算法 |
| `app.manifest` | DPI 感知配置 |

---

## 依赖项

- `System.Drawing.Common` 8.0.0 - GDI+ 屏幕捕获
- .NET 8.0 Windows (WPF)

---

## 后续开发建议

1. **解决点击穿透问题**：需要重新设计架构，可能需要使用 Windows API 或分离窗口
2. **性能优化**：考虑使用 Desktop Duplication API 替代 GDI+ 捕获
3. **功能扩展**：添加快捷键、配置保存、多区域支持等