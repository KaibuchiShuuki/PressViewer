using System.Windows;
using System.Windows.Media;

namespace PressViewer.Helpers;

/// <summary>
/// 让贴边的表格按卡片圆角裁掉四角，避免表头/行背景戳出圆角。
/// 用法：helpers:CornerClip.Radius="14"
/// </summary>
public static class CornerClip
{
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.RegisterAttached(
        "Radius",
        typeof(double),
        typeof(CornerClip),
        new PropertyMetadata(0.0, OnRadiusChanged)
    );

    public static void SetRadius(DependencyObject element, double value) =>
        element.SetValue(RadiusProperty, value);

    public static double GetRadius(DependencyObject element) =>
        (double)element.GetValue(RadiusProperty);

    private static void OnRadiusChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element)
            return;

        element.SizeChanged -= OnSizeChanged;

        if (e.NewValue is double radius && radius > 0)
        {
            element.SizeChanged += OnSizeChanged;
            Apply(element, radius);
        }
        else
        {
            element.Clip = null;
        }
    }

    private static void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is FrameworkElement element)
            Apply(element, GetRadius(element));
    }

    private static void Apply(FrameworkElement element, double radius)
    {
        if (radius <= 0 || element.ActualWidth <= 0 || element.ActualHeight <= 0)
            return;

        element.Clip = new RectangleGeometry(
            new Rect(0, 0, element.ActualWidth, element.ActualHeight),
            radius,
            radius
        );
    }
}
