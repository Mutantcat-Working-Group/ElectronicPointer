using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Platform.Storage;
using Mutantcat.ElectronicPointer;
using Mutantcat.ElectronicPointer.App.Shell;
using Mutantcat.ElectronicPointer.App.Session;
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
        Width = 520;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        CanResize = false;
        RequestedThemeVariant = ThemeVariant.Light;
        Background = new SolidColorBrush(Color.Parse("#F4F6F9"));

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
            CornerRadius = new CornerRadius(8),
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
        var root = new DockPanel { LastChildFill = true };
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
        Content = root;

        Opened += OnOpened;
        Closed += OnClosed;
    }

    private readonly StackPanel _footerHost;

    /// <summary>
    /// Lifts the dialog above the canvases once it has a native handle to hand over, the same
    /// way the palette is lifted. Windows put an ordinary window below every topmost canvas,
    /// and AppKit stacks strictly by level, so without this the dialog is simply not there.
    /// </summary>
    private void OnOpened(object? sender, EventArgs e) => LiftAboveOverlays();

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
        return panel;
    }

    private void OnAutoStartChanged(object? sender, RoutedEventArgs e)
    {
        _shell.SetAutoStart(_autoStart.IsChecked == true);
    }

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
            SuggestedFileName = "电子教鞭批注.epboard",
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
    private static string? NoteFor(PlatformFeature feature)
    {
        var windows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var mac = RuntimeInformation.IsOSPlatform(OSPlatform.OSX);
        var linux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

        return feature switch
        {
            PlatformFeature.GlobalHotkey when mac => "需要在「系统设置 > 隐私与安全性 > 辅助功能」中授权",
            PlatformFeature.GlobalHotkey when linux => "X11 会话可用，Wayland 会话受界面协议限制",
            PlatformFeature.ScreenCapture when linux => "X11 会话可用，Wayland 会话受界面协议限制",
            PlatformFeature.PresentationDetection when !windows => "通过监视全屏放映进程实现，弱于 Office 插件",
            PlatformFeature.HandwritingRecognition => "内置跨平台墨迹引擎，离线运行，整理结果可撤销",
            _ => null,
        };
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
