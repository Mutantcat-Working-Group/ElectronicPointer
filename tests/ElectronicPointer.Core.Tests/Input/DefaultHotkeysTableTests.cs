using System.Reflection;
using Mutantcat.ElectronicPointer.Core.Input;
using Xunit;

namespace Mutantcat.ElectronicPointer.Core.Tests.Input;

/// <summary>
/// The table is what the shell registers, so anything missing from it is a gesture that
/// cannot fire and a settings list that under-reports what the app answers to. A gesture
/// listed twice registers twice, and a gesture with no label shows up in the settings as
/// an anonymous key combination.
/// </summary>
public class DefaultHotkeysTableTests
{
    [Fact]
    public void EveryDeclaredGestureIsListed()
    {
        // Ctrl+Y was built, wired into the shell's action table and then left out of the
        // list, so nothing ever registered it. Reflecting over the fields catches that
        // class of omission instead of catching this one instance of it.
        var listed = DefaultHotkeys.All.Select(entry => entry.Hotkey).ToHashSet();

        foreach (var field in typeof(DefaultHotkeys).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.FieldType != typeof(Hotkey))
                continue;

            var gesture = (Hotkey)field.GetValue(null)!;
            Assert.True(listed.Contains(gesture), $"{field.Name} ({gesture.Text}) is not listed in All");
        }
    }

    [Fact]
    public void EveryGestureIsListedOnce()
    {
        var seen = new HashSet<Hotkey>();

        foreach (var (hotkey, _) in DefaultHotkeys.All)
        {
            Assert.True(seen.Add(hotkey), $"{hotkey.Text} is listed more than once");
        }
    }

    [Fact]
    public void EveryGestureCarriesAName()
    {
        foreach (var (hotkey, description) in DefaultHotkeys.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(description), $"{hotkey.Text} has no description");
        }
    }

    [Fact]
    public void EveryGestureHasAKey()
    {
        foreach (var (hotkey, _) in DefaultHotkeys.All)
        {
            Assert.NotEqual(KeyCode.None, hotkey.Key);
            Assert.True(hotkey.Text.Length > 0, "a gesture has to render as something a user can read");
        }
    }

    [Fact]
    public void RedoHasAFallbackGesture()
    {
        // Redo is the one undo everybody reaches for immediately afterwards, so it needs a
        // gesture that works on hosts where Shift is taken by something else.
        var listed = DefaultHotkeys.All.Select(entry => entry.Hotkey).ToHashSet();

        Assert.Contains(DefaultHotkeys.RedoAlternate, listed);
        Assert.NotEqual(DefaultHotkeys.Redo, DefaultHotkeys.RedoAlternate);
        Assert.Equal(KeyCode.Y, DefaultHotkeys.RedoAlternate.Key);
    }
}
