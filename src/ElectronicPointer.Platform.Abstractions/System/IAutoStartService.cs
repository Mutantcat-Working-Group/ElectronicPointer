// The namespace is deliberately not called "System": any namespace ending in
// .System shadows the BCL "System" inside every Mutantcat.ElectronicPointer.Platform.*
// file, which breaks "System.Text" and friends there.
namespace Mutantcat.ElectronicPointer.Platform.Startup;

/// <summary>
/// "Start when I log in". Windows writes a Run key, macOS a launch agent, Linux a
/// desktop entry in ~/.config/autostart.
/// </summary>
public interface IAutoStartService
{
    bool IsSupported { get; }

    bool IsEnabled { get; }

    void SetEnabled(bool enabled);
}
