using Mutantcat.ElectronicPointer.Platform.Startup;

namespace Mutantcat.ElectronicPointer.Platform.MacOS;

/// <summary>
/// "Start when I log in" through a launch agent plist. The plist lives in
/// <c>~/Library/LaunchAgents</c> and is named after the bundle identifier, the same string
/// the installer registers, so the two never disagree.
/// </summary>
public sealed class MacOSAutoStartService : IAutoStartService
{
    public bool IsSupported => OperatingSystem.IsMacOS();

    public bool IsEnabled
    {
        get
        {
            var path = AgentPath;
            return File.Exists(path);
        }
    }

    public void SetEnabled(bool enabled)
    {
        var path = AgentPath;
        if (!enabled)
        {
            if (File.Exists(path))
                File.Delete(path);

            return;
        }

        var folder = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(folder);

        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
            return;

        File.WriteAllText(path, BuildPlist(executable));
    }

    private static string AgentPath
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "LaunchAgents", $"{AppIdentity.ApplicationId}.plist");
        }
    }

    private static string BuildPlist(string executable)
    {
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
            + "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n"
            + "<plist version=\"1.0\">\n"
            + "<dict>\n"
            + "  <key>Label</key>\n"
            + $"  <string>{AppIdentity.ApplicationId}</string>\n"
            + "  <key>ProgramArguments</key>\n"
            + "  <array>\n"
            + $"    <string>{executable}</string>\n"
            + "  </array>\n"
            + "  <key>RunAtLoad</key>\n"
            + "  <true/>\n"
            + "</dict>\n"
            + "</plist>\n";
    }
}
