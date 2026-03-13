# ScreenInverter 项目开发报告

**生成日期**: 2026-03-13
**项目版本**: v3.0
**技术栈**: .NET 8.0 WPF / C#

---

## 项目概述

**屏幕颜色反转器 (ScreenInverter)** 是一个 WPF 桌面应用程序，可在屏幕任意区域覆盖反色滤镜，有效缓解眼睛疲劳。特别适用于阅读不支持深色模式的 PDF 文档和网页。

### 核心特性
- 三种颜色反转模式（智能文档/强力全反/仅反亮度）
- 系统托盘后台运行
- 自定义全局快捷键
- 鼠标穿透/锁定模式（含浮动锁定按钮）
- DPI 感知 / 多显示器支持
- 可配置关闭行为（最小化到托盘 / 直接退出）
- DPI 缩放快捷选择（覆盖窗口顶部下拉框）

---

## 完整功能清单

### 1. 基础架构 ✅
- [x] WPF 项目框架 (.NET 8.0 Windows)
- [x] DPI 感知配置 (Per-Monitor V2)
- [x] 多显示器支持 (使用 VirtualScreen)
- [x] 系统托盘支持 (Windows Forms NotifyIcon)
- [x] 应用清单文件 (app.manifest)

### 2. 屏幕捕获 ✅
- [x] GDI+ 屏幕捕获 (`Graphics.CopyFromScreen`)
- [x] 指定区域捕获
- [x] DPI 缩放适配（自动模式 + 手动模式）
- [x] 使用 `SetWindowDisplayAffinity` 排除窗口自身捕获

### 3. 颜色反转（三种模式）✅

#### 模式 1: 智能文档模式
| 特性 | 说明 |
|------|------|
| 算法 | 亮度/饱和度双阈值判定 |
| 公式 | `luma = (R*77 + G*150 + B*29) >> 8` |
| 判定 | `(sat > 40) && (luma > 140)` → 图片区域 |
| 效果 | 白底→深灰、黑字→灰白、彩色图片保留 |

#### 模式 2: 强力全反模式（柔和版）— 默认模式
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
- [x] 可配置关闭行为（最小化到托盘 / 直接退出）

### 5. 全局配置系统 ✅
- [x] JSON 配置文件 (`config.json`)
- [x] 自定义快捷键（修饰键 + 字母键）
- [x] 分辨率模式配置（Auto/1080p/2K/4K/自定义）
- [x] DPI 缩放比例配置（Auto/100%/125%/150%/175%/200%/自定义）
- [x] 关闭行为配置（MinimizeToTray/Exit）
- [x] 配置持久化存储
- [x] 动态生效（无需重启）

### 6. 窗口交互 ✅
- [x] 透明顶层窗口
- [x] 拖动移动窗口
- [x] 8 个方向的调整大小手柄
- [x] 控制栏（DPI 选择、模式切换、关闭按钮）
- [x] 锁定模式（穿透模式）
- [x] 鼠标穿透（`WS_EX_TRANSPARENT`）
- [x] 浮动锁定按钮（可被鼠标点击，即使穿透状态）
- [x] 锁定按钮拖动移动窗口
- [x] 锁定状态鼠标位置轮询（50ms 间隔）

---

## 已解决的技术问题

### 问题 1: 调整窗口大小时崩溃 ✅
**原因**: `ActualWidth` 与 `Width` 不一致导致 `Marshal.Copy` 缓冲区溢出

**解决方案**:
```csharp
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

### 问题 6: DPI 与分辨率绑定导致坐标错误 ✅
**原因**: 将分辨率倍数与 DPI 倍数相乘，导致 2K+100% 时倍率错误为 1.33

**解决方案**: DPI 优先策略——设置了 DPI 后以 DPI 为绝对主导，忽略分辨率乘数；同时 UI 上当 DPI 非 Auto 时禁用分辨率下拉框

### 问题 7: 锁定模式下锁定按钮不可点击 ✅
**原因**: `WS_EX_TRANSPARENT` 穿透会让所有鼠标事件穿透窗口

**解决方案**: 鼠标位置轮询 + 动态切换穿透属性
```csharp
// 50ms 轮询鼠标位置
GetCursorPos(out POINT pt);
// 判断鼠标是否在锁定按钮上
bool isMouseOverButton = /* 坐标比较 */;
if (isMouseOverButton) {
    // 移除穿透，允许点击
    SetWindowLong(handle, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);
} else {
    // 恢复穿透
    SetWindowLong(handle, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT);
}
```

### 问题 8: 锁定按钮点击与拖动冲突 ✅
**原因**: 需要同时支持点击（锁定/解锁）和拖动（移动窗口）

**解决方案**: PreviewMouse 事件 + 拖动距离阈值判定
```csharp
// MouseDown 记录起始点并捕获鼠标
// MouseMove 检测拖动距离，超过阈值则释放捕获并 DragMove()
// MouseUp 如果未拖动则视为点击，触发 ToggleLockState()
```

---

## 当前架构

```
┌──────────────────────────────────────────────────────────────────┐
│                           App.xaml.cs                            │
│  ┌────────────────────────────────────────────────────────────┐ │
│  │ 系统托盘 (NotifyIcon)                                       │ │
│  │ - 双击：打开设置窗口                                        │ │
│  │ - 右键菜单：开启/关闭、设置、退出                           │ │
│  │ HandleMainWindowClosing() - 关闭行为分流                    │ │
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
│  │ - DPI 缩放比例            │  │   │  │ - WS_EX_TRANSPARENT       │  │
│  │ - 分辨率配置              │  │   │  │ - GetAsyncKeyState        │  │
│  │ - 关闭行为                │  │   │  │ - GetCursorPos            │  │
│  │ - 保存配置                │  │   │  │ - WndProc Hook            │  │
│  │ - 开启/关闭遮罩           │  │   │  └───────────────────────────┘  │
│  │ - DPI↔分辨率联动禁用      │  │   │  ┌───────────────────────────┐  │
│  └───────────────────────────┘  │   │  │ 控制栏                    │  │
└─────────────────────────────────┘   │  │ - DPI 缩放下拉框          │  │
         ▲                            │  │ - 模式切换按钮            │  │
         │                            │  │ - 关闭按钮                │  │
         │                            │  └───────────────────────────┘  │
┌────────┴────────────────────────┐   │  ┌───────────────────────────┐  │
│    SettingsManager              │   │  │ LockButton (浮动)         │  │
│  ┌───────────────────────────┐  │   │  │ - 点击：锁定/解锁         │  │
│  │ config.json               │  │   │  │ - 拖动：移动窗口          │  │
│  │ - ModifierKey / ActionKey │  │   │  │ - 图标：🔓/🔒             │  │
│  │ - ShortcutName            │  │   │  └───────────────────────────┘  │
│  │ - ResolutionMode          │  │   │  ┌───────────────────────────┐  │
│  │ - DpiScaleMode            │  │   │  │ DispatcherTimer (30FPS)   │  │
│  │ - CloseBehavior           │  │   │  │ - 快捷键检测 (边缘触发)   │  │
│  └───────────────────────────┘  │   │  │ - CaptureAndUpdateAsync   │  │
│  Load() / Save()                │   │  └───────────────────────────┘  │
└─────────────────────────────────┘   │  ┌───────────────────────────┐  │
                                      │  │ HitTestTimer (50ms)       │  │
                                      │  │ - 锁定时鼠标位置轮询     │  │
                                      │  │ - 动态穿透属性切换       │  │
                                      │  └───────────────────────────┘  │
                                      └─────────────────────────────────┘
                                                    │
                                                    ▼
                                      ┌─────────────────────────────────┐
                                      │  颜色处理流水线                 │
                                      │  捕获 → 反转 → UpdateBitmap    │
                                      └─────────────────────────────────┘
```

---

## 文件结构

| 文件 | 行数 | 用途 |
|------|------|------|
| `ScreenInverter.csproj` | 19 | 项目配置（WPF + WindowsForms） |
| `app.manifest` | 18 | DPI 感知 / Windows 兼容性配置 |
| `App.xaml` | 8 | WPF 应用定义（无 StartupUri） |
| `App.xaml.cs` | 99 | 应用入口 / 托盘管理 / 生命周期 / 关闭行为分流 |
| `MainWindow.xaml` | 95 | 设置面板 UI（快捷键/分辨率/DPI/关闭行为） |
| `MainWindow.xaml.cs` | 159 | 设置面板逻辑 / DPI↔分辨率联动 |
| `InverterOverlayWindow.xaml` | 332 | 反色窗口 UI（含控制栏/锁定按钮/ComboBox 模板） |
| `InverterOverlayWindow.xaml.cs` | 590 | 核心逻辑：捕获/反转/交互/锁定按钮/HitTest |
| `ScreenCapture.cs` | 45 | GDI+ 屏幕捕获封装 |
| `Inverter.cs` | 164 | 颜色处理算法（三种模式 + LUT） |
| `SettingsManager.cs` | 54 | 配置管理（JSON 序列化） |
| `config.json` | 11 | 用户配置文件 |

---

## 核心算法说明

### 柔和反转查找表
```csharp
// 输入 [0, 255] → 输出 [215, 25]
double normalized = i / 255.0;
double inverted = 1.0 - normalized;
byte val = (byte)(25 + (inverted * (215 - 25)));
```

### 智能文档判定
| 条件 | 判定 | 处理 |
|------|------|------|
| `sat > 40 && luma > 140` | 图片/高亮 | 保留原色，压暗 10% (`*230>>8`) |
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

### DPI 坐标计算策略
```
Auto+Auto → WPF PointToScreen (最稳定)
DPI 手动  → 以 DPI 值为绝对准则，忽略分辨率乘数
仅分辨率  → 降级兼容，用分辨率做粗略倍率推算
```

---

## 使用说明

| 操作 | 默认值/说明 |
|------|-------------|
| 拖动窗口 | 点击内容区域拖动 / 拖动锁定按钮 |
| 调整大小 | 拖动边缘或角落（8 方向） |
| 切换模式 | 点击控制栏"模式"按钮 |
| 锁定/解锁 | `Ctrl+L`（可自定义）/ 点击🔓按钮 |
| DPI 缩放 | 控制栏下拉框快捷切换 |
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
| v2.1 | 2026-03-06 | DPI 与分辨率解耦 / DPI 优先策略 / UI 联动禁用 |
| v2.2 | 2026-03-06 | 关闭行为配置 / overlay 控制栏 DPI 下拉框 |
| v3.0 | 2026-03-07 | 浮动锁定按钮 / HitTest 轮询 / 按钮拖动窗口 / WndProc Hook |

---

## 后续开发建议

1. **参数可调**: 允许用户自定义柔和反转的亮度范围
2. **多区域支持**: 支持创建多个独立滤镜区域
3. **窗口配置持久化**: 保存窗口位置、大小、模式偏好
4. **性能优化**: 探索 Desktop Duplication API 替代 GDI+
5. **S 曲线映射**: 进一步优化中间调过渡
6. **自定义图标**: 为托盘图标添加专属 `.ico` 文件
7. **DPI 自动检测优化**: 减少手动 DPI 选择的需求

---

## 当前状态

- **构建状态**: ✅ 成功
- **配置文件**: ✅ 已生成 `config.json`（含 9 个配置项）
- **功能完整度**: ✅ 核心功能全部完成
- **已知问题**: 无

---

*报告更新时间：2026-03-13*
