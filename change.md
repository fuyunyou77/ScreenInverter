导致软件在调整窗口大小时崩溃的根本原因是 **非托管内存越界（Access Violation Buffer Overrun）**。

在进行底层图像处理时，`Marshal.Copy` 试图将像素数据写入 `WriteableBitmap` 的后台缓冲区。崩溃的触发链条如下：
1. `CaptureAndUpdateAsync` 截图时，使用的是窗口的**实际渲染尺寸**（`this.ActualWidth`）。
2. 但 `ReinitializeBitmap` 重新创建位图时，读取的却是**布局请求尺寸**（`this.Width`）。
3. 在拖拽改变窗口大小时，受到 WPF 布局舍入（Layout Rounding）、DPI 缩放以及 `MinWidth/MinHeight` 的影响，这两者经常不相等。
4. 当实际截取到的像素数组（`pixelData.Length`）大于 `WriteableBitmap` 按 `this.Width` 分配的后台缓冲区时，`Marshal.Copy` 就会发生内存写越界，这种底层的 C++ 级别内存破坏会绕过 C# 的 `try-catch` 并直接导致进程闪退。

同时，原代码在处理左侧和上侧边缘拖拽时，如果窗口缩小到了 `MinWidth/MinHeight`，继续拖t`，继续拖动鼠标会导致窗口位置错误偏移（漂移 Bug）。

我已经修复了这些内存安全隐患和边界漂移问题。以下是 `InverterOverlayWindow.xaml.cs` 的**完整实现**，你可以直接替换原有代码：

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
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using DragDeltaEventArgs = System.Windows.Controls.Primitives.DragDeltaEventArgs;
using DragStartedEventArgs = System.Windows.Controls.Primitives.DragStartedEventArgs;
using DragCompletedEventArgs = System.Windows.Controls.Primitives.DragCompletedEventArgs;

namespace ScreenInverter;

/// <summary>
/// 屏幕反转覆盖窗口
/// 使用 SetWindowDisplayAffinity 排除截图捕获，避免闪烁
/// 使用 WS_EX_TRANSPARENT 实现鼠标穿透 (Ctrl+L 切换)
/// </summary>
public partial class InverterOverlayWindow : Window
{
    // --- Win32 API 定义 ---
    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    [DllImport("user32.dll")]
    private static extern uint SetWindowDisplayAffinity(IntPtr hwnd, uint dwAffinity);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    // --- 字段 ---
    private readonly ScreenCapture _screenCapture;
    private WriteableBitmap? _writeableBitmap;
    private bool _isDragging;
    private bool _isResizing;
    private Point _dragStartPoint;
    private int _inversionMode;
    private readonly DispatcherTimer _captureTimer;
    private double _dpiScaleX = 1.0;
    private double _dpiScaleY = 1.0;
    private bool _isCapturing;
    private bool _isLocked = false;

    public InverterOverlayWindow()
    {
        InitializeComponent();

        _screenCapture = new ScreenCapture();
        _screenCapture.Initialize();

        this.SourceInitialized += OnSourceInitialized;

        this.Loaded += (s, e) =>
        {
            var source = PresentationSource.FromVisual(this);
            if (source != null)
            {
                _dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                _dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
            }
            // 移除原本的无参 ReinitializeBitmap() 调用，改在第一帧到达时按实际像素懒加载初始化，避免早期尺寸不符
        };

        this.Left = 100;
        this.Top = 100;
        this.Width = 500;
        this.Height = 400;

        this.MouseLeftButtonDown += OnMouseLeftButtonDown;
        this.MouseLeftButtonUp += OnMouseLeftButtonUp;
        this.MouseMove += OnMouseMove;

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
        bool isCtrlPressed = (GetAsyncKeyState(0x11) & 0x8000) != 0;
        bool isLPressed = (GetAsyncKeyState(0x4C) & 0x8000) != 0;

        if (isCtrlPressed && isLPressed)
        {
            ToggleLockState();
            await Task.Delay(300);
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
            StatusText.Text = "已锁定 (穿透模式)。按 Ctrl+L 解锁";

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
        if (_isDragging || _isResizing || _isCapturing) return;
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
                int x = 0, y = 0, w = 0, h = 0;

                Dispatcher.Invoke(() =>
                {
                    x = (int)Math.Round(this.Left * _dpiScaleX);
                    y = (int)Math.Round(this.Top * _dpiScaleY);
                    w = (int)Math.Round(this.ActualWidth * _dpiScaleX);
                    h = (int)Math.Round(this.ActualHeight * _dpiScaleY);
                });

                if (w <= 0 || h <= 0) return;

                int screenLeft = SystemInformation.VirtualScreen.Left;
                int screenTop = SystemInformation.VirtualScreen.Top;
                int captureX = x + screenLeft;
                int captureY = y + screenTop;

                using var bitmap = _screenCapture.CaptureRegion(captureX, captureY, w, h);
                if (bitmap == null) return;

                var data = bitmap.LockBits(
                    new Rectangle(0, 0, w, h),
                    ImageLockMode.ReadWrite,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                try
                {
                    int byteCount = w * h * 4;
                    var pixelData = new byte[byteCount];
                    Marshal.Copy(data.Scan0, pixelData, 0, byteCount);

                    int mode = 0;
                    Dispatcher.Invoke(() => mode = _inversionMode);

                    if (mode == 0)
                        Inverter.ProcessSmartInvert(pixelData, w, h);
                    else if (mode == 1)
                        Inverter.InvertColors(pixelData, w, h);
                    else
                        Inverter.InvertLightnessOnly(pixelData, w, h);

                    // 将宽度和高度传递给 UI 线程，以确保内存严格对齐
                    Dispatcher.Invoke(() => UpdateBitmap(pixelData, w, h));
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

    // 重构方法：直接接受准确的 width 和 height，不再依赖 this.Width (避免尺寸不同步)
    private void ReinitializeBitmap(int width, int height)
    {
        if (_isResizing || width <= 0 || height <= 0) return;

        try
        {
            _writeableBitmap = new WriteableBitmap(
                width,
                height,
                96 * _dpiScaleX,
                96 * _dpiScaleY,
                System.Windows.Media.PixelFormats.Bgra32,
                null);

            if (CapturedImage != null)
            {
                CapturedImage.Source = _writeableBitmap;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"ReinitializeBitmap error: {ex.Message}");
        }
    }

    private void UpdateBitmap(byte[] pixelData, int width, int height)
    {
        if (_isResizing) return;
        if (width <= 0 || height <= 0) return;

        if (_writeableBitmap == null ||
            _writeableBitmap.PixelWidth != width ||
            _writeableBitmap.PixelHeight != height)
        {
            ReinitializeBitmap(width, height);
        }

        if (_writeableBitmap == null || _writeableBitmap.PixelWidth != width || _writeableBitmap.PixelHeight != height) return;

        try
        {
            _writeableBitmap.Lock();
            try
            {
                // [关键安全检查]：确保要复制的像素数据长度不超过 BackBuffer 的容量，杜绝引发奔溃
                int maxBytes = _writeableBitmap.BackBufferStride * _writeableBitmap.PixelHeight;
                if (pixelData.Length <= maxBytes)
                {
                    Marshal.Copy(pixelData, 0, _writeableBitmap.BackBuffer, pixelData.Length);
                    _writeableBitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
                }
            }
            finally
            {
                _writeableBitmap.Unlock();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"UpdateBitmap error: {ex.Message}");
        }
    }

    // --- 交互事件处理 ---

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isLocked) return; 
        _isDragging = true;
        _dragStartPoint = e.GetPosition(this);
        this.CaptureMouse();
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            this.ReleaseMouseCapture();
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging && !_isLocked)
        {
            var currentPoint = e.GetPosition(this);
            this.Left += currentPoint.X - _dragStartPoint.X;
            this.Top += currentPoint.Y - _dragStartPoint.Y;
        }
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

    // --- 调整大小事件处理 (已修复受 MinWidth/MinHeight 约束导致的漂移 Bug) ---
    private void OnResizeDragStarted(object sender, DragStartedEventArgs e)
    {
        _isResizing = true;
    }

    private void OnResizeDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _isResizing = false;
    }

    private void OnTopLeftCornerResize(object sender, DragDeltaEventArgs e)
    {
        if (_isLocked) return;
        var newWidth = Math.Max(this.MinWidth, this.Width - e.HorizontalChange);
        var newHeight = Math.Max(this.MinHeight, this.Height - e.VerticalChange);
        double actualChangeX = this.Width - newWidth;
        double actualChangeY = this.Height - newHeight;
        
        this.Width = newWidth;
        this.Height = newHeight;
        this.Left += actualChangeX;
        this.Top += actualChangeY;
    }

    private void OnTopRightCornerResize(object sender, DragDeltaEventArgs e)
    {
        if (_isLocked) return;
        this.Width = Math.Max(this.MinWidth, this.Width + e.HorizontalChange);
        var newHeight = Math.Max(this.MinHeight, this.Height - e.VerticalChange);
        double actualChangeY = this.Height - newHeight;
        
        this.Height = newHeight;
        this.Top += actualChangeY;
    }

    private void OnBottomLeftCornerResize(object sender, DragDeltaEventArgs e)
    {
        if (_isLocked) return;
        var newWidth = Math.Max(this.MinWidth, this.Width - e.HorizontalChange);
        double actualChangeX = this.Width - newWidth;
        
        this.Width = newWidth;
        this.Height = Math.Max(this.MinHeight, this.Height + e.VerticalChange);
        this.Left += actualChangeX;
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
        double actualChangeY = this.Height - newHeight;
        
        this.Height = newHeight;
        this.Top += actualChangeY;
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
        double actualChangeX = this.Width - newWidth;
        
        this.Width = newWidth;
        this.Left += actualChangeX;
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

替换后编译运行，如果之后在处理 C++ 或底层硬件交互（比如你开发 Qt 客户端时的图像渲染）遇到类似的内存冲突，排查思路也基本相同。需要我继续协助优化这里并行的帧处理性能吗？