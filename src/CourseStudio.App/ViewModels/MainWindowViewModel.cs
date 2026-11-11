using System.Collections.ObjectModel;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CourseStudio.App.Services;
using CourseStudio.Core.Editing;
using CourseStudio.Core.Media;
using CourseStudio.Core.Models;
using CourseStudio.Core.Persistence;
using CourseStudio.Core.Rendering;

namespace CourseStudio.App.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    public CourseProject Project { get; private set; } = new();
    public HistoryService History { get; } = new();
    public EditEngine Engine { get; }
    public FrameExtractor FrameExtractor { get; }
    public PreviewCompositor Preview { get; }

    private readonly ProbeService _probe;
    private string? _projectPath;
    private string? _autoSavePath;

    public ObservableCollection<ClipRowViewModel> Rows { get; } = new();

    [ObservableProperty] private ClipRowViewModel? _selectedRow;
    [ObservableProperty] private string _statusMessage = "Ready.";
    [ObservableProperty] private double _exportProgress;
    [ObservableProperty] private bool _isExporting;
    [ObservableProperty] private bool _showCamera = true;
    [ObservableProperty] private double _previewScale = 1.0;

    // Playhead in microseconds on the project timeline.
    [ObservableProperty] private long _playheadMicros;

    // Ripple selection: which tracks move together when time is cut.
    [ObservableProperty] private bool _rippleScreen = true;
    [ObservableProperty] private bool _rippleCamera = true;
    [ObservableProperty] private bool _rippleAudio = true;

    // Ripple range endpoints (project timeline microseconds).
    [ObservableProperty] private long _rangeStartMicros;
    [ObservableProperty] private long _rangeEndMicros;

    // PIP editing
    [ObservableProperty] private double _pipWidthRatio = 0.25;
    [ObservableProperty] private int _pipMargin = 24;
    [ObservableProperty] private double _pipOpacity = 1.0;
    [ObservableProperty] private PipCorner _pipCorner = PipCorner.BottomRight;

    // Trim inputs in seconds
    [ObservableProperty] private double _trimInSeconds;
    [ObservableProperty] private double _trimOutSeconds;
    [ObservableProperty] private double _clipStartSeconds;

    public MainWindowViewModel()
    {
        History.Changed += OnHistoryChanged;
        Engine = new EditEngine(History);
        FrameExtractor = new FrameExtractor(ffmpegPath: ToolLocator.Ffmpeg);
        Preview = new PreviewCompositor(FrameExtractor);
        _probe = new ProbeService(ToolLocator.Ffprobe);
        _autoSavePath = Path.Combine(Path.GetTempPath(), "coursestudio-autosave.json");
        SyncPipFromProject();
        RebuildRows();
    }

    public TimeCode Playhead => new(PlayheadMicros);
    public TimeCode RangeStart => new(RangeStartMicros);
    public TimeCode RangeEnd => new(RangeEndMicros);
    public long TimelineDurationMicros => Project.TimelineDuration.Micros;
    public bool CanUndo => History.CanUndo;
    public bool CanRedo => History.CanRedo;

    public event Action? ProjectChanged;
    public event Action<string>? ErrorRaised;

    private void OnHistoryChanged()
    {
        OnPropertyChanged(nameof(CanUndo));
        OnPropertyChanged(nameof(CanRedo));
        OnPropertyChanged(nameof(TimelineDurationMicros));
        SyncPipFromProject();
        RebuildRows();
        AutoSave();
        ProjectChanged?.Invoke();
    }

    partial void OnShowCameraChanged(bool value) => Preview.ShowCamera = value;
    partial void OnPlayheadMicrosChanged(long value) => ProjectChanged?.Invoke();

    private void SyncPipFromProject()
    {
        PipWidthRatio = Project.Pip.WidthRatio;
        PipMargin = Project.Pip.Margin;
        PipOpacity = Project.Pip.Opacity;
        PipCorner = Project.Pip.Corner;
    }

    private void RebuildRows()
    {
        var selectedId = SelectedRow?.Id;
        Rows.Clear();
        foreach (var clip in Project.AllClips.OrderBy(c => c.Track).ThenBy(c => c.Start.Micros))
        {
            var row = new ClipRowViewModel(clip) { IsSelected = clip.Id == selectedId };
            Rows.Add(row);
        }
        SelectedRow = Rows.FirstOrDefault(r => r.IsSelected);
        OnPropertyChanged(nameof(TimelineDurationMicros));
    }

    // ---------- import ----------

    [RelayCommand]
    private async Task ImportScreen(string filePath) => await Import(filePath, MediaKind.Screen);

    [RelayCommand]
    private async Task ImportCamera(string filePath) => await Import(filePath, MediaKind.Camera);

    [RelayCommand]
    private async Task ImportAudio(string filePath) => await Import(filePath, MediaKind.Audio);

    public async Task Import(string filePath, MediaKind kind)
    {
        try
        {
            StatusMessage = $"Importing {Path.GetFileName(filePath)}…";
            var source = await _probe.ImportAsync(filePath, kind);
            Project.Sources.Add(source);

            Clip clip = kind switch
            {
                MediaKind.Screen => new ScreenClip
                {
                    SourceId = source.Id,
                    Start = TimeCode.Zero,
                    SourceIn = TimeCode.Zero,
                    SourceOut = source.Duration
                },
                MediaKind.Camera => new CameraClip
                {
                    SourceId = source.Id,
                    Start = TimeCode.Zero,
                    SourceIn = TimeCode.Zero,
                    SourceOut = source.Duration
                },
                _ => new AudioClip
                {
                    SourceId = source.Id,
                    Start = TimeCode.Zero,
                    SourceIn = TimeCode.Zero,
                    SourceOut = source.Duration
                }
            };
            // Independent origins: new clips start at zero by default but can be moved freely.
            var result = Engine.AddClip(Project, clip);
            StatusMessage = result.Success
                ? $"Imported {source.NaturalWidth}x{source.NaturalHeight} {kind} source."
                : result.ErrorMessage ?? "Import failed.";
        }
        catch (Exception ex)
        {
            ErrorRaised?.Invoke(ex.Message);
            StatusMessage = $"Import failed: {ex.Message}";
        }
    }

    // ---------- selection / editing ----------

    partial void OnSelectedRowChanged(ClipRowViewModel? value)
    {
        if (value is null) return;
        TrimInSeconds = value.Clip.SourceIn.TotalSeconds;
        TrimOutSeconds = value.Clip.SourceOut.TotalSeconds;
        ClipStartSeconds = value.Clip.Start.TotalSeconds;
    }

    [RelayCommand]
    private void SetRangeFromPlayhead(bool start)
    {
        if (start) RangeStartMicros = PlayheadMicros;
        else RangeEndMicros = PlayheadMicros;
    }

    [RelayCommand]
    private void RippleDelete()
    {
        var options = new RippleOptions(RippleScreen, RippleCamera, RippleAudio);
        var result = Engine.RippleDelete(Project, TrackKind.Screen, RangeStart, RangeEnd, options);
        ReportResult(result, "Ripple delete");
    }

    [RelayCommand]
    private void ApplyTrim()
    {
        if (SelectedRow is null) return;
        var result = Engine.TrimClip(Project, SelectedRow.Clip,
            TimeCode.FromSeconds(TrimInSeconds),
            TimeCode.FromSeconds(TrimOutSeconds),
            TimeCode.FromSeconds(ClipStartSeconds));
        ReportResult(result, "Trim");
    }

    [RelayCommand]
    private void MoveSelectedToPlayhead()
    {
        if (SelectedRow is null) return;
        var result = Engine.MoveClip(Project, SelectedRow.Clip, Playhead);
        ReportResult(result, "Move");
    }

    [RelayCommand]
    private void AddMute()
    {
        var result = Engine.AddMuteRegion(Project, RangeStart, RangeEnd);
        ReportResult(result, "Mute");
    }

    [RelayCommand]
    private void HideCamera()
    {
        if (SelectedRow?.Clip is not CameraClip cam) return;
        var result = Engine.SetCameraHidden(Project, cam, !cam.Hidden);
        ReportResult(result, "Camera visibility");
    }

    [RelayCommand]
    private void ApplyPip()
    {
        var result = Engine.UpdatePip(Project, new PipSettings
        {
            Corner = PipCorner,
            WidthRatio = PipWidthRatio,
            Margin = PipMargin,
            Opacity = PipOpacity
        });
        ReportResult(result, "PIP");
    }

    [RelayCommand]
    private void ToggleLock(string track)
    {
        var kind = Enum.Parse<TrackKind>(track);
        var before = Project.Clone();
        var edit = new SnapshotEdit($"Toggle {kind} lock", before, before, p =>
        {
            switch (kind)
            {
                case TrackKind.Screen: p.ScreenLocked = !p.ScreenLocked; break;
                case TrackKind.Camera: p.CameraLocked = !p.CameraLocked; break;
                case TrackKind.Audio: p.AudioLocked = !p.AudioLocked; break;
            }
        });
        History.Execute(Project, edit);
    }

    public bool IsLocked(TrackKind kind) => Project.IsTrackLocked(kind);

    [RelayCommand]
    private void Undo()
    {
        History.Undo(Project);
        StatusMessage = $"Undo: {History.LastAction ?? "done"}";
    }

    [RelayCommand]
    private void Redo()
    {
        History.Redo(Project);
    }

    private void ReportResult(EditResult result, string action)
    {
        if (result.Success)
        {
            StatusMessage = $"{action} completed.";
            return;
        }
        StatusMessage = $"{action} blocked: {result.ErrorMessage}";
        foreach (var conflict in result.Conflicts)
        {
            StatusMessage += $" [{conflict.Track} locked]";
        }
        ErrorRaised?.Invoke(StatusMessage);
    }

    // ---------- project file ----------

    [RelayCommand]
    private void NewProject()
    {
        Project = new CourseProject();
        History.Clear();
        _projectPath = null;
        PlayheadMicros = 0;
        RangeStartMicros = 0;
        RangeEndMicros = 0;
        SyncPipFromProject();
        RebuildRows();
        ProjectChanged?.Invoke();
        StatusMessage = "New project.";
    }

    public void SaveProject(string path)
    {
        ProjectSerializer.Save(Project, path);
        _projectPath = path;
        StatusMessage = $"Saved {path}";
    }

    public string? ProjectPath => _projectPath;

    public void LoadProject(string path)
    {
        var result = ProjectSerializer.Load(path);
        Project = result.Project;
        _projectPath = path;
        History.Clear();
        PlayheadMicros = 0;
        SyncPipFromProject();
        RebuildRows();
        ProjectChanged?.Invoke();
        StatusMessage = result.MissingFiles.Count > 0
            ? $"Loaded with {result.MissingFiles.Count} missing media file(s)."
            : $"Loaded {path}";
        if (result.MissingFiles.Count > 0)
            ErrorRaised?.Invoke(string.Join(Environment.NewLine, result.MissingFiles));
    }

    private void AutoSave()
    {
        if (_autoSavePath is null) return;
        try
        {
            ProjectSerializer.Save(Project, _autoSavePath);
        }
        catch
        {
            // autosave is best-effort
        }
    }

    /// <summary>Called on window close; reopening the app restores this exact state.</summary>
    public void OnWindowClosing()
    {
        AutoSave();
        if (_projectPath is not null)
        {
            try { ProjectSerializer.Save(Project, _projectPath); } catch { /* best effort */ }
        }
    }

    public bool TryRecoverAutoSave()
    {
        if (_autoSavePath is null || !File.Exists(_autoSavePath)) return false;
        try
        {
            var result = ProjectSerializer.Load(_autoSavePath);
            Project = result.Project;
            History.Clear();
            SyncPipFromProject();
            RebuildRows();
            ProjectChanged?.Invoke();
            StatusMessage = "Recovered last session from auto-save.";
            return true;
        }
        catch
        {
            return false;
        }
    }

    // ---------- export ----------

    public async Task Export(string outputPath)
    {
        if (IsExporting) return;
        IsExporting = true;
        ExportProgress = 0;
        try
        {
            var exporter = new VideoExporter(ToolLocator.Ffmpeg);
            var progress = new Progress<ExportProgress>(p => ExportProgress = p.Ratio);
            await exporter.ExportAsync(Project, outputPath, new ExportOptions(), progress);
            StatusMessage = $"Exported {outputPath} ({Project.CanvasWidth}x{Project.CanvasHeight})";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
            ErrorRaised?.Invoke(ex.Message);
        }
        finally
        {
            IsExporting = false;
        }
    }
}
