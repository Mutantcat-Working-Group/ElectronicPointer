using System.Runtime.InteropServices;

namespace Mutantcat.ElectronicPointer.Platform.Linux.Native;

/// <summary>
/// One-time process setup for Xlib. <c>XInitThreads</c> has to run before the first display
/// connection is opened, and this app opens two: one on the UI thread for the overlay and one
/// on a dedicated thread for the shortcut loop. Calling it again later is a no-op in Xlib, but
/// the flag here keeps the call off the hot paths and makes the intent explicit at each site.
/// </summary>
internal static class X11Runtime
{
    private static int _initialized;

    private static readonly object Gate = new();

    /// <summary>True once Xlib has been told the process is multithreaded.</summary>
    public static bool EnsureThreadsInitialized()
    {
        if (Volatile.Read(ref _initialized) == 1)
            return true;

        lock (Gate)
        {
        try
        {
            if (X11NativeMethods.XInitThreads() == 0)
                return false;

            // Only a successful call flips the flag, so a later retry is still possible.
            Volatile.Write(ref _initialized, 1);
            return true;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
        }
    }
}
