using Size = System.Windows.Size;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using PressViewer.Services;
using PressViewer.ViewModels;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using UserControl = System.Windows.Controls.UserControl;

namespace PressViewer.Views;

/// <summary>
/// 鼠标热力图：俯视的立体鼠标，左键 / 滚轮（中键）/ 右键各自着色。
/// 数字一律带千分位完整显示，左右键正面能装下 8 位数；滚轮较窄，位数多时折行（如「114,」/「514」）。
/// </summary>
public partial class MouseShapeView : UserControl
{
    // 画布 360×430，外面 Viewbox 等比缩放
    private const double MidX = 180;

    /// <summary>左右键的底边位置（下面就是机身）。</summary>
    private const double KeyBottom = 246;

    private const string BodyData =
        "M 180,8 C 252,8 310,54 316,132 C 321,198 318,302 296,358 "
        + "C 276,408 234,424 180,424 C 126,424 84,408 64,358 "
        + "C 42,302 39,198 44,132 C 50,54 108,8 180,8 Z";

    // 左右键贴着机身内沿（只留一条窄边），外缘顺着机身走，底边圆角收口
    private const string LeftKeyData =
        "M 178,12 C 114,12 60,52 53,130 C 47,176 47,212 56,230 "
        + "C 64,242 92,246 120,246 L 173,246 L 173,12 Z";

    private const string RightKeyData =
        "M 182,12 C 246,12 300,52 307,130 C 313,176 313,212 304,230 "
        + "C 296,242 268,246 240,246 L 187,246 L 187,12 Z";

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable<KeyCountItem>),
        typeof(MouseShapeView),
        new PropertyMetadata(null, (_, __) => { })
    );

    private readonly Canvas _root;
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
        _root = Root;
        Loaded += (_, _) =>
        {
            if (!_subscribed)
            {
                ThemeService.Changed += OnThemeChanged;
                _subscribed = true;
            }
            Redraw();

            _timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(700),
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
                ThemeService.Changed -= OnThemeChanged;
                _subscribed = false;
            }
        };
    }

    private void OnThemeChanged() => Redraw();

    private int CountOf(string key) => ItemsSource?.FirstOrDefault(x => x.Key == key)?.Count ?? 0;

    private void Redraw()
    {
        if (!IsLoaded)
            return;

        _root.Children.Clear();
        bool dark = ThemeService.IsDark;

        int left = CountOf("Left");
        int middle = CountOf("Middle");
        int right = CountOf("Right");
        int max = new[] { 1, left, middle, right }.Max();
        int min = new[] { left, middle, right }.Where(v => v > 0).DefaultIfEmpty(1).Min();

        Color outline = ThemeService.Blend(ThemeService.HeatIdle, ThemeService.MutedInk, dark ? 0.55 : 0.35);
        Color bodyTop = dark
            ? ThemeService.Blend(ThemeService.Surface, Colors.White, 0.10)
            : Colors.White;
        Color bodyBottom = dark
            ? ThemeService.Blend(ThemeService.Surface, Colors.Black, 0.42)
            : ThemeService.Blend(ThemeService.Surface, ThemeService.MutedInk, 0.18);

        // 落影：整只鼠标压在地面上
        var shadow = PathOf(BodyData, new SolidColorBrush(Alpha(Colors.Black, dark ? (byte)150 : (byte)46)), null, 0);
        shadow.RenderTransform = new TranslateTransform(0, 12);
        shadow.Effect = new BlurEffect { Radius = 18, RenderingBias = RenderingBias.Performance };
        _root.Children.Add(shadow);

        // 机身
        var body = PathOf(BodyData, new LinearGradientBrush(bodyTop, bodyBottom, new Point(0.5, 0), new Point(0.5, 1)), outline, 1.6);
        _root.Children.Add(body);

        // 左键 / 右键
        var (leftBg, leftFg) = ThemeService.Heat(left, max, min);
        var (rightBg, rightFg) = ThemeService.Heat(right, max, min);
        AddKey(LeftKeyData, leftBg, outline, dark, out var leftPlate);
        AddKey(RightKeyData, rightBg, outline, dark, out var rightPlate);

        // 滚轮（中键）：加宽的口袋，数字可折行
        DrawWheel(middle, max, min, dark, outline);

        // 左右键数字：贴着外侧居中，纵向略偏下，避开中间的滚轮
        AddNumber(NumberFormatService.Format(left), 112, 178, leftFg, leftBg, 84, 30);
        AddNumber(NumberFormatService.Format(right), 248, 178, rightFg, rightBg, 84, 30);
    }

    private void AddKey(string data, Color bg, Color outline, bool dark, out Path plate)
    {
        var keyShadow = PathOf(data, new SolidColorBrush(Alpha(Colors.Black, dark ? (byte)150 : (byte)48)), null, 0);
        keyShadow.RenderTransform = new TranslateTransform(0, 5);
        keyShadow.Effect = new BlurEffect { Radius = 10, RenderingBias = RenderingBias.Performance };
        _root.Children.Add(keyShadow);

        plate = PathOf(data, Grad3(bg, dark), outline, 1.4);
        _root.Children.Add(plate);

        // 键面高光：左上到右下的柔光块
        var gloss = PathOf(data, Grad3Gloss(dark), null, 0);
        _root.Children.Add(gloss);
    }

    /// <summary>滚轮：竖长的胶囊，中键次数写在里面，超过 4 个字符就折行。</summary>
    private void DrawWheel(int count, int max, int min, bool dark, Color outline)
    {
        const double w = 58;
        const double h = 104;
        double x = MidX - w / 2;
        const double y = 32;

        var (bg, heatFg) = ThemeService.Heat(count, max, min);
        Color fg = ThemeService.HeatText(bg);

        var shadow = new Border
        {
            Width = w,
            Height = h,
            CornerRadius = new CornerRadius(w / 2),
            Background = new SolidColorBrush(Alpha(Colors.Black, dark ? (byte)150 : (byte)50)),
            Effect = new BlurEffect { Radius = 6, RenderingBias = RenderingBias.Performance },
            RenderTransform = new TranslateTransform(0, 3),
        };
        Canvas.SetLeft(shadow, x);
        Canvas.SetTop(shadow, y);
        _root.Children.Add(shadow);

        var plate = new Border
        {
            Width = w,
            Height = h,
            CornerRadius = new CornerRadius(w / 2),
            Background = Grad3(bg, dark),
            BorderBrush = new SolidColorBrush(outline),
            BorderThickness = new Thickness(1.4),
        };
        Canvas.SetLeft(plate, x);
        Canvas.SetTop(plate, y);
        _root.Children.Add(plate);

        // 轮面靠三段渐变本身出体积，不再叠竖向高光条（那条细线看着像多余的缝）

        var text = NumberFormatService.Format(count);
        var lines = WrapDigits(text, 4);
        var block = new TextBlock
        {
            Text = string.Join("\n", lines),
            FontWeight = FontWeights.SemiBold,
            TextAlignment = TextAlignment.Center,
            Foreground = new SolidColorBrush(count > 0 ? fg : ThemeService.Blend(fg, bg, 0.45)),
            IsHitTestVisible = false,
        };

        double baseSize = lines.Count <= 1 ? 18 : lines.Count == 2 ? 16 : 14;
        Fit(block, w - 14, h - 16, baseSize);
        block.LineHeight = Math.Round(block.FontSize * 1.15, 1);

        _root.Children.Add(block);
        var bs = TextSize(block);
        Canvas.SetLeft(block, x + w / 2 - bs.Width / 2);
        Canvas.SetTop(block, y + h / 2 - bs.Height / 2);
    }

    /// <summary>按键上的完整数字；宽了就缩字号，下限 6.5。</summary>
    private void AddNumber(string text, double cx, double cy, Color fg, Color bg, double maxWidth, double baseSize)
    {
        var block = new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(ThemeService.Blend(fg, bg, 0.12)),
            IsHitTestVisible = false,
        };

        Fit(block, maxWidth, 120, baseSize);
        _root.Children.Add(block);
        var size = TextSize(block);
        Canvas.SetLeft(block, cx - size.Width / 2);
        Canvas.SetTop(block, cy - size.Height / 2);
    }

    /// <summary>文字实际尺寸：元素已在画布上时用 Measure 的真实结果，拿不到才退回估算
    /// （数字按 0.56em、非 ASCII 按 1em，多行按行数累加高度）。</summary>
    private static Size TextSize(TextBlock tb)
    {
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size measured = tb.DesiredSize;
        if (measured.Width > 0.5 && measured.Height > 0.5)
        {
            return measured;
        }

        string text = tb.Text ?? string.Empty;
        string[] parts = text.Split('\n');
        double widest = 0;
        foreach (string line in parts)
        {
            double w = 0;
            foreach (char c in line)
                w += (c > 127 ? 1.0 : 0.56) * tb.FontSize;
            widest = Math.Max(widest, w);
        }
        return new Size(widest, parts.Length * tb.FontSize * 1.22);
    }

    /// <summary>按估算宽度 / 高度把字号压到塞得下，下限 6.5。</summary>
    private static void Fit(TextBlock tb, double maxWidth, double maxHeight, double baseSize)
    {
        string text = tb.Text ?? string.Empty;
        string[] parts = text.Split('\n');
        double widestEm = 0;
        foreach (string line in parts)
        {
            double em = 0;
            foreach (char c in line)
                em += c > 127 ? 1.0 : 0.56;
            widestEm = Math.Max(widestEm, em);
        }
        widestEm = Math.Max(0.56, widestEm);

        double byWidth = maxWidth / widestEm;
        double byHeight = maxHeight / (parts.Length * 1.22);
        tb.FontSize = Math.Max(6.5, Math.Min(baseSize, Math.Min(byWidth, byHeight)));
    }

    /// <summary>
    /// 把带千分位的数字按逗号切成小段，每行最多 maxChars 个字符。
    /// 例：12,345,678 → ["12,", "345,", "678"]，114,514 → ["114,", "514"]。
    /// </summary>
    internal static List<string> WrapDigits(string text, int maxChars)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text))
            return lines;

        string buffer = string.Empty;
        foreach (char ch in text)
        {
            buffer += ch;
            bool atComma = ch == ',';
            if (atComma && buffer.Length >= maxChars - 1)
            {
                lines.Add(buffer);
                buffer = string.Empty;
            }
            else if (!atComma && buffer.Length >= maxChars)
            {
                lines.Add(buffer);
                buffer = string.Empty;
            }
        }
        if (buffer.Length > 0)
            lines.Add(buffer);

        // 最后一行只剩逗号之类的碎片时并回上一行
        if (lines.Count > 1 && lines[^1].Trim(',').Length == 0)
        {
            lines[^2] += lines[^1];
            lines.RemoveAt(lines.Count - 1);
        }
        return lines;
    }

    // ---------- 画图工具 ----------

    private static Path PathOf(string data, Brush? fill, Color? stroke, double thickness) =>
        new()
        {
            Data = Geometry.Parse(data),
            Fill = fill,
            Stroke = stroke is { } s ? new SolidColorBrush(s) : null,
            StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round,
        };

    /// <summary>三段渐变：顶部提亮、中段本色、底部压暗。</summary>
    private static LinearGradientBrush Grad3(Color bg, bool dark)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1),
        };
        brush.GradientStops.Add(new GradientStop(ThemeService.Blend(bg, Colors.White, dark ? 0.16 : 0.34), 0));
        brush.GradientStops.Add(new GradientStop(bg, 0.52));
        brush.GradientStops.Add(new GradientStop(ThemeService.Blend(bg, Colors.Black, dark ? 0.18 : 0.10), 1));
        return brush;
    }

    /// <summary>键面柔光：左上亮、右下消失。</summary>
    private static LinearGradientBrush Grad3Gloss(bool dark) =>
        new(
            Alpha(Colors.White, dark ? (byte)30 : (byte)110),
            Alpha(Colors.White, 0),
            new Point(0.2, 0),
            new Point(0.8, 0.7)
        );

    private static Color Alpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
}
