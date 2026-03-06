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
