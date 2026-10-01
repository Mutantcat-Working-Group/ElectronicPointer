using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Platform.Storage;
using Mutantcat.ElectronicPointer;
using Mutantcat.ElectronicPointer.App.Shell;
using Mutantcat.ElectronicPointer.App.Session;
using Mutantcat.ElectronicPointer.Core.Input;
using Mutantcat.ElectronicPointer.Platform;
using Mutantcat.ElectronicPointer.Platform.Overlay;

namespace Mutantcat.ElectronicPointer.App.Views;

/// <summary>
/// Settings plus a per-platform capability report. The second half is what makes the
/// cross-platform promise honest: every behaviour the host could not provide is listed with
/// the reason, so a mac or Linux user understands why one feature behaves differently instead
/// of discovering it by pressing a dead button. Everything here writes through
/// <see cref="OverlayShell"/>, never into the platform layer directly.
/// </summary>
public sealed class SettingsWindow : Window
{
    private readonly OverlayShell _shell;
    private readonly OverlayCompanion _companion;
    private readonly ToggleSwitch _autoStart = new();
    private readonly TextBlock _status = new();

    private static readonly (PlatformFeature Feature, string Label)[] CapabilityTable = new[]
    {
        (PlatformFeature.ClickThroughOverlay, "点击穿透"),
        (PlatformFeature.AlwaysOnTopOverlay, "窗口置顶"),
        (PlatformFeature.HideFromTaskSwitcher, "隐藏于任务栏"),
        (PlatformFeature.GlobalHotkey, "全局快捷键"),
        (PlatformFeature.ScreenCapture, "屏幕捕捉"),
        (PlatformFeature.PerDisplayPlacement, "多显示器"),
        (PlatformFeature.PresentationDetection, "放映检测"),
        (PlatformFeature.HandwritingRecognition, "墨迹识别"),
        (PlatformFeature.AutoStart, "登录自启"),
    };

    public SettingsWindow(OverlayShell shell)
    {
        ArgumentNullException.ThrowIfNull(shell);

        _shell = shell;
        _companion = new OverlayCompanion(_shell.Platform);

        Title = AppIdentity.GetDisplayName() + " 设置";
        // The system title bar is the one corner the sheet cannot round: it caps the
        // window in a square frame of the host's own, which leaves the top of a dialog
        // built out of rounded cards the only square part of it. Dropping it puts the
        // dialog on the same footing as the palette, and the sheet keeps a header of its
        // own below carrying the name and the way out.
        SystemDecorations = SystemDecorations.None;
        Width = 520;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.Manual;
        CanResize = false;
        RequestedThemeVariant = ThemeVariant.Light;
        // The dialog's shape is the sheet below and nothing else, and for the same reason as
        // the palette's card: a window that paints its own opaque background lays a square
        // sheet under a rounded one, and whatever the window manager then does to a frameless
        // window lands on top of what was drawn, so no two corners agree. The sheet carries
        // all four corners itself instead, which every platform then shows the same way.
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };

        // The desk is covered in topmost ink while this is open, so an ordinary window would
        // be painted underneath all of it: the dialog opens, and nothing arrives. Joining the
        // same layer is also the fallback for a host whose chrome cannot lift it, such as a
        // Wayland session, where the canvases are not on top either.
        Topmost = true;

        var page = new StackPanel { Margin = new Thickness(18), Spacing = 16 };
        page.Children.Add(BuildGeneralSection());
        page.Children.Add(BuildFileSection());
        page.Children.Add(BuildAboutSection());
        page.Children.Add(BuildPlatformSection());
        page.Children.Add(BuildCapabilitySection());

        // The card is a local, not the window's Content: the scrolling root below wraps it,
        // and a control already parented to the window cannot be re-parented into the
        // ScrollViewer without Avalonia refusing the move outright.
        var card = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#FFFFFF")),
            BorderBrush = new SolidColorBrush(Color.Parse("#D7DCE3")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(CompanionShape.CornerRadius),
            Padding = new Thickness(4),
            Child = page,
        };

        _status.FontSize = 11;
        _status.Foreground = new SolidColorBrush(Color.Parse("#8A9099"));
        _status.TextWrapping = TextWrapping.Wrap;
        _status.Text = $"配置目录：{_shell.Configuration.Path}";

        var footer = new StackPanel { Margin = new Thickness(22, 0, 22, 18) };
        footer.Children.Add(_status);
        _footerHost = footer;

        // The settings page scrolls when the capability list grows, and the status line stays
        // pinned below it, so a long failure message never pushes the buttons away.
        // The frameless dialog keeps the header the title bar used to provide: the name on
        // the left, the way out on the right, and the strip itself drags the window the way
        // the palette's frame does. The caption stays out of hit testing so a press on the
        // words drags too, and the button handles its own press, so only the bare strip
        // arrives at the drag.
        var closeButton = new Button
        {
            Content = "×",
            Background = new SolidColorBrush(Color.Parse("#F2F4F7")),
            BorderBrush = new SolidColorBrush(Color.Parse("#E3E7EC")),
            BorderThickness = new Thickness(1),
            Foreground = new SolidColorBrush(Color.Parse("#4A5058")),
            FontSize = 14,
            CornerRadius = new CornerRadius(6),
            Width = 30,
            Height = 30,
            MinHeight = 30,
            Padding = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        closeButton.Click += (_, _) => Close();

        var caption = new TextBlock
        {
            Text = "设置",
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#4A5058")),
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
        };

        var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(closeButton, 1);
        headerGrid.Children.Add(caption);
        headerGrid.Children.Add(closeButton);

        var header = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#F4F6F9")),
            Padding = new Thickness(18, 12, 14, 8),
            Child = headerGrid,
        };
        header.PointerPressed += OnHeaderPressed;

        var root = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        DockPanel.SetDock(_footerHost, Dock.Bottom);
        root.Children.Add(_footerHost);
        root.Children.Add(new ScrollViewer
        {
            Content = new Border
            {
                Margin = new Thickness(4),
                Child = card,
            },
        });
        // The sheet is the window's own shape now, so it is the thing that carries the
        // rounding: the card inside is already rounded, but it sits four pixels in, which
        // leaves the window's corners to whatever the host decides to do with them.
        // The radius is the one every companion window draws, and the host is told it too.
        Content = new Border
        {
            Background = new SolidColorBrush(Color.Parse("#F4F6F9")),
            BorderBrush = new SolidColorBrush(Color.Parse("#D7DCE3")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(CompanionShape.CornerRadius),
            Child = root,
        };

        Opened += OnOpened;
        Closed += OnClosed;
        LayoutUpdated += OnLayoutUpdated;
        KeyDown += OnKeyDown;
        // The dialog gets its chrome settled before the window manager composes it, for the
        // reason the palette does, and again after the resizes that settle its own height:
        // the host settles the corner shape once per frame change, and a handover that
        // arrives earlier than that is answered with rather than carried out.
        this.KeepChromeCurrent(LiftAboveOverlays);

        // The dialog can be dragged by its header, is activated as the key window, and is
        // composed again as it settles, so it asks the host to keep its hands off the corners
        // at those moments as well, the same way the palette does.
        _companion.GuardAgainstLateCornerRounding(this);
    }

    private readonly StackPanel _footerHost;

    /// <summary>
    /// Lifts the dialog above the canvases once it has a native handle to hand over, the same
    /// way the palette is lifted. Windows put an ordinary window below every topmost canvas,
    /// and AppKit stacks strictly by level, so without this the dialog is simply not there.
    /// </summary>
    private void OnOpened(object? sender, EventArgs e)
    {
        // SizeToContent only settles the real height once the window is on screen, so this
        // placement is a first estimate, and the layout passes that follow repeat it until
        // the measured size stops moving, exactly like the palette it is placed against.
        LiftAboveOverlays();
        PlaceAbovePalette();
    }

    /// <summary>
    /// Drags the dialog by its header, the way the palette is dragged by its frame. The
    /// caption is kept out of hit testing so a press on the words drags as well, and the
    /// close button handles its own press, so what arrives here is the bare strip.
    /// </summary>
    private void OnHeaderPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is not Border)
            return;

        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        BeginMoveDrag(e);
    }

    /// <summary>Closes on Escape, the keyboard answer to the header's close button.</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
            return;

        Close();
        e.Handled = true;
    }

    /// <summary>Called again after the canvases are rebuilt, which buries a window left behind.</summary>
    internal void LiftAboveOverlays() => _companion.Lift(this, CompanionRole.Dialog);

    private Control BuildGeneralSection()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Heading("通用"));

        _autoStart.Content = "登录后自动运行";
        _autoStart.IsChecked = _shell.Platform.AutoStart.IsEnabled;
        _autoStart.IsEnabled = _shell.CanAutoStart;
        _autoStart.FontSize = 13;
        _autoStart.IsCheckedChanged += OnAutoStartChanged;
        panel.Children.Add(Row("开机启动", _autoStart));
        panel.Children.Add(BuildFreezeDisplayRow());
        return panel;
    }

    private void OnAutoStartChanged(object? sender, RoutedEventArgs e)
    {
        _shell.SetAutoStart(_autoStart.IsChecked == true);
    }

    /// <summary>
    /// Which display "freeze screen" reads. The index travelled in the configuration from
    /// the start and nothing ever wrote it, so every grab landed on whatever the host
    /// enumerated first, no matter which desk the user was annotating: this picker is
    /// the way in. A host that cannot grab, or a desk with one screen, gets a worded
    /// answer instead of a control that would pretend to choose.
    /// </summary>
    private Control BuildFreezeDisplayRow()
    {
        if (!_shell.CanFreezeScreen)
            return Row("冻结显示器", Hint("当前平台无法抓取屏幕。"));

        var displays = _shell.Platform.ScreenCapture.Displays;
        if (displays.Count == 0)
            return Row("冻结显示器", Hint("没有找到可抓取的显示器。"));

        var picker = new ComboBox
        {
            FontSize = 13,
            MinWidth = 240,
            HorizontalAlignment = HorizontalAlignment.Left,
        };

        foreach (var display in displays)
        {
            var item = new ComboBoxItem
            {
                Content = DisplayLabel.Of(displays, display),
                Tag = display.Index,
            };
            picker.Items.Add(item);
        }

        // The stored index is a display count ago stale after a monitor is unplugged, and
        // the freeze path clamps the same way, so the picker lands on the display that
        // will actually be grabbed rather than past the end of the list.
        picker.SelectedIndex = Math.Clamp(_shell.Session.FrozenScreenIndex, 0, displays.Count - 1);
        picker.SelectionChanged += OnFreezeDisplayChanged;
        ToolTip.SetTip(picker, "「冻结屏幕」抓取哪台显示器的画面；墨迹落在对应的画布上");

        return Row("冻结显示器", picker);
    }

    private void OnFreezeDisplayChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count == 0 || e.AddedItems[0] is not ComboBoxItem { Tag: int index })
            return;

        if (index == _shell.Session.FrozenScreenIndex)
            return;

        _shell.Session.FrozenScreenIndex = index;

        // Written straight away rather than at close, the way autostart is: the desk may
        // go away under a session that is about to be asked to shut down.
        _shell.SaveConfiguration();
    }

    private static TextBlock Hint(string text) => new()
    {
        Text = text,
        FontSize = 11,
        Foreground = new SolidColorBrush(Color.Parse("#8A9099")),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private Control BuildFileSection()
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        panel.Children.Add(FlatButton("打开批注", null, OnOpenBoard));
        panel.Children.Add(FlatButton("保存批注", null, OnSaveBoard));
        panel.Children.Add(FlatButton("保存图片", null, OnSaveImage));

        var group = new StackPanel { Spacing = 8 };
        group.Children.Add(Heading("文件"));
        group.Children.Add(Row("批注与图片", panel));
        return group;
    }

    private async void OnOpenBoard()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "打开批注",
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("电子教鞭批注") { Patterns = new[] { "*.epboard" } },
                new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } },
            },
        });

        if (files.Count == 0)
            return;

        var path = files[0].Path.LocalPath;

        try
        {
            var json = await File.ReadAllTextAsync(path);
            if (_shell.Session.TryLoad(json, out var reason))
                _status.Text = $"已打开 {Path.GetFileName(path)}";
            else
                _status.Text = $"无法打开 {Path.GetFileName(path)}：{reason}";
        }
        catch (Exception exception)
        {
            _status.Text = $"打开失败：{exception.Message}";
        }
    }

    private async void OnSaveBoard()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "保存批注",
            DefaultExtension = "epboard",
            SuggestedFileName = $"{AppIdentity.ChineseName}批注-{DateTime.Now:yyyyMMdd-HHmmss}.epboard",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("电子教鞭批注") { Patterns = new[] { "*.epboard" } },
                new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } },
            },
        });

        if (file is null)
            return;

        var path = file.Path.LocalPath;

        try
        {
            await File.WriteAllTextAsync(path, _shell.Session.Serialize());
            _shell.Session.MarkSaved();
            _status.Text = $"已保存 {Path.GetFileName(path)}";
        }
        catch (Exception exception)
        {
            _status.Text = $"保存失败：{exception.Message}";
        }
    }

    private async void OnSaveImage()
    {
        _status.Text = await _shell.SaveImageAsync();
    }

    /// <summary>
    /// The product card: what this is, which build it is, and who publishes it. The
    /// publisher line is the one place that question is answered inside the app, so a user
    /// who got the installer from a mirror still learns where the software comes from,
    /// with the site one click away rather than spelled out in a forum post.
    /// </summary>
    private Control BuildAboutSection()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(Heading("关于"));

        panel.Children.Add(Row("名称", new SelectableTextBlock
        {
            Text = $"{AppIdentity.GetDisplayName()} {AppIdentity.ProductName}",
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        }));
        panel.Children.Add(Row("版本", new SelectableTextBlock
        {
            Text = AppIdentity.Version,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        }));

        var publisher = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        publisher.Children.Add(new TextBlock
        {
            Text = AppIdentity.PublisherName,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        });
        publisher.Children.Add(PublisherLink());
        panel.Children.Add(Row("发行者", publisher));

        return panel;
    }

    /// <summary>The publisher site as a link; Avalonia hands the URI to the platform launcher.</summary>
    private static HyperlinkButton PublisherLink()
    {
        var link = new HyperlinkButton
        {
            Content = AppIdentity.PublisherDomain,
            NavigateUri = new Uri(AppIdentity.PublisherWebsite),
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            BorderThickness = new Thickness(0),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(link, $"打开 {AppIdentity.PublisherWebsite}");
        return link;
    }

    private Control BuildPlatformSection()
    {
        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(Heading("平台"));
        panel.Children.Add(new SelectableTextBlock
        {
            Text = $"{_shell.Platform.PlatformName} · {AppIdentity.GetDisplayName()} {AppIdentity.Version} · {AppIdentity.ApplicationId}",
            FontSize = 12,
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"配置目录：{AppIdentity.GetConfigDirectory()}",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.Parse("#8A9099")),
            TextWrapping = TextWrapping.Wrap,
        });
        return panel;
    }

    private Control BuildCapabilitySection()
    {
        var panel = new StackPanel { Spacing = 3 };
        panel.Children.Add(Heading("本机能力"));

        var capabilities = _shell.Platform.Capabilities;
        foreach (var (feature, label) in CapabilityTable)
            panel.Children.Add(CapabilityRow(label, capabilities.Supports(feature), NoteFor(feature)));

        if (capabilities.Notes is { Length: > 0 } notes)
            panel.Children.Add(new TextBlock
            {
                Text = notes,
                FontSize = 11,
                Margin = new Thickness(0, 6, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.Parse("#8A9099")),
            });

        return panel;
    }

    /// <summary>The one-line reason a capability is unavailable on the running system.</summary>
    private string? NoteFor(PlatformFeature feature)
    {
        var windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var mac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        var linux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        var note = feature switch
        {
            PlatformFeature.GlobalHotkey when mac => "需要在「系统设置 > 隐私与安全性 > 辅助功能」中授权",
            PlatformFeature.GlobalHotkey when linux => "X11 会话可用，Wayland 会话受界面协议限制",
            PlatformFeature.ScreenCapture when linux => "X11 会话可用，Wayland 会话受界面协议限制",
            PlatformFeature.PresentationDetection when !windows => "通过监视全屏放映进程实现，弱于 Office 插件",
            PlatformFeature.HandwritingRecognition => "内置跨平台墨迹引擎，离线运行，整理结果可撤销",
            _ => null,
        };

        // The row reports the host accepting global shortcuts as a whole, which stays true
        // when a single gesture loses to another program, so the exceptions are named here.
        // Without them the row reads "supported" over shortcuts that do nothing at all.
        if (feature == PlatformFeature.GlobalHotkey && _shell.RefusedHotkeys.Count > 0)
        {
            var conflicts = HotkeyConflicts.Describe(_shell.RefusedHotkeys);
            return note is null ? $"已被其他程序占用：{conflicts}" : $"{note}；已被其他程序占用：{conflicts}";
        }

        return note;
    }

    private void OnLayoutUpdated(object? sender, EventArgs e)
    {
        // SizeToContent settles the real height over more than one pass, and the cap the
        // room above the palette asks for changes it again, so this repeats until the
        // measured size stops moving. A size that has stopped is the geometry the sheet
        // will actually show, and that is the only geometry worth placing against.
        var size = Bounds.Size;
        if (Math.Abs(size.Width - _placedSize.Width) < 0.5 && Math.Abs(size.Height - _placedSize.Height) < 0.5)
            return;

        PlaceAbovePalette();
    }

    /// <summary>Physical pixels of air kept between the sheet and the palette below it.</summary>
    private const int GapPixels = 24;

    /// <summary>Room above the palette below which the sheet takes the middle of the desk instead.</summary>
    private const int MinimumRoomPixels = 320;

    private Size _placedSize = new Size(double.NaN, double.NaN);

    /// <summary>
    /// Opens the sheet over the palette that asked for it, both centred on the same axis,
    /// so the palette and its buttons stay visible while the settings are being changed. A
    /// centred-on-the-desk dialog covers that palette entirely, which for a window lifted
    /// over ink means the only thing the user can reach is the dialog. The sheet is capped
    /// to the room above the palette and its page scrolls, so a long capability report
    /// never has to be shown by covering the very thing it reports on.
    /// </summary>
    private void PlaceAbovePalette()
    {
        var palette = _shell.Toolbar;
        var screen = Screens?.All.FirstOrDefault(s => s.IsPrimary) ?? Screens?.All.FirstOrDefault();
        if (palette is null || screen is null)
            return;

        var area = screen.WorkingArea;
        var scale = screen.Scaling > 0 ? screen.Scaling : 1d;

        // The desk and the palette are counted in physical pixels, and the sheet is built
        // in the layout units its own screen was designed around, so every measure of the
        // sheet crosses the scale factor once, here, rather than half way through the
        // arithmetic, which is what left a sheet at 125% hanging off the bottom of a desk
        // it was meant to sit on.
        var width = Math.Max(1, (int)Math.Round((Bounds.Width > 0 ? Bounds.Width : Width) * scale));
        var height = Math.Max(1, (int)Math.Round((Bounds.Height > 0 ? Bounds.Height : 600) * scale));
        var paletteWidth = Math.Max(1, (int)Math.Round((palette.Bounds.Width > 0 ? palette.Bounds.Width : 800) * scale));
        var paletteTop = palette.Position.Y;

        // The room is what sits above the palette inside the desk, and the palette is the
        // one thing the sheet may not cover: it is where the user was a moment ago, and
        // its buttons are the ones the sheet is here to change the behaviour of.
        var room = paletteTop - GapPixels - area.Y;

        // A palette parked high enough up that the room over it is not worth living in
        // leaves the sheet the middle of the desk, which is the most of it any placement
        // can show at once.
        var centred = room < MinimumRoomPixels;
        var cap = centred ? Math.Max(1, area.Height) : Math.Max(1, room);

        // A sheet taller than the room it is given is capped to that room and the page
        // inside scrolls, so it never covers the palette it came from and never leaves the
        // desk. The cap changes the measured height, and the size that comes back is what
        // this places against, so the cap goes in first and the layout pass repeats this.
        MaxHeight = cap / scale;

        var x = palette.Position.X + (paletteWidth - width) / 2;
        var y = centred
            ? area.Y + Math.Max(0, (area.Height - height) / 2)
            : paletteTop - GapPixels - height;

        Position = new PixelPoint(
            Math.Min(Math.Max(x, area.X), Math.Max(area.X, area.Right - width)),
            Math.Min(Math.Max(y, area.Y), Math.Max(area.Y, area.Bottom - height)));

        _placedSize = Bounds.Size;
    }

    private static Control CapabilityRow(string label, bool supported, string? note)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("128,*") };

        var name = new TextBlock
        {
            Text = label,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var state = new TextBlock
        {
            Text = supported ? "支持" : "不可用",
            FontSize = 12,
            Width = 44,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(supported ? Color.Parse("#1B7F4B") : Color.Parse("#D7263D")),
        };

        var detail = new TextBlock
        {
            Text = note ?? string.Empty,
            FontSize = 11,
            Margin = new Thickness(10, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#8A9099")),
        };

        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        right.Children.Add(state);
        right.Children.Add(detail);

        Grid.SetColumn(right, 1);
        grid.Children.Add(name);
        grid.Children.Add(right);
        return grid;
    }

    private static TextBlock Heading(string text) => new()
    {
        Text = text,
        FontSize = 14,
        FontWeight = FontWeight.SemiBold,
        Margin = new Thickness(0, 2, 0, 0),
    };

    private static Grid Row(string label, Control content)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("128,*") };

        var caption = new TextBlock
        {
            Text = label,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = new SolidColorBrush(Color.Parse("#4A5058")),
        };

        Grid.SetColumn(content, 1);
        grid.Children.Add(caption);
        grid.Children.Add(content);
        return grid;
    }

    private static Button FlatButton(string text, string? hint, Action action)
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

    private void OnClosed(object? sender, EventArgs e)
    {
        // The lift is let go before the configuration is written, so a dialog that fails to
        // save for any reason still leaves the window plain on its way out.
        _companion.Release();
        _shell.SaveConfiguration();
        _autoStart.IsCheckedChanged -= OnAutoStartChanged;
    }
}
