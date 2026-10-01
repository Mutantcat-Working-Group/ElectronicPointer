using System.Text.Json;
using System.Text.Json.Serialization;
using Mutantcat.ElectronicPointer;
using Mutantcat.ElectronicPointer.Core.Ink;

namespace Mutantcat.ElectronicPointer.App.Session;

/// <summary>
/// The handful of choices worth remembering between lectures: which tool was in hand, how
/// thick the ink was, whether the pen should sit through clicks, and whether the app should
/// already be running at login. Stored as plain JSON under the platform's own config
/// directory, so the file lands where the user expects on each OS.
/// </summary>
public sealed class AppConfiguration
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [JsonIgnore]
    public string Path { get; private set; } = string.Empty;

    /// <summary>
    /// Whether a file was there to read. A first run has nothing to inherit, which is the
    /// one moment the app can afford to explain itself before the user has learned where
    /// anything is, and the only moment where an unsaved setting is not a lost one.
    /// </summary>
    [JsonIgnore]
    public bool WasLoaded { get; private set; }

    public ToolKind Tool { get; set; } = ToolKind.Pen;

    public uint Color { get; set; } = InkPalette.Colors[0];

    public double PenSize { get; set; } = StrokeStyle.DefaultPen().Size;

    public double HighlighterSize { get; set; } = StrokeStyle.DefaultHighlighter().Size;

    public double EraserRadius { get; set; } = 24;

    public bool PassThrough { get; set; }

    public int FrozenScreenIndex { get; set; }

    public bool AutoStart { get; set; }

    /// <summary>
    /// Reads the file, or falls back to defaults. A corrupt or unreadable file is never
    /// fatal: losing a remembered pen size is not worth refusing to start.
    /// </summary>
    public static AppConfiguration Load()
    {
        var configuration = new AppConfiguration();
        var path = FilePath();
        configuration.Path = path;

        try
        {
            if (!File.Exists(path))
                return configuration;

            var text = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(text))
                return configuration;

            var loaded = JsonSerializer.Deserialize<AppConfiguration>(text, SerializerOptions);
            if (loaded is null)
                return configuration;

            loaded.Path = path;
            loaded.WasLoaded = true;
            return Sanitize(loaded);
        }
        catch (JsonException)
        {
            return configuration;
        }
        catch (IOException)
        {
            return configuration;
        }
        catch (UnauthorizedAccessException)
        {
            return configuration;
        }
    }

    public void ApplyTo(BoardSession session)
    {
        session.Tool = Tool;
        session.Color = Color;
        session.PenSize = PenSize;
        session.HighlighterSize = HighlighterSize;
        session.EraserRadius = EraserRadius;
        session.PassThrough = PassThrough;
        session.FrozenScreenIndex = FrozenScreenIndex;
    }

    public void ReadFrom(BoardSession session)
    {
        Tool = session.Tool;
        Color = session.Color;
        PenSize = session.PenSize;
        HighlighterSize = session.HighlighterSize;
        EraserRadius = session.EraserRadius;
        PassThrough = session.PassThrough;
        FrozenScreenIndex = session.FrozenScreenIndex;
    }

    public void Save()
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrEmpty(directory))
                System.IO.Directory.CreateDirectory(directory);

            File.WriteAllText(Path, JsonSerializer.Serialize(this, SerializerOptions));
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string FilePath()
    {
        return System.IO.Path.Combine(AppIdentity.GetConfigDirectory(), "settings.json");
    }

    private static AppConfiguration Sanitize(AppConfiguration loaded)
    {
        if (!Enum.IsDefined(loaded.Tool))
            loaded.Tool = ToolKind.Pen;

        loaded.PenSize = Clamp(loaded.PenSize, StrokeStyle.DefaultPen().Size);
        loaded.HighlighterSize = Clamp(loaded.HighlighterSize, StrokeStyle.DefaultHighlighter().Size);
        loaded.EraserRadius = Clamp(loaded.EraserRadius, 24);
        loaded.FrozenScreenIndex = Math.Max(0, loaded.FrozenScreenIndex);
        return loaded;
    }

    private static double Clamp(double value, double fallback)
    {
        if (double.IsNaN(value) || value <= 0)
            return fallback;

        return Math.Min(value, 96);
    }
}
