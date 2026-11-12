using CourseStudio.TestKit;
using Xunit;

namespace CourseStudio.Acceptance;

/// <summary>
/// Positive and negative samples organized around the PIP window ratio: the same
/// lesson exported with ratio 0.25 and 0.40 must place the camera window at two
/// exactly predicted rectangles, and each export must NOT match the other's
/// expectation (a swap would go unnoticed otherwise).
/// </summary>
[Collection(AcceptanceCollection.Name)]
public class PipWindowRatioTests
{
    private readonly AcceptanceFixture _fx;

    public PipWindowRatioTests(AcceptanceFixture fixture) => _fx = fixture;

    [Fact]
    public void RatioChange_MovesPipExtent_ToTheDerivedRectangles()
    {
        if (!_fx.Enabled) return;
        var mainRef = new ReferenceCompositor(_fx.MainReopenedProject, _fx.Media);
        var wideRef = new ReferenceCompositor(_fx.WidePipReopenedProject, _fx.Media);

        // Independently derived rectangles: 0.25 -> 320x240 at (936,456);
        // 0.40 -> 512x384 at (744,312).
        Assert.Equal((936, 456, 320, 240), mainRef.ExpectedPipRect());
        Assert.Equal((744, 312, 512, 384), wideRef.ExpectedPipRect());

        const int n = 25; // t=2.55s, camera block 1
        var t = n / (double)LessonSpec.Fps + 0.05;
        var mainFrames = CompositeFrameReader.Open(_fx.MainExport,
            LessonSpec.CanvasWidth, LessonSpec.CanvasHeight, new[] { n });
        var wideFrames = CompositeFrameReader.Open(_fx.WidePipExport,
            LessonSpec.CanvasWidth, LessonSpec.CanvasHeight, new[] { n });

        // Positive samples: every probe matches its own export's reference.
        foreach (var (x, y) in Probes())
        {
            ColorAssert.Close(mainRef.ExpectedPixel(x, y, t), mainFrames.Pixel(n, x, y), 12,
                $"ratio 0.25 export at ({x},{y})");
            ColorAssert.Close(wideRef.ExpectedPixel(x, y, t), wideFrames.Pixel(n, x, y), 12,
                $"ratio 0.40 export at ({x},{y})");
        }

        // Differential probes: inside the wide rect but outside the main rect.
        foreach (var (x, y) in new[] { (900, 400), (760, 330), (745, 313) })
        {
            var inMain = mainFrames.Pixel(n, x, y);
            var inWide = wideFrames.Pixel(n, x, y);
            Assert.True(ColorAssert.Distance(inMain, inWide) > 40,
                $"({x},{y}) must flip between ratios: main rgb({inMain.R},{inMain.G},{inMain.B}) " +
                $"vs wide rgb({inWide.R},{inWide.G},{inWide.B})");
            ColorAssert.Close(LessonSpec.ScreenABackground, inMain, 12,
                $"({x},{y}) is plain screen in the 0.25 export");
        }

        // Negative sample: the wide export must NOT satisfy the main export's
        // reference at the differential probes (guards against swapped artifacts).
        foreach (var (x, y) in new[] { (900, 400), (760, 330) })
        {
            var wrongExpectation = mainRef.ExpectedPixel(x, y, t);
            var actual = wideFrames.Pixel(n, x, y);
            Assert.True(ColorAssert.Distance(actual, wrongExpectation) > 40,
                $"wide export unexpectedly matches the 0.25 reference at ({x},{y})");
        }
    }

    [Fact]
    public void CameraTimeMarker_StaysReadable_AtBothRatios()
    {
        if (!_fx.Enabled) return;
        const int n = 45; // t=4.55s, camera source frame 35
        var mainRef = new ReferenceCompositor(_fx.MainReopenedProject, _fx.Media);
        var wideRef = new ReferenceCompositor(_fx.WidePipReopenedProject, _fx.Media);
        var mainFrames = CompositeFrameReader.Open(_fx.MainExport,
            LessonSpec.CanvasWidth, LessonSpec.CanvasHeight, new[] { n });
        var wideFrames = CompositeFrameReader.Open(_fx.WidePipExport,
            LessonSpec.CanvasWidth, LessonSpec.CanvasHeight, new[] { n });

        Assert.Equal(35, mainFrames.DecodeStrip(n, mainRef.CameraStripSamplePoints()));
        Assert.Equal(35, wideFrames.DecodeStrip(n, wideRef.CameraStripSamplePoints()));
    }

    private static IEnumerable<(int X, int Y)> Probes()
    {
        yield return (640, 360);   // shared screen center
        yield return (1000, 500);  // inside both rects
        yield return (1100, 650);  // inside main, inside wide
        yield return (900, 400);   // inside wide only
        yield return (760, 330);   // inside wide only
        yield return (130, 700);   // screen in both
        yield return (20, 20);     // screen corner in both
    }
}
