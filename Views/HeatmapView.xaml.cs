using System.Windows;
using PressViewer.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace PressViewer.Views;

public partial class HeatmapView : UserControl
{
    public HeatmapView()
    {
        InitializeComponent();
    }

    private void OnApplyCustomRange(object sender, RoutedEventArgs e) =>
        (DataContext as HeatmapViewModel)?.ApplyCustomRange();
}
