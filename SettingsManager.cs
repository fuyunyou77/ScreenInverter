using System.IO;
using System.Text.Json;

namespace ScreenInverter;

public class AppSettings
{
    // 快捷键设置
    public int ModifierKey { get; set; } = 0x11;
    public int ActionKey { get; set; } = 0x4C;
    public string ShortcutName { get; set; } = "Ctrl+L";

    // 分辨率适配设置
    public string ResolutionMode { get; set; } = "Auto";
    public double CustomResW { get; set; } = 1920;
    public double CustomResH { get; set; } = 1080;

    // DPI 缩放比例设置 (对应 Windows 系统设置里的 100%, 125%, 150% 等)
    public string DpiScaleMode { get; set; } = "Auto";
    public double CustomDpiScale { get; set; } = 100;

    // 关闭行为设置 (MinimizeToTray 或 Exit)
    public string CloseBehavior { get; set; } = "MinimizeToTray";

    // 遮罩窗口位置和大小记忆
    public double OverlayWindowX { get; set; } = 100;
    public double OverlayWindowY { get; set; } = 100;
    public double OverlayWindowWidth { get; set; } = 500;
    public double OverlayWindowHeight { get; set; } = 400;
}

public static class SettingsManager
{
    private static readonly string ConfigPath = Path.Combine(AppContext.BaseDirectory, "config.json");
    public static AppSettings Current { get; private set; } = new AppSettings();

    public static void Load()
    {
        if (File.Exists(ConfigPath))
        {
            try
            {
                string json = File.ReadAllText(ConfigPath);
                Current = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch { Current = new AppSettings(); }
        }
    }

    public static void Save()
    {
        try
        {
            string json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigPath, json);
        }
        catch { }
    }
}
