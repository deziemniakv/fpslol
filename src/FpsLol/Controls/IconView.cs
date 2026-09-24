using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace FpsLol.Controls;

/// <summary>
/// Lightweight stroke-icon renderer. Draws a 24×24 geometry scaled to the element size, using the
/// inherited text foreground so icons automatically match the surrounding text color.
/// </summary>
public sealed class IconView : FrameworkElement
{
    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data), typeof(Geometry), typeof(IconView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(IconView),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(IconView),
        new FrameworkPropertyMetadata(2.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(IconView),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    static IconView()
    {
        WidthProperty.OverrideMetadata(typeof(IconView), new FrameworkPropertyMetadata(18.0));
        HeightProperty.OverrideMetadata(typeof(IconView), new FrameworkPropertyMetadata(18.0));
        IsHitTestVisibleProperty.OverrideMetadata(typeof(IconView), new UIPropertyMetadata(false));
        SnapsToDevicePixelsProperty.OverrideMetadata(typeof(IconView), new FrameworkPropertyMetadata(true));
    }

    public Geometry? Data
    {
        get => (Geometry?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>Stroke thickness in 24-unit icon space.</summary>
    public double StrokeThickness
    {
        get => (double)GetValue(StrokeThicknessProperty);
        set => SetValue(StrokeThicknessProperty, value);
    }

    public Brush? Fill
    {
        get => (Brush?)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        if (Data is null) return;
        var scale = Math.Min(ActualWidth, ActualHeight) / 24.0;
        if (scale <= 0) return;

        var pen = new Pen(Foreground, StrokeThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round,
        };
        pen.Freeze();

        dc.PushTransform(new TranslateTransform((ActualWidth - 24 * scale) / 2, (ActualHeight - 24 * scale) / 2));
        dc.PushTransform(new ScaleTransform(scale, scale));
        dc.DrawGeometry(Fill, pen, Data);
        dc.Pop();
        dc.Pop();
    }
}
