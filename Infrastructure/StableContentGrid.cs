using System.Windows;
using System.Windows.Controls;

namespace DailyPlan.Infrastructure;

public sealed class StableContentGrid : Grid
{
    public int MeasurePasses { get; private set; }
    protected override Size MeasureOverride(Size availableSize)
    {
        MeasurePasses++;
        return base.MeasureOverride(availableSize);
    }
}
