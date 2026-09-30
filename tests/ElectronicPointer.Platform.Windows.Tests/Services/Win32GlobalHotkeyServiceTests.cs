using Mutantcat.ElectronicPointer.Core.Input;
using Mutantcat.ElectronicPointer.Platform.Windows;
using Xunit;

namespace Mutantcat.ElectronicPointer.Platform.Windows.Tests;

/// <summary>
/// Round trips the hotkey service against the real message window. Windows only lets
/// RegisterHotKey through on the thread that created the window, which is never the
/// thread a caller arrives on: the version that registered in place answered every
/// gesture with ERROR_WINDOW_OF_OTHER_THREAD, and the app read that as another program
/// owning the key. Asserting the round trip from an ordinary test thread is what keeps
/// the sink honest, and it is also why every assertion here waits for Windows.
/// </summary>
public sealed class Win32GlobalHotkeyServiceTests : IDisposable
{
    // A combination no desktop program is known to claim, so a refusal in a test means
    // the sink itself, not the machine it ran on.
    private static readonly Hotkey Gesture =
        new(HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Shift, KeyCode.F12);

    // Built on first use so a non-Windows run never reaches the sink: constructing it
    // would touch user32 from a thread that has no window to own.
    private readonly Lazy<Win32GlobalHotkeyService> _service = new(() => new Win32GlobalHotkeyService());

    [Fact]
    public void TheSinkWindowComesUp()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Assert.True(_service.Value.IsSupported);
    }

    [Fact]
    public void AGestureIsGrantedFromAnotherThread()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Assert.True(_service.Value.Register(Gesture));
    }

    [Fact]
    public void UnregisteringReleasesTheGesture()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Assert.True(_service.Value.Register(Gesture));
        Assert.True(_service.Value.Unregister(Gesture));
    }

    [Fact]
    public void RegisteringTheSameGestureTwiceKeepsOneRegistration()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Assert.True(_service.Value.Register(Gesture));
        Assert.True(_service.Value.Register(Gesture));
        Assert.True(_service.Value.Unregister(Gesture));
        Assert.False(_service.Value.Unregister(Gesture));
    }

    [Fact]
    public void ALateCallerIsToldTheSinkIsGone()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // Shut the sink down first: whatever arrives afterwards must be answered with
        // "there is no window" immediately instead of waiting out the round trip
        // timeout on a queue whose message loop has already left.
        var service = new Win32GlobalHotkeyService();
        service.Dispose();

        Assert.False(service.IsSupported);
        Assert.False(service.Register(Gesture));
    }

    public void Dispose()
    {
        if (_service.IsValueCreated)
            _service.Value.Dispose();
    }
}
