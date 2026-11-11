namespace CourseStudio.Core.Models;

/// <summary>
/// The whole editable course project. Output canvas size is deliberately separate
/// from source pixel sizes: sources keep their native resolution and are sampled
/// at full resolution during export regardless of preview scaling.
/// </summary>
public sealed class CourseProject
{
    public string Name { get; set; } = "Untitled course";
    public int CanvasWidth { get; set; } = 1280;
    public int CanvasHeight { get; set; } = 720;
    public int FrameRate { get; set; } = 30;
    public string BackgroundHex { get; set; } = "#101014";

    public PipSettings Pip { get; set; } = new();

    /// <summary>Locked tracks reject edit operations and produce a conflict report instead.</summary>
    public bool ScreenLocked { get; set; }
    public bool CameraLocked { get; set; }
    public bool AudioLocked { get; set; }

    public List<MediaSource> Sources { get; set; } = new();
    public List<ScreenClip> ScreenClips { get; set; } = new();
    public List<CameraClip> CameraClips { get; set; } = new();
    public List<AudioClip> AudioClips { get; set; } = new();
    public List<MuteRegion> MuteRegions { get; set; } = new();

    public bool IsTrackLocked(TrackKind kind) => kind switch
    {
        TrackKind.Screen => ScreenLocked,
        TrackKind.Camera => CameraLocked,
        TrackKind.Audio => AudioLocked,
        _ => false
    };

    public IEnumerable<Clip> AllClips =>
        ScreenClips.Cast<Clip>().Concat(CameraClips).Concat(AudioClips);

    public MediaSource? FindSource(string id) => Sources.FirstOrDefault(s => s.Id == id);

    public TimeCode TimelineDuration
    {
        get
        {
            long end = 0;
            foreach (var c in AllClips)
            {
                end = Math.Max(end, c.End.Micros);
            }
            return new TimeCode(end);
        }
    }

    public CourseProject Clone()
    {
        return new CourseProject
        {
            Name = Name,
            CanvasWidth = CanvasWidth,
            CanvasHeight = CanvasHeight,
            FrameRate = FrameRate,
            BackgroundHex = BackgroundHex,
            Pip = Pip.Clone(),
            ScreenLocked = ScreenLocked,
            CameraLocked = CameraLocked,
            AudioLocked = AudioLocked,
            Sources = Sources.Select(s => new MediaSource
            {
                Id = s.Id,
                Kind = s.Kind,
                FilePath = s.FilePath,
                NaturalWidth = s.NaturalWidth,
                NaturalHeight = s.NaturalHeight,
                Duration = s.Duration
            }).ToList(),
            ScreenClips = ScreenClips.Select(c => (ScreenClip)c.Clone()).ToList(),
            CameraClips = CameraClips.Select(c => (CameraClip)c.Clone()).ToList(),
            AudioClips = AudioClips.Select(c => (AudioClip)c.Clone()).ToList(),
            MuteRegions = MuteRegions.Select(m => m.Clone()).ToList()
        };
    }
}
