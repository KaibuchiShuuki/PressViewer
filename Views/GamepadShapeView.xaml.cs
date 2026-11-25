using Size = System.Windows.Size;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using PressViewer.Models;
using PressViewer.Services;
using PressViewer.ViewModels;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Point = System.Windows.Point;
using Ellipse = System.Windows.Shapes.Ellipse;
using UserControl = System.Windows.Controls.UserControl;

namespace PressViewer.Views;

/// <summary>
/// 手柄热力图。两套画法，设置页可切换：
/// <list type="bullet">
/// <item>Form —— 手柄形：保留手柄轮廓做底，按键按真实手柄的相对位置摆，写实、有质感。</item>
/// <item>Tile —— 键帽式：放弃轮廓，把按键排成大键帽分区，每个键都有足够面积写完整数字。</item>
/// </list>
/// 两边都保证数字完整（带千分位），装不下时自动缩字号，不做 K / M 缩写。
/// </summary>
public partial class GamepadShapeView : UserControl
{
    // 画布 1000×400，外面 Viewbox 等比缩放
    private const double CanvasWidth = 1000;
    private const double CanvasHeight = 520;

    /// <summary>
    /// 手柄形模式的机身轮廓：两侧肩峰略高、中央手柄脊平缓，下方两握把向外收圆。
    /// 参考 Xbox 手柄的俯视剪影——上沿饱满、下沿中间比握把底高。
    /// </summary>
    private const string BodyData =
        "M 500,112 C 560,112 594,92 646,86 C 708,78 772,90 806,112 "
        + "C 848,140 864,204 858,278 C 850,356 814,424 758,456 "
        + "C 706,486 662,460 630,418 C 600,378 552,368 500,368 "
        + "C 448,368 400,378 370,418 C 338,460 294,486 242,456 "
        + "C 186,424 150,356 142,278 C 136,204 152,140 194,112 "
        + "C 228,90 292,78 354,86 C 406,92 440,112 500,112 Z";

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(
        nameof(ItemsSource),
        typeof(IEnumerable<KeyCountItem>),
        typeof(GamepadShapeView),
        new PropertyMetadata(null, (_, __) => { })
    );

    private readonly List<KeyVisual> _visuals = new();
    private readonly Canvas _root;
    private FrameworkElement? _body;
    private FrameworkElement? _bodyShadow;

    private bool _subscribed;
    private bool _dirty = true;
    private string _builtLayout = "";
    private bool _builtDark;
    private System.Windows.Threading.DispatcherTimer? _timer;

    public IEnumerable<KeyCountItem>? ItemsSource
    {
        get => (IEnumerable<KeyCountItem>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public GamepadShapeView()
    {
        InitializeComponent();
        _root = Root;
        Loaded += (_, _) =>
        {
            if (!_subscribed)
            {
                ThemeService.Changed += OnThemeChanged;
                GamepadLayoutService.Changed += OnLayoutChanged;
                _subscribed = true;
            }
            _dirty = true;
            Refresh(force: true);

            _timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(700),
            };
            _timer.Tick += (_, _) => Refresh();
            _timer.Start();
        };
        Unloaded += (_, _) =>
        {
            _timer?.Stop();
            _timer = null;
            if (_subscribed)
            {
                ThemeService.Changed -= OnThemeChanged;
                GamepadLayoutService.Changed -= OnLayoutChanged;
                _subscribed = false;
            }
        };
    }

    private void OnThemeChanged()
    {
        _dirty = true;
        Refresh();
    }

    private void OnLayoutChanged()
    {
        _dirty = true;
        Refresh(force: true);
    }

    private int CountOf(string id) => ItemsSource?.FirstOrDefault(x => x.Key == id)?.Count ?? 0;

    /// <summary>这批数据里最小的非零次数，作为色标起点。</summary>
    private int _heatMin = 1;

    private void Refresh(bool force = false)
    {
        if (!IsLoaded)
            return;

        string layout = GamepadLayoutService.Current;
        if (force || _dirty || layout != _builtLayout || ThemeService.IsDark != _builtDark)
        {
            Build(layout);
            _dirty = false;
            _builtLayout = layout;
            _builtDark = ThemeService.IsDark;
        }

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

        bool dark = ThemeService.IsDark;
        foreach (var v in _visuals)
        {
            int count = CountOf(v.Key.Id);
            var (bg, fg) = ThemeService.Heat(count, max, _heatMin);
            SetPlateFill(v.Plate, Grad3(bg, dark));
            v.Number.Text = NumberFormatService.Format(count);
            v.Number.Foreground = new SolidColorBrush(
                count > 0 ? fg : ThemeService.Blend(fg, bg, 0.55)
            );
            // 标签跟着键面颜色走，浅色键上也不会糊成一片
            v.Label.Foreground = new SolidColorBrush(
                ThemeService.Blend(ThemeService.HeatText(bg), bg, 0.3)
            );
            LayoutNumber(v);
        }
    }

    // ---------- 构建 ----------

    private void Build(string layout)
    {
        _root.Children.Clear();
        _visuals.Clear();
        _body = null;
        _bodyShadow = null;

        bool dark = ThemeService.IsDark;
        Color outline = ThemeService.Blend(ThemeService.HeatIdle, ThemeService.MutedInk, dark ? 0.6 : 0.4);

        var keys = layout == GamepadLayoutService.Tile ? TileLayout() : FormLayout();

        if (layout != GamepadLayoutService.Tile)
        {
            Color bodyTop = dark ? ThemeService.Blend(ThemeService.Surface, Colors.White, 0.08) : Colors.White;
            Color bodyBottom = dark
                ? ThemeService.Blend(ThemeService.Surface, Colors.Black, 0.34)
                : ThemeService.Blend(ThemeService.Surface, ThemeService.MutedInk, 0.14);

            _bodyShadow = MakePath(BodyData, new SolidColorBrush(Alpha(Colors.Black, dark ? (byte)120 : (byte)40)), null, 0);
            _bodyShadow.RenderTransform = new TranslateTransform(0, 10);
            _bodyShadow.Effect = new BlurEffect { Radius = 22, RenderingBias = RenderingBias.Performance };
            _root.Children.Add(_bodyShadow);

            // 斜向渐变：左上受光、右下背光，比纯上下渐变更像一块有厚度的塑料壳
            _body = MakePath(BodyData, GradDiag(bodyTop, bodyBottom), outline, 1.8);
            _root.Children.Add(_body);

            // 两侧握把的暗部，压出圆柱的体积
            foreach (var gx in new[] { 200.0, 800.0 })
            {
                var grip = new Ellipse
                {
                    Width = 200,
                    Height = 170,
                    Fill = new RadialGradientBrush(Alpha(Colors.Black, dark ? (byte)70 : (byte)26), Alpha(Colors.Black, 0)),
                };
                Canvas.SetLeft(grip, gx - 100);
                Canvas.SetTop(grip, 330 - 85);
                _root.Children.Add(grip);
            }

            // 机身上沿的一条柔光，让轮廓从背景里浮起来
            var rim = MakePath(
                "M 194,112 C 228,90 292,78 354,86 C 406,92 440,112 500,112 "
                    + "C 560,112 594,92 646,86 C 708,78 772,90 806,112",
                null,
                null,
                0
            );
            rim.Stroke = new SolidColorBrush(Alpha(Colors.White, dark ? (byte)30 : (byte)120));
            rim.StrokeThickness = 2.4;
            rim.StrokeStartLineCap = PenLineCap.Round;
            rim.StrokeEndLineCap = PenLineCap.Round;
            _root.Children.Add(rim);
        }

        foreach (var key in keys)
        {
            var shadow = ShapeOf(key, new SolidColorBrush(Alpha(Colors.Black, dark ? (byte)155 : (byte)55)), null, 0);
            shadow.RenderTransform = new TranslateTransform(0, 4);
            shadow.Effect = new BlurEffect { Radius = 7, RenderingBias = RenderingBias.Performance };
            Canvas.SetLeft(shadow, key.Left);
            Canvas.SetTop(shadow, key.Top);
            _root.Children.Add(shadow);

            var plate = ShapeOf(key, Grad3(ThemeService.HeatIdle, dark), outline, 1.5);
            Canvas.SetLeft(plate, key.Left);
            Canvas.SetTop(plate, key.Top);
            _root.Children.Add(plate);

            // 按键顶面的内高光
            var gloss = ShapeOf(
                key,
                new LinearGradientBrush(
                    Alpha(Colors.White, dark ? (byte)26 : (byte)80),
                    Alpha(Colors.White, 0),
                    new Point(0.5, 0),
                    new Point(0.5, 0.55)
                ),
                null,
                0
            );
            Canvas.SetLeft(gloss, key.Left);
            Canvas.SetTop(gloss, key.Top);
            _root.Children.Add(gloss);

            double labelSize = Math.Clamp(Math.Min(key.Width, key.Height) * 0.17, 8, 14);
            var label = new TextBlock
            {
                Text = key.Label,
                FontSize = key.Circle ? Math.Clamp(key.Width * 0.22, 8, 15) : labelSize,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(ThemeService.MutedInk),
                IsHitTestVisible = false,
            };
            _root.Children.Add(label);

            var number = new TextBlock
            {
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false,
            };
            _root.Children.Add(number);

            _visuals.Add(new KeyVisual(key, plate, number, label));
        }
    }

    /// <summary>手柄形：肩键压在肩峰上，左摇杆 + 十字在左，ABXY + 右摇杆在右，Back / Start 并排在中间。</summary>
    private static List<PadKey> FormLayout() =>
        new()
        {
            new PadKey(GamepadButtonIds.LT, "LT", 196, 58, 124, 50, 15),
            new PadKey(GamepadButtonIds.LB, "LB", 214, 116, 114, 46, 13),
            new PadKey(GamepadButtonIds.RT, "RT", 680, 58, 124, 50, 15),
            new PadKey(GamepadButtonIds.RB, "RB", 672, 116, 114, 46, 13),
            PadKey.CircleOf(GamepadButtonIds.LStick, "LS", 312, 234, 56),
            new PadKey(GamepadButtonIds.DPadUp, "↑", 288, 306, 42, 42, 11),
            new PadKey(GamepadButtonIds.DPadDown, "↓", 288, 392, 42, 42, 11),
            new PadKey(GamepadButtonIds.DPadLeft, "←", 240, 349, 42, 42, 11),
            new PadKey(GamepadButtonIds.DPadRight, "→", 336, 349, 42, 42, 11),
            new PadKey(GamepadButtonIds.Back, "⧉", 400, 244, 88, 46, 12),
            new PadKey(GamepadButtonIds.Start, "≡", 502, 244, 88, 46, 12),
            PadKey.CircleOf(GamepadButtonIds.Y, "Y", 700, 184, 34),
            PadKey.CircleOf(GamepadButtonIds.X, "X", 640, 248, 34),
            PadKey.CircleOf(GamepadButtonIds.B, "B", 760, 248, 34),
            PadKey.CircleOf(GamepadButtonIds.A, "A", 700, 312, 34),
            PadKey.CircleOf(GamepadButtonIds.RStick, "RS", 700, 410, 52),
        };

    /// <summary>键帽式：左右两列匀称肩键，左摇杆 + 十字，右 ABXY + 右摇杆，Back / Start 是中间两枚小方块。</summary>
    private static List<PadKey> TileLayout() =>
        new()
        {
            // 肩键：左右镜像，与键盘 Tab 键差不多大
            new PadKey(GamepadButtonIds.LT, "LT", 32, 37, 132, 58, 16),
            new PadKey(GamepadButtonIds.LB, "LB", 58, 105, 110, 50, 14),
            new PadKey(GamepadButtonIds.RT, "RT", 716, 37, 132, 58, 16),
            new PadKey(GamepadButtonIds.RB, "RB", 712, 105, 110, 50, 14),
            // 左半：LS 在左上、十字键在右下；右半镜像成 ABXY 在右上、RS 在左下
            PadKey.CircleOf(GamepadButtonIds.LStick, "LS", 250, 213, 62),
            new PadKey(GamepadButtonIds.DPadUp, "↑", 280, 298, 60, 60, 13),
            new PadKey(GamepadButtonIds.DPadDown, "↓", 280, 418, 60, 60, 13),
            new PadKey(GamepadButtonIds.DPadLeft, "←", 220, 358, 60, 60, 13),
            new PadKey(GamepadButtonIds.DPadRight, "→", 340, 358, 60, 60, 13),
            // Back / Start：同一高度、关于中轴对称轴左右并排，键内图形在上、数字在下
            new PadKey(GamepadButtonIds.Back, "⧉", 366, 66, 68, 68, 14),
            new PadKey(GamepadButtonIds.Start, "≡", 446, 66, 68, 68, 14),
            PadKey.CircleOf(GamepadButtonIds.Y, "Y", 630, 154, 38),
            PadKey.CircleOf(GamepadButtonIds.X, "X", 572, 214, 38),
            PadKey.CircleOf(GamepadButtonIds.B, "B", 688, 214, 38),
            PadKey.CircleOf(GamepadButtonIds.A, "A", 630, 268, 38),
            PadKey.CircleOf(GamepadButtonIds.RStick, "RS", 570, 362, 56),
        };

    // ---------- 数字排版 ----------

    /// <summary>数字放在「去掉底部标签后的区域」中央，宽度超了就缩字号。
    /// 特别矮的键（十字键、肩键）改成「数字占满居中 + 标签缩到右上角」，避免两行挤在一起。</summary>
    private void LayoutNumber(KeyVisual v)
    {
        var k = v.Key;
        double cx = k.Left + k.Width / 2;

        // 圆形键（ABXY、摇杆）：数字在圆内居中，标签缩成底部小字
        if (k.Circle)
        {
            double numTop = k.Top + k.Height * 0.05;
            double numZone = k.Height * 0.72;
            double zoneTop = k.Top + k.Height * 0.76;
            double zoneH = k.Height * 0.19;

            Fit(v.Number, k.Width * 0.62, numZone, Math.Min(k.Width * 0.32, 36));
            var ns = TextSize(v.Number);
            Canvas.SetLeft(v.Number, cx - ns.Width / 2);
            Canvas.SetTop(v.Number, numTop + numZone / 2 - ns.Height / 2);

            v.Label.FontSize = Math.Clamp(k.Width * 0.17, 8, 14);
            var ls = TextSize(v.Label);
            Canvas.SetLeft(v.Label, cx - ls.Width / 2);
            Canvas.SetTop(v.Label, zoneTop + zoneH / 2 - ls.Height / 2);
            return;
        }

        bool compact = k.Height < 70;

        // Back / Start：标签是图形提示（⧉ / ≡）
        if (k.Label is "⧉" or "≡")
        {
            // 方块键（键帽式）：图形在上、数字在下，各自居中
            if (k.Height >= 60)
            {
                v.Label.FontSize = Math.Clamp(k.Height * 0.22, 9, 18);
                var lsq = TextSize(v.Label);
                Fit(v.Number, k.Width - 14, k.Height * 0.46, Math.Min(k.Width * 0.40, 26));
                var nsq = TextSize(v.Number);

                double iconCy = k.Top + k.Height * 0.33;
                Canvas.SetLeft(v.Label, cx - lsq.Width / 2);
                Canvas.SetTop(v.Label, iconCy - lsq.Height / 2);

                double numCy = k.Top + k.Height * 0.70;
                Canvas.SetLeft(v.Number, cx - nsq.Width / 2);
                Canvas.SetTop(v.Number, numCy - nsq.Height / 2);
                return;
            }

            // 扁条键（手柄形）：图形在左、数字小字在右
            v.Label.FontSize = Math.Min(k.Height * 0.36, 16);
            var ls0 = TextSize(v.Label);
            Fit(v.Number, k.Width * 0.5, k.Height * 0.44, Math.Min(k.Height * 0.30, 14));
            var ns0 = TextSize(v.Number);

            const double gap = 5;
            double startX = cx - (ls0.Width + gap + ns0.Width) / 2;
            Canvas.SetLeft(v.Label, startX);
            Canvas.SetTop(v.Label, k.Top + (k.Height - ls0.Height) / 2);
            Canvas.SetLeft(v.Number, startX + ls0.Width + gap);
            Canvas.SetTop(v.Number, k.Top + (k.Height - ns0.Height) / 2);
            return;
        }

        if (compact)
        {
            v.Label.FontSize = Math.Clamp(Math.Min(k.Width, k.Height) * 0.26, 8, 13);
            double maxW = k.Width - 8;
            double maxH = k.Height - 8;
            double size = Math.Min(Math.Min(k.Width * 0.40, k.Height * 0.44), 38);

            Fit(v.Number, maxW, maxH, size);
            var ns = TextSize(v.Number);
            Canvas.SetLeft(v.Number, cx - ns.Width / 2);
            Canvas.SetTop(v.Number, k.Top + k.Height / 2 - ns.Height / 2);

            var ls = TextSize(v.Label);
            Canvas.SetLeft(v.Label, k.Left + k.Width - ls.Width - 5);
            Canvas.SetTop(v.Label, k.Top + 3);
            return;
        }

        double labelH = v.Label.FontSize * 1.5;
        double wideMax = k.Width - 18;
        double highMax = Math.Max(10, k.Height - labelH - 26);
        double baseSize = Math.Min(Math.Min(k.Width * 0.40, k.Height * 0.38), 36);

        Fit(v.Number, wideMax, highMax, baseSize);

        var ns3 = TextSize(v.Number);
        Canvas.SetLeft(v.Number, cx - ns3.Width / 2);
        Canvas.SetTop(v.Number, k.Top + (k.Height - labelH) / 2 - ns3.Height / 2);

        var ls3 = TextSize(v.Label);
        Canvas.SetLeft(v.Label, cx - ls3.Width / 2);
        Canvas.SetTop(v.Label, k.Top + k.Height - labelH + (labelH - ls3.Height) / 2);
    }

    /// <summary>文字的实际尺寸：优先问一次 Measure（元素已挂在画布上，能拿到真实排版结果），
    /// 拿不到才退回估算（数字按 0.56em、非 ASCII 按 1em）。</summary>
    private static Size TextSize(TextBlock tb)
    {
        tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Size measured = tb.DesiredSize;
        if (measured.Width > 0.5 && measured.Height > 0.5)
        {
            return measured;
        }

        string text = tb.Text ?? string.Empty;
        double w = 0;
        foreach (char c in text)
            w += (c > 127 ? 1.0 : 0.56) * tb.FontSize;
        return new Size(w, tb.FontSize * 1.25);
    }

    /// <summary>按估算宽度 / 高度把字号压到塞得下，下限 6.5。</summary>
    private static void Fit(TextBlock tb, double maxWidth, double maxHeight, double baseSize)
    {
        string text = tb.Text ?? string.Empty;
        double em = 0;
        foreach (char c in text)
            em += c > 127 ? 1.0 : 0.56;
        em = Math.Max(0.56, em);

        double byWidth = maxWidth / em;
        double byHeight = maxHeight / 1.25;
        tb.FontSize = Math.Max(6.5, Math.Min(baseSize, Math.Min(byWidth, byHeight)));
    }

    // ---------- 绘制工具 ----------

    private sealed record PadKey(
        string Id,
        string Label,
        double Left,
        double Top,
        double Width,
        double Height,
        double CornerRadius,
        bool Circle
    )
    {
        public PadKey(string id, string label, double left, double top, double w, double h, double radius)
            : this(id, label, left, top, w, h, radius, false) { }

        public static PadKey CircleOf(string id, string label, double cx, double cy, double r) =>
            new(id, label, cx - r, cy - r, r * 2, r * 2, 0, true);
    }

    private sealed record KeyVisual(PadKey Key, FrameworkElement Plate, TextBlock Number, TextBlock Label);

    private static void SetPlateFill(FrameworkElement plate, Brush brush)
    {
        switch (plate)
        {
            case Ellipse ellipse:
                ellipse.Fill = brush;
                break;
            case Border border:
                border.Background = brush;
                break;
        }
    }

    private static FrameworkElement ShapeOf(PadKey key, Brush? fill, Color? stroke, double thickness)
    {
        Brush? strokeBrush = stroke is { } s ? new SolidColorBrush(s) : null;
        if (key.Circle)
        {
            return new Ellipse
            {
                Width = key.Width,
                Height = key.Height,
                Fill = fill,
                Stroke = strokeBrush,
                StrokeThickness = thickness,
            };
        }

        return new Border
        {
            Width = key.Width,
            Height = key.Height,
            CornerRadius = new CornerRadius(key.CornerRadius),
            Background = fill,
            BorderBrush = strokeBrush,
            BorderThickness = new Thickness(thickness),
        };
    }

    private static System.Windows.Shapes.Path MakePath(string data, Brush? fill, Color? stroke, double thickness) =>
        new()
        {
            Data = Geometry.Parse(data),
            Fill = fill,
            Stroke = stroke is { } s ? new SolidColorBrush(s) : null,
            StrokeThickness = thickness,
            StrokeLineJoin = PenLineJoin.Round,
        };

    /// <summary>三段渐变：顶部提亮、中段本色、底部压暗，做出按键的厚度。</summary>
    private static LinearGradientBrush Grad3(Color bg, bool dark)
    {
        double topLight = dark ? 0.14 : 0.34;
        double bottomDark = dark ? 0.14 : 0.08;
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5, 0),
            EndPoint = new Point(0.5, 1),
        };
        brush.GradientStops.Add(new GradientStop(ThemeService.Blend(bg, Colors.White, topLight), 0));
        brush.GradientStops.Add(new GradientStop(bg, 0.55));
        brush.GradientStops.Add(new GradientStop(ThemeService.Blend(bg, Colors.Black, bottomDark), 1));
        return brush;
    }

    private static LinearGradientBrush Grad(Color top, Color bottom) =>
        new(top, bottom, new Point(0.5, 0), new Point(0.5, 1));

    /// <summary>斜向渐变：左上受光、右下背光，用来做机身塑料壳的体积。</summary>
    private static LinearGradientBrush GradDiag(Color top, Color bottom) =>
        new(top, bottom, new Point(0.12, 0), new Point(0.88, 1));

    private static Color Alpha(Color c, byte a) => Color.FromArgb(a, c.R, c.G, c.B);
}
