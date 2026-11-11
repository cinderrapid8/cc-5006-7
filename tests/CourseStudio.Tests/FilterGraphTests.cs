using CourseStudio.Core.Models;
using CourseStudio.Core.Rendering;
using Xunit;

namespace CourseStudio.Tests;

public class FilterGraphTests
{
    [Fact]
    public void Graph_UsesNativeSourceSize_NotCanvasOrPreviewSize()
    {
        var (p, screenA, _, _, _) = ProjectFactory.BuildWithThreeTracks();
        var graph = new FilterGraphBuilder(p).Build();

        // The 1920x1080 source is fitted against the 1280x720 canvas; preview scale is irrelevant.
        Assert.Contains(graph.FilterLines, l => l.Contains("scale=w=1280:h=720"));
        Assert.DoesNotContain(graph.FilterLines, l => l.Contains("960")); // preview maxWidth never leaks
        // Four unique source files feed the graph (two screen resolutions, camera, audio).
        Assert.Equal(4, graph.Inputs.Distinct().Count());
        Assert.Equal(4, graph.Inputs.Count);
    }

    [Fact]
    public void HiddenCamera_IsExcludedFromGraph_ButKeepsTimelineClip()
    {
        var (p, _, _, _, _) = ProjectFactory.BuildWithThreeTracks();
        var before = new FilterGraphBuilder(p).Build();
        Assert.Contains(before.FilterLines, l => l.Contains("overlay"));

        p.CameraClips[0].Hidden = true;
        var hidden = new FilterGraphBuilder(p).Build();
        Assert.DoesNotContain(hidden.FilterLines, l => l.Contains("overlay"));
        Assert.Single(p.CameraClips); // still on the timeline
    }

    [Fact]
    public void Graph_PlacesTracksAtIndependentStarts_AndAppliesMuteEnvelope()
    {
        var (p, _, _, _, _) = ProjectFactory.BuildWithThreeTracks();
        p.MuteRegions.Add(new MuteRegion
        {
            Start = TimeCode.FromSeconds(2),
            End = TimeCode.FromSeconds(4)
        });
        var graph = new FilterGraphBuilder(p).Build();

        // Camera overlay enabled between its own start (2s) and end (11s).
        Assert.Contains(graph.FilterLines, l => l.Contains("gte(t,2)") && l.Contains("lt(t,11)"));
        // Audio delayed to its independent 1s start and muted over 2-4s.
        Assert.Contains(graph.FilterLines, l => l.Contains("adelay=delays=1000:all=1"));
        Assert.Contains(graph.FilterLines, l => l.Contains("between(t") && l.Contains("volume"));
        Assert.Single(graph.AudioMapLabels);
    }

    [Fact]
    public void ResolutionChangeAcrossClips_BuildsSeparateScaleChains()
    {
        var (p, screenA, screenB, _, _) = ProjectFactory.BuildWithThreeTracks();
        Assert.NotEqual(screenA.NaturalWidth, screenB.NaturalWidth);

        var graph = new FilterGraphBuilder(p).Build();
        var fitLines = graph.FilterLines
            .Where(l => l.Contains("scale=w=1280:h=720"))
            .ToList();
        Assert.True(fitLines.Count >= 2);
        // Both screen chains converge onto the same canvas size; the camera PIP scale is separate.
        Assert.All(fitLines, l => Assert.Contains("1280:h=720", l));
    }

    [Fact]
    public void MissingAudioSegments_StillExportVideoWithoutCrashingGraph()
    {
        var (p, _, _, _, _) = ProjectFactory.BuildWithThreeTracks();
        p.AudioClips.Clear(); // audio track gap: voice missing entirely
        var graph = new FilterGraphBuilder(p).Build();
        Assert.NotNull(graph.VideoMapLabel);
        Assert.Empty(graph.AudioMapLabels);
    }
}
