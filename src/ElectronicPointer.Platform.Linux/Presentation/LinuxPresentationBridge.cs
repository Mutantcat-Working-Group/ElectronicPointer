using System.ComponentModel;
using System.Diagnostics;
using Mutantcat.ElectronicPointer.Platform.Presentation;

namespace Mutantcat.ElectronicPointer.Platform.Linux;

/// <summary>
/// Watches for a running slideshow on Linux. There is no integration point like the Windows
/// add-in, so the best a portable client can do is look for a process that is presenting.
/// <c>Process.GetProcesses</c> is the one source every Linux system exposes, and the
/// executable names below are what LibreOffice Impress and the dedicated Linux presenters run.
///
/// This is inherently weaker than the Windows behaviour: LibreOffice shares one process across
/// all of its modules, so a presentation and a typed-in document look alike from the outside.
/// That is a cosmetic nuisance rather than a failure, so the bridge stays available and simply
/// tells the user what it matches on. A slideshow started inside a browser is not visible at
/// all, which is the other half of why this is a heuristic rather than a contract.
/// </summary>
public sealed class LinuxPresentationBridge : IPresentationBridge, IDisposable
{
    private static readonly string[] PresentationProcessNames =
    {
        // LibreOffice shares one process across all its modules, hence the qualifier below.
        "soffice.bin",
        "impress",
        // Dedicated Linux presenters.
        "pympress",
        "pdfpc",
        "impressive",
    };

    private readonly Lock Sync = new();

    private readonly Timer _timer;

    private volatile bool _active;

    private bool _disposed;

    public bool IsSupported => true;

    public bool IsPresentationActive => _active;

    public event EventHandler<bool>? PresentationActiveChanged;

    public LinuxPresentationBridge()
    {
        // A slow, low cost poll: the check is a /proc scan, not a message subscription.
        _timer = new Timer(_ => Poll(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        lock (Sync)
        {
            if (_disposed)
                return;

            _ = _timer.Change(TimeSpan.Zero, TimeSpan.FromMilliseconds(1500));
        }
    }

    public void Stop()
    {
        lock (Sync)
        {
            if (_disposed)
                return;

            _ = _timer.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    public void Dispose()
    {
        lock (Sync)
        {
            if (_disposed)
                return;

            _disposed = true;
        }

        _timer.Dispose();
    }

    private void Poll()
    {
        var active = IsPresentationProcessRunning();
        if (active == _active)
            return;

        _active = active;

        // Raised off a timer thread, so callers marshal just like they do for hotkeys.
        PresentationActiveChanged?.Invoke(this, active);
    }

    internal static bool IsPresentationProcessRunning()
    {
        try
        {
            foreach (var process in Process.GetProcesses())
            {
                foreach (var candidate in PresentationProcessNames)
                {
                    if (string.Equals(process.ProcessName, candidate, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                // The only way to separate an Impress slideshow from a Writer document: the
                // binary LibreOffice launches for a presentation.
                if (process.ProcessName.StartsWith("soffice", StringComparison.OrdinalIgnoreCase)
                    && IsImpressProcess(process))
                    return true;
            }

            return false;
        }
        catch (InvalidOperationException)
        {
            // A process that exits between the enumeration and the name read.
            return false;
        }
    }

    private static bool IsImpressProcess(Process process)
    {
        try
        {
            var module = process.MainModule?.FileName;
            return module is not null
                && module.Contains("impress", StringComparison.OrdinalIgnoreCase);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (Win32Exception)
        {
            // A module belonging to another user, or one that exited while reading it.
            return false;
        }
    }
}
