using Mutantcat.ElectronicPointer.Core.Input;

namespace Mutantcat.ElectronicPointer.Platform.Input;

/// <summary>
/// System wide shortcuts, so the toolbar and the tool switch work while the user is
/// presenting in another application. Registration may fail on a platform that does not
/// hand out global keys, or when another app already owns the combination.
/// </summary>
public interface IGlobalHotkeyService
{
    bool IsSupported { get; }

    event EventHandler<Hotkey>? Pressed;

    bool Register(Hotkey hotkey);

    bool Unregister(Hotkey hotkey);

    void UnregisterAll();
}
