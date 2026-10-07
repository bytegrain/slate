using System.Windows;
using System.Windows.Media;

namespace Slate.Wpf;

public partial class DataGrid
{
    private static DependencyProperty Token<T>(string name, T fallback, Action<DataGrid> changed) =>
        DependencyProperty.Register(name, typeof(T), typeof(DataGrid), new FrameworkPropertyMetadata(fallback, (d, _) => changed((DataGrid)d)));

    private static readonly Action<DataGrid> Repaint = g => g.InvalidateLayoutAndRows();
    private static readonly Action<DataGrid> Remeasure = g =>
    {
        g.Controller.RowHeight = Math.Max(20, g.TokenRowHeight);
        g.Controller.Remeasure();
        g.InvalidateLayoutAndRows();
        g.UpdateChrome();
    };

    internal static readonly DependencyProperty TokenHoverProperty = Token<Brush>("TokenHover", Brushes.Transparent, Repaint);
    internal static readonly DependencyProperty TokenSelectedProperty = Token<Brush>("TokenSelected", Brushes.Transparent, Repaint);
    internal static readonly DependencyProperty TokenIndicatorProperty = Token<Brush>("TokenIndicator", Brushes.Teal, Repaint);
    internal static readonly DependencyProperty TokenActiveProperty = Token<Brush>("TokenActive", Brushes.Teal, Repaint);
    internal static readonly DependencyProperty TokenBorderProperty = Token<Brush>("TokenBorder", Brushes.LightGray, Repaint);
    internal static readonly DependencyProperty TokenGroupProperty = Token<Brush>("TokenGroup", Brushes.WhiteSmoke, Repaint);
    internal static readonly DependencyProperty TokenHeaderProperty = Token<Brush>("TokenHeader", Brushes.WhiteSmoke, Repaint);
    internal static readonly DependencyProperty TokenHeaderTextProperty = Token<Brush>("TokenHeaderText", Brushes.Gray, Repaint);
    internal static readonly DependencyProperty TokenSurfaceProperty = Token<Brush>("TokenSurface", Brushes.White, Repaint);
    internal static readonly DependencyProperty TokenStripeProperty = Token<Brush>("TokenStripe", Brushes.WhiteSmoke, Repaint);
    internal static readonly DependencyProperty TokenTextProperty = Token<Brush>("TokenText", Brushes.Black, Repaint);
    internal static readonly DependencyProperty TokenTertiaryProperty = Token<Brush>("TokenTertiary", Brushes.Gray, Repaint);
    internal static readonly DependencyProperty TokenTrackProperty = Token<Brush>("TokenTrack", Brushes.LightGray, Repaint);
    internal static readonly DependencyProperty TokenFillProperty = Token<Brush>("TokenFill", Brushes.Teal, Repaint);
    internal static readonly DependencyProperty TokenDangerProperty = Token<Brush>("TokenDanger", Brushes.Red, Repaint);
    internal static readonly DependencyProperty TokenSkeletonProperty = Token<Brush>("TokenSkeleton", Brushes.LightGray, Repaint);
    internal static readonly DependencyProperty TokenMonoProperty = Token<FontFamily>("TokenMono", new FontFamily("Consolas"), Repaint);
    internal static readonly DependencyProperty TokenRowHeightProperty = Token<double>("TokenRowHeight", 32.0, Remeasure);
    internal static readonly DependencyProperty TokenHeaderHeightProperty = Token<double>("TokenHeaderHeight", 40.0, Remeasure);
    internal static readonly DependencyProperty TokenPaddingProperty = Token<double>("TokenPadding", 12.0, Repaint);

    internal Brush TokenHover => (Brush)GetValue(TokenHoverProperty);
    internal Brush TokenSelected => (Brush)GetValue(TokenSelectedProperty);
    internal Brush TokenIndicator => (Brush)GetValue(TokenIndicatorProperty);
    internal Brush TokenActive => (Brush)GetValue(TokenActiveProperty);
    internal Brush TokenBorder => (Brush)GetValue(TokenBorderProperty);
    internal Brush TokenGroup => (Brush)GetValue(TokenGroupProperty);
    internal Brush TokenHeader => (Brush)GetValue(TokenHeaderProperty);
    internal Brush TokenHeaderText => (Brush)GetValue(TokenHeaderTextProperty);
    internal Brush TokenSurface => (Brush)GetValue(TokenSurfaceProperty);
    internal Brush TokenStripe => (Brush)GetValue(TokenStripeProperty);
    internal Brush TokenText => (Brush)GetValue(TokenTextProperty);
    internal Brush TokenTertiary => (Brush)GetValue(TokenTertiaryProperty);
    internal Brush TokenTrack => (Brush)GetValue(TokenTrackProperty);
    internal Brush TokenFill => (Brush)GetValue(TokenFillProperty);
    internal Brush TokenDanger => (Brush)GetValue(TokenDangerProperty);
    internal Brush TokenSkeleton => (Brush)GetValue(TokenSkeletonProperty);
    internal FontFamily TokenMono => (FontFamily)GetValue(TokenMonoProperty);
    internal double TokenRowHeight => (double)GetValue(TokenRowHeightProperty);
    internal double TokenHeaderHeight => (double)GetValue(TokenHeaderHeightProperty);
    internal double TokenPadding => (double)GetValue(TokenPaddingProperty);

    private void InitTokens()
    {
        SetResourceReference(TokenHoverProperty, GridKeys.RowHover);
        SetResourceReference(TokenSelectedProperty, GridKeys.RowSelected);
        SetResourceReference(TokenIndicatorProperty, GridKeys.SelectionIndicator);
        SetResourceReference(TokenActiveProperty, GridKeys.ActiveCell);
        SetResourceReference(TokenBorderProperty, GridKeys.Border);
        SetResourceReference(TokenGroupProperty, GridKeys.GroupBackground);
        SetResourceReference(TokenHeaderProperty, GridKeys.HeaderBackground);
        SetResourceReference(TokenHeaderTextProperty, GridKeys.HeaderText);
        SetResourceReference(TokenSurfaceProperty, GridKeys.Background);
        SetResourceReference(TokenStripeProperty, GridKeys.Stripe);
        SetResourceReference(TokenTextProperty, GridKeys.Text);
        SetResourceReference(TokenTertiaryProperty, GridKeys.TextTertiary);
        SetResourceReference(TokenTrackProperty, GridKeys.Track);
        SetResourceReference(TokenFillProperty, GridKeys.Fill);
        SetResourceReference(TokenDangerProperty, GridKeys.Danger);
        SetResourceReference(TokenSkeletonProperty, GridKeys.Skeleton);
        SetResourceReference(TokenMonoProperty, GridKeys.MonoFont);
        SetResourceReference(TokenRowHeightProperty, GridKeys.RowHeight);
        SetResourceReference(TokenHeaderHeightProperty, GridKeys.HeaderHeight);
        SetResourceReference(TokenPaddingProperty, GridKeys.CellPadding);
    }

    /// <summary>Resolves a themed resource for code-built visuals (null when the key is missing).</summary>
    internal Brush? FindBrush(string key) => TryFindResource(key) as Brush;
}
