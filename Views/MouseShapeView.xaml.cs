using Size = System.Windows.Size;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using PressViewer.Services;
using PressViewer.ViewModels;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using UserControl = System.Windows.Controls.UserControl;

namespace PressViewer.Views;

/// <summary>俯视鼠标图形：左键、右键、滚轮三块按各自次数着色，0 次不着色。</summary>
public partial class MouseShapeView : UserControl
{
    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable<KeyCountItem>),
        typeof(MouseShapeView),
        new PropertyMetadata(null, (_, __) => { })
    );

    private bool _subscribed;
    private System.Windows.Threading.DispatcherTimer? _timer;

    public IEnumerable<KeyCountItem>? ItemsSource
    {
        get => (IEnumerable<KeyCountItem>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public MouseShapeView()
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

    private void Redraw()
    {
        if (!IsLoaded)
            return;

        Root.Children.Clear();

        int left = CountOf("Left");
        int right = CountOf("Right");
        int middle = CountOf("Middle");
        int max = Math.Max(1, Math.Max(left, Math.Max(right, middle)));
        int min = new[] { left, right, middle }.Where(v => v > 0).DefaultIfEmpty(1).Min();

        var (leftBg, leftFg) = ThemeService.Heat(left, max, min);
        var (rightBg, rightFg) = ThemeService.Heat(right, max, min);
        var (midBg, midFg) = ThemeService.Heat(middle, max, min);

        Color outline = ThemeService.Blend(ThemeService.HeatIdle, ThemeService.MutedInk, 0.45);
        Color bodyFill = ThemeService.Surface;

        // 机身下半部
        Add(
            MakePath(
                "M 58,138 L 182,138 L 182,182 C 182,226 160,258 120,258 C 80,258 58,226 58,182 Z",
                bodyFill,
                outline
            )
        );

        // 左键 / 右键
        Add(
            MakePath(
                "M 120,18 C 92,18 66,36 58,70 L 58,126 L 118,126 L 118,18 Z",
                leftBg,
                outline
            )
        );
        Add(
            MakePath(
                "M 120,18 C 148,18 174,36 182,70 L 182,126 L 122,126 L 122,18 Z",
                rightBg,
                outline
            )
        );

        // 中间分缝
        Add(MakeLine(120, 18, 120, 126, outline));

        // 滚轮
        var wheel = new Border
        {
            Width = 22,
            Height = 44,
            CornerRadius = new CornerRadius(11),
            Background = new SolidColorBrush(midBg),
            BorderBrush = new SolidColorBrush(outline),
            BorderThickness = new Thickness(1.5),
        };
        Canvas.SetLeft(wheel, 109);
        Canvas.SetTop(wheel, 50);
        Add(wheel);

        if (left > 0)
            AddFitText(NumberFormatService.Format(left), 86, 84, leftFg, 88, 16);
        if (right > 0)
            AddFitText(NumberFormatService.Format(right), 154, 84, rightFg, 88, 16);
        if (middle > 0)
        {
            var badgeText = new TextBlock
            {
                Text = NumberFormatService.FormatCompact(middle),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(ThemeService.HeatText(ThemeService.Surface)),
            };
            var badge = new Border
            {
                Background = new SolidColorBrush(ThemeService.Surface),
                BorderBrush = new SolidColorBrush(ThemeService.Blend(ThemeService.Surface, ThemeService.MutedInk, 0.45)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(5, 1, 5, 1),
                Child = badgeText,
            };
            badge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(badge, 120 - badge.DesiredSize.Width / 2);
            Canvas.SetTop(badge, 104);
            Add(badge);
        }


    }

    private void Add(UIElement element) => Root.Children.Add(element);

    private static System.Windows.Shapes.Path MakePath(string data, Color fill, Color stroke) =>
        new()
        {
            Data = Geometry.Parse(data),
            Fill = new SolidColorBrush(fill),
            Stroke = new SolidColorBrush(stroke),
            StrokeThickness = 1.4,
        };

    private static System.Windows.Shapes.Line MakeLine(double x1, double y1, double x2, double y2, Color stroke) =>
        new()
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = new SolidColorBrush(stroke),
            StrokeThickness = 1.4,
        };

    private static string Compact(int n) => n >= 1_000_000
        ? (n / 1_000_000.0).ToString("0.#") + "M"
        : n >= 10_000
            ? (n / 1_000.0).ToString("0.#") + "K"
            : n.ToString();

    private void AddFitText(string text, double cx, double cy, Color fg, double maxWidth, double baseSize)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = baseSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(fg),
        };
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        if (tb.DesiredSize.Width > maxWidth)
        {
            tb.FontSize = Math.Max(7, baseSize * maxWidth / tb.DesiredSize.Width);
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        }
        Canvas.SetLeft(tb, cx - tb.DesiredSize.Width / 2);
        Canvas.SetTop(tb, cy - tb.DesiredSize.Height / 2);
        Add(tb);
    }

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
}
