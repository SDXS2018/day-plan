namespace DailyPlan.Infrastructure;

/// <summary>Analytic critically damped spring. Reversals retain position and velocity.</summary>
public sealed class DampedSpring
{
    public double Position { get; private set; }
    public double Velocity { get; private set; }
    public double Target { get; set; }
    public bool IsSettled => Math.Abs(Position - Target) < 0.002 && Math.Abs(Velocity) < 0.08;

    public void Reset(double value) { Position = Target = value; Velocity = 0; }

    public void Advance(double elapsed, double frequency)
    {
        elapsed = Math.Clamp(elapsed, 0, 0.1);
        var offset = Position - Target;
        var coefficient = Velocity + frequency * offset;
        var decay = Math.Exp(-frequency * elapsed);
        Position = Target + (offset + coefficient * elapsed) * decay;
        Velocity = (Velocity - frequency * coefficient * elapsed) * decay;
        if (IsSettled) { Position = Target; Velocity = 0; }
    }
}
