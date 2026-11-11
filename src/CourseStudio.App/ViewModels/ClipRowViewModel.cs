using CommunityToolkit.Mvvm.ComponentModel;
using CourseStudio.Core.Models;

namespace CourseStudio.App.ViewModels;

public partial class ClipRowViewModel : ObservableObject
{
    public Clip Clip { get; }

    public ClipRowViewModel(Clip clip) => Clip = clip;

    public string Id => Clip.Id;
    public TrackKind Track => Clip.Track;
    public string Label => $"{Clip.SourceId[..6]} · {Clip.Start.ToDisplay()} → {Clip.End.ToDisplay()}";

    [ObservableProperty] private bool _isSelected;

    public bool Hidden => Clip is CameraClip { Hidden: true };

    public void NotifyChanged()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Hidden));
    }
}
