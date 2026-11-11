using CourseStudio.Core.Models;

namespace CourseStudio.Core.Editing;

/// <summary>
/// Generic undoable mutation: keeps a before-snapshot and the mutation delegate.
/// Apply runs the mutation on the live project; Revert restores the exact prior
/// arrangement (used for camera hide and every other edit).
/// </summary>
public sealed class SnapshotEdit : IEditCommand
{
    private readonly CourseProject _before;
    private readonly Action<CourseProject> _mutate;

    public string Description { get; }

    public SnapshotEdit(string description, CourseProject before, CourseProject placeholder, Action<CourseProject> mutate)
    {
        Description = description;
        _before = before.Clone();
        _mutate = mutate;
    }

    public void Apply(CourseProject project) => _mutate(project);

    public void Revert(CourseProject project) => CopyState(_before, project);

    internal static void CopyState(CourseProject from, CourseProject to)
    {
        to.Name = from.Name;
        to.CanvasWidth = from.CanvasWidth;
        to.CanvasHeight = from.CanvasHeight;
        to.FrameRate = from.FrameRate;
        to.BackgroundHex = from.BackgroundHex;
        to.Pip = from.Pip.Clone();
        to.ScreenLocked = from.ScreenLocked;
        to.CameraLocked = from.CameraLocked;
        to.AudioLocked = from.AudioLocked;
        to.Sources = from.Sources.Select(s => new MediaSource
        {
            Id = s.Id, Kind = s.Kind, FilePath = s.FilePath,
            NaturalWidth = s.NaturalWidth, NaturalHeight = s.NaturalHeight, Duration = s.Duration
        }).ToList();
        to.ScreenClips = from.ScreenClips.Select(c => (ScreenClip)c.Clone()).ToList();
        to.CameraClips = from.CameraClips.Select(c => (CameraClip)c.Clone()).ToList();
        to.AudioClips = from.AudioClips.Select(c => (AudioClip)c.Clone()).ToList();
        to.MuteRegions = from.MuteRegions.Select(m => m.Clone()).ToList();
    }
}
