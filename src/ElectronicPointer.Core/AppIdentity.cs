using System.Reflection;
using System.Runtime.InteropServices;

namespace Mutantcat.ElectronicPointer;

/// <summary>
/// Single source of truth for the product name and for every packaging identifier.
/// Build targets, installers and the application itself all read these values so the
/// identifiers can never drift apart.
/// </summary>
public static class AppIdentity
{
    public const string ChineseName = "电子教鞭";

    public const string ProductName = "ElectronicPointer";

    public const string ExecutableName = "ElectronicPointer";

    public const string OrganizationName = "Mutantcat Working Group";

    public const string OrganizationDomain = "org.mutantcat";

    public const string Organization = "mutantcat";

    /// <summary>macOS bundle identifier and Linux Flatpak application ID.</summary>
    public const string ApplicationId = "org.mutantcat.electronicpointer";

    public const string PluginIdPrefix = "org.mutantcat.electronicpointer.plugin";

    /// <summary>Windows MSIX package identity name (used for AUMID lookups).</summary>
    public const string WindowsMsixIdentity = "Mutantcat.ElectronicPointer";

    public const string WindowsAumid = "Mutantcat.ElectronicPointer.App";

    public const string DebianPackageName = "electronicpointer";

    private const string FallbackVersion = "0.0.0-dev";

    /// <summary>
    /// Product version stamped by the build, in the major.minor.YYYYMMDD form. It is read
    /// back from the assembly rather than assigned by the host so a repackaged or signed
    /// build reports the version it was actually built as.
    /// </summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        foreach (var assembly in new[] { typeof(AppIdentity).Assembly, Assembly.GetEntryAssembly() })
        {
            if (assembly is null)
                continue;

            var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(informational))
            {
                // The SDK appends "+<commit>" when source revisions are included. That is
                // build metadata, not something a user should read in an about box.
                return informational.Split('+')[0];
            }
        }

        return FallbackVersion;
    }

    public static string GetPluginId(string name) => $"{PluginIdPrefix}.{name}";

    /// <summary>The display name every window title and about box shows.</summary>
    public static string GetDisplayName() => ChineseName;

    /// <summary>
    /// Per-user configuration directory. Mirrors the conventions each platform expects:
    /// %AppData%\Mutantcat\ElectronicPointer, ~/Library/Application Support/&lt;id&gt;
    /// and ~/.config/&lt;id&gt; respectively.
    /// </summary>
    public static string GetConfigDirectoryName()
    {
        // Windows keeps the vendor/product convention every program there follows. Unix uses
        // the reverse-DNS application id, the same string the installers register.
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Path.Combine("Mutantcat", ProductName);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return Path.Combine("Application Support", ApplicationId);

        return ApplicationId;
    }

    public static string GetConfigDirectory()
    {
        var root = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return Path.Combine(root, GetConfigDirectoryName());
    }
}
