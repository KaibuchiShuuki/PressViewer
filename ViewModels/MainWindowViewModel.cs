using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using PressViewer.Services;
using PressViewer.Services.Interfaces;

namespace PressViewer.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IStartupService _startupService;
    private readonly IConfigurationService _configService;
    private readonly HomeViewModel _homeViewModel;

    [ObservableProperty]
    private double _windowWidth;

    partial void OnWindowWidthChanged(double value) => _configService.SetWindowWidth(value);

    [ObservableProperty]
    private double _windowHeight;

    partial void OnWindowHeightChanged(double value) => _configService.SetWindowHeight(value);

    [ObservableProperty]
    private ViewModelBase? _currentViewModel;

    /// <summary>当前导航页标识，与侧栏 ListBox 双向绑定。</summary>
    [ObservableProperty]
    private string _currentPage = "Home";

    private bool _syncingPage;

    partial void OnCurrentPageChanged(string value)
    {
        if (!_syncingPage)
            Navigate(value);
    }

    [ObservableProperty]
    private bool _isStartWithWindowsEnabled;

    public string VersionText => AppVersion.SemVer;

    public MainWindowViewModel(
        IStartupService startupService,
        IConfigurationService configService,
        HomeViewModel homeViewModel
    )
    {
        _startupService = startupService;
        _configService = configService;
        WindowWidth = _configService.GetWindowWidth();
        WindowHeight = _configService.GetWindowHeight();
        CurrentViewModel = homeViewModel;
        _homeViewModel = homeViewModel;
        // 直接写 backing field：走属性 setter 会触发 OnIsStartWithWindowsEnabledChanged，
        // 每次启动都重新注册一遍计划任务（还会多打一条「已升级」日志）。
        _isStartWithWindowsEnabled = _startupService.IsStartWithWindowsEnabled();
    }

    partial void OnIsStartWithWindowsEnabledChanged(bool value)
    {
        _startupService.SetAutoStart(value);
    }

    [RelayCommand]
    public void Navigate(string pageName)
    {
        if (!string.Equals(CurrentPage, pageName, StringComparison.Ordinal))
        {
            _syncingPage = true;
            try
            {
                CurrentPage = pageName;
            }
            finally
            {
                _syncingPage = false;
            }
        }

        CurrentViewModel = pageName switch
        {
            "Home" => _homeViewModel,
            "Heatmap" => App.Services.GetRequiredService<HeatmapViewModel>(),
            "ClickCounter" => App.Services.GetRequiredService<ClickCounterViewModel>(),
            "KeyCounter" => App.Services.GetRequiredService<KeyCounterViewModel>(),
            "GamepadCounter" => App.Services.GetRequiredService<GamepadCounterViewModel>(),
            "Usage" => App.Services.GetRequiredService<UsageViewModel>(),
            "AppUsage" => App.Services.GetRequiredService<AppUsageViewModel>(),
            "Logs" => App.Services.GetRequiredService<LogViewerViewModel>(),
            "Settings" => App.Services.GetRequiredService<SettingsViewModel>(),
            "About" => App.Services.GetRequiredService<AboutViewModel>(),
            _ => CurrentViewModel,
        };
    }
}
