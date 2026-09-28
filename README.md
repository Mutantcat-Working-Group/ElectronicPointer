<div align=center>
<img src="./icon.png" style="width:100px;" width="100"/>
<h2>电子教鞭</h2>
</div>

ElectronicPointer · 跨平台桌面电子教鞭与批注 · [English](./README.en.md)

### 一、产品概述

- 一套代码跑在 Windows、macOS、Linux 三个桌面平台上的电子教鞭：在任意窗口之上浮一层透明画布，用笔写、用荧光笔标、用橡皮擦，不打断讲解节奏。
- 画笔、荧光笔、橡皮、套索选择、多页批注、撤销重做由同一份跨平台内核实现，三个平台上的书写手感与快捷键完全一致。
- 由 Mutantcat Working Group 开发，基于 GNU General Public License v3.0 开源，应用标识 `org.mutantcat.electronicpointer`。

核心价值：

- 分层单向依赖：App（Avalonia 外壳）→ Platform（Windows/macOS/Linux）→ Platform.Abstractions → Rendering → Core。平台差异被吸收在平台层，业务逻辑一次实现三端生效。
- 启动时探测所在平台真实能力（点击穿透、置顶、隐藏于任务栏、全局快捷键、屏幕捕捉、多显示器、放映检测、墨迹识别、登录自启共九项），不支持的功能在界面里置灰并用中文说明原因，而不是报错崩溃。
- 一份批注文档（`.epboard`）在三个平台之间可以互相打开。

### 二、功能说明

#### 书写

- 四种工具：画笔 `Alt+P`、荧光笔 `Alt+H`、橡皮 `Alt+E`、套索选择 `Alt+S`。
- 墨色色板取色，粗细滑条随工具切换量程：画笔 1-24px、荧光笔 8-48px、橡皮 4-96px。
- 压感采样进入笔迹几何，落笔轻重会反映在线宽变化上。

#### 批注

- 多页画板：上一页 `Ctrl+PageUp`、下一页 `Ctrl+PageDown`、新建页 `Ctrl+N`，工具条上显示为 `n / m`。
- 撤销 `Ctrl+Z`、重做 `Ctrl+Shift+Z`、清空当前页 `Ctrl+Shift+Del`；按钮提示会写出下一步撤销的具体是哪一笔。
- 冻结屏幕：把当前屏幕抓成底图钉在画布后面，再往截图上标注。
- 识别墨迹：把选中的笔迹转成文字并写入剪贴板，识别引擎是可替换的。
- 套索选择后可整体移动选中笔迹。

#### 桌面融合

- 穿透开关 `Ctrl+Alt+T`：开启后鼠标事件落到桌面的原应用上，关闭后才能书写。
- 画布始终置顶，可从任务栏、Dock 与窗口切换器中隐藏。
- 全局快捷键共 13 条，macOS 上 `Ctrl` 自动映射为 `Cmd`。
- 多显示器按屏幕定位画布；进入全屏放映时自动进入穿透。
- 设置窗口提供登录自启开关、批注文档的打开与保存、以及把当前页导出为图片。

#### 状态可见

- 设置窗口列出当前平台能力清单（支持 / 不可用 + 原因）、配置目录、版本号与应用标识。
- 配置目录遵循各平台惯例：Windows `%AppData%\Mutantcat\ElectronicPointer`、macOS `~/Library/Application Support/org.mutantcat.electronicpointer`、Linux `~/.config/org.mutantcat.electronicpointer`。

### 三、安装与下载

从 [Releases](https://github.com/Mutantcat-Working-Group/ElectronicPointer/releases) 下载对应平台的产物。版本号形如 `1.0.20260928`，即小版本加构建日期。

| 平台 | 产物 | 说明 |
| --- | --- | --- |
| Windows | `ElectronicPointer-1.0.20260928-win-x64-setup.exe` | NSIS 安装包，中文界面，x64/x86/arm64 三架构 |
| Windows | `ElectronicPointer-1.0.20260928-win-x64.msix` | 安装版，带开始菜单项与卸载入口 |
| Windows | `ElectronicPointer-1.0.20260928-win-x64.zip` | 便携版，解压即用 |
| macOS | `ElectronicPointer-1.0.20260928-osx-arm64.dmg` | 磁盘映像，含 Applications 快捷方式，x64 与 arm64 各一份 |
| Linux | `electronicpointer_1.0.20260928_amd64.deb` | Debian/Ubuntu 安装包，注册桌面入口与图标 |
| Linux | `ElectronicPointer-1.0.20260928-x86_64.AppImage` | 单文件运行，不需要 root |
| Linux | `electronicpointer-1.0.20260928-linux-x64.tar.gz` | 纯净目录树，可放进 U 盘或自建源 |

1. macOS 首次启动若被 Gatekeeper 拦下，执行一次 `xattr -cr /Applications/ElectronicPointer.app`。
2. Windows 安装包使用自签名证书，SmartScreen 会询问一次是否信任该发布者。
3. Linux 下 X11 会话功能完整；Wayland 会话下部分能力受合成器限制，不可用时改用 X11 会话。
4. Linux 的 deb 依赖 X11 客户端库、fontconfig 和 libGL，apt 会自动装上；AppImage 与 tar.gz 不带安装步骤，需要系统里已有这些库，桌面环境默认都有。

### 四、快速上手

1. 启动后屏幕下方中间出现工具条，默认拿的是画笔，直接就能写。
2. `Alt+P` / `Alt+H` / `Alt+E` / `Alt+S` 切换画笔、荧光笔、橡皮、选择。
3. 需要点击屏幕背后的窗口时打开「穿透」（`Ctrl+Alt+T`），讲完再关掉。
4. 翻页用 `Ctrl+PageUp` / `Ctrl+PageDown`，新起一页按 `Ctrl+N`。
5. 想把幻灯片或网页钉在后面当底图时点「冻结屏幕」，然后照图标注。
6. 写错按 `Ctrl+Z`；不想让人看见工具条按 `Ctrl+Tab`；下课按 `Ctrl+Q` 退出。

### 五、平台差异与文件格式

**Windows** 功能最全：PowerPoint 联动走 COM，能读到放映页数；安装形态有每机一份的 NSIS 安装版、带开始菜单的 MSIX 和便携 zip 三种。

**macOS** 能力由运行时授权决定：第一次截屏时系统会弹出屏幕录制授权，控制电脑需要在「系统设置 > 隐私与安全性 > 辅助功能」中放行。应用不运行在沙箱内，`Ctrl` 系列快捷键在界面上显示为 `Cmd`。

**Linux** 按会话而不是按内核判断能力：X11 会话下穿透、置顶、全局热键、截屏都可用；Wayland 会话下这几项取决于合成器是否转发热键与允许截屏，墨迹、工具、页面、撤销照常可用。

**文件格式** `.epboard` 是 JSON 文本，记录格式版本、生成它的应用标识与版本、活动页以及每页的笔迹列表，也接受 `.json` 后缀。老文件在新版本上继续可读，格式升级是显式的。

**注定不一致的地方** 见 [docs/cross-platform-design.md](./docs/cross-platform-design.md)，其中也说明了旧 InkCanvasForClass 为什么搬不过来（WPF、Windows Ink、VSTO 与 Office COM、.NET Framework 原生识别库），以及它们各自被换成了什么。

### 六、开发进度

- [X] 跨平台主体：Core、Rendering、Platform.Abstractions、Platform.Windows、Platform.MacOS、Platform.Linux、App
- [X] 工具栏七组：工具、墨色、粗细、页面、编辑、画板、文件
- [X] 全局快捷键 13 条，macOS Ctrl 折成 Cmd
- [X] 多页画板、撤销重做、套索选择与移动
- [X] `.epboard` 文档读写与图片导出
- [X] 冻结屏幕与墨迹识别入口
- [X] 平台能力探测与降级提示
- [X] 登录自启开关
- [X] 打包：Windows NSIS/MSIX/zip、macOS dmg、Linux deb/AppImage/tar.gz
- [X] GitHub Actions 打包与发布流水线
- [ ] 真实 macOS 与 Linux 桌面环境实测（由 release 工作流首次执行）
- [ ] 内置墨迹识别引擎（接口已就绪，引擎可替换）
- [ ] 快捷键自定义入口
