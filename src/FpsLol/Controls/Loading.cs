using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FpsLol.Controls;

/// <summary>Skeleton placeholder with a soft shimmer (static when animations are reduced).</summary>
public sealed class SkeletonBlock : Border
{
    private readonly TranslateTransform _shift = new(-1, 0);

    public SkeletonBlock()
    {
        CornerRadius = new CornerRadius(8);
        Height = 14;
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 0),
            RelativeTransform = _shift,
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), 0),
                new GradientStop(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF), 0.5),
                new GradientStop(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF), 1),
            },
        };
        Background = brush;
        // Animate only while actually visible: a collapsed-but-loaded shimmer would keep WPF rendering at 60 fps.
        IsVisibleChanged += (_, _) => Update();
        Unloaded += (_, _) => _shift.BeginAnimation(TranslateTransform.XProperty, null);
    }

    private void Update()
    {
        _shift.BeginAnimation(TranslateTransform.XProperty, null);
        if (!IsVisible || !Motion.Enabled)
        {
            _shift.X = 0;
            return;
        }
        var shimmer = new DoubleAnimation(-1, 1, new Duration(TimeSpan.FromMilliseconds(1400)))
        {
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = Motion.EaseInOut,
        };
        Timeline.SetDesiredFrameRate(shimmer, 30);
        _shift.BeginAnimation(TranslateTransform.XProperty, shimmer);
    }
}

/// <summary>Minimal indeterminate spinner (a rotating arc).</summary>
public sealed class Spinner : FrameworkElement
{
    private readonly RotateTransform _rotate = new();

    public static readonly DependencyProperty ForegroundProperty = Control.ForegroundProperty.AddOwner(
        typeof(Spinner), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Foreground
    {
        get => (Brush)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    static Spinner()
    {
        WidthProperty.OverrideMetadata(typeof(Spinner), new FrameworkPropertyMetadata(18.0));
        HeightProperty.OverrideMetadata(typeof(Spinner), new FrameworkPropertyMetadata(18.0));
    }

    public Spinner()
    {
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = _rotate;
        IsVisibleChanged += (_, _) => Update();
        Loaded += (_, _) => Update();
        Unloaded += (_, _) => _rotate.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    private void Update()
    {
        if (IsVisible)
        {
            var spin = new DoubleAnimation(0, 360, new Duration(TimeSpan.FromMilliseconds(900))) { RepeatBehavior = RepeatBehavior.Forever };
            Timeline.SetDesiredFrameRate(spin, 40);
            _rotate.BeginAnimation(RotateTransform.AngleProperty, spin);
        }
        else
            _rotate.BeginAnimation(RotateTransform.AngleProperty, null);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 4) return;
        var thickness = Math.Max(1.6, size / 10);
        var r = (size - thickness) / 2;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);

        var track = new Pen(new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), thickness);
        track.Freeze();
        dc.DrawEllipse(null, track, c, r, r);

        var pen = new Pen(Foreground, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(c.X, c.Y - r), false, false);
            ctx.ArcTo(new Point(c.X + r, c.Y), new Size(r, r), 0, false, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        dc.DrawGeometry(null, pen, g);
    }
}
