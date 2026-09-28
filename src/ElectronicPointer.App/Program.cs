using Avalonia;

namespace Mutantcat.ElectronicPointer.App;

/// <summary>
/// Classic desktop entry point. Every platform the app supports runs through here: the
/// platform specific work happens behind <see cref="Platform.Services.IPlatformServices"/>,
/// so nothing in this file knows which operating system it is about to draw on.
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Handled before Avalonia boots: asking for the version has to work on a headless CI
        // runner and from a plain terminal, where there is no display server to talk to.
        if (args.Length > 0)
        {
            switch (args[0].Trim().ToLowerInvariant())
            {
                case "--version":
                case "-v":
                    Console.WriteLine($"{AppIdentity.ProductName} {AppIdentity.Version}");
                    Console.WriteLine($"{AppIdentity.GetDisplayName()} ({AppIdentity.ApplicationId})");
                    return 0;

                case "--help":
                case "-h":
                case "-?":
                    Console.WriteLine($"{AppIdentity.GetDisplayName()} {AppIdentity.Version}");
                    Console.WriteLine($"用法：{AppIdentity.ExecutableName} [--version|--help]");
                    Console.WriteLine("不带参数启动即为电子教鞭本体，所有功能都在浮动工具栏上。");
                    return 0;
            }
        }

        return BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Kept separate from <see cref="Main"/> so a previewer or a designer host can build the
    /// same application without entering a message loop.
    /// </summary>
    public static AppBuilder BuildAvaloniaApp()
    {
        // No bundled font: the toolbar and the settings window show Chinese text, and each
        // platform renders that best with the font family the user already has.
        return AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
    }
}
