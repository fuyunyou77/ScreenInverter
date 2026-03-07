using System;
using System.Windows;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace ScreenInverter;

public partial class App : System.Windows.Application
{
    private Forms.NotifyIcon? _notifyIcon;
    private MainWindow? _mainWindow;
    private InverterOverlayWindow? _overlayWindow;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 加载配置
        SettingsManager.Load();

        // 初始化系统托盘图标
        InitializeTrayIcon();

        // 启动时默认打开设置窗口
        ShowMainWindow();
    }

    private void InitializeTrayIcon()
    {
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = Drawing.SystemIcons.Application,
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
