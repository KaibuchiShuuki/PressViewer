// Services/Interfaces/IConfigurationService.cs
using PressViewer.Models;

namespace PressViewer.Services.Interfaces;

/// <summary>
/// 应用配置服务：读写持久化设置（记录开关、窗口、外观、关闭行为）。
/// </summary>
public interface IConfigurationService
{
    AppSettings Settings { get; }

    bool GetRecordingAutoStart();
    void SetRecordingAutoStart(bool isRecording);

    bool GetKeyRecordingAutoStart();
    void SetKeyRecordingAutoStart(bool autoStart);

    bool GetGamepadRecordingAutoStart();
    void SetGamepadRecordingAutoStart(bool autoStart);

    // ---------- 窗口 ----------
    double GetWindowWidth();
    void SetWindowWidth(double width);

    double GetWindowHeight();
    void SetWindowHeight(double height);

    // ---------- 日志面板 ----------
    bool GetIsLogExpanded();
    void SetIsLogExpanded(bool expanded);

    // ---------- 外观 ----------
    string GetTheme();
    void SetTheme(string theme);

    bool GetDarkMode();
    void SetDarkMode(bool dark);

    /// <summary>手柄热力图样式：键帽式（Tile）为当前固定用法，手柄形（Form）保留备用，暂无切换入口。</summary>
    string GetGamepadLayout();

    // ---------- 行为 ----------
    /// <summary>Minimize = 关窗口时隐藏到托盘；Exit = 直接退出。</summary>
    string GetCloseAction();
    void SetCloseAction(string action);

    bool GetAutoStartMonitoring();
    void SetAutoStartMonitoring(bool autoStart);

    /// <summary>把内存中的设置写回 appsettings.json。</summary>
    void Save();
}
