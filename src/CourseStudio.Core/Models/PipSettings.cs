namespace CourseStudio.Core.Models;

/// <summary>Picture-in-picture placement of the camera layer over the screen canvas.</summary>
public sealed class PipSettings
{
    public PipCorner Corner { get; set; } = PipCorner.BottomRight;

    /// <summary>PIP width as a fraction (0..1) of the canvas width.</summary>
    public double WidthRatio { get; set; } = 0.25;

    /// <summary>Margin in canvas pixels.</summary>
    public int Margin { get; set; } = 24;

    /// <summary>Opacity 0..1 applied to the camera layer.</summary>
    public double Opacity { get; set; } = 1.0;

    public PipSettings Clone() => new()
    {
        Corner = Corner,
        WidthRatio = WidthRatio,
        Margin = Margin,
        Opacity = Opacity
    };
}
