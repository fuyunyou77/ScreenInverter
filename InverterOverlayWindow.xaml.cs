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
    private bool _isLocked = false; // 锁定状态（穿透模式）

    public InverterOverlayWindow()
    {
        InitializeComponent();

        _screenCapture = new ScreenCapture();
        _screenCapture.Initialize();

        // 窗口初始化完成后应用 "截图排除" 属性
        this.SourceInitialized += OnSourceInitialized;

        this.Loaded += (s, e) =>
        {
            var source = PresentationSource.FromVisual(this);
            if (source != null)
            {
                _dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                _dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
            }
            ReinitializeBitmap();
        };

        // 初始位置
        this.Left = 100;
        this.Top = 100;
        this.Width = 500;
        this.Height = 400;

        // 事件绑定
        this.MouseLeftButtonDown += OnMouseLeftButtonDown;
        this.MouseLeftButtonUp += OnMouseLeftButtonUp;
        this.MouseMove += OnMouseMove;

        // 定时器：处理截图刷新 + 检测快捷键
        _captureTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33) // ~30FPS，不再闪烁
        };
        _captureTimer.Tick += TimerTick;
        _captureTimer.Start();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(this);
        // 关键：设置此窗口不被系统截图捕获 (CopyFromScreen 会直接"看穿"它)
        // 仅支持 Windows 10 2004 及以上版本
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
        // 1. 检测快捷键 (Ctrl + L) 切换锁定状态
        // VK_CONTROL = 0x11, L = 0x4C
        bool isCtrlPressed = (GetAsyncKeyState(0x11) & 0x8000) != 0;
        bool isLPressed = (GetAsyncKeyState(0x4C) & 0x8000) != 0;

        if (isCtrlPressed && isLPressed)
        {
            ToggleLockState();
            // 简单延时防止连续触发
            await Task.Delay(300);
        }

        // 2. 执行捕获
        await CaptureLoopAsync();
    }

    private void ToggleLockState()
    {
        _isLocked = !_isLocked;
        var helper = new WindowInteropHelper(this);
        int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);

        if (_isLocked)
        {
            // 进入穿透模式：设置 WS_EX_TRANSPARENT
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT);

            // 隐藏 UI 元素，只留滤镜
            MainBorder.BorderThickness = new Thickness(0);
            ControlBar.Visibility = Visibility.Collapsed;
            ResizeThumbsVisibility(Visibility.Collapsed);
            StatusText.Visibility = Visibility.Visible;
            StatusText.Text = "已锁定 (穿透模式)。按 Ctrl+L 解锁";

            // 3秒后隐藏提示
            Task.Delay(3000).ContinueWith(_ => Dispatcher.Invoke(() => StatusText.Visibility = Visibility.Collapsed));
        }
        else
        {
            // 退出穿透模式：移除 WS_EX_TRANSPARENT
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);

            // 恢复 UI
            MainBorder.BorderThickness = new Thickness(2);
            ControlBar.Visibility = Visibility.Visible;
            ResizeThumbsVisibility(Visibility.Visible);
            StatusText.Visibility = Visibility.Collapsed;
        }
    }

    private void ResizeThumbsVisibility(Visibility v)
    {
        // 遍历 Grid 里的所有 Thumb 控件并隐藏/显示
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

        // 既然去掉了 Hide/Show 的闪烁副作用，可以一直刷新
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
                // 获取窗口物理参数
                int x = 0, y = 0, w = 0, h = 0;

                // 使用 Math.Round 进行四舍五入，减少坐标偏移
                Dispatcher.Invoke(() =>
                {
                    x = (int)Math.Round(this.Left * _dpiScaleX);
                    y = (int)Math.Round(this.Top * _dpiScaleY);
                    w = (int)Math.Round(this.ActualWidth * _dpiScaleX);
                    h = (int)Math.Round(this.ActualHeight * _dpiScaleY);
                });

                if (w <= 0 || h <= 0) return;

                // 修正坐标 (VirtualScreen)
                int screenLeft = SystemInformation.VirtualScreen.Left;
                int screenTop = SystemInformation.VirtualScreen.Top;
                int captureX = x + screenLeft;
                int captureY = y + screenTop;

                // --- 核心修改：不再调用 Hide() 和 Show() ---
                // 因为 SetWindowDisplayAffinity 已经让 API "看不见" 这个窗口了

                using var bitmap = _screenCapture.CaptureRegion(captureX, captureY, w, h);
                if (bitmap == null) return;

                var data = bitmap.LockBits(
                    new Rectangle(0, 0, w, h),
                    ImageLockMode.ReadWrite,
                    PixelFormat.Format32bppArgb);

                try
                {
                    int byteCount = w * h * 4;
                    var pixelData = new byte[byteCount];
                    Marshal.Copy(data.Scan0, pixelData, 0, byteCount);

                    // 颜色处理 - 3种模式
                    int mode = 0;
                    Dispatcher.Invoke(() => mode = _inversionMode);

                    if (mode == 0)
                    {
                        // 模式 0: 智能文档模式 (推荐)
                        Inverter.ProcessSmartInvert(pixelData, w, h);
                    }
                    else if (mode == 1)
                    {
                        // 模式 1: 全局简单反转 (夜视仪风格)
                        Inverter.InvertColors(pixelData, w, h);
                    }
                    else
                    {
                        // 模式 2: 仅反转亮度 (保留颜色)
                        Inverter.InvertLightnessOnly(pixelData, w, h);
                    }

                    // 回到 UI 线程更新
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

    private void ReinitializeBitmap()
    {
        var pixelWidth = (int)(this.Width * _dpiScaleX);
        var pixelHeight = (int)(this.Height * _dpiScaleY);

        if (pixelWidth <= 0 || pixelHeight <= 0) return;

        _writeableBitmap = new WriteableBitmap(
            pixelWidth,
            pixelHeight,
            96 * _dpiScaleX,
            96 * _dpiScaleY,
            System.Windows.Media.PixelFormats.Bgra32,
            null);
        CapturedImage.Source = _writeableBitmap;
    }

    private void UpdateBitmap(byte[] pixelData, int width, int height)
    {
        if (_writeableBitmap == null ||
            _writeableBitmap.PixelWidth != width ||
            _writeableBitmap.PixelHeight != height)
        {
            ReinitializeBitmap();
        }

        if (_writeableBitmap == null) return;

        _writeableBitmap.Lock();
        try
        {
            Marshal.Copy(pixelData, 0, _writeableBitmap.BackBuffer, pixelData.Length);
            _writeableBitmap.AddDirtyRect(new Int32Rect(0, 0, width, height));
        }
        finally
        {
            _writeableBitmap.Unlock();
        }
    }

    // --- 交互事件处理 ---

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isLocked) return; // 锁定模式下不处理
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
        // 现在有 3 种模式
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

    // 调整大小事件处理
    private void OnResizeDragStarted(object sender, DragStartedEventArgs e)
    {
        _isResizing = true;
    }

    private void OnResizeDragCompleted(object sender, DragCompletedEventArgs e)
    {
        _isResizing = false;
    }

    // 八个方向的调整大小
    private void OnTopLeftCornerResize(object sender, DragDeltaEventArgs e)
    {
        if (_isLocked) return;
        var newWidth = Math.Max(100, this.Width - e.HorizontalChange);
        var newHeight = Math.Max(100, this.Height - e.VerticalChange);
        this.Width = newWidth;
        this.Height = newHeight;
        this.Left += e.HorizontalChange;
        this.Top += e.VerticalChange;
    }

    private void OnTopRightCornerResize(object sender, DragDeltaEventArgs e)
    {
        if (_isLocked) return;
        var newWidth = Math.Max(100, this.Width + e.HorizontalChange);
        var newHeight = Math.Max(100, this.Height - e.VerticalChange);
        this.Width = newWidth;
        this.Height = newHeight;
        this.Top += e.VerticalChange;
    }

    private void OnBottomLeftCornerResize(object sender, DragDeltaEventArgs e)
    {
        if (_isLocked) return;
        var newWidth = Math.Max(100, this.Width - e.HorizontalChange);
        var newHeight = Math.Max(100, this.Height + e.VerticalChange);
        this.Width = newWidth;
        this.Height = newHeight;
        this.Left += e.HorizontalChange;
    }

    private void OnBottomRightCornerResize(object sender, DragDeltaEventArgs e)
    {
        if (_isLocked) return;
        this.Width = Math.Max(100, this.Width + e.HorizontalChange);
        this.Height = Math.Max(100, this.Height + e.VerticalChange);
    }

    private void OnTopEdgeResize(object sender, DragDeltaEventArgs e)
    {
        if (_isLocked) return;
        var newHeight = Math.Max(100, this.Height - e.VerticalChange);
        this.Height = newHeight;
        this.Top += e.VerticalChange;
    }

    private void OnBottomEdgeResize(object sender, DragDeltaEventArgs e)
    {
        if (_isLocked) return;
        this.Height = Math.Max(100, this.Height + e.VerticalChange);
    }

    private void OnLeftEdgeResize(object sender, DragDeltaEventArgs e)
    {
        if (_isLocked) return;
        var newWidth = Math.Max(100, this.Width - e.HorizontalChange);
        this.Width = newWidth;
        this.Left += e.HorizontalChange;
    }

    private void OnRightEdgeResize(object sender, DragDeltaEventArgs e)
    {
        if (_isLocked) return;
        this.Width = Math.Max(100, this.Width + e.HorizontalChange);
    }

    protected override void OnClosed(EventArgs e)
    {
        _captureTimer.Stop();
        _screenCapture.Dispose();
        base.OnClosed(e);
    }
}