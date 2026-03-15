using System;
using System.IO;
using System.Threading;
using System.Windows;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace ScreenInverter;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _notifyIcon;
    private MainWindow? _mainWindow;
    private InverterOverlayWindow? _overlayWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // --- 全局异常捕获 ---
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;

        // 加载配置
        SettingsManager.Load();

        // 初始化系统托盘图标
        InitializeTrayIcon();

        // 启动时默认打开设置窗口
        ShowMainWindow();
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception, "DispatcherUnhandledException");
        e.Handled = true;
        System.Windows.MessageBox.Show($"软件发生崩溃 (UI Thread):\n{e.Exception.Message}\n请查看程序目录下的 crash_log.txt", "崩溃提示", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogCrash(ex, "AppDomain UnhandledException");
            System.Windows.MessageBox.Show($"软件发生致命崩溃 (Background Thread):\n{ex.Message}\n请查看程序目录下的 crash_log.txt", "致命错误", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void TaskScheduler_UnobservedTaskException(object? sender, System.Threading.Tasks.UnobservedTaskExceptionEventArgs e)
    {
        if (e.Exception != null)
        {
            LogCrash(e.Exception, "UnobservedTaskException");
            e.SetObserved();
        }
    }

    private void LogCrash(Exception ex, string source)
    {
        try
        {
            File.AppendAllText("crash_log.txt", $"[{DateTime.Now}] [{source}]\n{ex}\n\n");
        }
        catch { }
    }

    private void InitializeTrayIcon()
    {
        // 从 exe 自身提取图标（避免依赖外部文件）
        using var process = System.Diagnostics.Process.GetCurrentProcess();
        var exePath = process.MainModule?.FileName ?? "";
        var exeIcon = string.IsNullOrEmpty(exePath) ? null : Drawing.Icon.ExtractAssociatedIcon(exePath);

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = exeIcon ?? Drawing.SystemIcons.WinLogo,
            Visible = true,
            Text = "Screen Inverter (双击打开设置)"
        };

        _notifyIcon.DoubleClick += (s, args) => ShowMainWindow();

        // 托盘右键菜单
        var contextMenu = new Forms.ContextMenuStrip();
        contextMenu.Items.Add("开启/关闭遮罩", null, (s, args) => ToggleOverlay());
        contextMenu.Items.Add("设置...", null, (s, args) => ShowMainWindow());
        contextMenu.Items.Add("-");
        contextMenu.Items.Add("完全退出", null, (s, args) => ExitApplication());

        _notifyIcon.ContextMenuStrip = contextMenu;
    }

    public void ShowMainWindow()
    {
        if (_mainWindow == null)
        {
            _mainWindow = new MainWindow();
        }
        _mainWindow.Show();
        _mainWindow.Activate();
    }

    public void ToggleOverlay()
    {
        if (_overlayWindow == null)
        {
            _overlayWindow = new InverterOverlayWindow();
            _overlayWindow.Closed += (s, args) => _overlayWindow = null;
            _overlayWindow.Show();
        }
        else
        {
            _overlayWindow.Close();
        }
    }

    private void ExitApplication()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
        System.Windows.Application.Current.Shutdown();
    }

    // 处理主窗口关闭事件，根据配置决定是退出还是最小化到托盘
    public void HandleMainWindowClosing(MainWindow window, System.ComponentModel.CancelEventArgs e)
    {
        if (SettingsManager.Current.CloseBehavior == "Exit")
        {
            // 直接退出程序
            ExitApplication();
        }
        else
        {
            // 最小化到托盘（隐藏窗口）
            e.Cancel = true;
            window.Hide();
        }
    }
}
