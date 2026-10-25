using System.Windows;
using System.Windows.Media;

namespace DailyPlan.Infrastructure;

/// <summary>Only this rounded shell is redrawn during a morph. Its children never reflow.</summary>
public sealed class FluidSurface : FrameworkElement
{
    private static readonly Brush Paper = Frozen(new LinearGradientBrush(
        Color.FromRgb(255, 255, 255), Color.FromRgb(249, 250, 253), 90));
    private static readonly Brush Pearl = CreatePearl();
    private static readonly Pen Edge = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(26, 66, 74, 94)), 0.7));
    private static readonly Pen Highlight = Frozen(new Pen(new SolidColorBrush(Color.FromArgb(228, 255, 255, 255)), 1));
    private static readonly Brush[] Shadows = Enumerable.Range(0, 7)
        .Select(index => (Brush)Frozen(new SolidColorBrush(Color.FromArgb((byte)(index + 1), 38, 49, 72)))).ToArray();
    private double _progress, _hover, _press;

    public Rect CompactBounds { get; set; } = new(10, 10, 100, 32);
    public Rect ExpandedBounds { get; set; } = new(10, 10, 312, 220);
    public Rect CurrentBounds => Interpolate(CompactBounds, ExpandedBounds, Math.Clamp(_progress, 0, 1));
    public double CurrentRadius => 16 + 6 * Math.Clamp(_progress, 0, 1);

    public void Update(double progress, double hover, double press)
    {
        _progress = progress; _hover = hover; _press = press;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext context)
    {
        var rectangle = CurrentBounds;
        var interaction = (1 - Math.Clamp(_progress, 0, 1)) * (0.018 * _hover - 0.035 * _press);
        rectangle.Inflate(rectangle.Width * interaction / 2, rectangle.Height * interaction / 2);
        var radius = CurrentRadius;
        // Layered vector shadows avoid animated shader effects and large intermediate bitmaps.
        for (var index = 0; index < Shadows.Length; index++)
        {
            var shadow = rectangle;
            shadow.Inflate(7 - index, 7 - index);
            shadow.Offset(0, 2.5);
            context.DrawRoundedRectangle(Shadows[index], null, shadow, radius + 7 - index, radius + 7 - index);
        }
        context.DrawRoundedRectangle(Paper, Edge, rectangle, radius, radius);
        if (_progress < 1)
        {
            context.PushOpacity(1 - Math.Clamp(_progress, 0, 1));
            context.DrawRoundedRectangle(Pearl, null, rectangle, radius, radius);
            context.Pop();
        }
        rectangle.Inflate(-1, -1);
        context.DrawRoundedRectangle(null, Highlight, rectangle, Math.Max(0, radius - 1), Math.Max(0, radius - 1));
    }

    protected override HitTestResult? HitTestCore(PointHitTestParameters parameters)
        => ContainsPoint(parameters.HitPoint) ? new PointHitTestResult(this, parameters.HitPoint) : null;

    public bool ContainsPoint(Point point)
    {
        var rectangle = CurrentBounds;
        if (!rectangle.Contains(point)) return false;
        var radius = Math.Min(CurrentRadius, rectangle.Height / 2);
        var centerX = Math.Clamp(point.X, rectangle.Left + radius, rectangle.Right - radius);
        var centerY = Math.Clamp(point.Y, rectangle.Top + radius, rectangle.Bottom - radius);
        var dx = point.X - centerX;
        var dy = point.Y - centerY;
        return dx * dx + dy * dy <= radius * radius;
    }

    private static Rect Interpolate(Rect from, Rect to, double amount) => new(
        from.X + (to.X - from.X) * amount, from.Y + (to.Y - from.Y) * amount,
        from.Width + (to.Width - from.Width) * amount, from.Height + (to.Height - from.Height) * amount);

    private static Brush CreatePearl()
    {
        var brush = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 218, 205), 0));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(222, 242, 217), 0.25));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(205, 231, 255), 0.5));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(228, 216, 255), 0.75));
        brush.GradientStops.Add(new GradientStop(Color.FromRgb(255, 216, 234), 1));
        return Frozen(brush);
    }

    private static T Frozen<T>(T value) where T : Freezable { value.Freeze(); return value; }
}
