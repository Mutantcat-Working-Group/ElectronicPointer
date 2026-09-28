# 跨平台设计

电子教鞭的前身 InkCanvasForClass 是 Windows 专属软件。这份文档说明为什么它必须换栈、换成了什么，以及「三个平台上用起来一样」这件事具体是怎么被保证的。怎么把各平台的安装包做出来见 [../packaging/README.md](../packaging/README.md)，这份只讲架构。

## 为什么旧项目搬不过去

不是编译目标的问题，是四样东西把旧实现钉死在 Windows 上：

| 依赖 | 旧实现 | 为什么跨不了 |
| --- | --- | --- |
| UI 框架 | WPF / `PresentationFramework` | 透明置顶窗口、`WindowChrome`、命中测试穿透都是它的 API，别的系统上没有对应物 |
| 输入与墨迹 | Windows Ink、`InkCanvasForClass.IccInkCanvas` | 笔压、墨迹平滑是 Windows 输入栈提供的服务。macOS 只给坐标和压力，Linux 的 XInput2 还得看设备，两边都没有现成墨迹引擎 |
| PPT 联动 | VSTO 加载项 + Office COM | 放映状态和批注接口是 COM 活字。Office for Mac 的 COM 是另一套，Linux 上根本没有 Office 可调 |
| 手写识别 | `InkCanvasForClass.IACoreHelper` | IACore 是 .NET Framework 4.7.2 + x86 的原生库，二进制层面绑死了 Windows |

这四样在新实现里分别换成：Avalonia、自己写的墨迹模型、平台进程监视、外部识别扩展。

## 新架构

```
ElectronicPointer.App                     Avalonia 外壳：窗口、工具栏、设置、会话编排
  ├── ElectronicPointer.Platform.Windows / MacOS / Linux
  ├── ElectronicPointer.Platform.Abstractions    平台能力接口与能力清单
  ├── ElectronicPointer.Rendering                SkiaSharp 渲染与位图导出
  └── ElectronicPointer.Core                      笔迹、画板、撤销、序列化
```

五层，依赖方向单向向下：

| 层 | 内容 | 引用 |
| --- | --- | --- |
| `Core` | `Vec2`、`Stroke`、`StrokeStyle`、`BoardPage`/`BoardDocument`、`UndoRedoStack`、`Lasso`、`BoardDocumentSerializer`、`DefaultHotkeys`、`AppIdentity` | 无。纯逻辑，连 Skia 都不引 |
| `Rendering` | `BoardRenderer`、`RenderOptions`、`BitmapExporter` | Core + SkiaSharp |
| `Platform.Abstractions` | `IPlatformServices` 及其成员接口、`PlatformCapabilities` | Core |
| `Platform.Windows` / `MacOS` / `Linux` | 各自一套 P/Invoke 实现 | Core + Abstractions |
| `App` | `OverlayShell`、`BoardSession`、`OverlayWindow`、`ToolbarWindow`、`SettingsWindow` | 全部 |

Core 是「三个平台跑同一份代码」的那部分。它不知道自己会被画到屏幕上还是存成文件，也不知道自己在哪个操作系统上。

### 平台层怎么接到窗口上

`Platform.*` 三个工程都不引用 Avalonia。它们要改一个 Avalonia 窗口的属性（置顶、穿透、隐藏于 Dock），拿到的只是一句原生句柄：

```csharp
public interface IOverlayWindowTarget { nint Handle { get; } }
```

App 侧的 `AvaloniaHandle` 在 `OverlayWindow.AttachChrome` 里从 `TopLevel.TryGetPlatformHandle()` 借出窗口句柄，交给 `IOverlayChrome.Attach`，之后平台层拿它去调 `SetWindowLong`、`NSWindow` 或 X11。

这条边界带来两个直接好处：平台层不需要显示服务器就能被单测覆盖（`tests/ElectronicPointer.Platform.Linux.Tests` 的 30 个测试就跑在没有任何显示器的 CI 机器上）；将来要换 UI 框架，重写的是 App 一层，而不是三个平台。

### 能力清单

平台差异不靠 try/catch 掩盖。每个平台启动时给出一个 `PlatformCapabilities`，列出九项 `PlatformFeature`（穿透、置顶、隐藏于任务切换器、全局热键、截屏、演示文稿检测、手写识别、自启、跨显示器放置）里哪些真的可用，外加一句中文说明告诉用户缺的东西要怎么补。工具栏读到清单就把对应按钮置灰，而不是等用户点了没反应。

## 「使用效果一致」具体指什么

这句话拆成三类兑现。

### 1. 完全一致：由 Core 决定的部分

笔迹采样与实时绘制（按下即在画，抬起才成形）、圆形橡皮擦除、套索选择与拖拽、撤销/重做栈、多页面与页面切换、序列化格式、导出 PNG/JPEG。这些是纯逻辑加纯数学，三个平台执行同一段代码，没有平台分支，结果逐像素一致。

### 2. 协议一致：由 App 决定的部分

指针一律建模成 begin / move / end 三段（`BoardSession.PointerPressed/Moved/Released`），压力从 `PointerPoint.Properties.Pressure` 取。Apple Pencil、数位板、手指和鼠标走同一条路，所以「笔锋」在三个系统上由同一套采样逻辑算出，只是压感的来源不同。

快捷键表 `DefaultHotkeys` 的 13 条手势写在 Core，全平台一致。只有一处平台化改写：macOS 上 Ctrl 折叠成 Command（`OverlayShell.MapHotkey`），因为 Mac 用户的手已经习惯 Cmd+Z 撤销、Cmd+Q 退出。热键事件来自 AppKit 或 X11 的后台线程，统一经 `Dispatcher.UIThread.Post` 回到 UI 线程。

### 3. 做法一致：由 Platform 层吸收的部分

穿透、置顶、隐藏于任务切换器、截屏、自启这些，操作系统各有各的原生做法，但接口是同一个，且每个成员返回 `bool` 表示这次调用到底生效了没有：

```csharp
bool SetClickThrough(bool enabled);
bool SetAlwaysOnTop(bool enabled);
bool SetHiddenFromSwitcher(bool hidden);
```

Windows 上是 `WS_EX_TRANSPARENT` 加分层窗口，macOS 上是 `ignoresMouseEvents`，Linux X11 上是 SHAPE 扩展。UI 看到的都是同一个返回值，能据此提示用户，不会静默失效。

### 渲染路径

三个平台的 Avalonia 桌面后端都基于 Skia，所以墨迹走 `BoardRenderer` + `SkiaSharp` 这一条路，和离屏导出 PNG/JPEG 共用同一个 `RenderOptions`。屏幕上的画面和存到磁盘的画面因此是同一种代码的产物。

Avalonia 11.3 删掉了 `ISkiaSharpApi`，`DrawingContext` 也不再暴露底层 Skia canvas，所以 `OverlayCanvas.Render` 只有一条路：渲染进 `SKBitmap(w, h, Bgra8888, Premul)`，`Marshal.Copy` 到 `WriteableBitmap.Lock()` 的地址，再 `context.DrawImage` 交给平台合成。这不是退而求其次的备选路径，而是唯一的绘制入口，好处是它恰好和导出共用代码，两端不可能对不上。

## 哪些地方注定不一致

| 功能 | Windows | macOS | Linux |
| --- | --- | --- | --- |
| 穿透 / 置顶 | 完整 | 完整（AppKit 窗口状态） | X11 完整，Wayland 协议不开放，置灰 |
| 全局热键 | `RegisterHotKey` | 事件 tap，需辅助功能权限 | X11 `XGrabKey`，Wayland 多数合成器不转发，置灰 |
| 冻结屏幕 | GDI 抓屏 | `CGWindowListCreateImage`，首次要「屏幕录制」授权 | X11 `XGetImage`，Wayland 不可用 |
| 演示文稿联动 | COM 读放映状态，最准 | 前端应用进程监视 | `/proc` 扫进程名 |
| 手写识别 | IACore 专有 | 无系统 API，交给 OCR 扩展 | 同左 |
| 自启 | 注册表 Run 项 / AUMID | LaunchAgent plist | `~/.config/autostart` 桌面项 |

三条值得多说：

**Linux 不是一个大平台。** `LinuxSession.Current` 区分 X11、Wayland 和无图形会话，能力清单按会话而不是按内核来算。Wayland 下穿透、置顶、全局热键、截屏四项消失，但墨迹、工具、页面、撤销全在 Core，照常可用；设置里提示改用 X11 会话。

**macOS 的权限是运行时的事。** 能力清单启动时检查辅助功能和屏幕录制的授权状态，没给权限就把相关项标为不可用并说明去哪打开。权限窗口由系统弹，应用只是随后重新计算清单。

**PPT 联动是三个平台差距最大的一块。** Windows 版本能读到放映页数和幻灯片内容，靠的是 COM。跨平台只能做到「检测到某个进程进入全屏放映就自动进入穿透」，页级联动做不到。

## 命名

C# 命名空间用 `Mutantcat.ElectronicPointer` 而不是 `org.mutantcat.xxx`。反向域名是给操作系统识别的东西用的：macOS bundle id、Linux `.desktop` 文件名、`~/.config` 下的配置目录。放进 C# 命名空间只会让每个文件顶上的 using 变得难读，而防重名这件事 C# 的程序集加命名空间两级体系已经解决了。

反过来，凡是操作系统要认识这个名字的地方都从同一个地方取：`AppIdentity`。它是唯一的真源，`Directory.Build.props` 和三个打包脚本读同一组字符串，所以 `.deb` 里的包名、`.app` 里的 bundle id、程序自报的 ID 不会各自漂移。Windows 是例外中的例外：系统上只有一个安装副本，所以 MSIX 身份 `Mutantcat.ElectronicPointer` 和配置目录 `%AppData%\Mutantcat\ElectronicPointer` 都不带任何后缀——一套名字，一份安装，没有开发版和稳定版并存的岔路。

`AppIdentity` 同时带版本号：`1.0.<yyyymmdd>`，和包名同源。`AssemblyVersion` 固定 `1.0.0.0`，`InformationalVersion` 带日期戳，`--version` 读的是后者。

## 旧目录
旧 Windows 实现整体搬到了 `old/` 下：主程序 `InkCanvasForClass`、控件 `InkCanvasForClass.IccInkCanvas` 及其 Demo、IACore 封装、两个 PowerPoint 工程，以及中途废弃的 `InkCanvasForClassX`，外加旧的解决方案和截图。目录从仓库根挪走，是为了让根目录只剩电子教鞭自己的东西；内容一行没动，仍作为交互细节和算法的参考。它不参与构建，`ElectronicPointer.sln` 里没有任何一项指向它。新工程全部在 `src/`、`tests/` 和 `packaging/` 下。

## 测试

110 个测试按风险分布，不追求覆盖率数字：

- Core 58 个，撤销栈、套索面积符号、序列化往返、身份与版本号，全是跨平台一致的核心契约
- Rendering 22 个，位图导出尺寸与编码往返
- Linux 平台 30 个，键码映射、能力清单随会话收窄、进程监视的判定逻辑

Linux 平台层能在一台 Windows 机器上测，靠的就是 `IOverlayWindowTarget` 只暴露一个 `nint`：测试里塞进去的句柄可以是个假值，被测逻辑不会真的去连 X server。

Windows 链路（publish → 便携 zip → MSIX → NSIS 安装包）在本机完整跑通过，三个 RID 都试过；Linux 和 macOS 的打包脚本由 CI 首次真实执行。这一点在 [../packaging/README.md](../packaging/README.md) 的「现状」一节里记着。

Windows 那条链路末端多一样东西：一个 NSIS 安装包。它的存在理由是 MSIX 装不进某些机器（域策略、精简系统、离线环境），而 zip 又没有卸载入口。三个架构共用同一个 32 位 `x86-unicode` 安装程序，载荷的架构和安装程序的架构是两件事，理由和取舍记在 [../packaging/README.md](../packaging/README.md) 的「NSIS 安装包」一节。
