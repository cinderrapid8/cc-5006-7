using CourseStudio.TestKit;
using Xunit;

namespace CourseStudio.Acceptance;

/// <summary>
/// Transparent-edge samples: with PIP opacity 0.5 the exported pixels inside the
/// window must be the 50/50 blend of camera and screen, and the blend must stop
/// exactly at the window edge — one pixel outside is pure screen.
/// </summary>
[Collection(AcceptanceCollection.Name)]
public class TransparencyTests
{
    private readonly AcceptanceFixture _fx;

    public TransparencyTests(AcceptanceFixture fixture) => _fx = fixture;

    [Fact]
    public void HalfOpacityPip_BlendsCameraOverScreen_UpToTheExactEdge()
    {
        if (!_fx.Enabled) return;
        var reference = new ReferenceCompositor(_fx.TransparentReopenedProject, _fx.Media);
        var pip = reference.ExpectedPipRect();
        const int n = 25; // t=2.55s: camera block 1 over screen A
        var frames = CompositeFrameReader.Open(_fx.TransparentExport,
            LessonSpec.CanvasWidth, LessonSpec.CanvasHeight, new[] { n });

        // Interior blend over plain background: 0.5 * palette[1] + 0.5 * screen bg.
        var expectedInterior = Blend(LessonSpec.CameraPalette[1], LessonSpec.ScreenABackground);
        ColorAssert.Close(expectedInterior, frames.Pixel(n, 1100, 600), 14,
            "blended pip interior");

        // Blend over the under-pip marker: 0.5 * palette[1] + 0.5 * magenta.
        var expectedMarker = Blend(LessonSpec.CameraPalette[1], LessonSpec.UnderPipA);
        ColorAssert.Close(expectedMarker, frames.Pixel(n, 1050, 550), 14,
            "blended marker zone");

        // Camera's white border blends to a half-white edge inside the window.
        var expectedEdge = Blend(LessonSpec.CameraBorder, LessonSpec.ScreenABackground);
        ColorAssert.Close(expectedEdge, frames.Pixel(n, pip.X + 2, pip.Y + 2), 14,
            "blended camera border");

        // The blend stops at the window edge: outside pixels are pure screen.
        ColorAssert.Close(LessonSpec.ScreenABackground, frames.Pixel(n, pip.X - 2, pip.Y + pip.H / 2),
            12, "one pixel left of the pip window");
        ColorAssert.Close(LessonSpec.ScreenABackground, frames.Pixel(n, pip.X + pip.W + 1, pip.Y + pip.H / 2),
            12, "one pixel right of the pip window");

        // Negative samples: the actual pixel equals neither the un-blended camera
        // (opacity ignored) nor the un-covered screen (camera dropped).
        var actual = frames.Pixel(n, 1100, 600);
        Assert.True(ColorAssert.Distance(actual, LessonSpec.CameraPalette[1]) > 30,
            "interior must not be the raw camera color (opacity dropped)");
        Assert.True(ColorAssert.Distance(actual, LessonSpec.ScreenABackground) > 30,
            "interior must not be the raw screen color (camera missing)");
    }

    [Fact]
    public void OpaqueExport_ShowsNoBlendResidual()
    {
        if (!_fx.Enabled) return;
        // Same instant in the opaque main export: interior is pure camera color.
        const int n = 25;
        var frames = CompositeFrameReader.Open(_fx.MainExport,
            LessonSpec.CanvasWidth, LessonSpec.CanvasHeight, new[] { n });
        ColorAssert.Close(LessonSpec.CameraPalette[1], frames.Pixel(n, 1100, 600), 12,
            "opaque pip interior");
    }

    private static (int R, int G, int B) Blend((int R, int G, int B) top, (int R, int G, int B) bottom) =>
        ((top.R + bottom.R) / 2, (top.G + bottom.G) / 2, (top.B + bottom.B) / 2);
}
