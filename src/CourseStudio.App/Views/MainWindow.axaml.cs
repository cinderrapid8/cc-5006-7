using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CourseStudio.App.ViewModels;
using CourseStudio.Core.Models;

namespace CourseStudio.App.Views;

public partial class MainWindow : Window
{
    public MainWindowViewModel Vm => (MainWindowViewModel)DataContext!;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, _) =>
        {
            Vm.ErrorRaised += ShowError;
            Vm.ProjectChanged += InvalidateTimeline;
            if (Vm.TryRecoverAutoSave())
            {
                InvalidateTimeline();
            }
        };
    }

    private void InvalidateTimeline()
    {
        // Custom controls redraw themselves through ProjectChanged; nothing else needed.
    }

    private async void ShowError(string message)
    {
        var dialog = new Window
        {
            Title = "Notice",
            Width = 480,
            Height = 240,
            Content = new TextBlock
            {
                Text = message,
                TextWrapping = Avalonia.Media.TextWrapping.Wrap,
                Margin = new Avalonia.Thickness(16)
            }
        };
        await dialog.ShowDialog(this);
    }

    private static readonly FilePickerFileType VideoFiles = new("Video")
    {
        Patterns = new[] { "*.mp4", "*.mkv", "*.mov", "*.webm", "*.avi" }
    };
    private static readonly FilePickerFileType AudioFiles = new("Audio")
    {
        Patterns = new[] { "*.wav", "*.mp3", "*.m4a", "*.aac", "*.flac", "*.ogg" }
    };
    private static readonly FilePickerFileType ProjectFiles = new("Course project")
    {
        Patterns = new[] { "*.course.json", "*.json" }
    };

    private async void OnImportScreen(object? sender, RoutedEventArgs e)
    {
        var file = await PickFile(new[] { VideoFiles });
        if (file is not null) await Vm.Import(file, MediaKind.Screen);
    }

    private async void OnImportCamera(object? sender, RoutedEventArgs e)
    {
        var file = await PickFile(new[] { VideoFiles });
        if (file is not null) await Vm.Import(file, MediaKind.Camera);
    }

    private async void OnImportAudio(object? sender, RoutedEventArgs e)
    {
        var file = await PickFile(new[] { AudioFiles, VideoFiles });
        if (file is not null) await Vm.Import(file, MediaKind.Audio);
    }

    private async Task<string?> PickFile(IReadOnlyList<FilePickerFileType> types)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = types
        });
        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    private async void OnOpenProject(object? sender, RoutedEventArgs e)
    {
        var file = await PickFile(new[] { ProjectFiles });
        if (file is not null) Vm.LoadProject(file);
    }

    private async void OnSaveProject(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            DefaultExtension = "course.json",
            FileTypeChoices = new[] { ProjectFiles },
            SuggestedFileName = "lesson.course.json"
        });
        var path = file?.TryGetLocalPath();
        if (path is not null) Vm.SaveProject(path);
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            DefaultExtension = "mp4",
            FileTypeChoices = new[] { new FilePickerFileType("MP4 video") { Patterns = new[] { "*.mp4" } } },
            SuggestedFileName = "course-output.mp4"
        });
        var path = file?.TryGetLocalPath();
        if (path is not null) await Vm.Export(path);
    }

    private void OnPipCorner(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string tag } && Enum.TryParse<PipCorner>(tag, out var corner))
        {
            Vm.PipCorner = corner;
        }
    }
}
