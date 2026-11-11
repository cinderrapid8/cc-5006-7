namespace CourseStudio.Core.Models;

public enum MediaKind
{
    Screen,
    Camera,
    Audio
}

/// <summary>How a screen source whose pixel size differs from the canvas is fitted.</summary>
public enum ResizeMode
{
    /// <summary>Scale uniformly until the whole frame fits; letterbox with canvas background.</summary>
    Fit,
    /// <summary>Scale uniformly to cover the canvas, cropping overflow.</summary>
    Fill,
    /// <summary>Map source pixels 1:1, cropping anything outside the canvas.</summary>
    Original
}

public enum PipCorner
{
    TopLeft,
    TopRight,
    BottomLeft,
    BottomRight
}

public enum TrackKind
{
    Screen,
    Camera,
    Audio
}
