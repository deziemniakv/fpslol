using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace FpsLol.Controls;

/// <summary>ContentControl that fades and slides new content in (page transitions / dialog content).</summary>
public sealed class PageHost : ContentControl
{
    public static readonly DependencyProperty OffsetProperty = DependencyProperty.Register(
        nameof(Offset), typeof(double), typeof(PageHost), new PropertyMetadata(10.0));

    public static readonly DependencyProperty ScaleFromProperty = DependencyProperty.Register(
        nameof(ScaleFrom), typeof(double), typeof(PageHost), new PropertyMetadata(1.0));

    public double Offset
    {
        get => (double)GetValue(OffsetProperty);
        set => SetValue(OffsetProperty, value);
    }

    public double ScaleFrom
    {
        get => (double)GetValue(ScaleFromProperty);
        set => SetValue(ScaleFromProperty, value);
    }

    public PageHost()
    {
        Focusable = false;
        IsTabStop = false;
    }

    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        if (newContent is null || !Motion.Enabled)
        {
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            RenderTransform = Transform.Identity;
            return;
        }

        var translate = new TranslateTransform(0, Motion.Distance(Offset));
        var scale = new ScaleTransform(1, 1);
        RenderTransformOrigin = new Point(0.5, 0.5);
        RenderTransform = new TransformGroup { Children = { scale, translate } };

        var duration = Motion.Duration(280);
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration) { EasingFunction = Motion.EaseOut });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(Motion.Distance(Offset), 0, duration) { EasingFunction = Motion.EaseOut });
        if (Math.Abs(ScaleFrom - 1) > 0.001)
        {
            var from = 1 - (1 - ScaleFrom) * Motion.Intensity;
            var anim = new DoubleAnimation(from, 1, duration) { EasingFunction = Motion.EaseOut };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }
    }
}
