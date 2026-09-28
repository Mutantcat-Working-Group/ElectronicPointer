using System.Globalization;
using Mutantcat.ElectronicPointer.Platform.Presentation;
using System.Text;
using System.Diagnostics;

namespace Mutantcat.ElectronicPointer.Platform.Windows;

/// <summary>
/// Notices that a slideshow went full screen by watching for the window class the common
/// presentation hosts use for their slide show surface. Polling is deliberate: the old
/// build needed a PowerPoint add-in for this, and a watcher keeps working for any host
/// that shows up in <see cref="SlideShowWindowClasses"/>.
///
/// Events arrive on the watcher thread, so the receiver has to marshal them itself.
/// </summary>
public sealed class Win32PresentationBridge : IPresentationBridge, IDisposable
{
    private static readonly string[] SlideShowWindowClasses =
    {
        "screenClass",       // Microsoft PowerPoint
        "__WMPaintView",     // LibreOffice Impress slideshow
        "wpsSlide",          // WPS Presentation
    };

    private readonly Lock _gate = new();
    private readonly List<EventHandler<bool>> _handlers = new();
    private Thread? _watcher;
    private bool _stopRequested;
    private bool _active;
    private bool _disposed;

    public bool IsSupported => true;

    public bool IsPresentationActive
    {
        get
        {
            lock (_gate)
            {
                return _active;
            }
        }
    }

    public event EventHandler<bool>? PresentationActiveChanged
    {
        add
        {
            if (value is null)
                return;

            lock (_gate)
            {
                _handlers.Add(value);
            }
        }

        remove
        {
            if (value is null)
                return;

            lock (_gate)
            {
                _handlers.Remove(value);
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_watcher is not null || _disposed)
                return;

            _stopRequested = false;
            _watcher = new Thread(Watch)
            {
                IsBackground = true,
                Name = "ElectronicPointer presentation watcher",
                Priority = ThreadPriority.BelowNormal,
            };
            _watcher.Start();
        }
    }

    public void Stop()
    {
        Thread? watcher;
        lock (_gate)
        {
            watcher = _watcher;
            _watcher = null;
            _stopRequested = true;
        }

        // The poll interval is short enough that joining never becomes a visible delay.
        watcher?.Join(TimeSpan.FromSeconds(2));
    }

    public void Dispose()
    {
        Stop();
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    private void Watch()
    {
        while (true)
        {
            lock (_gate)
            {
                if (_stopRequested)
                    return;
            }

            var detected = DetectSlideShow();
            var changed = false;

            lock (_gate)
            {
                if (detected != _active)
                {
                    _active = detected;
                    changed = true;
                }
            }

            if (changed)
            {
                EventHandler<bool>[] subscribers;
                lock (_gate)
                {
                    subscribers = _handlers.ToArray();
                }

                foreach (var handler in subscribers)
                {
                    try
                    {
                        handler(this, detected);
                    }
                    catch (Exception exception)
                    {
                        Debug.WriteLine(
                            string.Create(CultureInfo.InvariantCulture, $"Presentation handler threw: {exception}"));
                    }
                }
            }

            Thread.Sleep(TimeSpan.FromMilliseconds(700));
        }
    }

    private static bool DetectSlideShow()
    {
        var found = false;

        NativeMethods.EnumWindows(
            (window, parameter) =>
            {
                if (found)
                    return false;

                if (!NativeMethods.IsWindowVisible(window))
                    return true;

                var buffer = new StringBuilder(256);
                _ = NativeMethods.GetClassName(window, buffer, buffer.Capacity);
                foreach (var slideShowClass in SlideShowWindowClasses)
                {
                    if (string.Equals(buffer.ToString(), slideShowClass, StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        return false;
                    }
                }

                return true;
            },
            IntPtr.Zero);

        return found;
    }
}
