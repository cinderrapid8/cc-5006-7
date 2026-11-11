using CourseStudio.Core.Models;

namespace CourseStudio.Core.Editing;

/// <summary>An undoable mutation of a project.</summary>
public interface IEditCommand
{
    string Description { get; }
    void Apply(CourseProject project);
    void Revert(CourseProject project);
}

/// <summary>Snapshot-based undo/redo. Hiding the camera is reverted to the exact prior state.</summary>
public sealed class HistoryService
{
    private readonly Stack<(string description, CourseProject snapshot)> _undo = new();
    private readonly Stack<(string description, CourseProject snapshot)> _redo = new();

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string? LastAction => _undo.Count > 0 ? _undo.Peek().description : null;

    public event Action? Changed;

    public void Execute(CourseProject project, IEditCommand command)
    {
        var before = project.Clone();
        command.Apply(project);
        _undo.Push((command.Description, before));
        _redo.Clear();
        Changed?.Invoke();
    }

    public bool Undo(CourseProject project)
    {
        if (_undo.Count == 0) return false;
        var (description, before) = _undo.Pop();
        _redo.Push((description, project.Clone()));
        Restore(project, before);
        Changed?.Invoke();
        return true;
    }

    public bool Redo(CourseProject project)
    {
        if (_redo.Count == 0) return false;
        var (description, after) = _redo.Pop();
        _undo.Push((description, project.Clone()));
        Restore(project, after);
        Changed?.Invoke();
        return true;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        Changed?.Invoke();
    }

    private static void Restore(CourseProject target, CourseProject snapshot)
    {
        target.Name = snapshot.Name;
        target.CanvasWidth = snapshot.CanvasWidth;
        target.CanvasHeight = snapshot.CanvasHeight;
        target.FrameRate = snapshot.FrameRate;
        target.BackgroundHex = snapshot.BackgroundHex;
        target.Pip = snapshot.Pip.Clone();
        target.ScreenLocked = snapshot.ScreenLocked;
        target.CameraLocked = snapshot.CameraLocked;
        target.AudioLocked = snapshot.AudioLocked;
        target.Sources = snapshot.Sources.Select(s => new MediaSource
        {
            Id = s.Id, Kind = s.Kind, FilePath = s.FilePath,
            NaturalWidth = s.NaturalWidth, NaturalHeight = s.NaturalHeight, Duration = s.Duration
        }).ToList();
        target.ScreenClips = snapshot.ScreenClips.Select(c => (ScreenClip)c.Clone()).ToList();
        target.CameraClips = snapshot.CameraClips.Select(c => (CameraClip)c.Clone()).ToList();
        target.AudioClips = snapshot.AudioClips.Select(c => (AudioClip)c.Clone()).ToList();
        target.MuteRegions = snapshot.MuteRegions.Select(m => m.Clone()).ToList();
    }
}
