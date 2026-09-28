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
using Mutantcat.ElectronicPointer.Core.Ink;

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
    private readonly Button[] _toolButtons = new Button[4];
    private readonly Button[] _colorButtons;
    private readonly Slider _sizeSlider = new();
    private readonly TextBlock _sizeLabel = new();
    private readonly TextBlock _pageLabel = new();
    // These three are built into the palette after construction, so the null-forgiving
    // assignment keeps the non-null contract without pretending they are immutable.
    private Button _undoButton = null!;
    private Button _redoButton = null!;
    private readonly ToggleSwitch _passThrough = new();
    private Button _freezeButton = null!;
    private Button _recognizeButton = null!;
    private bool _refreshing;

    public ToolbarWindow(OverlayShell shell)
    {
        ArgumentNullException.ThrowIfNull(shell);

        _shell = shell;
        _colorButtons = new Button[InkPalette.Colors.Length];

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
        Background = new SolidColorBrush(Color.Parse("#F4F6F9"));

        var frame = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#FFFFFF")),
            BorderBrush = new SolidColorBrush(Color.Parse("#D7DCE3")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
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
        Refresh();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        // SizeToContent only knows the real height once the window is on screen, so the
        // first frame is invisible and the palette is placed before it becomes visible.
        var screen = Screens?.All.FirstOrDefault(s => s.IsPrimary) ?? Screens?.All.FirstOrDefault();
        if (screen is not null)
            MaxWidth = Math.Max(MinWidth, screen.WorkingArea.Width - 48);

        Position = PreferredPosition();
        Opacity = 1;
    }

    /// <summary>Bottom centre of the primary screen's usable area, clamped so the whole palette stays on screen.</summary>
    private PixelPoint PreferredPosition()
    {
        var screen = Screens?.All.FirstOrDefault(s => s.IsPrimary) ?? Screens?.All.FirstOrDefault();
        var width = (int)Math.Round(Bounds.Width > 0 ? Bounds.Width : Width);
        var height = (int)Math.Round(Bounds.Height > 0 ? Bounds.Height : 80);

        if (screen is null)
            return new PixelPoint(Math.Max(0, (1920 - width) / 2), 1080 - height - 48);

        var area = screen.WorkingArea;
        var x = area.X + Math.Max(0, (area.Width - width) / 2);
        var y = area.Bottom - height - 32;
        return new PixelPoint(
            Math.Min(Math.Max(x, area.X), Math.Max(area.X, area.Right - width)),
            Math.Min(Math.Max(y, area.Y), Math.Max(area.Y, area.Bottom - height)));
    }

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

        var previous = FlatButton("上一页", "Ctrl+PageUp", _shell.Session.PreviousPage);
        var next = FlatButton("下一页", "Ctrl+PageDown", _shell.Session.NextPage);
        var add = FlatButton("新建页", "Ctrl+N", _shell.Session.AddPage);

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
        _groups.Children.Add(Segment("页面", panel));
    }

    private void BuildHistoryGroup()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };

        _undoButton = FlatButton("撤销", "Ctrl+Z", _shell.Session.Undo);
        _redoButton = FlatButton("重做", "Ctrl+Shift+Z", _shell.Session.Redo);
        panel.Children.Add(_undoButton);
        panel.Children.Add(_redoButton);
        panel.Children.Add(FlatButton("清空", "Ctrl+Shift+Del", _shell.Session.ClearPage));

        _groups.Children.Add(Segment("编辑", panel));
    }

    private void BuildWorkspaceGroup()
    {
        var panel = new WrapPanel { Orientation = Orientation.Horizontal };

        _passThrough.Content = "穿透";
        _passThrough.IsChecked = _shell.Session.PassThrough;
        _passThrough.VerticalAlignment = VerticalAlignment.Center;
        _passThrough.FontSize = 13;
        ToolTip.SetTip(_passThrough, "Ctrl+Alt+T：开启后鼠标落在桌面上，关闭后才能在屏幕上书写");

        _passThrough.IsCheckedChanged += (_, _) =>
        {
            if (!_refreshing)
                _shell.Session.PassThrough = _passThrough.IsChecked == true;
        };

        _freezeButton = FlatButton("冻结屏幕", "抓取当前屏幕作为批注底图", () => _shell.FreezeScreen());
        _freezeButton.IsEnabled = _shell.CanFreezeScreen;

        _recognizeButton = FlatButton("识别墨迹", "把选中的笔迹转成文字", OnRecognize);
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

        panel.Children.Add(FlatButton("保存图片", "把当前页导出为图片", () => _ = _shell.SaveImageAsync()));
        panel.Children.Add(FlatButton("设置", null, () => { _shell.ShowSettings(); }));
        panel.Children.Add(FlatButton("退出", "Ctrl+Q", () => _shell.Quit()));
        _groups.Children.Add(Segment("文件", panel));
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
    {
        var button = new Button
        {
            Content = new TextBlock { Text = text, FontSize = 13 },
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

    private async void OnRecognize()
    {
        var recognizer = _shell.Platform.Recognizer;
        _recognizeButton.IsEnabled = false;

        try
        {
            var text = await _shell.Session.RecognizeAsync(recognizer, default);
            if (!string.IsNullOrWhiteSpace(text))
                await Clipboard!.SetTextAsync(text);
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
            var tool = (ToolKind)i;
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
        _undoButton.IsEnabled = session.CanUndo;
        _redoButton.IsEnabled = session.CanRedo;
        _passThrough.IsChecked = session.PassThrough;
        _freezeButton.IsEnabled = _shell.CanFreezeScreen;

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
