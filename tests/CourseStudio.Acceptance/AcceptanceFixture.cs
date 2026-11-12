using CourseStudio.Core.Media;
using CourseStudio.Core.Models;
using CourseStudio.Core.Persistence;
using CourseStudio.Core.Rendering;
using CourseStudio.TestKit;
using Xunit;

namespace CourseStudio.Acceptance;

/// <summary>
/// Shared harness for the compositing acceptance suite. Every export in this
/// suite follows the same rule: the project is saved to a .course.json file and
/// REOPENED through <see cref="ProjectSerializer"/> before it is rendered, so the
/// accepted path is the one a user gets after closing and reopening the app —
/// never an in-memory shortcut and never an isolated filter graph.
/// </summary>
public sealed class AcceptanceFixture : IAsyncLifetime
{
    /// <summary>False when ffmpeg is unavailable; tests skip instead of failing the build.</summary>
    public bool Enabled { get; private set; }

    public string WorkDir { get; private set; } = "";
    public CourseMediaSet Media { get; private set; } = null!;
    public CourseProject TemplateProject { get; private set; } = null!;

    /// <summary>Main lesson export: PIP ratio 0.25, opacity 1.0.</summary>
    public string MainExport { get; private set; } = "";
    public CourseProject MainReopenedProject { get; private set; } = null!;

    /// <summary>Window-ratio variant: PIP ratio 0.40.</summary>
    public string WidePipExport { get; private set; } = "";
    public CourseProject WidePipReopenedProject { get; private set; } = null!;

    /// <summary>Transparency variant: PIP opacity 0.5.</summary>
    public string TransparentExport { get; private set; } = "";
    public CourseProject TransparentReopenedProject { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        if (!FfmpegTools.Available()) return; // acceptance.sh hard-fails in that case; dotnet test skips
        Enabled = true;

        WorkDir = Path.Combine(RepoRoot(), "artifacts", "acceptance",
            DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(WorkDir);

        Media = SyntheticCourseMedia.Generate(Path.Combine(WorkDir, "media"));
        TemplateProject = await BuildLessonProject();

        (MainExport, MainReopenedProject) = await SaveReopenExport(TemplateProject, "lesson-main", _ => { });
        (WidePipExport, WidePipReopenedProject) = await SaveReopenExport(TemplateProject, "lesson-widepip",
            p => p.Pip.WidthRatio = 0.40);
        (TransparentExport, TransparentReopenedProject) = await SaveReopenExport(TemplateProject, "lesson-transparent",
            p => p.Pip.Opacity = 0.5);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    /// <summary>
    /// The acceptance entry point for every export: mutate a clone of the lesson,
    /// save it, reopen it from disk, then export the REOPENED project.
    /// </summary>
    public async Task<(string OutputPath, CourseProject Reopened)> SaveReopenExport(
        CourseProject template, string name, Action<CourseProject> mutate,
        ExportOptions? options = null)
    {
        var reopened = SaveAndReopen(template, name, mutate);
        var output = Path.Combine(WorkDir, name + ".mp4");
        await new VideoExporter(FfmpegTools.Ffmpeg).ExportAsync(reopened, output,
            options ?? new ExportOptions { Preset = "ultrafast", Crf = 18 });
        return (output, reopened);
    }

    /// <summary>Save a mutated clone and reopen it from disk without exporting.</summary>
    public CourseProject SaveAndReopen(CourseProject template, string name, Action<CourseProject> mutate)
    {
        var project = template.Clone();
        mutate(project);
        var projectFile = Path.Combine(WorkDir, name + ".course.json");
        ProjectSerializer.Save(project, projectFile);
        var loaded = ProjectSerializer.Load(projectFile);
        if (loaded.MissingFiles.Count > 0)
            throw new InvalidOperationException("Reopened project reports missing media: " +
                                                string.Join(", ", loaded.MissingFiles));
        return loaded.Project;
    }

    private async Task<CourseProject> BuildLessonProject()
    {
        var probe = new ProbeService(FfmpegTools.Ffprobe);
        var screenA = await probe.ImportAsync(Media.ScreenAPath, MediaKind.Screen);
        var screenB = await probe.ImportAsync(Media.ScreenBPath, MediaKind.Screen);
        var camera = await probe.ImportAsync(Media.CameraPath, MediaKind.Camera);
        var voice = await probe.ImportAsync(Media.VoicePath, MediaKind.Audio);

        var project = new CourseProject
        {
            Name = "Acceptance lesson",
            CanvasWidth = LessonSpec.CanvasWidth,
            CanvasHeight = LessonSpec.CanvasHeight,
            FrameRate = LessonSpec.Fps,
            BackgroundHex = LessonSpec.ProjectBackgroundHex
        };
        project.Pip.WidthRatio = LessonSpec.PipWidthRatio;
        project.Pip.Margin = LessonSpec.PipMargin;
        project.Pip.Corner = PipCorner.BottomRight;
        project.Sources.AddRange(new[] { screenA, screenB, camera, voice });

        project.ScreenClips.Add(new ScreenClip
        {
            SourceId = screenA.Id,
            Start = TimeCode.FromSeconds(LessonSpec.ScreenAStart),
            SourceIn = TimeCode.Zero,
            SourceOut = TimeCode.FromSeconds(LessonSpec.ScreenAFrames / (double)LessonSpec.Fps),
            ResizeMode = ResizeMode.Fit,
            BackgroundHex = LessonSpec.ClipBackgroundHex
        });
        project.ScreenClips.Add(new ScreenClip
        {
            SourceId = screenB.Id,
            Start = TimeCode.FromSeconds(LessonSpec.ScreenBStart),
            SourceIn = TimeCode.Zero,
            SourceOut = TimeCode.FromSeconds(LessonSpec.ScreenBFrames / (double)LessonSpec.Fps),
            ResizeMode = ResizeMode.Fit,
            BackgroundHex = LessonSpec.ClipBackgroundHex
        });
        project.CameraClips.Add(new CameraClip
        {
            SourceId = camera.Id,
            Start = TimeCode.FromSeconds(LessonSpec.CameraStart),
            SourceIn = TimeCode.Zero,
            SourceOut = TimeCode.FromSeconds(LessonSpec.CameraLength)
        });
        project.AudioClips.Add(new AudioClip
        {
            SourceId = voice.Id,
            Start = TimeCode.FromSeconds(LessonSpec.AudioStart),
            SourceIn = TimeCode.Zero,
            SourceOut = TimeCode.FromSeconds(LessonSpec.AudioLength)
        });
        return project;
    }

    public static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CourseStudio.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Cannot locate the repository root.");
    }
}

[CollectionDefinition(Name)]
public sealed class AcceptanceCollection : ICollectionFixture<AcceptanceFixture>
{
    public const string Name = "composition-acceptance";
}
