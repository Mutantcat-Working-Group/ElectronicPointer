using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using Mutantcat.ElectronicPointer.App.Session;
using Mutantcat.ElectronicPointer.App.Views;
using Mutantcat.ElectronicPointer;
using Mutantcat.ElectronicPointer.Core.Board;
using Mutantcat.ElectronicPointer.Core.Geometry;
using Mutantcat.ElectronicPointer.Core.Ink;
using Mutantcat.ElectronicPointer.Core.Input;
using Mutantcat.ElectronicPointer.Platform.Capture;
using Mutantcat.ElectronicPointer.Platform.Displays;
using Mutantcat.ElectronicPointer.Platform.Linux;
using Mutantcat.ElectronicPointer.Platform.MacOS;
using Mutantcat.ElectronicPointer.Platform.Services;
using Mutantcat.ElectronicPointer.Platform.Windows;
using Mutantcat.ElectronicPointer.Rendering;
using SkiaSharp;

namespace Mutantcat.ElectronicPointer.App.Shell;

/// <summary>
/// Everything the original Windows build did at the WPF shell level, expressed once for
/// every desktop platform: pick the host's services, put one transparent canvas on each
/// screen, and route the global shortcuts back onto the UI thread.
///
/// The shell is the only place that learns which operating system it runs on, and it does
/// that by choosing an <see cref="IPlatformServices"/> implementation rather than by
/// branching on the OS inside a feature, so supporting another platform is a new project
/// instead of edits scattered through the UI.
/// </summary>
public sealed class OverlayShell : IDisposable
{
    private readonly List<OverlayWindow> _overlays = new();
    private readonly List<(Hotkey Hotkey, Action Action)> _bindings = new();
    private readonly AppConfiguration _configuration = AppConfiguration.Load();
    private ToolbarWindow? _toolbar;
    private SettingsWindow? _settings;
    private bool _disposed;

    public OverlayShell()
    {
        Platform = CreatePlatformServices();
        Session = new BoardSession();
        _configuration.ApplyTo(Session);
        _configuration.AutoStart = Platform.AutoStart.IsEnabled;
        Session.Changed += OnSessionChanged;
        Platform.Presentation.PresentationActiveChanged += OnPresentationActiveChanged;
    }

    /// <summary>The one and only piece of mutable UI state in the app.</summary>
    public BoardSession Session { get; }

    public IPlatformServices Platform { get; }

    public AppConfiguration Configuration => _configuration;

    /// <summary>
    /// The window the desktop lifetime treats as the app's main window: the first overlay,
    /// so the app always has a window even with every toolbar closed.
    /// </summary>
    public Window MainWindow { get; private set; } = null!;

    public ToolbarWindow? Toolbar => _toolbar;

    public bool CanFreezeScreen => Platform.ScreenCapture.IsSupported;

    public bool CanRecognize => Platform.Recognizer.IsSupported;

    public bool CanAutoStart => Platform.AutoStart.IsSupported;

    public void Start()
    {
        // Avalonia only publishes the display list through a live window, so the toolbar is
        // opened first and then asked where the screens are. Building the canvases from
        // that answer is what makes a three-monitor desk behave the same as a laptop.
        _toolbar = new ToolbarWindow(this);
        _toolbar.Show();

        foreach (var screen in AvailableScreens())
        {
            var overlay = new OverlayWindow(screen);
            _overlays.Add(overlay);
            overlay.Show();
            overlay.AttachChrome(Platform, Session);
            overlay.Bind(Session);
        }

        MainWindow = _overlays.Count > 0 ? _overlays[0] : _toolbar;
        RegisterHotkeys();
        Platform.Presentation.Start();
    }

    /// <summary>Every display, read through the toolbar's host window.</summary>
    private IReadOnlyList<Screen> AvailableScreens()
    {
        return _toolbar?.Screens?.All ?? Array.Empty<Screen>();
    }

    // ------------------------------------------------------------------ shortcuts

    private void RegisterHotkeys()
    {
        if (!Platform.Hotkeys.IsSupported)
            return;

        foreach (var (hotkey, _) in DefaultHotkeys.All)
        {
            var mapped = MapHotkey(hotkey);
            if (Platform.Hotkeys.Register(mapped))
                _bindings.Add((mapped, ActionFor(hotkey)));
        }

        Platform.Hotkeys.Pressed += OnHotkeyPressed;
    }

    /// <summary>
    /// Control on macOS means Command. The shared binding table was written for Windows,
    /// and a Mac user's fingers already expect Cmd+Z to undo and Cmd+Q to quit, so the
    /// translation happens here and every platform keeps a single set of gestures.
    /// </summary>
    private static Hotkey MapHotkey(Hotkey hotkey)
    {
        if (!OperatingSystem.IsMacOS())
            return hotkey;

        var modifiers = hotkey.Modifiers;
        if ((modifiers & HotkeyModifiers.Control) != 0)
        {
            modifiers &= ~HotkeyModifiers.Control;
            modifiers |= HotkeyModifiers.Command;
        }

        return new Hotkey(modifiers, hotkey.Key);
    }

    private Action ActionFor(Hotkey hotkey)
    {
        if (hotkey == DefaultHotkeys.TogglePassThrough)
            return TogglePassThrough;

        if (hotkey == DefaultHotkeys.Pen)
            return () => Session.Tool = ToolKind.Pen;

        if (hotkey == DefaultHotkeys.Highlighter)
            return () => Session.Tool = ToolKind.Highlighter;

        if (hotkey == DefaultHotkeys.Eraser)
            return () => Session.Tool = ToolKind.Eraser;

        if (hotkey == DefaultHotkeys.Select)
            return () => Session.Tool = ToolKind.Select;

        if (hotkey == DefaultHotkeys.Undo)
            return Session.Undo;

        if (hotkey == DefaultHotkeys.Redo || hotkey == DefaultHotkeys.RedoAlternate)
            return Session.Redo;

        if (hotkey == DefaultHotkeys.Quit)
            return Quit;

        if (hotkey == DefaultHotkeys.ClearPage)
            return Session.ClearPage;

        if (hotkey == DefaultHotkeys.NewPage)
            return Session.AddPage;

        if (hotkey == DefaultHotkeys.NextPage)
            return Session.NextPage;

        if (hotkey == DefaultHotkeys.PreviousPage)
            return Session.PreviousPage;

        if (hotkey == DefaultHotkeys.ToggleToolbar)
            return ToggleToolbar;

        return static () => { };
    }

    private void OnHotkeyPressed(object? sender, Hotkey hotkey)
    {
        foreach (var (registered, action) in _bindings)
        {
            if (registered != hotkey)
                continue;

            // This arrives on the platform's own thread: an AppKit run loop or an X11
            // listener. Avalonia may only be touched from the UI thread.
            Dispatcher.UIThread.Post(action);
            return;
        }
    }

    // ------------------------------------------------------------------ commands

    public void TogglePassThrough()
    {
        Session.PassThrough = !Session.PassThrough;
    }

    public void ToggleToolbar()
    {
        Session.ToolbarVisible = !Session.ToolbarVisible;
        if (_toolbar is not null)
            _toolbar.IsVisible = Session.ToolbarVisible;
    }

    /// <summary>
    /// Grabs a screen and puts the picture behind the ink, so the user can annotate a slide
    /// that has already scrolled past, or a page that refuses to be drawn over reliably.
    /// </summary>
    public void FreezeScreen()
    {
        if (!ResolveFreezeTarget(out var screen, out var display))
            return;

        var captured = Platform.ScreenCapture.Capture(display);
        if (captured is null)
            return;

        // The board measures in device independent pixels while a grab is taken in real
        // ones, so the picture is resampled to the display's own unit size first.
        var scale = screen?.Scaling ?? (captured.ScaleFactor > 0 ? captured.ScaleFactor : 1d);
        var width = Math.Max(1, (int)Math.Round(captured.Width / scale));
        var height = Math.Max(1, (int)Math.Round(captured.Height / scale));
        var pixels = captured.Pixels;

        if (width != captured.Width || height != captured.Height)
        {
            using var source = ToBitmap(captured);
            using var resized = BitmapExporter.Resize(source, width, height);
            pixels = FromBitmap(resized);
        }

        var origin = screen is { } target
            ? new Vec2(target.Bounds.X / target.Scaling, target.Bounds.Y / target.Scaling)
            : Vec2.Zero;

        Session.Freeze(new BoardBackground(pixels, width, height, origin.X, origin.Y));

        // Freezing is always followed by writing, so the pen is handed back as well.
        Session.PassThrough = false;
    }

    /// <summary>
    /// The display the pen is on, plus the screen Avalonia thinks lives there. The capture
    /// service and Avalonia enumerate displays independently, so the two are matched by
    /// size: a grab taken on the monitor to the right has to land on the right.
    /// </summary>
    private bool ResolveFreezeTarget(out Screen? screen, out DisplayInfo display)
    {
        screen = null;
        display = null!;

        var displays = Platform.ScreenCapture.Displays;
        if (displays.Count == 0)
            return false;

        var index = Math.Clamp(Session.FrozenScreenIndex, 0, displays.Count - 1);
        display = displays[index];

        var screens = AvailableScreens();
        if (screens.Count == 0)
            return true;

        // Index first, because both lists report displays the way the OS orders them, and
        // size as the fallback, because a capture backend and Avalonia do enumerate
        // independently. Either way a grab taken on the monitor to the right must land on
        // the right, which is what the size test protects.
        var byIndex = Session.FrozenScreenIndex >= 0 && Session.FrozenScreenIndex < screens.Count
            ? screens[Session.FrozenScreenIndex]
            : null;

        // The lambda cannot capture an "out" parameter, so the display is copied first.
        var targetDisplay = display;
        screen = byIndex is { } indexed && SameSize(indexed, targetDisplay)
            ? indexed
            : screens.FirstOrDefault(s => SameSize(s, targetDisplay));

        return true;
    }

    private static bool SameSize(Screen screen, DisplayInfo display) =>
        screen.Bounds.Width == display.Width && screen.Bounds.Height == display.Height;

    /// <summary>Saves the annotated page as a picture covering the whole desk.</summary>
    public async Task SaveImageAsync()
    {
        if (_toolbar is null)
            return;

        var file = await _toolbar.StorageProvider.SaveFilePickerAsync(new Avalonia.Platform.Storage.FilePickerSaveOptions
        {
            Title = "保存标注图片",
            DefaultExtension = "png",
            SuggestedFileName = "电子教鞭批注.png",
            FileTypeChoices = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("PNG 图片")
                {
                    Patterns = new[] { "*.png" },
                },
                new Avalonia.Platform.Storage.FilePickerFileType("JPEG 图片")
                {
                    Patterns = new[] { "*.jpg", "*.jpeg" },
                },
            },
        });

        if (file is null)
            return;

        var path = file.Path.LocalPath;
        var page = Session.Document.ActivePage;
        var (origin, width, height) = ExportBounds();

        using var renderer = new BoardRenderer();
        using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using (var canvas = new SKCanvas(bitmap))
        {
            renderer.Render(
                canvas,
                page,
                RenderOptions.Default with
                {
                    Width = width,
                    Height = height,
                    Scale = 1f,
                    Origin = origin,
                    Background = new SKColor(0xFF, 0xFF, 0xFF, 0xFF),
                });
        }

        var jpeg = path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);

        var bytes = jpeg ? BitmapExporter.EncodeJpeg(bitmap) : BitmapExporter.EncodePng(bitmap);
        await File.WriteAllBytesAsync(path, bytes);
    }

    /// <summary>
    /// The board rectangle worth exporting: every screen stitched into one rectangle,
    /// expressed in board units so a three-monitor desk exports as a single wide picture.
    /// </summary>
    private (Vec2 Origin, int Width, int Height) ExportBounds()
    {
        var screens = AvailableScreens();
        if (screens.Count == 0)
            return (Vec2.Zero, 1920, 1080);

        var minX = screens.Min(s => s.Bounds.X);
        var minY = screens.Min(s => s.Bounds.Y);
        var maxX = screens.Max(s => s.Bounds.Right);
        var maxY = screens.Max(s => s.Bounds.Bottom);

        var primary = screens.FirstOrDefault(s => s.IsPrimary) ?? screens[0];
        var scale = primary.Scaling > 0 ? primary.Scaling : 1d;

        return (
            new Vec2(minX / scale, minY / scale),
            Math.Max(1, (int)Math.Round((maxX - minX) / scale)),
            Math.Max(1, (int)Math.Round((maxY - minY) / scale)));
    }

    public void ShowSettings()
    {
        if (_settings is { IsVisible: true })
        {
            _settings.Activate();
            return;
        }

        _settings = new SettingsWindow(this);
        _settings.Show();
    }

    public void SetAutoStart(bool enabled)
    {
        _configuration.AutoStart = enabled;
        Platform.AutoStart.SetEnabled(enabled);
        SaveConfiguration();
    }

    public void SaveConfiguration()
    {
        _configuration.ReadFrom(Session);
        _configuration.Save();
    }

    public void Quit()
    {
        SaveConfiguration();

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

    // ------------------------------------------------------------------ plumbing

    private void OnSessionChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            foreach (var overlay in _overlays)
                overlay.ApplyClickThrough(Session.PassThrough);

            _toolbar?.Refresh();
        });
    }

    private void OnPresentationActiveChanged(object? sender, bool active)
    {
        // Raised from the platform's own watcher thread.
        Dispatcher.UIThread.Post(() => Session.PassThrough = active);
    }

    private static SKBitmap ToBitmap(CapturedScreen captured)
    {
        var bitmap = new SKBitmap(captured.Width, captured.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var length = Math.Min(captured.Pixels.Length, captured.Width * captured.Height * 4);
        Marshal.Copy(captured.Pixels, 0, bitmap.GetPixels(), length);
        return bitmap;
    }

    private static byte[] FromBitmap(SKBitmap bitmap)
    {
        var pixels = new byte[bitmap.Width * bitmap.Height * 4];
        Marshal.Copy(bitmap.GetPixels(), pixels, 0, pixels.Length);
        return pixels;
    }

    /// <summary>
    /// Chooses the platform implementation. Only the running OS decides, so the same binary
    /// runs everywhere and degrades to a clear message on an unsupported host rather than
    /// throwing on startup.
    /// </summary>
    private static IPlatformServices CreatePlatformServices()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsPlatformServices();

        if (OperatingSystem.IsMacOS())
            return new MacOSPlatformServices();

        if (OperatingSystem.IsLinux())
            return new LinuxPlatformServices();

        return new UnsupportedPlatformServices("电子教鞭目前支持 Windows、macOS 和 Linux。");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        SaveConfiguration();
        Session.Changed -= OnSessionChanged;
        Platform.Presentation.PresentationActiveChanged -= OnPresentationActiveChanged;

        foreach (var overlay in _overlays.ToArray())
            overlay.Close();

        _overlays.Clear();

        if (Platform.Hotkeys.IsSupported)
        {
            Platform.Hotkeys.Pressed -= OnHotkeyPressed;
            Platform.Hotkeys.UnregisterAll();
        }

        Platform.Presentation.Stop();
        Platform.Dispose();
    }
}
