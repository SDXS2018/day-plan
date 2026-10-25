namespace DailyPlan.Models;

public sealed class AppSettings
{
    public bool AutoCollapseOnDeactivated { get; set; } = true;
    public double WindowMargin { get; set; } = 12;
    public double? CapsuleLeft { get; set; }
    public double? CapsuleTop { get; set; }
}
