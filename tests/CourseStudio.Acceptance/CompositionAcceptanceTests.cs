using CourseStudio.TestKit;
using Xunit;

namespace CourseStudio.Acceptance;

/// <summary>
/// Positive acceptance of the full lesson: from screen + camera + voice sources
/// through save/reopen/export to the finished course video. Assertions read the
/// exported composite pixels and the decoded audio directly and compare them
/// against the independently derived reference model.
/// </summary>
[Collection(AcceptanceCollection.Name)]
public class CompositionAcceptanceTests
{
    private readonly AcceptanceFixture _fx;

    public CompositionAcceptanceTests(AcceptanceFixture fixture) => _fx = fixture;

    // Mid-frame timeline positions; covers screen A, screen B (input size change),
    // camera on/off and the under-PIP marker zone.
    private static readonly int[] SampledFrames = { 5, 15, 25, 45, 65, 85, 110 };

    [Fact]
    public void CompositePixels_MatchReference_AtPositionOcclusionAndMarkerProbes()
    {
        if (!_fx.Enabled) return;
        var reference = new ReferenceCompositor(_fx.MainReopenedProject, _fx.Media);
        var pip = reference.ExpectedPipRect();
        var frames = CompositeFrameReader.Open(_fx.MainExport,
            LessonSpec.CanvasWidth, LessonSpec.CanvasHeight, SampledFrames);

        foreach (var n in SampledFrames)
        {
            var t = n / (double)LessonSpec.Fps + 0.05;
            var probes = new Dictionary<string, (int X, int Y)>
            {
                ["screen edge border"] = (2, 2),
                ["screen center"] = (640, 360),
                ["under-pip marker zone"] = (1050, 550),
                ["pip interior near corner"] = (pip.X + 8, pip.Y + 8),
                ["pip interior far corner"] = (pip.X + pip.W - 8, pip.Y + pip.H - 8),
                ["one pixel left of pip"] = (pip.X - 2, pip.Y + pip.H / 2),
                ["one pixel right of pip"] = (pip.X + pip.W + 1, pip.Y + pip.H / 2),
                ["lower-left screen background"] = (100, 650),
                ["letterbox left bar"] = (10, 360),
                ["letterbox right bar"] = (1270, 360),
            };
            foreach (var (label, (x, y)) in probes)
            {
                var expected = reference.ExpectedPixel(x, y, t);
                var actual = frames.Pixel(n, x, y);
                ColorAssert.Close(expected, actual, 12,
                    $"frame {n} t={t:0.00}s probe '{label}' at ({x},{y})");
            }
        }
    }

    [Fact]
    public void CameraOccludesScreen_OnlyInsidePipWindow()
    {
        if (!_fx.Enabled) return;
        var reference = new ReferenceCompositor(_fx.MainReopenedProject, _fx.Media);
        var frames = CompositeFrameReader.Open(_fx.MainExport,
            LessonSpec.CanvasWidth, LessonSpec.CanvasHeight, new[] { 5, 15 });

        // t=0.55s: camera clip has not started — the marker under the PIP zone is visible.
        var before = frames.Pixel(5, 1050, 550);
        ColorAssert.Close(LessonSpec.UnderPipA, before, 12, "marker visible before camera starts");

        // t=1.55s: camera covers the marker — palette block 0 replaces it (occlusion).
        var during = frames.Pixel(15, 1050, 550);
        ColorAssert.Close(LessonSpec.CameraPalette[0], during, 12, "camera occludes marker");

        // The same instant, one pixel outside the PIP window is still pure screen.
        var pip = reference.ExpectedPipRect();
        var outside = frames.Pixel(15, pip.X - 2, pip.Y + pip.H / 2);
        var expectedOutside = reference.ExpectedPixel(pip.X - 2, pip.Y + pip.H / 2, 1.55);
        ColorAssert.Close(expectedOutside, outside, 12, "screen intact outside pip");
    }

    [Fact]
    public void TimeMarkers_IdentifyExactSourceFrames_InComposite()
    {
        if (!_fx.Enabled) return;
        var reference = new ReferenceCompositor(_fx.MainReopenedProject, _fx.Media);
        var frames = CompositeFrameReader.Open(_fx.MainExport,
            LessonSpec.CanvasWidth, LessonSpec.CanvasHeight, SampledFrames);

        foreach (var n in SampledFrames)
        {
            var t = n / (double)LessonSpec.Fps + 0.05;
            var expectedScreen = reference.ExpectedScreenSourceFrame(t);
            Assert.True(expectedScreen is not null, $"lesson has no screen gap at t={t:0.00}s");
            var decodedScreen = frames.DecodeStrip(n, reference.ScreenStripSamplePoints(t));
            Assert.True(decodedScreen == expectedScreen.Value,
                $"screen time marker mismatch at t={t:0.00}s: composite shows source frame " +
                $"{decodedScreen}, expected {expectedScreen.Value}");

            var expectedCamera = reference.ExpectedCameraSourceFrame(t);
            if (expectedCamera is not null)
            {
                var decodedCamera = frames.DecodeStrip(n, reference.CameraStripSamplePoints());
                Assert.True(decodedCamera == expectedCamera.Value,
                    $"camera time marker mismatch at t={t:0.00}s: composite shows source frame " +
                    $"{decodedCamera}, expected {expectedCamera.Value}");
            }
        }
    }

    [Fact]
    public void InputSizeChange_LetterboxesSecondSegment_AndKeepsFirstFullFrame()
    {
        if (!_fx.Enabled) return;
        var reference = new ReferenceCompositor(_fx.MainReopenedProject, _fx.Media);
        var frames = CompositeFrameReader.Open(_fx.MainExport,
            LessonSpec.CanvasWidth, LessonSpec.CanvasHeight, new[] { 25, 65 });
        var bar = ReferenceCompositor.ParseHex(LessonSpec.ClipBackgroundHex);

        // Segment A (1920x1080, same aspect as canvas): no bars, content reaches the edges.
        ColorAssert.Close(LessonSpec.EdgeBorder, frames.Pixel(25, 2, 2), 12,
            "segment A content reaches the frame corner");
        ColorAssert.Close(LessonSpec.EdgeBorder, frames.Pixel(25, 1277, 717), 12,
            "segment A content reaches the bottom-right corner");

        // Segment B (1024x768): 160px bars left/right filled with the clip background.
        ColorAssert.Close(bar, frames.Pixel(65, 10, 360), 12, "left letterbox bar");
        ColorAssert.Close(bar, frames.Pixel(65, 1270, 360), 12, "right letterbox bar");
        ColorAssert.Close(bar, frames.Pixel(65, 159, 360), 12, "bar ends before x=160");
        var content = frames.Pixel(65, 161, 360);
        Assert.True(ColorAssert.Distance(content, bar) > 30,
            $"x=161 must be screen B content, not bar color (got {content})");

        // Negative sample: if the pipeline had reused segment A geometry, the bars
        // would not exist — prove the two segments really differ at the same probe.
        var segAAtBar = frames.Pixel(25, 10, 360);
        Assert.True(ColorAssert.Distance(segAAtBar, bar) > 30,
            "segment A must not show letterbox bars (geometry leak between inputs)");
    }

    [Fact]
    public void ExportedProduct_PassesIntegrityChecks()
    {
        if (!_fx.Enabled) return;
        var report = ProductIntegrity.Inspect(_fx.MainExport);
        Assert.True(report.FileExists, "export file missing");
        Assert.True(report.FileSize > 50_000, $"export suspiciously small: {report.FileSize} bytes");
        Assert.True(report.Probed, $"ffprobe failed: {report.Error}");
        Assert.Equal(LessonSpec.CanvasWidth, report.Width);
        Assert.Equal(LessonSpec.CanvasHeight, report.Height);
        Assert.True(report.HasVideo, "no video stream");
        Assert.True(report.HasAudio, "no audio stream");
        Assert.True(Math.Abs(report.DurationSeconds - LessonSpec.TimelineSeconds) <= 0.35,
            $"duration {report.DurationSeconds:0.00}s != {LessonSpec.TimelineSeconds}s");
        Assert.Equal(LessonSpec.TimelineFrames, report.VideoFrameCount);
        Assert.True(report.MoovBeforeMdat, "moov atom must precede mdat (faststart)");
    }

    [Fact]
    public async Task ReopenedProject_RendersIdenticalVideo_ToInMemoryProject()
    {
        if (!_fx.Enabled) return;
        var inMemory = Path.Combine(_fx.WorkDir, "lesson-inmemory.mp4");
        await new CourseStudio.Core.Rendering.VideoExporter(FfmpegTools.Ffmpeg).ExportAsync(
            _fx.TemplateProject, inMemory,
            new CourseStudio.Core.Rendering.ExportOptions { Preset = "ultrafast", Crf = 18 });

        var hashReopened = await DecodedVideoMd5(_fx.MainExport);
        var hashInMemory = await DecodedVideoMd5(inMemory);
        Assert.Equal(hashInMemory, hashReopened);
    }

    private static async Task<string> DecodedVideoMd5(string video)
    {
        var result = await CourseStudio.Core.Media.ProcessRunner.RunAsync(FfmpegTools.Ffmpeg,
            $"-i \"{video}\" -an -f md5 -");
        Assert.True(result.Success, result.StdErr);
        return result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries).Last().Trim();
    }
}

internal static class ColorAssert
{
    public static int Distance((int R, int G, int B) a, (int R, int G, int B) b) =>
        Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

    public static int Distance((byte R, byte G, byte B) a, (int R, int G, int B) b) =>
        Distance(((int)a.R, (int)a.G, (int)a.B), b);

    public static void Close((int R, int G, int B) expected, (byte R, byte G, byte B) actual,
        int tolerance, string because)
    {
        var distance = Distance(actual, expected);
        Assert.True(distance <= tolerance,
            $"{because}: expected rgb({expected.R},{expected.G},{expected.B}), " +
            $"got rgb({actual.R},{actual.G},{actual.B}), distance {distance} > {tolerance}");
    }

    public static void Close((int R, int G, int B) expected, (int R, int G, int B) actual,
        int tolerance, string because) =>
        Close(expected, ((byte)actual.R, (byte)actual.G, (byte)actual.B), tolerance, because);
}
