using CourseStudio.TestKit;
using Xunit;

namespace CourseStudio.Acceptance;

/// <summary>
/// A/V sync acceptance: the voice beeps must land on their derived timeline
/// positions in the exported file, and each beep must coincide with the matching
/// on-screen time marker — audio and video are checked against each other, not
/// just against the spec.
/// </summary>
[Collection(AcceptanceCollection.Name)]
public class AudioSyncTests
{
    private const double MaxDeviationSeconds = 0.08;
    private readonly AcceptanceFixture _fx;

    public AudioSyncTests(AcceptanceFixture fixture) => _fx = fixture;

    [Fact]
    public void BeepsLandOnExpectedTimelinePositions()
    {
        if (!_fx.Enabled) return;
        var reference = new ReferenceCompositor(_fx.MainReopenedProject, _fx.Media);
        var expected = reference.ExpectedBeepOnsets();
        Assert.Equal(LessonSpec.BeepCount, expected.Length);

        var audio = AudioTrackReader.Decode(_fx.MainExport);
        var detected = audio.DetectOnsets();

        Assert.True(detected.Length == expected.Length,
            $"expected {expected.Length} beeps, detected {detected.Length}: " +
            $"[{string.Join(", ", detected.Select(d => d.ToString("0.00")))}]");
        var maxDeviation = detected.Zip(expected, (d, e) => Math.Abs(d - e)).Max();
        Assert.True(maxDeviation <= MaxDeviationSeconds,
            $"A/V deviation {maxDeviation * 1000:0}ms exceeds {MaxDeviationSeconds * 1000:0}ms; " +
            $"detected [{string.Join(", ", detected.Select(d => d.ToString("0.000")))}], " +
            $"expected [{string.Join(", ", expected.Select(e => e.ToString("0.000")))}]");
    }

    [Fact]
    public void BeepsAreSeparatedBySilence()
    {
        if (!_fx.Enabled) return;
        var audio = AudioTrackReader.Decode(_fx.MainExport);
        Assert.True(audio.Rms(0.52, 0.70) > 0.1, "beep 0 must be audible");
        Assert.True(audio.Rms(0.90, 1.40) < 0.01, "gap between beeps 0 and 1 must be silent");
        Assert.True(audio.Rms(11.55, 11.70) > 0.1, "last beep must be audible");
    }

    [Fact]
    public void BeepInstants_ShowMatchingScreenTimeMarkers()
    {
        if (!_fx.Enabled) return;
        var reference = new ReferenceCompositor(_fx.MainReopenedProject, _fx.Media);
        // Beep k sits at timeline 0.5+k; the export frame covering that instant is
        // n = 5 + 10k. The screen strip in that frame identifies the source second.
        var beepFrames = new[] { 0, 3, 6, 9, 11 }.Select(k => 5 + 10 * k).ToArray();
        var frames = CompositeFrameReader.Open(_fx.MainExport,
            LessonSpec.CanvasWidth, LessonSpec.CanvasHeight, beepFrames);

        foreach (var k in new[] { 0, 3, 6, 9, 11 })
        {
            var n = 5 + 10 * k;
            var t = n / (double)LessonSpec.Fps + 0.05;
            var expected = reference.ExpectedScreenSourceFrame(t);
            var decoded = frames.DecodeStrip(n, reference.ScreenStripSamplePoints(t));
            Assert.True(decoded == expected,
                $"at beep {k} (t={t:0.00}s) the screen shows source frame {decoded}, " +
                $"expected {expected} — picture and sound are out of sync");
        }
    }
}
