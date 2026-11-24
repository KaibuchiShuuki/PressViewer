using System;
using System.ComponentModel;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using PressViewer.Services.Interfaces;

namespace PressViewer.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (App.IsShuttingDown)
        {
            base.OnClosing(e);
            return;
        }

        // 关闭行为由设置页决定：最小化到托盘（继续记录）或直接退出
        var closeAction =
            App.Services?.GetService(typeof(IConfigurationService)) as IConfigurationService;
        if (
            closeAction is not null
            && string.Equals(
                closeAction.GetCloseAction(),
                "Exit",
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            e.Cancel = true;
            App.RequestShutdown();
            return;
        }

        e.Cancel = true;
        Hide();
        base.OnClosing(e);
    }
}
