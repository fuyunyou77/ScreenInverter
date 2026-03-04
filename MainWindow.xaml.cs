using System.Windows;

namespace ScreenInverter;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private void StartButton_Click(object sender, RoutedEventArgs e)
    {
        var overlay = new InverterOverlayWindow();
        overlay.Show();

        // 隐藏主窗口
        this.Hide();

        // 当覆盖窗口关闭时，退出应用
        overlay.Closed += (s, args) => this.Close();
    }
}
