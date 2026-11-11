using CourseStudio.Core.Models;

namespace CourseStudio.Core.Editing;

/// <summary>
/// Timeline semantics:
/// - Screen clips own the "wait time". Ripple-deleting a screen range cuts that time
///   out and shifts every selected track by the same delta, so sync is preserved.
/// - Locked selected tracks block the edit and are reported as conflicts; nothing moves.
/// - Mute regions only silence audio; they never delete time or move other tracks.
/// - Hiding a camera clip only changes compositing; the clip, its position and the
///   source file all remain, and undo restores the original layout.
/// - Pauses between screen clips are kept until the user explicitly cuts them.
/// </summary>
public sealed class EditEngine
{
    private readonly HistoryService _history;

    public EditEngine(HistoryService history) => _history = history;

    public EditResult RippleDelete(CourseProject project, TrackKind originTrack,
        TimeCode rangeStart, TimeCode rangeEnd, RippleOptions options)
    {
        if (rangeEnd.Micros <= rangeStart.Micros)
            return EditResult.Failed("Delete range must be non-empty.");

        var conflicts = CheckLockedTracks(project, options);
        if (conflicts.Count > 0)
            return EditResult.Failed("Edit blocked by locked track(s).", conflicts);

        var before = project.Clone();
        var delta = new TimeCode(rangeEnd.Micros - rangeStart.Micros);

        void ApplyTo(CourseProject p)
        {
            if (options.MoveScreen) RippleTrack(p.ScreenClips, rangeStart, rangeEnd, delta, p);
            if (options.MoveCamera) RippleTrack(p.CameraClips, rangeStart, rangeEnd, delta, p);
            if (options.MoveAudio)
            {
                RippleTrack(p.AudioClips, rangeStart, rangeEnd, delta, p);
                p.MuteRegions.RemoveAll(m => m.Start.Micros >= rangeStart.Micros && m.End.Micros <= rangeEnd.Micros);
                foreach (var m in p.MuteRegions)
                {
                    if (m.Start.Micros >= rangeEnd.Micros) m.Start = new TimeCode(m.Start.Micros - delta.Micros);
                    if (m.End.Micros >= rangeEnd.Micros) m.End = new TimeCode(m.End.Micros - delta.Micros);
                }
            }
        }

        var edit = new SnapshotEdit(
            $"Ripple delete {delta.TotalSeconds:0.###}s on {originTrack}", before, before, ApplyTo);
        _history.Execute(project, edit);
        return EditResult.Ok();
    }

    /// <summary>
    /// Clips fully inside the range are removed; clips crossing a boundary are split into
    /// head/tail pieces (timeline gaps preserved on the untouched side); clips starting
    /// after the range shift left by the range length together with their trailing gap.
    /// </summary>
    private static void RippleTrack<T>(List<T> clips, TimeCode rangeStart, TimeCode rangeEnd, TimeCode delta, CourseProject p)
        where T : Clip
    {
        var kept = new List<Clip>();
        foreach (var clip in clips)
        {
            var start = clip.Start.Micros;
            var end = clip.End.Micros;

            if (end <= rangeStart.Micros)
            {
                kept.Add(clip);
                continue;
            }
            if (start >= rangeEnd.Micros)
            {
                var shifted = start - delta.Micros;
                if (shifted < 0)
                {
                    // Clamp at zero and consume the overshoot from the head of the source
                    // range so the visible content still lands at the same project time.
                    var overshoot = -shifted;
                    clip.SourceIn = new TimeCode(Math.Min(clip.SourceOut.Micros - 1, clip.SourceIn.Micros + overshoot));
                    clip.Start = TimeCode.Zero;
                }
                else
                {
                    clip.Start = new TimeCode(shifted);
                }
                kept.Add(clip);
                continue;
            }

            var source = p.FindSource(clip.SourceId);
            var sourceDuration = source?.Duration.Micros ?? long.MaxValue;
            var headLen = Math.Max(0, rangeStart.Micros - start);
            var tailLen = Math.Max(0, end - rangeEnd.Micros);

            // Head piece: timeline position and source range unchanged.
            if (headLen > 0)
            {
                var head = (Clip)clip.Clone();
                head.SourceOut = new TimeCode(clip.SourceIn.Micros + headLen);
                kept.Add(head);
            }

            // Tail piece: source starts after the portion of the range this clip actually covered.
            if (tailLen > 0)
            {
                var tail = (Clip)clip.Clone();
                var coveredInsideClip = Math.Min(end, rangeEnd.Micros) - Math.Max(start, rangeStart.Micros);
                tail.SourceIn = new TimeCode(clip.SourceIn.Micros + headLen + coveredInsideClip);
                tail.SourceOut = new TimeCode(Math.Min(sourceDuration, tail.SourceIn.Micros + tailLen));
                tail.Start = new TimeCode(rangeStart.Micros);
                kept.Add(tail);
            }
        }
        clips.Clear();
        foreach (var item in kept) clips.Add((T)item);
    }

    public EditResult TrimClip(CourseProject project, Clip clip, TimeCode? newSourceIn, TimeCode? newSourceOut, TimeCode? newStart)
    {
        if (project.IsTrackLocked(clip.Track))
            return EditResult.Failed("Edit blocked by locked track.",
                new[] { new EditConflict(clip.Track, clip.Id, $"{clip.Track} track is locked.") });

        var source = project.FindSource(clip.SourceId);
        if (source is null) return EditResult.Failed("Clip source is missing from the project.");

        var inVal = newSourceIn ?? clip.SourceIn;
        var outVal = newSourceOut ?? clip.SourceOut;
        if (inVal.Micros < 0 || outVal.Micros <= inVal.Micros)
            return EditResult.Failed("Trim range is outside the source bounds.");
        if (inVal.Micros >= source.Duration.Micros || outVal.Micros > source.Duration.Micros)
            return EditResult.Failed("Trim range is outside the source bounds.");
        if (newStart is { } startVal && startVal.Micros < 0)
            return EditResult.Failed("Clip cannot start before zero.");

        var before = project.Clone();
        var targetId = clip.Id;
        var edit = new SnapshotEdit($"Trim {clip.Track} clip", before, before, p =>
        {
            var target = p.AllClips.First(c => c.Id == targetId);
            target.SourceIn = inVal;
            target.SourceOut = outVal;
            if (newStart is { } s) target.Start = s;
        });
        _history.Execute(project, edit);
        return EditResult.Ok();
    }

    public EditResult MoveClip(CourseProject project, Clip clip, TimeCode newStart)
    {
        if (project.IsTrackLocked(clip.Track))
            return EditResult.Failed("Edit blocked by locked track.",
                new[] { new EditConflict(clip.Track, clip.Id, $"{clip.Track} track is locked.") });
        if (newStart.Micros < 0)
            return EditResult.Failed("Clip cannot start before zero.");

        var before = project.Clone();
        var targetId = clip.Id;
        var edit = new SnapshotEdit($"Move {clip.Track} clip", before, before, p =>
        {
            p.AllClips.First(c => c.Id == targetId).Start = newStart;
        });
        _history.Execute(project, edit);
        return EditResult.Ok();
    }

    /// <summary>Add a mute region. No time is removed; screen operations keep their positions.</summary>
    public EditResult AddMuteRegion(CourseProject project, TimeCode start, TimeCode end)
    {
        if (project.AudioLocked)
            return EditResult.Failed("Edit blocked by locked track.",
                new[] { new EditConflict(TrackKind.Audio, string.Empty, "Audio track is locked.") });
        if (end.Micros <= start.Micros)
            return EditResult.Failed("Mute region must be non-empty.");

        var before = project.Clone();
        var edit = new SnapshotEdit("Add mute region", before, before, p =>
        {
            p.MuteRegions.Add(new MuteRegion { Start = start, End = end });
        });
        _history.Execute(project, edit);
        return EditResult.Ok();
    }

    public EditResult RemoveMuteRegion(CourseProject project, MuteRegion region)
    {
        if (project.AudioLocked)
            return EditResult.Failed("Edit blocked by locked track.",
                new[] { new EditConflict(TrackKind.Audio, region.Id, "Audio track is locked.") });

        var before = project.Clone();
        var edit = new SnapshotEdit("Remove mute region", before, before, p =>
        {
            p.MuteRegions.RemoveAll(m => m.Id == region.Id);
        });
        _history.Execute(project, edit);
        return EditResult.Ok();
    }

    /// <summary>Hide/show the camera PIP for one clip. Composition-only: source and timeline stay intact.</summary>
    public EditResult SetCameraHidden(CourseProject project, CameraClip clip, bool hidden)
    {
        if (project.CameraLocked)
            return EditResult.Failed("Edit blocked by locked track.",
                new[] { new EditConflict(TrackKind.Camera, clip.Id, "Camera track is locked.") });

        var before = project.Clone();
        var targetId = clip.Id;
        var edit = new SnapshotEdit(hidden ? "Hide camera" : "Show camera", before, before, p =>
        {
            if (p.CameraClips.FirstOrDefault(c => c.Id == targetId) is { } current)
                current.Hidden = hidden;
        });
        _history.Execute(project, edit);
        return EditResult.Ok();
    }

    public EditResult UpdatePip(CourseProject project, PipSettings settings)
    {
        var before = project.Clone();
        var edit = new SnapshotEdit("Update picture-in-picture", before, before, p =>
        {
            p.Pip = settings.Clone();
        });
        _history.Execute(project, edit);
        return EditResult.Ok();
    }

    public EditResult AddClip(CourseProject project, Clip clip)
    {
        if (project.IsTrackLocked(clip.Track))
            return EditResult.Failed("Edit blocked by locked track.",
                new[] { new EditConflict(clip.Track, clip.Id, $"{clip.Track} track is locked.") });

        var before = project.Clone();
        var edit = new SnapshotEdit($"Add {clip.Track} clip", before, before, p =>
        {
            switch (clip)
            {
                case ScreenClip s: p.ScreenClips.Add(s); break;
                case CameraClip c: p.CameraClips.Add(c); break;
                case AudioClip a: p.AudioClips.Add(a); break;
            }
        });
        _history.Execute(project, edit);
        return EditResult.Ok();
    }

    public EditResult DeleteClip(CourseProject project, Clip clip)
    {
        if (project.IsTrackLocked(clip.Track))
            return EditResult.Failed("Edit blocked by locked track.",
                new[] { new EditConflict(clip.Track, clip.Id, $"{clip.Track} track is locked.") });

        var before = project.Clone();
        var targetId = clip.Id;
        var edit = new SnapshotEdit($"Delete {clip.Track} clip", before, before, p =>
        {
            switch (clip.Track)
            {
                case TrackKind.Screen: p.ScreenClips.RemoveAll(c => c.Id == targetId); break;
                case TrackKind.Camera: p.CameraClips.RemoveAll(c => c.Id == targetId); break;
                case TrackKind.Audio: p.AudioClips.RemoveAll(c => c.Id == targetId); break;
            }
        });
        _history.Execute(project, edit);
        return EditResult.Ok();
    }

    private static List<EditConflict> CheckLockedTracks(CourseProject project, RippleOptions options)
    {
        var conflicts = new List<EditConflict>();
        if (options.MoveScreen && project.ScreenLocked)
            conflicts.Add(new EditConflict(TrackKind.Screen, string.Empty, "Screen track is locked."));
        if (options.MoveCamera && project.CameraLocked)
            conflicts.Add(new EditConflict(TrackKind.Camera, string.Empty, "Camera track is locked."));
        if (options.MoveAudio && project.AudioLocked)
            conflicts.Add(new EditConflict(TrackKind.Audio, string.Empty, "Audio track is locked."));
        return conflicts;
    }
}
