using System.Windows;
using System.Windows.Input;
using PressViewer.ViewModels;

namespace PressViewer.Views;

public partial class HomeView
{
    public HomeView()
    {
        InitializeComponent();
    }

    /// <summary>整块热力图卡片都能点进热力图页，不再只靠「查看详情」。</summary>
    private void OnHeatmapCardClick(object sender, MouseButtonEventArgs e)
    {
        if (Window.GetWindow(this)?.DataContext is MainWindowViewModel vm)
            vm.NavigateCommand.Execute("Heatmap");
    }
}
