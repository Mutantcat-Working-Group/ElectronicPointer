using System.Runtime.InteropServices;
using Mutantcat.ElectronicPointer.Platform.MacOS.Native;
using Mutantcat.ElectronicPointer.Platform.Presentation;

namespace Mutantcat.ElectronicPointer.Platform.MacOS;

/// <summary>
/// Slideshow detection on macOS. There is no add-in model here the way there is on Windows,
/// so the bridge watches the frontmost application and treats a known presentation host as
/// presenting. That is inherently weaker than the Windows bridge, and the interface says so:
/// it gets the common cases right without a privileged hook, which is the trade a portable
/// build has to make.
/// </summary>
public sealed class MacOSPresentationBridge : IPresentationBridge, IDisposable
{
    private static readonly string[] PresentationHosts =
    {
        "Keynote",
        "Microsoft PowerPoint",
        "PowerPoint",
        "LibreOffice",
        "WPS Presentation",
        "WPS Office",
    };

    private readonly Lock _sync = new();
    private Thread? _polling;
    private bool _running;
    private bool _active;
    private bool _disposed;

    public bool IsSupported => OperatingSystem.IsMacOS();

    public bool IsPresentationActive
    {
        get
        {
            lock (_sync)
                return _active;
        }
    }

    public event EventHandler<bool>? PresentationActiveChanged;

    public void Start()
    {
        if (_running)
            return;

        _running = true;
        _polling = new Thread(Watch)
        {
            IsBackground = true,
            Name = "ElectronicPointer.PresentationWatch",
        };

        _polling.Start();
    }

    public void Stop()
    {
        _running = false;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _running = false;
    }

    private void Watch()
    {
        while (_running && !_disposed)
        {
        try
        {
            var active = FrontmostHostIsPresenting();
            if (active != IsPresentationActive)
            {
                lock (_sync)
                    _active = active;

                PresentationActiveChanged?.Invoke(this, active);
            }
        }
        catch (DllNotFoundException)
        {
            return;
        }
        catch (EntryPointNotFoundException)
        {
            return;
        }

            Thread.Sleep(700);
        }
    }

    private static bool FrontmostHostIsPresenting()
    {
        var workspaceClass = MacOSNativeMethods.objc_getClass("NSWorkspace");
        if (workspaceClass == 0)
            return false;

        var workspace = MacOSNativeMethods.ObjcSendNoArgs(workspaceClass, Sel("sharedWorkspace"));
        if (workspace == 0)
            return false;

        var application = MacOSNativeMethods.ObjcSendNoArgs(workspace, Sel("frontmostApplication"));
        if (application == 0)
            return false;

        var name = ReadString(MacOSNativeMethods.ObjcSendNoArgs(application, Sel("localizedName")));
        if (string.IsNullOrEmpty(name))
            return false;

        foreach (var host in PresentationHosts)
        {
            if (name.Equals(host, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string? ReadString(nint handle)
    {
        if (handle == 0)
            return null;

        var utf8 = MacOSNativeMethods.ObjcSendNoArgs(handle, Sel("UTF8String"));
        return utf8 == 0 ? null : Marshal.PtrToStringUTF8(utf8);
    }

    private static nint Sel(string name) => MacOSNativeMethods.sel_registerName(name);
}
