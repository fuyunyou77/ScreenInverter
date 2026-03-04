using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using DragDeltaEventArgs = System.Windows.Controls.Primitives.DragDeltaEventArgs;

namespace ScreenInverter;

/// <summary>
/// 屏幕反转覆盖窗口 - 支持拖动、调整大小、实时颜色反转
/// 拖动时暂停更新以避免闪烁，拖动/调整大小结束后立即更新
/// </summary>
public partial class InverterOverlayWindow : Window
{
    private readonly ScreenCapture _screenCapture;
    private WriteableBitmap? _writeableBitmap;
    private bool _isDragging;
    private Point _dragStartPoint;
    private int _inversionMode; // 0 = 全反转，1 = 仅亮度
    private readonly DispatcherTimer _captureTimer;
    private double _dpiScaleX = 1.0;
    private double _dpiScaleY = 1.0;
    private bool _isCapturing;
    private double _lastLeft;
    private double _lastTop;
    private double _lastWidth;
    private double _lastHeight;
    private bool _needsRecapture;

    public InverterOverlayWindow()
    {
        InitializeComponent();

        _screenCapture = new ScreenCapture();
        _screenCapture.Initialize();

        // 在窗口加载后获取 DPI
        this.Loaded += async (s, e) =>
        {
            var source = PresentationSource.FromVisual(this);
            if (source != null)
            {
                _dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                _dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
            }
            // 初始捕获
            await CaptureAndUpdateAsync();
        };

        // 设置窗口位置到屏幕中央
        this.Left = 100;
        this.Top = 100;
        this.Width = 500;
        this.Height = 400;

        // 初始化位图
        ReinitializeBitmap();

        // 绑定事件
        this.MouseLeftButtonDown += OnMouseLeftButtonDown;
        this.MouseLeftButtonUp += OnMouseLeftButtonUp;
        this.MouseMove += OnMouseMove;
        this.SizeChanged += OnSizeChanged;
        this.LocationChanged += OnLocationChanged;

        // 设置定时器捕获 - 静止时定期更新
        _captureTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _captureTimer.Tick += async (s, e) => await CaptureLoopAsync();
        _captureTimer.Start();
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        // 位置改变时标记需要重新捕获
        _needsRecapture = true;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        ReinitializeBitmap();
        _needsRecapture = true;
    }

    private async Task CaptureLoopAsync()
    {
        // 拖动时或正在捕获时不更新
        if (_isDragging || _isCapturing) return;

        // 如果需要重新捕获（拖动/调整大小结束后）
        if (_needsRecapture)
        {
            _needsRecapture = false;
            _lastLeft = this.Left;
            _lastTop = this.Top;
            _lastWidth = this.ActualWidth;
            _lastHeight = this.ActualHeight;
            await CaptureAndUpdateAsync();
            return;
        }

        // 检查位置/大小是否有变化（被动更新）
        var changed = (_lastLeft != this.Left || _lastTop != this.Top ||
                       _lastWidth != this.ActualWidth || _lastHeight != this.ActualHeight);

        if (changed)
        {
            _lastLeft = this.Left;
            _lastTop = this.Top;
            _lastWidth = this.ActualWidth;
            _lastHeight = this.ActualHeight;
            await CaptureAndUpdateAsync();
        }
    }

    private async Task CaptureAndUpdateAsync()
    {
        if (_isCapturing || !this.IsVisible) return;

        _isCapturing = true;

        try
        {
            // 获取窗口在屏幕上的位置（物理像素）
            var windowLeftPx = (int)(this.Left * _dpiScaleX);
            var windowTopPx = (int)(this.Top * _dpiScaleY);
            var windowWidthPx = (int)(this.ActualWidth * _dpiScaleX);
            var windowHeightPx = (int)(this.ActualHeight * _dpiScaleY);

            if (windowWidthPx <= 0 || windowHeightPx <= 0) return;

            // 获取主显示器的位置偏移
            var screenLeft = (int)SystemInformation.VirtualScreen.Left;
            var screenTop = (int)SystemInformation.VirtualScreen.Top;

            // 计算捕获区域
            var captureX = windowLeftPx + screenLeft;
            var captureY = windowTopPx + screenTop;

            // 临时隐藏窗口进行捕获
            this.Hide();

            // 等待窗口完全隐藏
            await Task.Delay(16);

            try
            {
                var bitmap = _screenCapture.CaptureRegion(captureX, captureY, windowWidthPx, windowHeightPx);
                if (bitmap == null) return;

                try
                {
                    // 锁定像素
                    var data = bitmap.LockBits(
                        new System.Drawing.Rectangle(0, 0, windowWidthPx, windowHeightPx),
                        ImageLockMode.ReadWrite,
                        System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                    try
                    {
                        var pixelData = new byte[windowWidthPx * windowHeightPx * 4];
                        System.Runtime.InteropServices.Marshal.Copy(data.Scan0, pixelData, 0, pixelData.Length);

                        // 应用颜色反转
                        if (_inversionMode == 0)
                            Inverter.InvertColors(pixelData, windowWidthPx, windowHeightPx);
                        else
                            Inverter.InvertLightnessOnly(pixelData, windowWidthPx, windowHeightPx);

                        // 更新位图
                        UpdateBitmap(pixelData, windowWidthPx, windowHeightPx);
                    }
                    finally
                    {
                        bitmap.UnlockBits(data);
                    }
                }
                finally
                {
                    bitmap.Dispose();
                }
            }
            finally
            {
                // 恢复窗口显示
                this.Show();
                this.Topmost = true;
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

        _writeableBitmap?.Lock();
        try
        {
            _writeableBitmap?.WritePixels(
                new Int32Rect(0, 0, width, height),
                pixelData,
                width * 4,
                0);
        }
        finally
        {
            _writeableBitmap?.Unlock();
        }
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 只在点击窗口内部时开始拖动（Thumb 控件会自动处理边缘拖动）
        _isDragging = true;
        _dragStartPoint = e.GetPosition(this);
        this.CaptureMouse();
    }

    private async void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            this.ReleaseMouseCapture();
            // 拖动结束后立即更新
            _needsRecapture = true;
            await CaptureLoopAsync();
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_isDragging)
        {
            var currentPoint = e.GetPosition(this);
            this.Left += currentPoint.X - _dragStartPoint.X;
            this.Top += currentPoint.Y - _dragStartPoint.Y;
        }
        // 如果正在调整大小，不处理拖动逻辑
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // 不再处理滚轮调整大小，使用窗口自带的 ResizeGrip
    }

    private void ModeButton_Click(object sender, RoutedEventArgs e)
    {
        _inversionMode = (_inversionMode + 1) % 2;
        ModeButton.Content = _inversionMode == 0 ? "模式：全反转" : "模式：亮度反转";
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        _captureTimer.Stop();
        this.Close();
    }

    // 调整大小事件处理 - 八个方向
    private void OnTopLeftCornerResize(object sender, DragDeltaEventArgs e)
    {
        var newWidth = Math.Max(100, this.Width - e.HorizontalChange);
        var newHeight = Math.Max(100, this.Height - e.VerticalChange);
        this.Width = newWidth;
        this.Height = newHeight;
        this.Left += e.HorizontalChange;
        this.Top += e.VerticalChange;
    }

    private void OnTopRightCornerResize(object sender, DragDeltaEventArgs e)
    {
        var newWidth = Math.Max(100, this.Width + e.HorizontalChange);
        var newHeight = Math.Max(100, this.Height - e.VerticalChange);
        this.Width = newWidth;
        this.Height = newHeight;
        this.Top += e.VerticalChange;
    }

    private void OnBottomLeftCornerResize(object sender, DragDeltaEventArgs e)
    {
        var newWidth = Math.Max(100, this.Width - e.HorizontalChange);
        var newHeight = Math.Max(100, this.Height + e.VerticalChange);
        this.Width = newWidth;
        this.Height = newHeight;
        this.Left += e.HorizontalChange;
    }

    private void OnBottomRightCornerResize(object sender, DragDeltaEventArgs e)
    {
        this.Width = Math.Max(100, this.Width + e.HorizontalChange);
        this.Height = Math.Max(100, this.Height + e.VerticalChange);
    }

    private void OnTopEdgeResize(object sender, DragDeltaEventArgs e)
    {
        var newHeight = Math.Max(100, this.Height - e.VerticalChange);
        this.Height = newHeight;
        this.Top += e.VerticalChange;
    }

    private void OnBottomEdgeResize(object sender, DragDeltaEventArgs e)
    {
        this.Height = Math.Max(100, this.Height + e.VerticalChange);
    }

    private void OnLeftEdgeResize(object sender, DragDeltaEventArgs e)
    {
        var newWidth = Math.Max(100, this.Width - e.HorizontalChange);
        this.Width = newWidth;
        this.Left += e.HorizontalChange;
    }

    private void OnRightEdgeResize(object sender, DragDeltaEventArgs e)
    {
        this.Width = Math.Max(100, this.Width + e.HorizontalChange);
    }

    protected override void OnClosed(EventArgs e)
    {
        _captureTimer.Stop();
        _screenCapture.Dispose();
        base.OnClosed(e);
    }
}
