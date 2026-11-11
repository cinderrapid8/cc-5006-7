namespace CourseStudio.Core.Models;

/// <summary>
/// A source media file on disk. NaturalWidth/Height are the file's own pixel dimensions,
/// independent of the output canvas and of any preview downscaling.
/// </summary>
public sealed class MediaSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public MediaKind Kind { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public int NaturalWidth { get; set; }
    public int NaturalHeight { get; set; }
    public TimeCode Duration { get; set; } = TimeCode.Zero;

    public bool HasVideo => Kind is MediaKind.Screen or MediaKind.Camera;
}
