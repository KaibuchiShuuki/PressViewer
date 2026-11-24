namespace PressViewer.Models;

public class AppSettings
{
    public RecordingSettings Recording { get; set; } = new();
    public GeneralSettings General { get; set; } = new();
    public AppearanceSettings Appearance { get; set; } = new();
}

public class RecordingSettings
{
    public bool AutoStartRecording { get; set; } // 鼠标点击录制自动启动
    public bool AutoStartKeyRecording { get; set; } // 键盘按键录制自动启动
    public bool AutoStartGamepadRecording { get; set; } = true; // 手柄按键录制自动启动
}

public class GeneralSettings
{
    public bool StartMinimized { get; set; }
    public double WindowWidth { get; set; } = 1280;
    public double WindowHeight { get; set; } = 720;
    public bool IsLogExpanded { get; set; } = false;

    /// <summary>关闭窗口的行为：Minimize = 隐藏到托盘，Exit = 直接退出。</summary>
    public string CloseAction { get; set; } = "Minimize";

    /// <summary>启动时是否自动开始键盘 / 鼠标 / 手柄监测（设置页的总开关）。</summary>
    public bool AutoStartMonitoring { get; set; } = true;
}

public class AppearanceSettings
{
    /// <summary>主题色名称，见 ThemeService.Palettes。</summary>
    public string Theme { get; set; } = "Pink";

    public bool DarkMode { get; set; }

    /// <summary>数字是否加千分位（1,145）。</summary>
    public bool ShowThousandsSeparator { get; set; } = true;

    /// <summary>小面积按键（滚轮、肩键、ABXY 等）次数超过 999 时用 K/M 缩写。</summary>
    public bool CompactSmallNumbers { get; set; } = true;
}
