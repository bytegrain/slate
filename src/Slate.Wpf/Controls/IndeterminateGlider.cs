using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Slate.Wpf;

/// <summary>
/// Hosts the indeterminate progress bar: sizes its child to 30% of the width and slides it across forever.
/// Under reduced motion the child sits still at the start.
/// </summary>
public class IndeterminateGlider : Decorator
{
    private readonly TranslateTransform _offset = new();

    public IndeterminateGlider()
    {
        ClipToBounds = true;
        SizeChanged += (_, _) => Restart();
        IsVisibleChanged += (_, _) => Restart();
        Unloaded += (_, _) => _offset.BeginAnimation(TranslateTransform.XProperty, null);
    }

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        if (Child is { } child)
        {
            child.RenderTransform = _offset;
            child.Arrange(new Rect(0, 0, arrangeSize.Width * 0.3, arrangeSize.Height));
        }
        return arrangeSize;
    }

    private void Restart()
    {
        if (!IsVisible || ActualWidth <= 0 || SlateMotion.IsReduced)
        {
            _offset.BeginAnimation(TranslateTransform.XProperty, null);
            _offset.X = 0;
            return;
        }

        var glider = ActualWidth * 0.3;
        _offset.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-glider, ActualWidth, TimeSpan.FromMilliseconds(1400))
        {
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
        });
    }
}
