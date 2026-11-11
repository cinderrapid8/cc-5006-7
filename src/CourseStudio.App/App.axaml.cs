using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using CourseStudio.App.ViewModels;
using CourseStudio.App.Views;

namespace CourseStudio.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow
            {
                DataContext = new MainWindowViewModel()
            };
            desktop.MainWindow = window;
            desktop.ShutdownRequested += (_, _) =>
            {
                if (window.DataContext is MainWindowViewModel vm)
                {
                    vm.OnWindowClosing();
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
