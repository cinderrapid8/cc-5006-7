using CourseStudio.Core.Editing;
using CourseStudio.Core.Models;
using Xunit;

namespace CourseStudio.Tests;

public static class ProjectFactory
{
    public static (CourseProject project, MediaSource screenA, MediaSource screenB,
        MediaSource camera, MediaSource audio) BuildWithThreeTracks()
    {
        var p = new CourseProject
        {
            Name = "Sync test",
            CanvasWidth = 1280,
            CanvasHeight = 720,
            FrameRate = 30
        };
        var screenA = new MediaSource
        {
            Kind = MediaKind.Screen, FilePath = "screen-a.mp4",
            NaturalWidth = 1920, NaturalHeight = 1080, Duration = TimeCode.FromSeconds(10)
        };
        var screenB = new MediaSource
        {
            Kind = MediaKind.Screen, FilePath = "screen-b.mp4",
            NaturalWidth = 1280, NaturalHeight = 720, Duration = TimeCode.FromSeconds(6)
        };
        var camera = new MediaSource
        {
            Kind = MediaKind.Camera, FilePath = "cam.mp4",
            NaturalWidth = 640, NaturalHeight = 480, Duration = TimeCode.FromSeconds(20)
        };
        var audio = new MediaSource
        {
            Kind = MediaKind.Audio, FilePath = "voice.wav",
            Duration = TimeCode.FromSeconds(20)
        };
        p.Sources.AddRange(new[] { screenA, screenB, camera, audio });

        // Screen: 0-6s clip A, pause 6-8s, 8-12s clip B.
        p.ScreenClips.Add(new ScreenClip
        {
            SourceId = screenA.Id,
            Start = TimeCode.Zero,
            SourceIn = TimeCode.Zero,
            SourceOut = TimeCode.FromSeconds(6)
        });
        p.ScreenClips.Add(new ScreenClip
        {
            SourceId = screenB.Id,
            Start = TimeCode.FromSeconds(8),
            SourceIn = TimeCode.Zero,
            SourceOut = TimeCode.FromSeconds(4)
        });

        // Camera with its own independent start (2s) and microphone at 1s.
        p.CameraClips.Add(new CameraClip
        {
            SourceId = camera.Id,
            Start = TimeCode.FromSeconds(2),
            SourceIn = TimeCode.Zero,
            SourceOut = TimeCode.FromSeconds(9)
        });
        p.AudioClips.Add(new AudioClip
        {
            SourceId = audio.Id,
            Start = TimeCode.FromSeconds(1),
            SourceIn = TimeCode.Zero,
            SourceOut = TimeCode.FromSeconds(10)
        });

        return (p, screenA, screenB, camera, audio);
    }
}

public class RippleEditTests
{
    [Fact]
    public void RippleDelete_KeepsPauses_UnlessRangeCoversThem()
    {
        var (p, _, screenB, _, _) = ProjectFactory.BuildWithThreeTracks();
        var history = new HistoryService();
        var engine = new EditEngine(history);

        // Deleting 3-5s inside clip A removes 2s of content; the 6-8s pause is untouched,
        // so clip B shifts from 8s to 6s while the gap after clip A's tail remains.
        var result = engine.RippleDelete(p, TrackKind.Screen,
            TimeCode.FromSeconds(3), TimeCode.FromSeconds(5), RippleOptions.All);

        Assert.True(result.Success);
        var b = p.ScreenClips.Single(c => c.SourceId == screenB.Id);
        Assert.Equal(6.0, b.Start.TotalSeconds, 3);
        Assert.Equal(10.0, p.TimelineDuration.TotalSeconds, 3);

        // Clip A is split around the cut: head keeps source 0..3 at timeline 0, tail keeps
        // source 5..6 (the 3..5 section is gone) placed at timeline 3.
        var aPieces = p.ScreenClips.Where(c => c.SourceId != screenB.Id)
            .OrderBy(c => c.Start.Micros).ToList();
        Assert.Equal(2, aPieces.Count);
        Assert.Equal(0.0, aPieces[0].Start.TotalSeconds, 3);
        Assert.Equal(0.0, aPieces[0].SourceIn.TotalSeconds, 3);
        Assert.Equal(3.0, aPieces[0].SourceOut.TotalSeconds, 3);
        Assert.Equal(3.0, aPieces[1].Start.TotalSeconds, 3);
        Assert.Equal(5.0, aPieces[1].SourceIn.TotalSeconds, 3);
        Assert.Equal(6.0, aPieces[1].SourceOut.TotalSeconds, 3);
    }

    [Fact]
    public void RippleDelete_MovesAllSelectedTracksBySameDelta()
    {
        var (p, _, _, _, _) = ProjectFactory.BuildWithThreeTracks();
        var engine = new EditEngine(new HistoryService());

        var camBefore = p.CameraClips[0].Start.TotalSeconds;
        var audioBefore = p.AudioClips[0].Start.TotalSeconds;

        var result = engine.RippleDelete(p, TrackKind.Screen,
            TimeCode.FromSeconds(3), TimeCode.FromSeconds(5), RippleOptions.All);

        Assert.True(result.Success);
        // Camera (start 2s) covers 1s of the cut: it splits into a 1s head at timeline 2
        // and a tail shifted to timeline 3. Audio (start 1s) covers the full 2s cut: its
        // tail is clamped to zero with overshoot consumed from source-in.
        var camPieces = p.CameraClips.OrderBy(c => c.Start.Micros).ToList();
        Assert.Equal(2, camPieces.Count);
        Assert.Equal(2.0, camPieces[0].Start.TotalSeconds, 3);
        Assert.Equal(1.0, camPieces[0].Duration.TotalSeconds, 3);
        Assert.Equal(3.0, camPieces[1].Start.TotalSeconds, 3);

        var audioPieces = p.AudioClips.OrderBy(c => c.Start.Micros).ToList();
        // Audio 1..11: head (1..3) stays at timeline 1, tail shifts left by 2s to timeline 3.
        Assert.Equal(2, audioPieces.Count);
        Assert.Equal(1.0, audioPieces[0].Start.TotalSeconds, 3);
        Assert.Equal(2.0, audioPieces[0].Duration.TotalSeconds, 3);
        Assert.Equal(3.0, audioPieces[1].Start.TotalSeconds, 3);
        Assert.Equal(4.0, audioPieces[1].SourceIn.TotalSeconds, 3);
    }

    [Fact]
    public void RippleDelete_RespectsTrackSelection_AndReportsLockConflicts()
    {
        var (p, _, screenB, _, _) = ProjectFactory.BuildWithThreeTracks();
        var engine = new EditEngine(new HistoryService());
        p.CameraLocked = true;

        // Selecting camera while it is locked must block everything and report the conflict.
        var blocked = engine.RippleDelete(p, TrackKind.Screen,
            TimeCode.FromSeconds(3), TimeCode.FromSeconds(5),
            new RippleOptions(true, true, true));

        Assert.False(blocked.Success);
        Assert.Contains(blocked.Conflicts, c => c.Track == TrackKind.Camera);
        // Nothing moved: clip B still starts at 8s and camera still has one clip at 2s.
        Assert.Equal(8.0, p.ScreenClips.Single(c => c.SourceId == screenB.Id).Start.TotalSeconds, 3);

        // With camera excluded from the ripple, edit succeeds and camera must NOT move.
        var camStart = p.CameraClips[0].Start.TotalSeconds;
        var ok = engine.RippleDelete(p, TrackKind.Screen,
            TimeCode.FromSeconds(3), TimeCode.FromSeconds(5),
            new RippleOptions(true, false, true));
        Assert.True(ok.Success);
        Assert.Equal(camStart, p.CameraClips[0].Start.TotalSeconds, 3);
        Assert.Equal(6.0, p.ScreenClips.Single(c => c.SourceId == screenB.Id).Start.TotalSeconds, 3);
    }

    [Fact]
    public void MuteRegion_DoesNotMoveAnyClip()
    {
        var (p, _, _, _, _) = ProjectFactory.BuildWithThreeTracks();
        var engine = new EditEngine(new HistoryService());

        var screenStart = p.ScreenClips[1].Start.Micros;
        var result = engine.AddMuteRegion(p, TimeCode.FromSeconds(2), TimeCode.FromSeconds(4));

        Assert.True(result.Success);
        Assert.Single(p.MuteRegions);
        Assert.Equal(screenStart, p.ScreenClips[1].Start.Micros);
        Assert.Equal(12.0, p.TimelineDuration.TotalSeconds, 3);
    }
}

public class CameraHideTests
{
    [Fact]
    public void HidingCamera_KeepsClipPositionAndSource()
    {
        var (p, _, _, camera, _) = ProjectFactory.BuildWithThreeTracks();
        var engine = new EditEngine(new HistoryService());
        var clip = p.CameraClips[0];
        var startBefore = clip.Start.Micros;

        var result = engine.SetCameraHidden(p, clip, true);

        Assert.True(result.Success);
        Assert.True(p.CameraClips[0].Hidden);
        Assert.Equal(startBefore, p.CameraClips[0].Start.Micros);
        Assert.Equal(camera.Id, p.CameraClips[0].SourceId);
    }

    [Fact]
    public void Undo_RestoresCameraVisibilityAndExactPosition()
    {
        var (p, _, _, _, _) = ProjectFactory.BuildWithThreeTracks();
        var history = new HistoryService();
        var engine = new EditEngine(history);
        var clip = p.CameraClips[0];
        var startBefore = clip.Start.Micros;

        engine.SetCameraHidden(p, clip, true);
        engine.MoveClip(p, p.CameraClips[0], TimeCode.FromSeconds(5));
        Assert.True(p.CameraClips[0].Hidden);
        Assert.Equal(5.0, p.CameraClips[0].Start.TotalSeconds, 3);

        Assert.True(history.Undo(p));
        Assert.True(p.CameraClips[0].Hidden);
        Assert.Equal(startBefore, p.CameraClips[0].Start.Micros);

        Assert.True(history.Undo(p));
        Assert.False(p.CameraClips[0].Hidden);
        Assert.Equal(startBefore, p.CameraClips[0].Start.Micros);
    }

    [Fact]
    public void Redo_ReappliesHide()
    {
        var (p, _, _, _, _) = ProjectFactory.BuildWithThreeTracks();
        var history = new HistoryService();
        var engine = new EditEngine(history);

        engine.SetCameraHidden(p, p.CameraClips[0], true);
        history.Undo(p);
        Assert.False(p.CameraClips[0].Hidden);
        history.Redo(p);
        Assert.True(p.CameraClips[0].Hidden);
    }
}

public class TrimTests
{
    [Fact]
    public void Trim_RejectsBoundsOutsideSource()
    {
        var (p, _, _, _, _) = ProjectFactory.BuildWithThreeTracks();
        var engine = new EditEngine(new HistoryService());

        var result = engine.TrimClip(p, p.ScreenClips[0],
            TimeCode.FromSeconds(-1), TimeCode.FromSeconds(3), null);
        Assert.False(result.Success);

        var tooLong = engine.TrimClip(p, p.ScreenClips[0],
            TimeCode.Zero, TimeCode.FromSeconds(99), null);
        Assert.False(tooLong.Success);
    }

    [Fact]
    public void Trim_LockedTrack_BlocksAndReportsConflict()
    {
        var (p, _, _, _, _) = ProjectFactory.BuildWithThreeTracks();
        p.AudioLocked = true;
        var engine = new EditEngine(new HistoryService());

        var result = engine.TrimClip(p, p.AudioClips[0],
            TimeCode.FromSeconds(1), TimeCode.FromSeconds(4), null);
        Assert.False(result.Success);
        Assert.Contains(result.Conflicts, c => c.Track == TrackKind.Audio);
    }
}
