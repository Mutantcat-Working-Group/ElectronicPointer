using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Mutantcat.ElectronicPointer.App.Session;
using Mutantcat.ElectronicPointer.App.Shell;
using Mutantcat.ElectronicPointer;
using Mutantcat.ElectronicPointer.Core.Input;
using Mutantcat.ElectronicPointer.Core.Ink;
using Mutantcat.ElectronicPointer.Platform.Overlay;

namespace Mutantcat.ElectronicPointer.App.Views;

/// <summary>
/// The tool palette. It is a separate, non-transparent window rather than a strip inside the
/// overlay, for two reasons: the overlay may be told to let clicks through to the desktop
/// behind it, and the palette has to stay reachable while the pointer is somewhere else
/// entirely. Every control here writes into <see cref="BoardSession"/> and nothing else, so
/// the same palette drives all three platforms.
/// </summary>
public sealed class ToolbarWindow : Window
{
    private readonly OverlayShell _shell;
    private readonly WrapPanel _groups = new() { Orientation = Orientation.Horizontal };
    // Sized from the tool list below, so the two cannot drift apart: a button added without
    // a tool, or a tool without a button, fails loudly instead of pointing the wrong way.
    private readonly Button[] _toolButtons = new Button[PaletteTools.Length];
    private readonly Button[] _colorButtons;
    private readonly Slider _sizeSlider = new();
    private readonly TextBlock _sizeLabel = new();
    private readonly TextBlock _pageLabel = new();
    // These three are built into the palette after construction, so the null-forgiving
    // assignment keeps the non-null contract without pretending they are immutable.
    private Button _undoButton = null!;
    private Button _redoButton = null!;
    // The freeze button is a toggle, so the words on it are the state of the page rather
    // than a label fixed at build time: they live in a block that Refresh() rewrites.
    private readonly TextBlock _freezeLabel = new() { FontSize = 13 };
    private readonly ToggleSwitch _passThrough = new();
    private Button _freezeButton = null!;
    private Button _recognizeButton = null!;
    private Button _removePageButton = null!;
    private readonly TextBlock _statusLabel = new();
    private DispatcherTimer? _statusTimer;
    private bool _refreshing;

    // The palette travels with the canvases rather than being one of them, so it needs the
    // same lift they are given before it can be clicked at all.
    private readonly OverlayCompanion _companion = null!;

    // The tools the palette offers, in button order. Kept as a list rather than cast from
    // the button index: ToolKind gains members over time, and a cast would quietly point a
    // button at whichever tool happens to share that number.
    private static readonly ToolKind[] PaletteTools =
        { ToolKind.Pen, ToolKind.Highlighter, ToolKind.Eraser, ToolKind.Select };

    public ToolbarWindow(OverlayShell shell)
    {
        ArgumentNullException.ThrowIfNull(shell);

        _shell = shell;
        _colorButtons = new Button[InkPalette.Colors.Length];
        _companion = new OverlayCompanion(_shell.Platform);

        // Fixed width and trimmed: the outcome of a save or a recognition is longer than
        // the palette is prepared to grow for, and a palette that resizes under the user's
        // pointer mid gesture is worse than one that clips a sentence.
        _statusLabel.Width = 210;
        _statusLabel.FontSize = 11;
        _statusLabel.Foreground = new SolidColorBrush(Color.Parse("#8A9099"));
        _statusLabel.TextTrimming = TextTrimming.CharacterEllipsis;
        _statusLabel.VerticalAlignment = VerticalAlignment.Center;

        Title = AppIdentity.GetDisplayName();
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Opacity = 0;
        Width = 860;
        MinWidth = 460;
        SizeToContent = SizeToContent.Height;
        RequestedThemeVariant = ThemeVariant.Light;
        // The palette's shape is the card below and nothing else: a window that paints its
        // own opaque background puts a square sheet under the card's rounded corners, and
        // whatever the window manager then does to a frameless window, such as Windows 11
        // rounding one on its own while older systems leave it alone, lands on top of what
        // application drew, so no two corners end up agreeing. Letting the window stay
        // transparent hands all four corners to the card, which is the same thing the ink
        // canvas does, and the platform layer asks the host to keep its hands off that
        // shape rather than cutting the window to it.
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };

        var frame = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#FFFFFF")),
            BorderBrush = new SolidColorBrush(Color.Parse("#D7DCE3")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(CompanionShape.CornerRadius),
            Padding = new Thickness(10, 8),
            Child = _groups,
        };

        frame.PointerPressed += OnFramePressed;
        Content = frame;

        BuildToolGroup();
        BuildColorGroup();
        BuildSizeGroup();
        BuildPageGroup();
        BuildHistoryGroup();
        BuildWorkspaceGroup();
        BuildSystemGroup();

        Opened += OnOpened;
        // Windows 11 decides the shape of a frameless window the first time it composes one
        // and decides it again after every frame change, so refusing its rounding has to
        // happen while the native handle exists and the window is still hidden, and once
        // more after the layout that settles the palette's height: this palette sizes itself
        // to its content and grows by a resize, which is exactly the frame change that
        // settles the corners a second time. OnOpened is where the z-order lives, and the
        // resizes keep the corner shape from being left to the host on their own.
        this.KeepChromeCurrent(LiftAboveOverlays);

        // The palette is shifted into place after it opens and is composed again each time it
        // is, so the host is asked to keep its hands off the corners at those moments too,
        // exactly where the resizes above already do it.
        _companion.GuardAgainstLateCornerRounding(this);
        Refresh();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        // SizeToContent only knows the real height once the window is on screen, so the
        // first frame is invisible and the palette is placed before it becomes visible.
        var screen = Screens?.All.FirstOrDefault(s => s.IsPrimary) ?? Screens?.All.FirstOrDefault();
        if (screen is not null)
        {
            // The desk is counted in physical pixels and the palette is built in layout
            // units, so the cap crosses the screen's scale factor here. Without it a desk
            // at 125% is given a cap a quarter wider than the desk itself.
            var scale = screen.Scaling > 0 ? screen.Scaling : 1d;
            MaxWidth = Math.Max(MinWidth, screen.WorkingArea.Width / scale - 48);
        }

        Position = PreferredPosition();
        // The height that placement was computed from is still the pre-layout default
        // here; the content-sized height only arrives with the first layout pass. Without
        // the second placement the palette keeps a position derived from a height the
        // window never had and drifts away from the bottom edge, which is what X11 and
        // AppKit both show because they map the window before that pass runs.
        LayoutUpdated += OnLayoutUpdated;
        LiftAboveOverlays();
        Opacity = 1;
    }

    /// <summary>
    /// Asks the host to keep the palette above every canvas. The canvases float higher than
    /// ordinary windows, so a palette left at the ordinary level is buried under ink that
    /// swallows the click meant for its buttons: on macOS that used to leave the user with
    /// nothing to press at all. Canvases are rebuilt whenever the display list changes, and
    /// each new canvas asks to be on top again, so this is called once more after that.
    /// </summary>
    internal void LiftAboveOverlays()
    {
        _companion.Lift(this, CompanionRole.Palette);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _companion.Release();
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        LayoutUpdated -= OnLayoutUpdated;
        Position = PreferredPosition();
    }

    /// <summary>
    /// Bottom centre of the primary screen's usable area, clamped so the whole palette stays
    /// on screen. The desk is counted in physical pixels and the palette is built in the
    /// layout units its own screen was designed around, so the palette's size crosses that
    /// screen's scale factor here. Measured in layout units instead, a palette on a desk at
    /// 125% lands short of the centre by the amount the scale adds, and its bottom edge
    /// ends up under the desk rather than sitting above it.
    /// </summary>
    private PixelPoint PreferredPosition()
    {
        var screen = Screens?.All.FirstOrDefault(s => s.IsPrimary) ?? Screens?.All.FirstOrDefault();
        if (screen is null)
        {
            // A screen that will not describe itself is measured on a plain 1920x1080
            // desk, which is the closest thing to a default there is.
            var plainWidth = (int)Math.Round(Bounds.Width > 0 ? Bounds.Width : Width);
            var plainHeight = (int)Math.Round(Bounds.Height > 0 ? Bounds.Height : 80);
            return new PixelPoint(Math.Max(0, (1920 - plainWidth) / 2), 1080 - plainHeight - 48);
        }

        var scale = screen.Scaling > 0 ? screen.Scaling : 1d;
        var width = (int)Math.Round((Bounds.Width > 0 ? Bounds.Width : Width) * scale);
        var height = (int)Math.Round((Bounds.Height > 0 ? Bounds.Height : 80) * scale);

        var area = screen.WorkingArea;
        var x = area.X + Math.Max(0, (area.Width - width) / 2);
        var y = area.Bottom - height - 32;
        return new PixelPoint(
            Math.Min(Math.Max(x, area.X), Math.Max(area.X, area.Right - width)),
            Math.Min(Math.Max(y, area.Y), Math.Max(area.Y, area.Bottom - height)));
    }

    /// <summary>Puts the palette back where it belongs, after the desk changed underneath it.</summary>
    internal void Reposition() => Position = PreferredPosition();

    private void OnFramePressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Border)
            return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        BeginMoveDrag(e);
    }

    // ------------------------------------------------------------------ building

    private void BuildToolGroup()
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };
        _toolButtons[0] = ToolButton("画笔", "Alt+P", panel, () => SetTool(ToolKind.Pen));
        _toolButtons[1] = ToolButton("荧光笔", "Alt+H", panel, () => SetTool(ToolKind.Highlighter));
        _toolButtons[2] = ToolButton("橡皮", "Alt+E", panel, () => SetTool(ToolKind.Eraser));
        _toolButtons[3] = ToolButton("选择", "Alt+S", panel, () => SetTool(ToolKind.Select));
        _groups.Children.Add(Segment("工具", panel));
    }

    private Button ToolButton(string text, string hint, Panel host, Action action)
    {
        var button = FlatButton(text, hint, action);
        button.Width = 62;
        host.Children.Add(button);
        return button;
    }

    private void BuildColorGroup()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

        for (var i = 0; i < InkPalette.Colors.Length; i++)
        {
            var color = InkPalette.Colors[i];
            var index = i;
            var swatch = new Button
            {
                Content = new Border
                {
                    Width = 16,
                    Height = 16,
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(ToColor(color)),
                },
                Width = 30,
                Height = 30,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(6),
                BorderThickness = new Thickness(1),
            };

            ToolTip.SetTip(swatch, $"墨色 {index + 1}");
            swatch.Click += (_, _) => _shell.Session.Color = color;
            _colorButtons[index] = swatch;
            panel.Children.Add(swatch);
        }

        _groups.Children.Add(Segment("墨色", panel));
    }

    private void BuildSizeGroup()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        _sizeSlider.Minimum = 1;
        _sizeSlider.Maximum = 12;
        _sizeSlider.Width = 130;
        _sizeSlider.SmallChange = 1;
        _sizeSlider.Value = _shell.Session.PenSize;
        _sizeSlider.ValueChanged += OnSizeChanged;

        _sizeLabel.Text = Math.Round(_sizeSlider.Value).ToString();
        _sizeLabel.Width = 34;
        _sizeLabel.VerticalAlignment = VerticalAlignment.Center;
        _sizeLabel.FontSize = 13;

        panel.Children.Add(_sizeSlider);
        panel.Children.Add(_sizeLabel);
        _groups.Children.Add(Segment("粗细", panel));
    }

    private void OnSizeChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_refreshing)
            return;

        var session = _shell.Session;
        var value = e.NewValue;
        if (session.Tool == ToolKind.Eraser)
            session.EraserRadius = value;
        else if (session.Tool == ToolKind.Highlighter)
            session.HighlighterSize = value;
        else
            session.PenSize = value;
    }

    private void BuildPageGroup()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };

        var previous = FlatButton("上一页", DefaultHotkeys.PreviousPage.OnThisPlatform().Text, _shell.Session.PreviousPage);
        var next = FlatButton("下一页", DefaultHotkeys.NextPage.OnThisPlatform().Text, _shell.Session.NextPage);
        var add = FlatButton("新建页", DefaultHotkeys.NewPage.OnThisPlatform().Text, _shell.Session.AddPage);
        _removePageButton = FlatButton("删除页", "删除当前这一页，删除后可以撤销找回", _shell.Session.RemovePage);

        _pageLabel.Text = "1 / 1";
        _pageLabel.Width = 48;
        _pageLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _pageLabel.VerticalAlignment = VerticalAlignment.Center;
        _pageLabel.FontSize = 13;

        panel.Children.Add(previous);
        panel.Children.Add(_pageLabel);
        panel.Children.Add(next);
        panel.Children.Add(Separator());
        panel.Children.Add(add);
        panel.Children.Add(_removePageButton);
        _groups.Children.Add(Segment("页面", panel));
    }

    private void BuildHistoryGroup()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

        _undoButton = FlatButton("撤销", DefaultHotkeys.Undo.OnThisPlatform().Text, _shell.Session.Undo);
        _redoButton = FlatButton("重做", DefaultHotkeys.Redo.OnThisPlatform().Text, _shell.Session.Redo);
        panel.Children.Add(_undoButton);
        panel.Children.Add(_redoButton);
        panel.Children.Add(FlatButton("清空", DefaultHotkeys.ClearPage.OnThisPlatform().Text, _shell.Session.ClearPage));

        _groups.Children.Add(Segment("编辑", panel));
    }

    private void BuildWorkspaceGroup()
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };

        _passThrough.Content = "穿透";
        _passThrough.IsChecked = _shell.Session.PassThrough;
        _passThrough.VerticalAlignment = VerticalAlignment.Center;
        _passThrough.FontSize = 13;
        ToolTip.SetTip(
            _passThrough,
            $"{DefaultHotkeys.TogglePassThrough.OnThisPlatform().Text}：开启后鼠标落在桌面上，关闭后才能在屏幕上书写");

        _passThrough.IsCheckedChanged += (_, _) =>
        {
            if (!_refreshing)
                _shell.Session.PassThrough = _passThrough.IsChecked == true;
        };

        _freezeButton = FlatButton(_freezeLabel, FreezeHint(), OnFreezeToggled);
        _freezeButton.IsEnabled = _shell.CanFreezeScreen;

        _recognizeButton = FlatButton("识别墨迹", "把选中的笔迹整理成规范的直线、箭头、矩形、三角形或椭圆", OnRecognize);
        _recognizeButton.IsEnabled = _shell.CanRecognize;

        panel.Children.Add(_passThrough);
        panel.Children.Add(Separator());
        panel.Children.Add(_freezeButton);
        panel.Children.Add(_recognizeButton);
        _groups.Children.Add(Segment("画板", panel));
    }

    private void BuildSystemGroup()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

        panel.Children.Add(FlatButton("保存图片", "把当前页导出为图片", () => _ = OnSaveImageAsync()));
        panel.Children.Add(FlatButton("设置", null, () => { _shell.ShowSettings(); }));
        panel.Children.Add(FlatButton("退出", DefaultHotkeys.Quit.OnThisPlatform().Text, () => _shell.Quit()));
        panel.Children.Add(Separator());
        panel.Children.Add(_statusLabel);
        _groups.Children.Add(Segment("文件", panel));
    }

    /// <summary>
    /// The outcome of a press that has no other visible result to show, and the place a
    /// platform finding is reported: a shortcut that cannot fire, for instance.
    /// </summary>
    public void ShowStatus(string message)
    {
        _statusLabel.Text = message;
        // The label clips so it cannot push the palette wider, so the full sentence is
        // still reachable on hover.
        ToolTip.SetTip(_statusLabel, message);

        if (_statusTimer is null)
        {
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            _statusTimer.Tick += (_, _) =>
            {
                _statusTimer.Stop();
                _statusLabel.Text = string.Empty;
            };
        }

        // Restarting is what makes the message readable: a second press inside the interval
        // replaces the message and keeps it on screen for another full turn, rather than
        // blinking it out halfway through being read.
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private async Task OnSaveImageAsync()
    {
        ShowStatus("正在导出…");
        ShowStatus(await _shell.SaveImageAsync());
    }

    private static Border Segment(string label, Panel content)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(Caption(label));
        row.Children.Add(content);

        return new Border
        {
            Padding = new Thickness(8, 2),
            BorderThickness = new Thickness(0, 0, 1, 0),
            BorderBrush = new SolidColorBrush(Color.Parse("#E6E9EE")),
            Child = row,
        };
    }

    private static TextBlock Caption(string text) => new()
    {
        Text = text,
        FontSize = 11,
        Foreground = new SolidColorBrush(Color.Parse("#8A9099")),
        VerticalAlignment = VerticalAlignment.Center,
        Width = 28,
    };

    private static Border Separator() => new()
    {
        Width = 1,
        Margin = new Thickness(4, 4),
        Background = new SolidColorBrush(Color.Parse("#E6E9EE")),
    };

    private Button FlatButton(string text, string? hint, Action action)
        => FlatButton(new TextBlock { Text = text, FontSize = 13 }, hint, action);

    private Button FlatButton(Control content, string? hint, Action action)
    {
        var button = new Button
        {
            Content = content,
            Background = new SolidColorBrush(Color.Parse("#F2F4F7")),
            BorderBrush = new SolidColorBrush(Color.Parse("#E3E7EC")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(9, 5),
            MinHeight = 30,
        };

        if (!string.IsNullOrEmpty(hint))
            ToolTip.SetTip(button, hint);

        button.Click += (_, _) => action();
        return button;
    }

    private void SetTool(ToolKind tool)
    {
        _shell.Session.Tool = tool;
    }

    /// <summary>
    /// What the grab button promises, and it names the display it reads: with several
    /// monitors on the desk "current screen" points at whichever one the user is not
    /// looking at, and the hint is the only place that can say which.
    /// </summary>
    private string FreezeHint()
    {
        var target = _shell.DescribeFreezeTarget();
        return target is null ? "抓取当前屏幕作为批注底图" : $"抓取「{target}」作为批注底图";
    }

    /// <summary>
    /// The one grab button, both ways: it puts a frozen screen under the ink and takes it
    /// back off. Taking it off had no way in at all until now, so a user who froze the
    /// wrong screen had nothing to press but undo, or wipe the page.
    /// </summary>
    private void OnFreezeToggled()
    {
        if (_shell.Session.IsFrozen)
        {
            _shell.UnfreezeScreen();
            ShowStatus("已取消冻结");
            return;
        }

        _shell.FreezeScreen();

        // A host that cannot grab leaves the page unfrozen, and the status line is where
        // that shows up, rather than a button that quietly did nothing.
        if (_shell.Session.IsFrozen)
            ShowStatus("已冻结屏幕，现在可以直接在底图上书写。");
        else
            ShowStatus("当前平台无法抓取屏幕。");
    }

    private async void OnRecognize()
    {
        var recognizer = _shell.Platform.Recognizer;
        if (!recognizer.IsSupported)
        {
            ShowStatus("当前平台没有可用的墨迹引擎。");
            return;
        }

        _recognizeButton.IsEnabled = false;
        ShowStatus("正在识别…");

        try
        {
            var report = await _shell.Session.RecognizeAsync(recognizer, default);
            ShowStatus(report.Summary);
        }
        catch (OperationCanceledException)
        {
            ShowStatus("已取消识别。");
        }
        catch (Exception exception)
        {
            // Recognition sits on top of the board, never under it: the ink, the undo
            // history and the toolbar all have to survive an engine that misbehaves.
            ShowStatus($"识别失败：{exception.Message}");
        }
        finally
        {
            _recognizeButton.IsEnabled = _shell.CanRecognize;
        }
    }

    // ------------------------------------------------------------------ state

    /// <summary>
    /// Pushes the session's state into the controls. Called after every change, so the
    /// palette never disagrees with what the canvas is doing, including when a change came
    /// from a global shortcut while the palette was not focused.
    /// </summary>
    public void Refresh()
    {
        if (_refreshing)
            return;

        _refreshing = true;

        var session = _shell.Session;

        for (var i = 0; i < _toolButtons.Length; i++)
        {
            var tool = PaletteTools[i];
            _toolButtons[i].Tag = session.Tool == tool ? "Active" : null;
            _toolButtons[i].Background = new SolidColorBrush(
                session.Tool == tool ? Color.Parse("#0B6CD4") : Color.Parse("#F2F4F7"));
            _toolButtons[i].Foreground = new SolidColorBrush(
                session.Tool == tool ? Colors.White : Color.Parse("#1B1B1F"));
            _toolButtons[i].BorderBrush = new SolidColorBrush(
                session.Tool == tool ? Color.Parse("#0B6CD4") : Color.Parse("#E3E7EC"));
        }

        var accent = new SolidColorBrush(Color.Parse("#0B6CD4"));
        var neutral = new SolidColorBrush(Color.Parse("#FFFFFF"));
        for (var i = 0; i < _colorButtons.Length; i++)
        {
            var active = session.Color == InkPalette.Colors[i];
            _colorButtons[i].Background = active ? accent : neutral;
            _colorButtons[i].BorderBrush = active ? accent : new SolidColorBrush(Color.Parse("#D7DCE3"));
        }

        var (minimum, maximum, value, unit) = session.Tool switch
        {
            ToolKind.Eraser => (4d, 96d, session.EraserRadius, "px"),
            ToolKind.Highlighter => (8d, 48d, session.HighlighterSize, "px"),
            _ => (1d, 24d, session.PenSize, "px"),
        };

        _sizeSlider.Minimum = minimum;
        _sizeSlider.Maximum = maximum;
        _sizeSlider.Value = Math.Clamp(value, minimum, maximum);
        _sizeLabel.Text = $"{Math.Round(_sizeSlider.Value)}{unit}";

        _pageLabel.Text = $"{session.PageIndex} / {session.PageCount}";
        // A board with one page is a board already: pressing delete would do nothing, so
        // the button says so by being unclickable rather than by failing quietly.
        _removePageButton.IsEnabled = session.PageCount > 1;
        _undoButton.IsEnabled = session.CanUndo;
        _redoButton.IsEnabled = session.CanRedo;
        _passThrough.IsChecked = session.PassThrough;
        // Taking a picture off is possible on a host that cannot put one under, so the
        // button only goes dead when the page has nothing frozen and nothing to grab.
        _freezeButton.IsEnabled = _shell.CanFreezeScreen || _shell.Session.IsFrozen;

        var frozen = _shell.Session.IsFrozen;
        _freezeLabel.Text = frozen ? "取消冻结" : "冻结屏幕";
        ToolTip.SetTip(
            _freezeButton,
            frozen ? "把冻结的底图撤掉，墨迹保留，撤销可以把它找回来" : FreezeHint());

        ToolTip.SetTip(_undoButton, session.NextUndoLabel is { } undo ? $"撤销：{undo}" : "没有可撤销的操作");
        ToolTip.SetTip(_redoButton, session.NextRedoLabel is { } redo ? $"重做：{redo}" : "没有可重做的操作");

        _refreshing = false;
    }

    private static Color ToColor(uint argb) => Color.FromArgb(
        (byte)((argb >> 24) & 0xFF),
        (byte)((argb >> 16) & 0xFF),
        (byte)((argb >> 8) & 0xFF),
        (byte)(argb & 0xFF));
}
