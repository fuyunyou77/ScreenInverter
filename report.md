# ScreenInverter 项目开发报告

**生成日期**: 2026-03-05
**项目版本**: v1.0
**技术栈**: .NET 8.0 WPF / C#

---

## 项目概述

**屏幕颜色反转器 (ScreenInverter)** 是一个 WPF 桌面应用程序，可在屏幕任意区域覆盖反色滤镜，有效缓解眼睛疲劳。特别适用于阅读不支持深色模式的 PDF 文档和网页。

### 核心特性
- 三种颜色反转模式（智能文档/强力全反/仅反亮度）
- 系统托盘后台运行
- 自定义全局快捷键
- 鼠标穿透/锁定模式
- DPI 感知 / 多显示器支持

---

## 完整功能清单

### 1. 基础架构 ✅
- [x] WPF 项目框架 (.NET 8.0 Windows)
- [x] DPI 感知配置 (Per-Monitor V2)
- [x] 多显示器支持 (使用 VirtualScreen)
- [x] 系统托盘支持 (Windows Forms NotifyIcon)

### 2. 屏幕捕获 ✅
- [x] GDI+ 屏幕捕获 (`Graphics.CopyFromScreen`)
- [x] 指定区域捕获
- [x] DPI 缩放适配
- [x] 使用 `SetWindowDisplayAffinity` 排除窗口自身捕获

### 3. 颜色反转（三种模式）✅

#### 模式 1: 智能文档模式（推荐）
| 特性 | 说明 |
|------|------|
| 算法 | 亮度/饱和度双阈值判定 |
| 公式 | `luma = 0.299R + 0.587G + 0.114B` |
| 判定 | `(sat > 40) && (luma > 140)` → 图片区域 |
| 效果 | 白底→深灰、黑字→灰白、彩色图片保留 |

#### 模式 2: 强力全反模式（柔和版）
| 特性 | 说明 |
|------|------|
| 算法 | RGB 通道独立柔和反转 |
| 输出范围 | [25, 215]（避免纯黑纯白） |
| 效果 | 色相翻转（红→青），对比度降低 |

#### 模式 3: 仅反亮度模式（柔和版）
| 特性 | 说明 |
|------|------|
| 算法 | 保留色相，仅反转亮度 |
| 效果 | 颜色不变，明暗反转 |

### 4. 系统托盘 ✅
- [x] 托盘图标（右下角）
- [x] 双击打开设置窗口
- [x] 右键菜单：开启/关闭遮罩、设置、完全退出
- [x] 关闭主窗口后后台运行

### 5. 全局配置系统 ✅
- [x] JSON 配置文件 (`config.json`)
- [x] 自定义快捷键（修饰键 + 字母键）
- [x] 配置持久化存储
- [x] 动态生效（无需重启）

### 6. 窗口交互 ✅
- [x] 透明顶层窗口
- [x] 拖动移动窗口
- [x] 8 个方向的调整大小手柄
- [x] 控制栏（模式切换、关闭按钮）
- [x] 锁定模式（穿透模式）
- [x] 鼠标穿透（`WS_EX_TRANSPARENT`）

---

## 已解决的技术问题

### 问题 1: 调整窗口大小时崩溃 ✅
**原因**: `ActualWidth` 与 `Width` 不一致导致 `Marshal.Copy` 缓冲区溢出

**解决方案**:
```csharp
// 使用 ActualWidth/ActualHeight 捕获，传递准确尺寸
w = (int)Math.Round(this.ActualWidth * _dpiScaleX);
h = (int)Math.Round(this.ActualHeight * _dpiScaleY);

// 安全检查：确保不超出缓冲区
int maxBytes = _writeableBitmap.BackBufferStride * _writeableBitmap.PixelHeight;
if (pixelData.Length <= maxBytes) {
    Marshal.Copy(pixelData, 0, _writeableBitmap.BackBuffer, pixelData.Length);
}
```

### 问题 2: 窗口位置漂移 ✅
**原因**: 达到 `MinWidth/MinHeight` 时，偏移量计算错误

**解决方案**:
```csharp
var newWidth = Math.Max(this.MinWidth, this.Width - e.HorizontalChange);
double actualChangeX = this.Width - newWidth; // 使用实际变化量
this.Left += actualChangeX;
```

### 问题 3: 快捷键长按连发 ✅
**原因**: 每帧检测都触发切换

**解决方案**: 边缘触发机制
```csharp
if (isShortcutPressed && !_wasShortcutPressed) {
    ToggleLockState(); // 仅在按下瞬间触发
}
_wasShortcutPressed = isShortcutPressed;
```

### 问题 4: 屏幕捕获闪烁 ✅
**方案**: `SetWindowDisplayAffinity(WDA_EXCLUDEFROMCAPTURE)`

### 问题 5: ClearType 文字彩色重影 ✅
**方案**: 提高判定门槛 + 强制去色

---

## 当前架构

```
┌──────────────────────────────────────────────────────────────────┐
│                           App.xaml.cs                            │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │ 系统托盘 (NotifyIcon)                                       │ │
│  │ - 双击：打开设置窗口                                        │ │
│  │ - 右键菜单：开启/关闭、设置、退出                           │ │
│  └────────────────────────────────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────┘
                              │
         ┌────────────────────┼────────────────────┐
         ▼                                         ▼
┌─────────────────────────────────┐   ┌─────────────────────────────────┐
│         MainWindow              │   │    InverterOverlayWindow        │
│  ┌───────────────────────────┐  │   │  ┌───────────────────────────┐  │
│  │ 设置面板                  │  │   │  │ Win32 API                 │  │
│  │ - 快捷键选择 (修饰键+字母)│  │   │  │ - SetWindowDisplayAffinity│  │
│  │ - 保存配置                │  │   │  │ - WS_EX_TRANSPARENT       │  │
│  │ - 开启/关闭遮罩           │  │   │  └───────────────────────────┘  │
│  │ - 关闭时隐藏到托盘        │  │   │  ┌───────────────────────────┐  │
│  └───────────────────────────┘  │   │  │ SettingsManager           │  │
└─────────────────────────────────┘   │  │ - 读取自定义快捷键        │  │
         ▲                            │  │ - 动态更新 UI 提示          │  │
         │                            │  └───────────────────────────┘  │
         │                            │  ┌───────────────────────────┐  │
┌────────┴────────────────────────┐   │  │ DispatcherTimer (30FPS)   │  │
│    SettingsManager              │   │  │ - 快捷键检测              │  │
│  ┌───────────────────────────┐  │   │  │ - CaptureAndUpdateAsync   │  │
│  │ config.json               │  │   │  └───────────────────────────┘  │
│  │ - ModifierKey             │  │   └─────────────────────────────────┘
│  │ - ActionKey               │   │              │
│  │ - ShortcutName            │  │              ▼
│  └───────────────────────────┘  │   ┌─────────────────────────────────┐
│  Load() / Save()                │   │  颜色处理流水线                 │
│                                 │   │  捕获 → 反转 → UpdateBitmap    │
└─────────────────────────────────┘   └─────────────────────────────────┘
```

---

## 文件结构

| 文件 | 行数 | 用途 |
|------|------|------|
| `ScreenInverter.csproj` | 19 | 项目配置（WPF + WindowsForms） |
| `App.xaml(.cs)` | 78 | 应用入口 / 托盘管理 / 生命周期 |
| `MainWindow.xaml(.cs)` | 71 | 设置面板（快捷键配置） |
| `InverterOverlayWindow.xaml` | 190 | 反色窗口 UI 定义 |
| `InverterOverlayWindow.xaml.cs` | 445 | 核心逻辑：捕获/反转/交互 |
| `ScreenCapture.cs` | ~40 | GDI+ 屏幕捕获封装 |
| `Inverter.cs` | ~150 | 颜色处理算法 |
| `SettingsManager.cs` | 41 | 配置管理（JSON 序列化） |
| `config.json` | 5 | 用户配置文件 |
| `app.manifest` | ~30 | DPI 感知/兼容性配置 |

---

## 核心算法说明

### 柔和反转查找表
```csharp
// 输入 [0, 255] → 输出 [215, 25]
output = 25 + (255 - input) * (215 - 25) / 255
```

### 智能文档判定
| 条件 | 判定 | 处理 |
|------|------|------|
| `sat > 40 && luma > 140` | 图片/高亮 | 保留原色，压暗 10% |
| 其他 | 文字/背景 | 强制去色 + 柔和反转 |

### 快捷键检测（边缘触发）
```csharp
bool isModPressed = config.ModifierKey == 0 ||
                    (GetAsyncKeyState(config.ModifierKey) & 0x8000) != 0;
bool isActionPressed = (GetAsyncKeyState(config.ActionKey) & 0x8000) != 0;
if (isModPressed && isActionPressed && !_wasShortcutPressed) {
    ToggleLockState();
}
```

---

## 使用说明

| 操作 | 默认值/说明 |
|------|-------------|
| 拖动窗口 | 点击内容区域拖动 |
| 调整大小 | 拖动边缘或角落（8 方向） |
| 切换模式 | 点击"模式"按钮 |
| 锁定/解锁 | `Ctrl+L`（可自定义） |
| 打开设置 | 双击托盘图标 |
| 完全退出 | 右键托盘 → 完全退出 |

---

## 依赖项

| 依赖 | 版本 | 用途 |
|------|------|------|
| .NET 8.0 Windows | 8.0 | 运行时框架 |
| System.Drawing.Common | 8.0.0 | GDI+ 屏幕捕获 |
| Windows Forms | built-in | 系统托盘 (NotifyIcon) |

**系统要求**: Windows 10 2004+ (用于 `SetWindowDisplayAffinity`)

---

## 版本历史

| 版本 | 日期 | 更新内容 |
|------|------|----------|
| v1.0 | 2026-03-05 | 初始版本：基础反转功能 |
| v1.1 | 2026-03-04 | 三种模式 / 柔和反转 / 智能文档优化 |
| v1.2 | 2026-03-05 | 崩溃修复 / 漂移修复 / 边缘触发 |
| v2.0 | 2026-03-05 | 系统托盘 / 自定义快捷键 / 配置持久化 |

---

## 后续开发建议

1. **参数可调**: 允许用户自定义柔和反转的亮度范围
2. **多区域支持**: 支持创建多个独立滤镜区域
3. **窗口配置持久化**: 保存窗口位置、大小、模式偏好
4. **性能优化**: 探索 Desktop Duplication API 替代 GDI+
5. **S 曲线映射**: 进一步优化中间调过渡
6. **自定义图标**: 为托盘图标添加专属 `.ico` 文件

---

## 当前状态

- **构建状态**: ✅ 成功（0 错误，0 警告）
- **配置文件**: ✅ 已生成 `config.json`
- **功能完整度**: ✅ 核心功能全部完成
- **已知问题**: 无

---

*报告生成时间：2026-03-05*
