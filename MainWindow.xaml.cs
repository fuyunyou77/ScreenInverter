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
