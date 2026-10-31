using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Berreman.Gui;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;
            // A stack file to open: the first argument, as Explorer or a file manager passes it. It
            // is evaluated at once: opening a stack from outside is asking to see its results.
            if (desktop.Args is [var path, ..])
                window.Opened += async (_, _) => await window.OpenPath(path, evaluate: true);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
