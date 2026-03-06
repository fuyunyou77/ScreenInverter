你观察得非常敏锐！这正是 Windows 处理多显示器和 DPI 缩放时最容易让人迷惑的地方。

现象的本质是：**WPF 操作的是逻辑像素，而截屏代码需要的是物理像素。**
在你的 2K 显示器且 **100% 缩放**下，逻辑像素刚好等于物理像素（倍率为 1.0）。在之前的代码中，"1080p" 的选项对应的映射倍率就是 1.0，所以你选 1080p 反而是“完美对齐”的。当你选择 "2K" 时，代码强行乘以了 `1.33`（2560/1920）的倍率，这就导致截屏去抓取了 1.33 倍的超大区域，然后再强行挤进窗口里，看起来就像是内容被“缩小”了。

为了彻底解决这个问题，我为你添加了**显示器分辨率**和**系统缩放比例 (DPI)**的双重控制选项，以完美贴合 Windows 系统设置。

此外，我大幅重构了“自动获取”逻辑，利用 WPF 原生的底层 API `PointToScreen`。它能自动穿透 Windows 的虚拟化屏障，完美计算任何显示器组合和缩放比例的真实物理坐标。极大概。极大概率你现在只需要选择“自动获取 (推荐)”，就能在任意屏幕间自由拖拽。

以下是修改后的完整类实现：

### 1\. SettingsManager.cs

增加分别针对“分辨率”和“DPI缩放比例”的配置存储。

```csharp
using System.IO;
using System.Text.Json;

namespace ScreenInverter;

public class AppSettings
{
    // 快捷键设置
    public int ModifierKey { get; set; } = 0x11;
    public int ActionKey { get; set; } = 0x4C;
    public string ShortcutName { get; set; } = "Ctrl+L";

    // 分辨率适配设置
    public string ResolutionMode { get; set; } = "Auto";
    public double CustomResW { get; set; } = 1920;
    public double CustomResH { get; set; } = 1080;

    // DPI 缩放比例设置 (对应 Windows 系统设置里的 100%, 125%, 150% 等)
    public string DpiScaleMode { get; set; } = "Auto";
    public double CustomDpiScale { get; set; } = 100;
}

public static class SettingsManager
{
    private static readonly string Path = "config.json";
    public static AppSettings Current { get; private set; } = new AppSettings();

    public static void Load()
    {
        if (File.Exists(Path))
        {
            try
            {
                string json = File.ReadAllText(Path);
                Current = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch { Current = new AppSettings(); }
        }
    }

    public static void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path, json);
        }
        catch { }
    }
}
```

-----

### 2\. MainWindow.xaml

增加缩放比例的下拉框和自定义界面，对齐 Windows 系统设置逻辑。

```xml
<Window x:Class="ScreenInverter.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="屏幕颜色反转器 - 设置"
        Height="420" Width="450"
        WindowStartupLocation="CenterScreen"
        ResizeMode="NoResize"
        Closing="Window_Closing">
    <Grid Margin="20">
        <StackPanel>
            <TextBlock Text="控制面板"
                       FontSize="20"
                       FontWeight="Bold"
                       HorizontalAlignment="Center"
                       Margin="0,0,0,15"/>

            <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,0,0,15">
                <TextBlock Text="穿透快捷键：" VerticalAlignment="Center" Margin="0,0,10,0"/>
                <ComboBox x:Name="CmbModifier" Width="70" DisplayMemberPath="Key" SelectedValuePath="Value"/>
                <TextBlock Text=" + " VerticalAlignment="Center" Margin="5,0"/>
                <ComboBox x:Name="CmbActionKey" Width="70" DisplayMemberPath="Key" SelectedValuePath="Value"/>
            </StackPanel>

            <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,0,0,5">
                <TextBlock Text="显示器分辨率：" VerticalAlignment="Center" Margin="0,0,10,0"/>
                <ComboBox x:Name="CmbResolution" Width="180" SelectionChanged="CmbResolution_SelectionChanged">
                    <ComboBoxItem Content="自动获取 (推荐)" Tag="Auto"/>
                    <ComboBoxItem Content="1080p (1920x1080)" Tag="1080p"/>
                    <ComboBoxItem Content="2K (2560x1440)" Tag="2K"/>
                    <ComboBoxItem Content="4K (3840x2160)" Tag="4K"/>
                    <ComboBoxItem Content="自定义分辨率..." Tag="Custom"/>
                </ComboBox>
            </StackPanel>

            <StackPanel x:Name="PanelCustomRes" Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,0,0,10" Visibility="Collapsed">
                <TextBox x:Name="TxtResW" Width="60" TextAlignment="Center" VerticalContentAlignment="Center"/>
                <TextBlock Text=" × " VerticalAlignment="Center" Margin="5,0"/>
                <TextBox x:Name="TxtResH" Width="60" TextAlignment="Center" VerticalContentAlignment="Center"/>
            </StackPanel>

            <StackPanel Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,10,0,5">
                <TextBlock Text="系统缩放比例：" VerticalAlignment="Center" Margin="0,0,10,0"/>
                <ComboBox x:Name="CmbDpiScale" Width="180" SelectionChanged="CmbDpiScale_SelectionChanged">
                    <ComboBoxItem Content="自动获取 (推荐)" Tag="Auto"/>
                    <ComboBoxItem Content="100%" Tag="100"/>
                    <ComboBoxItem Content="125%" Tag="125"/>
                    <ComboBoxItem Content="150%" Tag="150"/>
                    <ComboBoxItem Content="175%" Tag="175"/>
                    <ComboBoxItem Content="200%" Tag="200"/>
                    <ComboBoxItem Content="自定义比例..." Tag="Custom"/>
                </ComboBox>
            </StackPanel>

            <StackPanel x:Name="PanelCustomDpi" Orientation="Horizontal" HorizontalAlignment="Center" Margin="0,0,0,15" Visibility="Collapsed">
                <TextBox x:Name="TxtDpiScale" Width="60" TextAlignment="Center" VerticalContentAlignment="Center"/>
                <TextBlock Text=" %" VerticalAlignment="Center" Margin="5,0"/>
            </StackPanel>

            <Button Content="保存所有设置"
                    Click="SaveButton_Click"
                    Padding="10,5"
                    Width="150"
                    Margin="0,10,0,15"/>

            <Button Content="开启 / 关闭反转遮罩"
                    Click="ToggleOverlayButton_Click"
                    Padding="20,10"
                    FontSize="14"
                    Background="#2080D0"
                    Foreground="White"/>

            <TextBlock Text="提示：关闭此窗口软件仍会在托盘后台运行"
                       HorizontalAlignment="Center"
                       Margin="0,15,0,0"
                       Foreground="#888"
                       FontSize="11"/>
        </StackPanel>
    </Grid>
</Window>
```

-----

### 3\. MainWindow.xaml.cs

处理下拉框显示、隐藏和数据的持久化存储。

```csharp
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using WpfMessageBox = System.Windows.MessageBox;
using WpfApplication = System.Windows.Application;

namespace ScreenInverter;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        LoadSettingsToUI();
    }

    private void LoadSettingsToUI()
    {
        // 绑定修饰键
        var modifiers = new List<KeyValuePair<string, int>>
        {
            new("无", 0),
            new("Ctrl", 0x11),
            new("Alt", 0x12),
            new("Shift", 0x10)
        };
        CmbModifier.ItemsSource = modifiers;

        var actionKeys = new List<KeyValuePair<string, int>>();
        for (int i = 0x41; i <= 0x5A; i++)
        {
            actionKeys.Add(new KeyValuePair<string, int>(((char)i).ToString(), i));
        }
        CmbActionKey.ItemsSource = actionKeys;

        CmbModifier.SelectedValue = SettingsManager.Current.ModifierKey;
        CmbActionKey.SelectedValue = SettingsManager.Current.ActionKey;

        // 回显分辨率
        foreach (ComboBoxItem item in CmbResolution.Items)
        {
            if (item.Tag.ToString() == SettingsManager.Current.ResolutionMode)
            {
                CmbResolution.SelectedItem = item;
                break;
            }
        }
        TxtResW.Text = SettingsManager.Current.CustomResW.ToString();
        TxtResH.Text = SettingsManager.Current.CustomResH.ToString();

        // 回显缩放比例
        foreach (ComboBoxItem item in CmbDpiScale.Items)
        {
            if (item.Tag.ToString() == SettingsManager.Current.DpiScaleMode)
            {
                CmbDpiScale.SelectedItem = item;
                break;
            }
        }
        TxtDpiScale.Text = SettingsManager.Current.CustomDpiScale.ToString();
    }

    private void CmbResolution_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PanelCustomRes == null) return;
        PanelCustomRes.Visibility = (CmbResolution.SelectedItem is ComboBoxItem item && item.Tag.ToString() == "Custom") 
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void CmbDpiScale_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PanelCustomDpi == null) return;
        PanelCustomDpi.Visibility = (CmbDpiScale.SelectedItem is ComboBoxItem item && item.Tag.ToString() == "Custom") 
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (CmbModifier.SelectedValue is int mod && CmbActionKey.SelectedValue is int act)
        {
            SettingsManager.Current.ModifierKey = mod;
            SettingsManager.Current.ActionKey = act;
            string modStr = mod == 0 ? "" : ((KeyValuePair<string, int>)CmbModifier.SelectedItem).Key + "+";
            SettingsManager.Current.ShortcutName = modStr + ((KeyValuePair<string, int>)CmbActionKey.SelectedItem).Key;
        }

        if (CmbResolution.SelectedItem is ComboBoxItem resItem)
        {
            SettingsManager.Current.ResolutionMode = resItem.Tag.ToString() ?? "Auto";
            if (SettingsManager.Current.ResolutionMode == "Custom" && 
                double.TryParse(TxtResW.Text, out double w) && double.TryParse(TxtResH.Text, out double h))
            {
                SettingsManager.Current.CustomResW = w;
                SettingsManager.Current.CustomResH = h;
            }
        }

        if (CmbDpiScale.SelectedItem is ComboBoxItem dpiItem)
        {
            SettingsManager.Current.DpiScaleMode = dpiItem.Tag.ToString() ?? "Auto";
            if (SettingsManager.Current.DpiScaleMode == "Custom" && double.TryParse(TxtDpiScale.Text, out double customDpi))
            {
                SettingsManager.Current.CustomDpiScale = customDpi;
            }
        }

        SettingsManager.Save();
        WpfMessageBox.Show("设置已保存！\n分辨率和缩放修改将在遮罩窗口实时生效。", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ToggleOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (WpfApplication.Current is App app)
        {
            app.ToggleOverlay();
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true;
        this.Hide();
    }
}
```

-----

### 4\. InverterOverlayWindow.xaml.cs (核心渲染逻辑更新)

移除容易出现坐标系偏差的 `GetWindowRect`，引入完美适配多屏幕/多DPI的高级转换 API `PointToScreen`，同时融合了手动双重计算模式。

```csharp
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;
using Point = System.Windows.Point;

namespace ScreenInverter;

public partial class InverterOverlayWindow : Window
{
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    [DllImport("user32.dll")]
    private static extern uint SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private readonly ScreenCapture _screenCapture;
    private WriteableBitmap? _writeableBitmap;
    private bool _isResizing;
    private int _inversionMode;
    private readonly DispatcherTimer _captureTimer;
    private bool _isCapturing;
    private bool _isLocked = false;
    private bool _wasShortcutPressed = false;

    public InverterOverlayWindow()
    {
        InitializeComponent();

        _screenCapture = new ScreenCapture();
        _screenCapture.Initialize();

        ShortcutHintText.Text = $"{SettingsManager.Current.ShortcutName} 锁定/解锁 (穿透模式)";

        this.SourceInitialized += OnSourceInitialized;

        this.Left = 100;
        this.Top = 100;
        this.Width = 500;
        this.Height = 400;

        this.MouseLeftButtonDown += OnMouseLeftButtonDown;

        _captureTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _captureTimer.Tick += TimerTick;
        _captureTimer.Start();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        try
        {
            SetWindowDisplayAffinity(helper.Handle, WDA_EXCLUDEFROMCAPTURE);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to set display affinity: {ex.Message}");
        }
    }

    private async void TimerTick(object? sender, EventArgs e)
    {
        var config = SettingsManager.Current;

        bool isModPressed = config.ModifierKey == 0 || (GetAsyncKeyState(config.ModifierKey) & 0x8000) != 0;
        bool isActionPressed = (GetAsyncKeyState(config.ActionKey) & 0x8000) != 0;
        bool isShortcutPressed = isModPressed && isActionPressed;

        if (isShortcutPressed && !_wasShortcutPressed)
        {
            ToggleLockState();
        }

        _wasShortcutPressed = isShortcutPressed;

        if (ShortcutHintText.Text != $"{config.ShortcutName} 锁定/解锁 (穿透模式)")
        {
            ShortcutHintText.Text = $"{config.ShortcutName} 锁定/解锁 (穿透模式)";
        }

        await CaptureLoopAsync();
    }

    private void ToggleLockState()
    {
        _isLocked = !_isLocked;
        var helper = new WindowInteropHelper(this);
        int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);

        if (_isLocked)
        {
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT);

            MainBorder.BorderThickness = new Thickness(0);
            ControlBar.Visibility = Visibility.Collapsed;
            ResizeThumbsVisibility(Visibility.Collapsed);
            StatusText.Visibility = Visibility.Visible;
            StatusText.Text = $"已锁定。按 {SettingsManager.Current.ShortcutName} 解锁";

            Task.Delay(3000).ContinueWith(_ => Dispatcher.Invoke(() => StatusText.Visibility = Visibility.Collapsed));
        }
        else
        {
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);

            MainBorder.BorderThickness = new Thickness(2);
            ControlBar.Visibility = Visibility.Visible;
            ResizeThumbsVisibility(Visibility.Visible);
            StatusText.Visibility = Visibility.Collapsed;
        }
    }

    private void ResizeThumbsVisibility(Visibility v)
    {
        foreach (var child in MainGrid.Children)
        {
            if (child is Thumb thumb)
            {
                thumb.Visibility = v;
            }
        }
    }

    private async Task CaptureLoopAsync()
    {
        if (_isResizing || _isCapturing) return;
        await CaptureAndUpdateAsync();
    }

    private async Task CaptureAndUpdateAsync()
    {
        if (_isCapturing || !this.IsVisible || this.WindowState == WindowState.Minimized) return;

        _isCapturing = true;

        await Task.Run(() =>
        {
            try
            {
                int physicalX = 0, physicalY = 0, physicalW = 0, physicalH = 0;
                string resMode = "Auto";
                string dpiMode = "Auto";
                double customW = 1920, customH = 1080, customDpi = 100;
                int currentMode = 0;

                Dispatcher.Invoke(() =>
                {
                    resMode = SettingsManager.Current.ResolutionMode;
                    dpiMode = SettingsManager.Current.DpiScaleMode;
                    customW = SettingsManager.Current.CustomResW;
                    customH = SettingsManager.Current.CustomResH;
                    customDpi = SettingsManager.Current.CustomDpiScale;
                    currentMode = _inversionMode;
                });

                if (resMode == "Auto" && dpiMode == "Auto")
                {
                    // 终极解法：使用 WPF 内部的物理屏幕映射体系
                    // PointToScreen 会完美将逻辑坐标映射到多显示器下的绝对物理坐标
                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            var source = PresentationSource.FromVisual(this);
                            if (source != null)
                            {
                                Point topLeft = this.PointToScreen(new Point(0, 0));
                                Point bottomRight = this.PointToScreen(new Point(this.ActualWidth, this.ActualHeight));
                                physicalX = (int)Math.Round(topLeft.X);
                                physicalY = (int)Math.Round(topLeft.Y);
                                physicalW = (int)Math.Round(bottomRight.X - topLeft.X);
                                physicalH = (int)Math.Round(bottomRight.Y - topLeft.Y);
                            }
                        }
                        catch { /* 忽略初始化尚未彻底完成时的偶发异常 */ }
                    });
                }
                else
                {
                    // 用户强制指定补偿比例
                    double finalScaleX = 1.0;
                    double finalScaleY = 1.0;

                    // 1. 分辨率倍数
                    if (resMode == "1080p") { finalScaleX *= 1.0; finalScaleY *= 1.0; }
                    else if (resMode == "2K") { finalScaleX *= 2560.0 / 1920.0; finalScaleY *= 1440.0 / 1080.0; }
                    else if (resMode == "4K") { finalScaleX *= 3840.0 / 1920.0; finalScaleY *= 2160.0 / 1080.0; }
                    else if (resMode == "Custom") { finalScaleX *= customW / 1920.0; finalScaleY *= customH / 1080.0; }

                    // 2. DPI 缩放倍数
                    if (dpiMode != "Auto" && dpiMode != "Custom" && double.TryParse(dpiMode, out double dpiVal))
                    {
                        finalScaleX *= dpiVal / 100.0;
                        finalScaleY *= dpiVal / 100.0;
                    }
                    else if (dpiMode == "Custom")
                    {
                        finalScaleX *= customDpi / 100.0;
                        finalScaleY *= customDpi / 100.0;
                    }

                    Dispatcher.Invoke(() =>
                    {
                        physicalX = (int)Math.Round(this.Left * finalScaleX);
                        physicalY = (int)Math.Round(this.Top * finalScaleY);
                        physicalW = (int)Math.Round(this.ActualWidth * finalScaleX);
                        physicalH = (int)Math.Round(this.ActualHeight * finalScaleY);
                    });
                }

                if (physicalW <= 0 || physicalH <= 0) return;

                using var bitmap = _screenCapture.CaptureRegion(physicalX, physicalY, physicalW, physicalH);
                if (bitmap == null) return;

                var data = bitmap.LockBits(
                    new Rectangle(0, 0, physicalW, physicalH),
                    ImageLockMode.ReadWrite,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                try
                {
                    int byteCount = physicalW * physicalH * 4;
                    var pixelData = new byte[byteCount];
                    Marshal.Copy(data.Scan0, pixelData, 0, byteCount);

                    if (currentMode == 0)
                        Inverter.ProcessSmartInvert(pixelData, physicalW, physicalH);
                    else if (currentMode == 1)
                        Inverter.InvertColors(pixelData, physicalW, physicalH);
                    else
                        Inverter.InvertLightnessOnly(pixelData, physicalW, physicalH);

                    Dispatcher.Invoke(() => UpdateBitmap(pixelData, physicalW, physicalH));
                }
                finally
                {
                    bitmap.UnlockBits(data);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Capture error: {ex}");
            }
            finally
            {
                _isCapturing = false;
            }
        });
    }

    private void ReinitializeBitmap(int width, int height)
    {
        if (_isResizing || width <= 0 || height <= 0) return;

        try
        {
            _writeableBitmap = new WriteableBitmap(
                width, height, 96, 96,
                System.Windows.Media.PixelFormats.Bgra32, null);

            if (CapturedImage != null) CapturedImage.Source = _writeableBitmap;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ReinitializeBitmap error: {ex.Message}");
        }
    }

    private void UpdateBitmap(byte[] pixelData, int width, int height)
    {
        if (_isResizing || width <= 0 || height <= 0) return;

        if (_writeableBitmap == null || _writeableBitmap.PixelWidth != width || _writeableBitmap.PixelHeight != height)
        {
            ReinitializeBitmap(width, height);
        }

        if (_writeableBitmap == null || _writeableBitmap.PixelWidth != width || _writeableBitmap.PixelHeight != height) return;

        try
        {
            _writeableBitmap.Lock();
            try
            {
                int maxBytes = _writeableBitmap.BackBufferStride * _writeableBitmap.PixelHeight;
                if (pixelData.Length <= maxBytes)
                {
                    Marshal.Copy(pixelData, 0, _writeableBitmap.BackBuffer, pixelData.Length);
                    _writeableBitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
                }
            }
            finally { _writeableBitmap.Unlock(); }
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"UpdateBitmap error: {ex.Message}"); }
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isLocked) return; 
        if (e.ChangedButton == MouseButton.Left) this.DragMove();
    }

    private void ModeButton_Click(object sender, RoutedEventArgs e)
    {
        _inversionMode = (_inversionMode + 1) % 3;
        switch (_inversionMode)
        {
            case 0: ModeButton.Content = "模式：智能文档"; break;
            case 1: ModeButton.Content = "模式：强力全反"; break;
            case 2: ModeButton.Content = "模式：仅反亮度"; break;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _captureTimer.Stop();
        this.Close();
    }

    private void OnResizeDragStarted(object sender, DragStartedEventArgs e) { _isResizing = true; }
    private void OnResizeDragCompleted(object sender, DragCompletedEventArgs e) { _isResizing = false; }

    private void OnTopLeftCornerResize(object sender, DragDeltaEventArgs e)
    {
        if (_isLocked) return;
        var newWidth = Math.Max(this.MinWidth, this.Width - e.HorizontalChange);
        var newHeight = Math.Max(this.MinHeight, this.Height - e.VerticalChange);
        this.Left += this.Width - newWidth;
        this.Top += this.Height - newHeight;
        this.Width = newWidth;
        this.Height = newHeight;
    }

    private void OnTopRightCornerResize(object sender, DragDeltaEventArgs e)
    {
        if (_isLocked) return;
        this.Width = Math.Max(this.MinWidth, this.Width + e.HorizontalChange);
        var newHeight = Math.Max(this.MinHeight, this.Height - e.VerticalChange);
        this.Top += this.Height - newHeight;
        this.Height = newHeight;
    }

    private void OnBottomLeftCornerResize(object sender, DragDeltaEventArgs e)
    {
        if (_isLocked) return;
        var newWidth = Math.Max(this.MinWidth, this.Width - e.HorizontalChange);
        this.Left += this.Width - newWidth;
        this.Width = newWidth;
        this.Height = Math.Max(this.MinHeight, this.Height + e.VerticalChange);
    }

    private void OnBottomRightCornerResize(object sender, DragDeltaEventArgs e)
    {
        if (_isLocked) return;
        this.Width = Math.Max(this.MinWidth, this.Width + e.HorizontalChange);
        this.Height = Math.Max(this.MinHeight, this.Height + e.VerticalChange);
    }

    private void OnTopEdgeResize(object sender, DragDeltaEventArgs e)
    {
        if (_isLocked) return;
        var newHeight = Math.Max(this.MinHeight, this.Height - e.VerticalChange);
        this.Top += this.Height - newHeight;
        this.Height = newHeight;
    }

    private void OnBottomEdgeResize(object sender, DragDeltaEventArgs e)
    {
        if (_isLocked) return;
        this.Height = Math.Max(this.MinHeight, this.Height + e.VerticalChange);
    }

    private void OnLeftEdgeResize(object sender, DragDeltaEventArgs e)
    {
        if (_isLocked) return;
        var newWidth = Math.Max(this.MinWidth, this.Width - e.HorizontalChange);
        this.Left += this.Width - newWidth;
        this.Width = newWidth;
    }

    private void OnRightEdgeResize(object sender, DragDeltaEventArgs e)
    {
        if (_isLocked) return;
        this.Width = Math.Max(this.MinWidth, this.Width + e.HorizontalChange);
    }

    protected override void OnClosed(EventArgs e)
    {
        _captureTimer.Stop();
        _screenCapture.Dispose();
        base.OnClosed(e);
    }
}
```