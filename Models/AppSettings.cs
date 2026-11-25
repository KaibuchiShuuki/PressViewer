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
    public string Theme { get; set; } = "MintSoda";

    public bool DarkMode { get; set; }

    /// <summary>
    /// 手柄热力图样式：Form = 手柄形（带手柄轮廓、写实质感），
    /// Tile = 键帽式（把按键排成大键帽分区）。见 GamepadLayoutService。
    /// 1.1.1 起固定用 Tile、设置页不再提供切换，这里只保留字段给以后恢复用。
    /// </summary>
    public string GamepadLayout { get; set; } = "Tile";
}
