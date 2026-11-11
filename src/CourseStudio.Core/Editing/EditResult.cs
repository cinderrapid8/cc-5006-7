using CourseStudio.Core.Models;

namespace CourseStudio.Core.Editing;

public sealed record EditConflict(TrackKind Track, string ClipId, string Message);

public sealed class EditResult
{
    public bool Success { get; }
    public List<EditConflict> Conflicts { get; } = new();
    public string? ErrorMessage { get; init; }

    private EditResult(bool success) => Success = success;

    public static EditResult Ok() => new(true);

    public static EditResult Failed(string message, IEnumerable<EditConflict>? conflicts = null)
    {
        var r = new EditResult(false) { ErrorMessage = message };
        if (conflicts is not null) r.Conflicts.AddRange(conflicts);
        return r;
    }
}

/// <summary>Which tracks take part in an edit. A locked selected track blocks the edit.</summary>
public sealed record RippleOptions(bool MoveScreen, bool MoveCamera, bool MoveAudio)
{
    public bool Includes(TrackKind kind) => kind switch
    {
        TrackKind.Screen => MoveScreen,
        TrackKind.Camera => MoveCamera,
        TrackKind.Audio => MoveAudio,
        _ => false
    };

    public static RippleOptions All => new(true, true, true);
}
