这是一个非常敏锐的发现！你完美触及了 Windows WPF 坐标系在多显示器开发中最容易踩的一个“深坑”。

### 为什么会发生这种情况？

根本原因在于：**WPF 的底层是以“逻辑像素（DIP）”运作的，它的坐标映射比例只受“DPI 缩放”影响，而与显示器的“物理分辨率（1080p/2K/4K）”毫无关系。**

1. **100% 缩放的真相**：在你的 2K 显示器（100% 缩放）上，1 个 WPF 逻辑像素完全等于 1 个屏幕物理像素。也就是说，对截屏 API 而言，正确的映射倍率应该是 **1.0**。
2. **之前的代码 Bug**：在上一版代码中，我将“分辨率倍数”和“DPI 倍数”做了**相乘**。当你选择了 2K（代码内部将其算作 `2560/1920 = 1.33`）和 100% 缩放（`1.0`）时，最终的倍率变成了 `1.33 * 1.0 = 1.33`。
3. **视觉表现**：因为倍率变成了 1.33，软件去截取了比窗口实际面积大 1.33 倍的物理屏幕区域，然后强行塞回原来的窗口里。这就导致了你看到的“画面缩小，但包含了更多内容”的现象。而选 1080p（倍率 1.0）或 2K 75%（`1.33 * 0.75 ≈ 1.0`）刚好歪打正着把倍率抵消回了 1.0，所以反而显示正常。

### 修复方案

我们需要**解除分辨率和 DPI 的强制绑定**。在手动计算坐标时，将 **DPI 缩放比例** 设为绝对主导。只要明确选择了 DPI（比如 100%），程序就严格按照这个 DPI 比例进行 1:1 映射，直接忽略那个会引发错误的分辨率乘数。

以下
以下是修改后的 `InverterOverlayWindow.xaml.cs` 完整实现（仅核心计算逻辑有变，其他文件维持不变即可正常运作）：

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
                    // 自动模式：使用 WPF 内部机制推算，最稳定
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
                        catch { }
                    });
                }
                else
                {
                    // 手动模式
                    double finalScaleX = 1.0;
                    double finalScaleY = 1.0;

                    // 【核心修复】只要设置了 DPI，就以 DPI 为绝对准则，丢弃物理分辨率的错误乘数
                    if (dpiMode != "Auto")
                    {
                        if (dpiMode == "Custom")
                        {
                            finalScaleX = customDpi / 100.0;
                            finalScaleY = customDpi / 100.0;
                        }
                        else if (double.TryParse(dpiMode, out double dpiVal))
                        {
                            finalScaleX = dpiVal / 100.0;
                            finalScaleY = dpiVal / 100.0;
                        }
                    }
                    else if (resMode != "Auto")
                    {
                        // 降级兼容：只有在 DPI 处于 Auto 的情况下，才退回使用分辨率进行粗略倍率推算
                        if (resMode == "1080p") { finalScaleX = 1.0; finalScaleY = 1.0; }
                        else if (resMode == "2K") { finalScaleX = 2560.0 / 1920.0; finalScaleY = 1440.0 / 1080.0; }
                        else if (resMode == "4K") { finalScaleX = 3840.0 / 1920.0; finalScaleY = 2160.0 / 1080.0; }
                        else if (resMode == "Custom") { finalScaleX = customW / 1920.0; finalScaleY = customH / 1080.0; }
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

需要我修改 `MainWindow.xaml.cs` 中的 UI 逻辑，当用户在下拉框中选择具体的“系统 DPI 缩放比例”时，自动禁用并灰掉“显示器分辨率”选项，以避免未来的用户产生同样的误解吗？