using System.Windows;
using System.Windows.Media;

namespace DailyPlan.Infrastructure;

/// <summary>A static pastel tint and crisp colored edge; no animation or timer.</summary>
public sealed class TaskEmphasisSurface : FrameworkElement
{
    public static readonly DependencyProperty IsEmphasizedProperty = DependencyProperty.Register(
        nameof(IsEmphasized), typeof(bool), typeof(TaskEmphasisSurface), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty TintProperty = DependencyProperty.Register(
        nameof(Tint), typeof(Brush), typeof(TaskEmphasisSurface), new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender, TintChanged));
    public bool IsEmphasized { get => (bool)GetValue(IsEmphasizedProperty); set => SetValue(IsEmphasizedProperty, value); }
    public Brush Tint { get => (Brush)GetValue(TintProperty); set => SetValue(TintProperty, value); }
    private Pen _edge = new(Brushes.Transparent, 1.2);

    public TaskEmphasisSurface()
    {
        IsHitTestVisible = false;
    }

    private static void TintChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var element = (TaskEmphasisSurface)sender;
        element._edge = new Pen((Brush)args.NewValue, 1.2);
        if (element._edge.CanFreeze) element._edge.Freeze();
    }

    protected override void OnRender(DrawingContext context)
    {
        if (!IsEmphasized || ActualWidth <= 2 || ActualHeight <= 2) return;
        var bounds = new Rect(1, 2, Math.Max(0, ActualWidth - 2), Math.Max(0, ActualHeight - 4));
        context.PushOpacity(0.075);
        context.DrawRoundedRectangle(Tint, null, bounds, 12, 12);
        context.Pop();
        context.PushOpacity(0.60);
        context.DrawRoundedRectangle(null, _edge, bounds, 12, 12);
        context.Pop();
        context.PushOpacity(0.95);
        context.DrawRoundedRectangle(Tint, null, new Rect(4, 13, 3, Math.Max(4, ActualHeight - 26)), 1.5, 1.5);
        context.Pop();
    }
}
