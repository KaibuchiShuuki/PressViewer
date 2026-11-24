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

    [ObservableProperty]
    private bool _showThousandsSeparator;

    [ObservableProperty]
    private bool _compactSmallNumbers;

    partial void OnShowThousandsSeparatorChanged(bool value)
    {
        NumberFormatService.Set(value);
        _config.SetShowThousandsSeparator(value);
    }

    partial void OnCompactSmallNumbersChanged(bool value)
    {
        NumberFormatService.SetCompactSmall(value);
        _config.SetCompactSmallNumbers(value);
    }

    public string DataFolder => AppDataPathHelper.GetAppDataFolder();

    public string VersionText => AppVersion.Display;

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
        _showThousandsSeparator = config.GetShowThousandsSeparator();
        _compactSmallNumbers = config.GetCompactSmallNumbers();
        NumberFormatService.SetCompactSmall(_compactSmallNumbers);
    }

    [RelayCommand]
    private void OpenDataFolder() => OpenFolder(DataFolder);

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
