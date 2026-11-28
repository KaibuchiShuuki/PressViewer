using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PressViewer.Services;
using PressViewer.Services.Interfaces;

namespace PressViewer.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    private readonly IConfigurationService _config;
    private readonly IStartupService _startup;

    public IReadOnlyList<ThemePalette> Themes => ThemeService.Palettes;

    [ObservableProperty]
    private ThemePalette? _selectedTheme;

    partial void OnSelectedThemeChanged(ThemePalette? value)
    {
        if (value is null)
            return;
        ThemeService.Apply(value.Name, IsDarkMode);
        _config.SetTheme(value.Name);
    }

    [ObservableProperty]
    private bool _isDarkMode;

    partial void OnIsDarkModeChanged(bool value)
    {
        ThemeService.Apply(SelectedTheme?.Name ?? _config.GetTheme(), value);
        _config.SetDarkMode(value);
    }

    [ObservableProperty]
    private bool _isStartWithWindows;

    partial void OnIsStartWithWindowsChanged(bool value) => _startup.SetAutoStart(value);

    /// <summary>0 = 最小化到托盘，1 = 直接退出。</summary>
    [ObservableProperty]
    private int _closeActionIndex;

    partial void OnCloseActionIndexChanged(int value) =>
        _config.SetCloseAction(value == 1 ? "Exit" : "Minimize");

    [ObservableProperty]
    private bool _isAutoStartMonitoring;

    partial void OnIsAutoStartMonitoringChanged(bool value)
    {
        _config.SetAutoStartMonitoring(value);
        // 总开关同时落到三个具体采集项上，下次启动按这个状态决定是否装钩子
        _config.SetRecordingAutoStart(value);
        _config.SetKeyRecordingAutoStart(value);
        _config.SetGamepadRecordingAutoStart(value);
    }

    public string DataFolder => AppDataPathHelper.GetAppDataFolder();

    public string VersionText => AppVersion.Display;

    /// <summary>压缩功能的说明 / 结果文案。</summary>
    [ObservableProperty]
    private string _compactionStatus =
        "把今天之前的按键明细合并成「每天每个键按了几次」，数据库会小很多，统计次数不受影响。";

    [ObservableProperty]
    private bool _isCompacting;

    public SettingsViewModel(IConfigurationService config, IStartupService startup)
    {
        _config = config;
        _startup = startup;

        // 直接写字段：走属性 setter 会在每次打开设置页时回写一遍配置
        _selectedTheme =
            System.Linq.Enumerable.FirstOrDefault(
                ThemeService.Palettes,
                p => p.Name == config.GetTheme()
            ) ?? ThemeService.Palettes[0];
        _isDarkMode = config.GetDarkMode();
        _isStartWithWindows = _startup.IsStartWithWindowsEnabled();
        _closeActionIndex = string.Equals(config.GetCloseAction(), "Exit", StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;
        _isAutoStartMonitoring = config.GetAutoStartMonitoring();
    }

    [RelayCommand]
    private void OpenDataFolder() => OpenFolder(DataFolder);

    [RelayCommand]
    private async System.Threading.Tasks.Task CompactHistoryAsync()
    {
        if (IsCompacting)
            return;

        var answer = System.Windows.MessageBox.Show(
            "将把今天之前的按键明细合并为按天汇总：\n\n"
                + "• 只保留「某天某个键按了几次」，具体时间点不再保存\n"
                + "• 各项统计与热力图的次数不受影响\n"
                + "• 已经压缩过的日期不会重复处理\n\n"
                + "是否继续？",
            "压缩历史数据",
            System.Windows.MessageBoxButton.OKCancel,
            System.Windows.MessageBoxImage.Question
        );
        if (answer != System.Windows.MessageBoxResult.OK)
            return;

        IsCompacting = true;
        CompactionStatus = "正在压缩…";
        try
        {
            var result = await System.Threading.Tasks.Task.Run(() =>
                DataCompactionService.Compact(msg =>
                {
                    var app = System.Windows.Application.Current;
                    if (app is null)
                        return;
                    app.Dispatcher.BeginInvoke(() => CompactionStatus = msg);
                })
            );

            CompactionStatus = result.HasChanges
                ? $"已压缩 {result.DaysCompacted} 天，合并 {result.RecordsRemoved:N0} 条明细；"
                    + $"数据文件 {FormatSize(result.BytesBefore)} → {FormatSize(result.BytesAfter)}"
                    + $"（省了 {FormatSize(result.BytesSaved)}）。"
                : result.Message;
        }
        catch (Exception ex)
        {
            CompactionStatus = "压缩失败：" + ex.Message;
        }
        finally
        {
            IsCompacting = false;
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024)
            return $"{bytes / 1024d / 1024 / 1024:0.##} GB";
        if (bytes >= 1024L * 1024)
            return $"{bytes / 1024d / 1024:0.##} MB";
        if (bytes >= 1024)
            return $"{bytes / 1024d:0.#} KB";
        return $"{bytes} B";
    }

    /// <summary>创建桌面快捷方式的结果文案。</summary>
    [ObservableProperty]
    private string _shortcutStatus =
        "在桌面放一个带图标的快捷方式，双击直接打开界面（程序需要管理员权限，会照常提权）。";

    /// <summary>
    /// 生成桌面快捷方式。
    ///
    /// 快捷方式的目标不直接写 PressViewer.exe：那个 exe 的 manifest 是 requireAdministrator，
    /// Explorer 会给这种目标叠一层 UAC 盾徽。改成写 powershell.exe（隐藏窗口）再由它
    /// Start-Process -Verb RunAs 拉起主程序，目标本身不要求提权，图标就干净了。
    /// </summary>
    [RelayCommand]
    private void CreateDesktopShortcut()
    {
        try
        {
            string? desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(desktop) || !Directory.Exists(desktop))
            {
                ShortcutStatus = "找不到桌面目录，创建失败。";
                return;
            }

            string exe =
                Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName
                ?? string.Empty;
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
            {
                ShortcutStatus = "找不到程序自身的位置，创建失败。";
                return;
            }

            string linkPath = Path.Combine(desktop, "PressViewer.lnk");
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null)
            {
                ShortcutStatus = "系统缺少 WScript.Shell，创建失败。";
                return;
            }

            string powershell = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                @"WindowsPowerShell\v1.0\powershell.exe"
            );
            // 单引号里再嵌一层单引号，PowerShell 用 '' 转义
            string escapedExe = exe.Replace("'", "''");
            string arguments =
                "-NoProfile -WindowStyle Hidden -Command \"Start-Process -FilePath '"
                + escapedExe
                + "' -Verb RunAs\"";

            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic shortcut = shell.CreateShortcut(linkPath);
            shortcut.TargetPath = File.Exists(powershell) ? powershell : exe;
            shortcut.Arguments = File.Exists(powershell) ? arguments : string.Empty;
            shortcut.WorkingDirectory = Path.GetDirectoryName(exe) ?? string.Empty;
            shortcut.IconLocation = exe + ",0";
            shortcut.Description = "PressViewer — 输入与使用统计";
            shortcut.WindowStyle = 1;
            shortcut.Save();

            ShortcutStatus = File.Exists(linkPath)
                ? "已在桌面创建快捷方式。"
                : "创建失败，桌面上没有生成文件。";
        }
        catch (Exception ex)
        {
            ShortcutStatus = "创建失败：" + ex.Message;
        }
    }

    [RelayCommand]
    private void OpenLogFolder() => OpenFolder(Path.Combine(DataFolder, "logs"));

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch
        {
            // 打不开资源管理器不算错误
        }
    }
}
