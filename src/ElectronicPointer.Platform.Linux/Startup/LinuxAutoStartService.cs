using System.Text;
using Mutantcat.ElectronicPointer.Platform.Startup;

namespace Mutantcat.ElectronicPointer.Platform.Linux;

/// <summary>
/// "Start when I log in" through a freedesktop autostart entry. The file is written with an
/// explicit UTF-8 encoding and no byte order mark: the spec requires UTF-8, and a BOM at the
/// start of a desktop entry makes the first key unreadable to almost every desktop.
///
/// The file is named after the bundle identifier rather than the executable, because that
/// is the same name the deb, the AppImage and the desktop entry register, so the autostart
/// switch only ever touches the entry this build owns.
/// </summary>
public sealed class LinuxAutoStartService : IAutoStartService
{
    public bool IsSupported => OperatingSystem.IsLinux();

    public bool IsEnabled => File.Exists(EntryPath);

    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            if (File.Exists(EntryPath))
                File.Delete(EntryPath);

            return;
        }

        var executable = Environment.ProcessPath;
        if (string.IsNullOrEmpty(executable))
            return;

        var folder = Path.GetDirectoryName(EntryPath)!;
        Directory.CreateDirectory(folder);

        File.WriteAllText(EntryPath, BuildEntry(executable), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string EntryPath
    {
        get
        {
            var config = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(config, ".config", "autostart", $"{AppIdentity.ApplicationId}.desktop");
        }
    }

    private static string BuildEntry(string executable)
    {
        var builder = new StringBuilder();
        builder.AppendLine("[Desktop Entry]");
        builder.AppendLine("Type=Application");
        builder.AppendLine($"Name={AppIdentity.ChineseName}");
        builder.AppendLine($"Name[zh_CN]={AppIdentity.ChineseName}");
        builder.AppendLine($"Name[en]={AppIdentity.ProductName}");
        builder.AppendLine("GenericName=Screen Annotation");
        builder.AppendLine("GenericName[zh_CN]=屏幕批注");
        builder.AppendLine($"Exec={executable} %U");
        builder.AppendLine("Terminal=false");
        builder.AppendLine("Categories=Utility;Presentation;");
        builder.AppendLine("Keywords=pointer;annotation;screen;whiteboard;教鞭;批注;");
        builder.AppendLine("X-GNOME-Autostart-enabled=true");
        builder.AppendLine("StartupNotify=false");
        return builder.ToString();
    }
}
