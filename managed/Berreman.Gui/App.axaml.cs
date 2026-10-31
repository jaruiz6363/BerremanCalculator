using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Platform.Storage;

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
            // A stack file to open: on Windows and Linux the first argument, as Explorer or a file
            // manager passes it; on macOS, Finder (and `open -a`) sends it as an activation
            // instead. It is evaluated at once: opening a stack from outside is asking to see its
            // results.
            if (desktop.Args is [var path, ..])
                window.Opened += async (_, _) => await window.OpenPath(path, evaluate: true);
            if (this.TryGetFeature<IActivatableLifetime>() is { } activatable)
                activatable.Activated += async (_, e) =>
                {
                    if (e is FileActivatedEventArgs { Files: var files } && files.FirstOrDefault()?.TryGetLocalPath() is { } opened)
                        await window.OpenPath(opened, evaluate: true);
                };
        }
        base.OnFrameworkInitializationCompleted();
    }
}
