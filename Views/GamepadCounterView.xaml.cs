using System.Windows.Controls;

// 项目同时启用了 WinForms，消除 UserControl 同名歧义。
using UserControl = System.Windows.Controls.UserControl;

namespace PressViewer.Views;

public partial class GamepadCounterView : UserControl
{
    public GamepadCounterView()
    {
        InitializeComponent();
    }
}
