using CourseStudio.Core.Rendering;
using CourseStudio.TestKit;
using Xunit;

namespace CourseStudio.Acceptance;

/// <summary>
/// Negative sample around export cancellation: a cancelled export must surface an
/// error and must not leave a product that passes the integrity checks.
/// </summary>
[Collection(AcceptanceCollection.Name)]
public class ExportCancellationTests
{
    private readonly AcceptanceFixture _fx;

    public ExportCancellationTests(AcceptanceFixture fixture) => _fx = fixture;

    [Fact]
    public async Task CancelledExport_Throws_AndLeavesNoCompleteProduct()
    {
        if (!_fx.Enabled) return;
        var reopened = _fx.SaveAndReopen(_fx.TemplateProject, "lesson-cancelled", _ => { });
        var output = Path.Combine(_fx.WorkDir, "lesson-cancelled.mp4");

        using var cts = new CancellationTokenSource();
        var completed = false;
        var progress = new SyncProgress(p =>
        {
            if (p.Ratio >= 1.0) completed = true;
            if (p.Ratio > 0.01) cts.Cancel(); // cancel as soon as the export is underway
        });
        cts.CancelAfter(TimeSpan.FromSeconds(4)); // backstop if no progress ever arrives

        var exporter = new VideoExporter(FfmpegTools.Ffmpeg);
        await Assert.ThrowsAnyAsync<Exception>(() => exporter.ExportAsync(reopened, output,
            new ExportOptions { Preset = "slow", Crf = 18 }, progress, cts.Token));
        Assert.False(completed, "export reported completion despite cancellation");

        // Product integrity of whatever remains on disk must fail the lesson checks.
        var report = ProductIntegrity.Inspect(output);
        Assert.False(
            report.IsCompleteLesson(LessonSpec.CanvasWidth, LessonSpec.CanvasHeight,
                LessonSpec.TimelineSeconds, LessonSpec.TimelineFrames, 0.35),
            $"cancelled export left a seemingly complete product: {report}");
    }

    [Fact]
    public async Task CompletedExport_PassesIntegrity_ForComparison()
    {
        if (!_fx.Enabled) return;
        // The positive counterpart: the same lesson, not cancelled, is complete.
        var report = ProductIntegrity.Inspect(_fx.MainExport);
        Assert.True(
            report.IsCompleteLesson(LessonSpec.CanvasWidth, LessonSpec.CanvasHeight,
                LessonSpec.TimelineSeconds, LessonSpec.TimelineFrames, 0.35),
            $"main export failed integrity: {report}");
        await Task.CompletedTask;
    }

    /// <summary>IProgress that invokes synchronously, so cancellation is immediate.</summary>
    private sealed class SyncProgress : IProgress<ExportProgress>
    {
        private readonly Action<ExportProgress> _onReport;
        public SyncProgress(Action<ExportProgress> onReport) => _onReport = onReport;
        public void Report(ExportProgress value) => _onReport(value);
    }
}
