# ScreenInverter - 屏幕颜色反转工具

一个使用 GPU Shader 进行屏幕颜色反转的 WPF 桌面应用。


## 核心功能

- **智能颜色反转**：提供多种反转模式（如软全反转、智能暗色等），有效保护视力并降低屏幕刺眼感。
- **高性能渲染**：采用 GPU 加速渲染（DirectX Shader），极低延迟，占用资源小，性能优异。
- **多屏支持**：能够同时处理并反转多个显示器的画面。
- **便捷控制**：提供系统托盘集成和全局快捷键支持，随时随地开启或关闭反转效果。

## 支持的系统

- **操作系统**: Windows 10 version 1809 或更高版本（**推荐 Windows 11**）
- **架构**: x64 处理器
- **显卡**: 支持 DirectX 11 的显卡
- **磁盘空间**: 约 100 MB

*注意：本项目依赖 DirectX Shader 编译（d3dcompiler_47.dll），Windows 10 1809 及以上固件已预装所需组件，无需额外配置环境。*

## 安装方法

本项目以**免安装单文件版 (Portable)** 的形式发布：

1. 您可以前往项目的 [Releases]((https://github.com/fuyunyou77/ScreenInverter/releases)) 页面，下载最新版的 `ScreenInverter.exe`。
2. 将该可执行文件保存在您的任意目录中。
3. 直接双击运行即可，无需安装依赖包（如 .NET 运行时），它已包含了运行所需的全部组件。

## 使用方法

1. **启动应用**：双击运行 `ScreenInverter.exe`。
2. **快捷键控制**：按下预设的全局快捷键即可随时一键反转屏幕颜色（可在设置中修改快捷键）。
3. **系统托盘菜单**：
   - 软件运行后，将在 Windows 任务栏右下角的系统托盘显示图标。
   - 右键点击该图标，可打开环境菜单。
   - 您可以通过菜单快速“开启/关闭”反转，切换不同的反转模型，或者进入“设置”。
4. **自定义设置**：在弹出的设置窗口中，您可以针对多显示器进行独立配置，调整具体的 Shader 参数等。

## 构建说明

### 环境准备

在编译本项目源码前，您需要安装 **.NET 8.0 SDK**：
- 请前往 [.NET 官网下载页面](https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0) 根据您的系统下载并安装 .NET 8.0 SDK。
- 安装完成后，您可以在命令行（如 PowerShell 或 CMD）中输入 `dotnet --version` 来验证是否安装成功（输出应为 `8.0.x` 开头）。

### 发布命令

如果需要从源码构建单文件可执行程序，请在项目根目录使用命令行执行以下命令：

```powershell
dotnet publish -c Release -r win-x64 ^
  --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:EnableCompressionInSingleFile=true ^
  -p:PublishTrimmed=false ^
  -o ./publish
```

### 配置说明

项目文件 `ScreenInverter.csproj` 已配置：
- 自包含部署（SelfContained）
- 单文件发布（PublishSingleFile）
- 启用压缩（EnableCompressionInSingleFile）
- 不裁剪代码（PublishTrimmed=false）- WPF 依赖反射，裁剪有风险

## 开源说明
本项目采用 GPL 协议开源。个人学习、研究可自由使用。若涉及商业用途（如集成进闭源产品、用于公司盈利项目等），请联系作者购买商业授权。