using CourseStudio.Core.Models;
using CourseStudio.Core.Persistence;
using Xunit;

namespace CourseStudio.Tests;

public class PersistenceTests
{
    [Fact]
    public void SaveThenLoad_RestoresFullEditingState()
    {
        var (project, _, _, _, _) = ProjectFactory.BuildWithThreeTracks();
        project.CameraLocked = true;
        project.CameraClips[0].Hidden = true;
        project.MuteRegions.Add(new MuteRegion
        {
            Start = TimeCode.FromSeconds(2),
            End = TimeCode.FromSeconds(3)
        });
        project.Pip.Corner = PipCorner.TopLeft;
        project.Pip.WidthRatio = 0.33;

        var dir = Path.Combine(Path.GetTempPath(), "coursestudio-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        // Touch media files so the missing-file check stays clean.
        foreach (var s in project.Sources) File.WriteAllText(Path.Combine(dir, s.FilePath), "x");
        // Make source paths absolute and inside the project dir.
        foreach (var s in project.Sources)
            s.FilePath = Path.Combine(dir, s.FilePath);

        var projectFile = Path.Combine(dir, "lesson.course.json");
        ProjectSerializer.Save(project, projectFile);
        var loaded = ProjectSerializer.Load(projectFile);

        Assert.Empty(loaded.MissingFiles);
        var p = loaded.Project;
        Assert.Equal(2, p.ScreenClips.Count);
        Assert.Single(p.CameraClips);
        Assert.Single(p.AudioClips);
        Assert.Single(p.MuteRegions);
        Assert.True(p.CameraLocked);
        Assert.True(p.CameraClips[0].Hidden);
        Assert.Equal(PipCorner.TopLeft, p.Pip.Corner);
        Assert.Equal(0.33, p.Pip.WidthRatio, 3);
        Assert.Equal(1280, p.CanvasWidth);
        // Independent starts preserved.
        Assert.Equal(2.0, p.CameraClips[0].Start.TotalSeconds, 3);
        Assert.Equal(1.0, p.AudioClips[0].Start.TotalSeconds, 3);
        // Resolution metadata preserved per source.
        Assert.Equal(1920, p.Sources.First(s => s.Kind == MediaKind.Screen && s.FilePath.Contains("screen-a")).NaturalWidth);
    }

    [Fact]
    public void Load_ReportsMissingMediaFiles_WithoutThrowing()
    {
        var (project, _, _, _, _) = ProjectFactory.BuildWithThreeTracks();
        var dir = Path.Combine(Path.GetTempPath(), "coursestudio-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var projectFile = Path.Combine(dir, "lesson.course.json");
        ProjectSerializer.Save(project, projectFile);

        var result = ProjectSerializer.Load(projectFile);
        Assert.Equal(project.Sources.Count, result.MissingFiles.Count);
        Assert.Equal(2, result.Project.ScreenClips.Count);
    }

    [Fact]
    public void ReopenedProject_ProducesIdenticalTimelineShape()
    {
        var (project, _, _, _, _) = ProjectFactory.BuildWithThreeTracks();
        var dir = Path.Combine(Path.GetTempPath(), "coursestudio-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var projectFile = Path.Combine(dir, "lesson.course.json");
        ProjectSerializer.Save(project, projectFile);

        var reopened = ProjectSerializer.Load(projectFile).Project;
        Assert.Equal(project.TimelineDuration.Micros, reopened.TimelineDuration.Micros);
        Assert.Equal(project.ScreenClips.Select(c => c.Start.Micros),
            reopened.ScreenClips.Select(c => c.Start.Micros));
        Assert.Equal(project.CameraClips.Select(c => c.Hidden),
            reopened.CameraClips.Select(c => c.Hidden));
    }
}
