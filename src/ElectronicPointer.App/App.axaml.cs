using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;
using Mutantcat.ElectronicPointer.App.Shell;

namespace Mutantcat.ElectronicPointer.App;

/// <summary>
/// Application shell. It boots into an always present, click-through drawing surface that
/// mirrors the original pointer experience, so the user can start inking the moment the
/// app launches instead of looking for a window first.
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// Lives for the whole process. The desktop lifetime is told to shut down only on an
    /// explicit quit, because closing a toolbar or an overlay window must never end a
    /// session the user is presenting in.
    /// </summary>
    internal static OverlayShell? Shell { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Shell = new OverlayShell();
            Shell.Start();
            desktop.MainWindow = Shell.MainWindow;

            // Exiting the lifetime ends the process without closing anything the shell
            // owns: the overlay windows, the global shortcuts and the platform watchers
            // would all be torn down by the OS instead of being let go, which on Linux
            // and macOS leaves hotkeys registered until the session restarts.
            desktop.Exit += (_, _) => Shell?.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
