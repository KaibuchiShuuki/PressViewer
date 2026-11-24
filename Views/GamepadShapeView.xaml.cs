using Size = System.Windows.Size;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PressViewer.Models;
using PressViewer.Services;
using Rectangle = System.Windows.Shapes.Rectangle;
using PressViewer.ViewModels;
using Color = System.Windows.Media.Color;
using Ellipse = System.Windows.Shapes.Ellipse;
using UserControl = System.Windows.Controls.UserControl;

namespace PressViewer.Views;

/// <summary>
/// Xbox 布局手柄图形：双摇杆、十字、ABXY、肩键/扳机、View/Menu/Guide。
/// 字母常显，次数写在按键外侧或胶囊右端；0 次不着色、不显示次数。
/// </summary>
public partial class GamepadShapeView : UserControl
{
    private const string BodyData =
        "M 122,78 C 190,46 450,46 518,78 C 568,100 600,150 590,212 "
        + "C 581,262 548,300 508,310 C 478,318 448,300 420,280 "
        + "C 395,263 364,256 320,256 C 276,256 245,263 220,280 "
        + "C 192,300 162,318 132,310 C 92,300 59,262 50,212 "
        + "C 40,150 72,100 122,78 Z";

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable<KeyCountItem>),
        typeof(GamepadShapeView),
        new PropertyMetadata(null, (_, __) => { })
    );

    private bool _subscribed;
    private System.Windows.Threading.DispatcherTimer? _timer;

    public IEnumerable<KeyCountItem>? ItemsSource
    {
        get => (IEnumerable<KeyCountItem>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public GamepadShapeView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (!_subscribed)
            {
                ThemeService.Changed += Redraw;
                NumberFormatService.Changed += Redraw;
                _subscribed = true;
            }
            Redraw();

            _timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(600),
            };
            _timer.Tick += (_, _) => Redraw();
            _timer.Start();
        };
        Unloaded += (_, _) =>
        {
            _timer?.Stop();
            _timer = null;
            if (_subscribed)
            {
                ThemeService.Changed -= Redraw;
                NumberFormatService.Changed -= Redraw;
                _subscribed = false;
            }
        };
    }

    private int CountOf(string id) => ItemsSource?.FirstOrDefault(x => x.Key == id)?.Count ?? 0;

    /// <summary>这批数据里最小的非零次数，作为色标起点。</summary>
    private int _heatMin = 1;

    private void Redraw()
    {
        if (!IsLoaded)
            return;

        Root.Children.Clear();

        int max = 1;
        int min = int.MaxValue;
        if (ItemsSource != null)
        {
            foreach (var item in ItemsSource)
            {
                if (item.Count <= 0)
                    continue;
                max = Math.Max(max, item.Count);
                min = Math.Min(min, item.Count);
            }
        }
        if (min == int.MaxValue)
            min = 1;
        _heatMin = min;

        Color outline = ThemeService.Blend(ThemeService.HeatIdle, ThemeService.MutedInk, 0.78);
        Add(MakePath(BodyData, ThemeService.Surface, outline, 2.0));

        // 顶部扳机与肩键
        Pill(100, 18, 100, 24, GamepadButtonIds.LT, "LT", max, outline, alignLeft: true);
        Pill(440, 18, 100, 24, GamepadButtonIds.RT, "RT", max, outline, alignLeft: false);
        Pill(110, 48, 100, 26, GamepadButtonIds.LB, "LB", max, outline, alignLeft: true);
        Pill(430, 48, 100, 26, GamepadButtonIds.RB, "RB", max, outline, alignLeft: false);

        // 左摇杆
        Stick(160, 150, GamepadButtonIds.LStick, max, outline);

        // 十字键
        DPad(160, 256, max, outline);

        // 右摇杆
        Stick(490, 256, GamepadButtonIds.RStick, max, outline);

        // ABXY
        Circle(496, 100, 20, GamepadButtonIds.Y, "", max, outline, 15);
        Circle(450, 146, 20, GamepadButtonIds.X, "", max, outline, 15);
        Circle(542, 146, 20, GamepadButtonIds.B, "", max, outline, 15);
        Circle(496, 186, 20, GamepadButtonIds.A, "", max, outline, 15);

        // 中央 View / Menu / Guide（Share 系统不暴露，只画图标不计次）
        Circle(266, 132, 13, GamepadButtonIds.Back, "⧉", max, outline, 10);
        Circle(374, 132, 13, GamepadButtonIds.Start, "≡", max, outline, 10);
        Circle(320, 92, 16, "", "◎", max, outline, 11);
    }

    /// <summary>胶囊键：按键名靠外、次数靠内（左右对称）。</summary>
    private void Pill(
        double x,
        double y,
        double w,
        double h,
        string id,
        string label,
        int max,
        Color outline,
        bool alignLeft
    )
    {
        int count = CountOf(id);
        var (bg, fg) = ThemeService.Heat(count, max, _heatMin);

        var border = new Border
        {
            Width = w,
            Height = h,
            CornerRadius = new CornerRadius(h / 2),
            Background = new SolidColorBrush(bg),
            BorderBrush = new SolidColorBrush(outline),
            BorderThickness = new Thickness(1.3),
        };
        Canvas.SetLeft(border, x);
        Canvas.SetTop(border, y);
        Add(border);

        if (alignLeft)
        {
            AddTextLeft(label, x + 11, y + h / 2, ThemeService.MutedInk, 10);
            if (count > 0)
                AddTextRight(NumberFormatService.FormatCompact(count), x + w - 11, y + h / 2, fg, 10);
        }
        else
        {
            AddTextRight(label, x + w - 11, y + h / 2, ThemeService.MutedInk, 10);
            if (count > 0)
                AddTextLeftValue(NumberFormatService.FormatCompact(count), x + 11, y + h / 2, fg, 10);
        }
    }

    /// <summary>圆形键：字母居中，次数在圆下方。</summary>
    private void Circle(
        double cx,
        double cy,
        double r,
        string id,
        string label,
        int max,
        Color outline,
        double fontSize = 13
    )
    {
        int count = CountOf(id);
        var (bg, fg) = ThemeService.Heat(count, max, _heatMin);

        var circle = new Ellipse
        {
            Width = r * 2,
            Height = r * 2,
            Fill = new SolidColorBrush(bg),
            Stroke = new SolidColorBrush(outline),
            StrokeThickness = 1.3,
        };
        Canvas.SetLeft(circle, cx - r);
        Canvas.SetTop(circle, cy - r);
        Add(circle);

        if (count > 0)
        {
            if (string.IsNullOrEmpty(label))
            {
                AddText(NumberFormatService.FormatCompact(count), cx, cy, fg, fontSize);
            }
            else
            {
                AddText(label, cx, cy - r * 0.34, fg, fontSize);
                AddText(NumberFormatService.FormatCompact(count), cx, cy + r * 0.36, fg, Math.Max(9, fontSize - 3));
            }
        }
        else if (!string.IsNullOrEmpty(label))
        {
            AddText(label, cx, cy, ThemeService.MutedInk, fontSize);
        }
    }

    /// <summary>摇杆：外圈 + 内圈，按下次数写在下方。</summary>
    private void Stick(double cx, double cy, string id, int max, Color outline)
    {
        int count = CountOf(id);
        var (bg, fg) = ThemeService.Heat(count, max, _heatMin);

        var outer = new Ellipse
        {
            Width = 84,
            Height = 84,
            Fill = new SolidColorBrush(ThemeService.Surface),
            Stroke = new SolidColorBrush(outline),
            StrokeThickness = 1.6,
        };
        Canvas.SetLeft(outer, cx - 42);
        Canvas.SetTop(outer, cy - 42);
        Add(outer);

        var inner = new Ellipse
        {
            Width = 56,
            Height = 56,
            Fill = new SolidColorBrush(bg),
            Stroke = new SolidColorBrush(outline),
            StrokeThickness = 1.2,
        };
        Canvas.SetLeft(inner, cx - 28);
        Canvas.SetTop(inner, cy - 28);
        Add(inner);

        if (count > 0)
            AddText(NumberFormatService.FormatCompact(count), cx, cy, fg, 12);
    }

    /// <summary>十字键：四个方向独立着色，次数写在各臂外侧。</summary>
    private void DPad(double cx, double cy, int max, Color outline)
    {
        const double arm = 30; // 单臂长
        const double width = 24; // 臂宽

        void Bar(double left, double top, double w, double h, string id)
        {
            int count = CountOf(id);
            var (bg, _) = ThemeService.Heat(count, max, _heatMin);
            var rect = new Rectangle
            {
                Width = w,
                Height = h,
                RadiusX = 3,
                RadiusY = 3,
                Fill = new SolidColorBrush(bg),
                Stroke = new SolidColorBrush(outline),
                StrokeThickness = 1.2,
            };
            Canvas.SetLeft(rect, left);
            Canvas.SetTop(rect, top);
            Add(rect);
        }

        Bar(cx - width / 2, cy - arm - width / 2, width, arm, GamepadButtonIds.DPadUp);
        Bar(cx - width / 2, cy + width / 2, width, arm, GamepadButtonIds.DPadDown);
        Bar(cx - arm - width / 2, cy - width / 2, arm, width, GamepadButtonIds.DPadLeft);
        Bar(cx + width / 2, cy - width / 2, arm, width, GamepadButtonIds.DPadRight);

        void Num(string id, double tx, double ty)
        {
            int count = CountOf(id);
            if (count > 0)
                AddText(NumberFormatService.FormatCompact(count), tx, ty, ThemeService.HeatText(ThemeService.Heat(count, max, _heatMin).Background), 9);
        }

        Num(GamepadButtonIds.DPadUp, cx, cy - arm / 2 - width / 2);
        Num(GamepadButtonIds.DPadDown, cx, cy + arm / 2 + width / 2);
        Num(GamepadButtonIds.DPadLeft, cx - arm / 2 - width / 2, cy);
        Num(GamepadButtonIds.DPadRight, cx + arm / 2 + width / 2, cy);
    }

    private void Add(UIElement element) => Root.Children.Add(element);

    private static System.Windows.Shapes.Path MakePath(
        string data,
        Color fill,
        Color stroke,
        double thickness
    ) =>
        new()
        {
            Data = Geometry.Parse(data),
            Fill = new SolidColorBrush(fill),
            Stroke = new SolidColorBrush(stroke),
            StrokeThickness = thickness,
        };

    private void AddText(string text, double cx, double cy, Color fg, double size)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(fg),
        };
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(tb, cx - tb.DesiredSize.Width / 2);
        Canvas.SetTop(tb, cy - tb.DesiredSize.Height / 2);
        Add(tb);
    }

    private void AddTextLeft(string text, double x, double cy, Color fg, double size)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(fg),
        };
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(tb, x);
        Canvas.SetTop(tb, cy - tb.DesiredSize.Height / 2);
        Add(tb);
    }

    private void AddTextLeftValue(string text, double x, double cy, Color fg, double size)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(fg),
        };
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(tb, x);
        Canvas.SetTop(tb, cy - tb.DesiredSize.Height / 2);
        Add(tb);
    }

    private void AddTextRight(string text, double right, double cy, Color fg, double size)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(fg),
        };
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(tb, right - tb.DesiredSize.Width);
        Canvas.SetTop(tb, cy - tb.DesiredSize.Height / 2);
        Add(tb);
    }
}
