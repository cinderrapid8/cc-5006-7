namespace CourseStudio.Core.Models;

/// <summary>Timeline position or duration in microseconds. Integer math keeps edits drift-free.</summary>
public readonly record struct TimeCode(long Micros)
{
    public static readonly TimeCode Zero = new(0);

    public double TotalSeconds => Micros / 1_000_000.0;

    public static TimeCode FromSeconds(double seconds) => new((long)Math.Round(seconds * 1_000_000.0));

    public static TimeCode FromMillis(long ms) => new(ms * 1000);

    public static TimeCode operator +(TimeCode a, TimeCode b) => new(a.Micros + b.Micros);
    public static TimeCode operator -(TimeCode a, TimeCode b) => new(a.Micros - b.Micros);

    public string ToDisplay()
    {
        var totalMs = Micros / 1000;
        var ms = totalMs % 1000;
        var s = (totalMs / 1000) % 60;
        var m = totalMs / 60000;
        return $"{m:00}:{s:00}.{ms:000}";
    }
}
