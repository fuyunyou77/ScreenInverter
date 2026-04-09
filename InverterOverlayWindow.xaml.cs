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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private const int WM_NCHITTEST = 0x0084;
    private const int HTTRANSPARENT = -1;
    private const int HTCLIENT = 1;

    private readonly ScreenCapture _screenCapture;
    private WriteableBitmap? _writeableBitmap;
    private bool _isResizing;
    private int _inversionMode = 1; // 默认柔和全反
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

        // 从设置读取上次保存的窗口位置和大小，如果没有保存则使用默认值
        this.Left = SettingsManager.Current.OverlayWindowX;
        this.Top = SettingsManager.Current.OverlayWindowY;
        this.Width = SettingsManager.Current.OverlayWindowWidth;
        this.Height = SettingsManager.Current.OverlayWindowHeight;

        this.MouseLeftButtonDown += OnMouseLeftButtonDown;

        // 初始化模式按钮文本为默认值
        ModeButton.Content = "模式：柔和全反";

        // 初始化 DPI 缩放下拉框
        InitDpiScaleComboBox();

        _captureTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _captureTimer.Tick += TimerTick;
        _captureTimer.Start();
    }

    private void InitDpiScaleComboBox()
    {
        // 根据当前设置选中对应的 DPI 选项
        string dpiMode = SettingsManager.Current.DpiScaleMode;
        foreach (ComboBoxItem item in CmbDpiScale.Items)
        {
            if (item.Tag.ToString() == dpiMode)
            {
                CmbDpiScale.SelectedItem = item;
                return;
            }
        }
        // 如果没有匹配，默认选自动
        CmbDpiScale.SelectedIndex = 0;
    }

    private void CmbDpiScale_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbDpiScale.SelectedItem is ComboBoxItem selectedItem)
        {
            string dpiMode = selectedItem.Tag.ToString() ?? "Auto";
            SettingsManager.Current.DpiScaleMode = dpiMode;
            SettingsManager.Save();
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var helper = new WindowInteropHelper(this);

        // Hook WndProc for precise hit testing
        HwndSource source = HwndSource.FromHwnd(helper.Handle);
        source?.AddHook(WndProc);

        try
        {
            SetWindowDisplayAffinity(helper.Handle, WDA_EXCLUDEFROMCAPTURE);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to set display affinity: {ex.Message}");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        return IntPtr.Zero;
    }

    private DispatcherTimer? _hitTestTimer;

    private void StartHitTestTimer()
    {
        if (_hitTestTimer == null)
        {
            _hitTestTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _hitTestTimer.Tick += (s, e) =>
            {
                if (!_isLocked) return;

                // 获取鼠标全局坐标
                GetCursorPos(out POINT pt);
                Point screenPoint = new Point(pt.X, pt.Y);

                // 获取按钮在屏幕上的边界
                Point buttonTopLeft = LockButton.PointToScreen(new Point(0, 0));
                Point buttonBottomRight = LockButton.PointToScreen(new Point(LockButton.ActualWidth, LockButton.ActualHeight));

                bool isMouseOverButton = screenPoint.X >= buttonTopLeft.X && screenPoint.X <= buttonBottomRight.X &&
                                         screenPoint.Y >= buttonTopLeft.Y && screenPoint.Y <= buttonBottomRight.Y;

                var helper = new WindowInteropHelper(this);
                int exStyle = GetWindowLong(helper.Handle, GWL_EXSTYLE);

                if (isMouseOverButton)
                {
                    // 鼠标在按钮上，移除穿透属性，允许点击
                    if ((exStyle & WS_EX_TRANSPARENT) != 0)
                    {
                        SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);
                    }
                }
                else
                {
                    // 鼠标不在按钮上，恢复穿透属性
                    if ((exStyle & WS_EX_TRANSPARENT) == 0)
                    {
                        SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT);
                    }
                }
            };
        }
        _hitTestTimer.Start();
    }

    private void StopHitTestTimer()
    {
        _hitTestTimer?.Stop();
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
            // 锁定初期，先将窗口设为穿透
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT);

            MainBorder.BorderThickness = new Thickness(0);
            ControlBar.Visibility = Visibility.Collapsed;
            ResizeThumbsVisibility(Visibility.Collapsed);
            StatusText.Visibility = Visibility.Visible;
            StatusText.Text = $"已锁定。按 {SettingsManager.Current.ShortcutName} 解锁";

            LockIcon.Text = "🔒";
            LockButton.ToolTip = "点击解锁\n拖动可移动此按钮和窗口";

            Task.Delay(3000).ContinueWith(_ => Dispatcher.Invoke(() => StatusText.Visibility = Visibility.Collapsed));

            // 开启鼠标位置轮询
            StartHitTestTimer();
        }
        else
        {
            // 解锁时，停止轮询并确保移除穿透属性
            StopHitTestTimer();
            SetWindowLong(helper.Handle, GWL_EXSTYLE, exStyle & ~WS_EX_TRANSPARENT);

            MainBorder.BorderThickness = new Thickness(2);
            ControlBar.Visibility = Visibility.Visible;
            ResizeThumbsVisibility(Visibility.Visible);
            StatusText.Visibility = Visibility.Collapsed;

            LockIcon.Text = "🔓";
            LockButton.ToolTip = "点击锁定";
        }
    }

    private void LockButton_Click(object sender, RoutedEventArgs e)
    {
        ToggleLockState();
        e.Handled = true; // 防止事件冒泡干扰
    }

    private Point _startDragPoint;
    private bool _isDraggingButton;

    private void LockButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            _startDragPoint = e.GetPosition(this);
            _isDraggingButton = true;
            LockButton.CaptureMouse();
        }
    }

    private void LockButton_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDraggingButton && e.LeftButton == MouseButtonState.Pressed)
        {
            Point currentPoint = e.GetPosition(this);
            if (Math.Abs(currentPoint.X - _startDragPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                Math.Abs(currentPoint.Y - _startDragPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
            {
                // 如果确认为拖动，则释放鼠标捕获并启动窗口拖动
                _isDraggingButton = false;
                LockButton.ReleaseMouseCapture();
                this.DragMove();
            }
        }
    }

    private void LockButton_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingButton)
        {
            // 如果鼠标抬起时还在处于“准备拖动但没真正拖动”的状态，那说明这是一次纯点击
            _isDraggingButton = false;
            LockButton.ReleaseMouseCapture();

            // 触发解锁/锁定逻辑
            ToggleLockState();
            e.Handled = true;
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

                Dispatcher.Invoke(() =>
                {
                    resMode = SettingsManager.Current.ResolutionMode;
                    dpiMode = SettingsManager.Current.DpiScaleMode;
                    customW = SettingsManager.Current.CustomResW;
                    customH = SettingsManager.Current.CustomResH;
                    customDpi = SettingsManager.Current.CustomDpiScale;
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
        var effect = CapturedImage.Effect as InvertEffect;
        if (effect == null) return;

        switch (_inversionMode)
        {
            case 0:
                ModeButton.Content = "模式：智能暗色";
                // 逐像素亮度阈值反转
                effect.Mode = 0.0;
                effect.DarkThreshold = 0.35;
                effect.BrightThreshold = 0.70;
                effect.TargetDarkLevel = 0.12;
                break;
            case 1:
                ModeButton.Content = "模式：柔和全反";
                effect.Mode = 1.0;
                break;
            case 2:
                ModeButton.Content = "模式：强力全反";
                effect.Mode = 2.0;
                break;
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
        // 在关闭时保存窗口的位置和大小
        SettingsManager.Current.OverlayWindowX = this.Left;
        SettingsManager.Current.OverlayWindowY = this.Top;
        SettingsManager.Current.OverlayWindowWidth = this.Width;
        SettingsManager.Current.OverlayWindowHeight = this.Height;
        SettingsManager.Save();

        _captureTimer.Stop();
        _screenCapture.Dispose();
        base.OnClosed(e);
    }
}
