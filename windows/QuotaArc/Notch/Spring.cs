namespace QuotaArc.Notch;

/// <summary>
/// Closed-form match for SwiftUI Animation.spring(response:dampingFraction:).
/// Numbers come from Sources/Notch/NotchMotion.swift so the Windows overlay
/// moves like the Mac one.
/// </summary>
internal readonly record struct SpringSpec(double Response, double DampingFraction)
{
    public static SpringSpec Unfold { get; } = new(0.42, 0.78);
    public static SpringSpec Contents { get; } = new(0.36, 0.82);
    public static SpringSpec Glide { get; } = new(0.50, 0.86);
    public static SpringSpec Reading { get; } = new(0.90, 0.90);
    public static SpringSpec OrbHover { get; } = new(0.36, 0.70);
    public static SpringSpec RefreshPress { get; } = new(0.30, 0.62);

    public static double StaggerDelay(int index) =>
        Math.Min(index * 0.045, 0.18);
}

internal sealed class SpringValue
{
    public double Current;
    public double Velocity;
    public double Target;
    public SpringSpec Spec = SpringSpec.Unfold;
    public double Delay;

    public SpringValue(double value = 0)
    {
        Current = value;
        Target = value;
    }

    public void Jump(double value)
    {
        Current = value;
        Target = value;
        Velocity = 0;
        Delay = 0;
    }

    public bool Settled =>
        Delay <= 0 && Math.Abs(Current - Target) < 0.0005 && Math.Abs(Velocity) < 0.0005;

    public bool Step(double dt, bool reduceMotion)
    {
        if (reduceMotion)
        {
            if (Delay > 0)
            {
                Delay -= dt;
                if (Delay > 0) return true;
            }
            Current = Target;
            Velocity = 0;
            return false;
        }

        if (Delay > 0)
        {
            Delay -= dt;
            if (Delay > 0) return true;
            dt = Math.Max(1e-6, -Delay);
            Delay = 0;
        }

        var response = Math.Max(0.001, Spec.Response);
        var zeta = Spec.DampingFraction;
        var omega = 2 * Math.PI / response;
        var remaining = dt;
        var moved = false;
        while (remaining > 0)
        {
            var slice = Math.Min(remaining, 1.0 / 120);
            remaining -= slice;
            var x = Current - Target;
            if (Math.Abs(x) < 0.0005 && Math.Abs(Velocity) < 0.0005)
            {
                Current = Target;
                Velocity = 0;
                return moved;
            }

            if (zeta >= 0.999)
            {
                Current += (Target - Current) * (1 - Math.Exp(-slice / Math.Max(0.04, response / 5)));
                Velocity = 0;
                moved = true;
                continue;
            }

            var wd = omega * Math.Sqrt(Math.Max(0, 1 - zeta * zeta));
            var exp = Math.Exp(-zeta * omega * slice);
            var a = x;
            var b = wd > 1e-9 ? (Velocity + zeta * omega * x) / wd : 0;
            var newX = exp * (a * Math.Cos(wd * slice) + b * Math.Sin(wd * slice));
            var newV = -zeta * omega * newX
                + exp * (-a * wd * Math.Sin(wd * slice) + b * wd * Math.Cos(wd * slice));
            Current = Target + newX;
            Velocity = newV;
            moved = true;
        }
        return moved;
    }
}
