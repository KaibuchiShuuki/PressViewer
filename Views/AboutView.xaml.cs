using System.Diagnostics;
using System.Windows.Controls;
using System.Windows.Navigation;

namespace PressViewer.Views;

public partial class AboutView
{
    public AboutView()
    {
        InitializeComponent();
    }

    /// <summary>用系统浏览器打开仓库地址；链接颜色保持主题的次要文字色，不做悬停/点击变色。</summary>
    private void OnRepoLinkNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch
        {
            // 打不开浏览器不应影响界面
        }

        e.Handled = true;
    }
}
