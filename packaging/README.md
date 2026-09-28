# 打包

这里存放把 **电子教鞭 / ElectronicPointer** 变成各平台可安装产物的全部输入和脚本。架构层面的说明在 [../docs/cross-platform-design.md](../docs/cross-platform-design.md)，这份文档只讲怎么把产物做出来。

## 共同的流水线

三个平台共用同一步，区别只在最后一段：

```powershell
pwsh packaging/publish.ps1 -Rid <RID> [-DateStamp <yyyymmdd>]
<平台脚本> <RID>
```

`publish.ps1` 做的事是 `dotnet publish --self-contained`，再在输出目录里写一个 `buildstamp.txt`：

```text
1.0.20260928
```

三个平台脚本都从这份文件取版本号和运行时标识，所以 `.deb`、`.AppImage`、`.dmg`、`.msix` 和 `-setup.exe` 的名字和内部版本永远一致，不会出现某个包忘了改版本号的情况。

发布输出落在 `artifacts/out/<RID>/`，最终产物落在 `artifacts/dist/`，拼装时的暂存落在 `artifacts/stage/`。

支持的运行时标识：`win-x64`、`win-x86`、`win-arm64`、`osx-x64`、`osx-arm64`、`linux-x64`、`linux-arm64`。

## Linux

```bash
pwsh packaging/publish.ps1 -Rid linux-x64
bash packaging/linux/package.sh linux-x64
```

| 产物 | 说明 |
| --- | --- |
| `electronicpointer_1.0.20260928_amd64.deb` | 装到 `/usr/lib/electronicpointer`，`/usr/bin/electronicpointer` 建软链，注册桌面入口和各尺寸图标 |
| `ElectronicPointer-1.0.20260928-x86_64.AppImage` | 单文件，解压即用，不需要 root |
| `electronicpointer-1.0.20260928-linux-x64.tar.gz` | 纯净目录树，适合塞进自建源或者 U 盘 |

三种产物装出来的可执行文件路径一致，都是 `/usr/lib/electronicpointer/ElectronicPointer`。应用在设置里打开「开机自启」时写的是 `~/.config/autostart/org.mutantcat.electronicpointer.desktop`，指向这个路径，所以无论用哪一种装上，自启都对。

`debian/control` 里的 `@VERSION@`、`@ARCH@` 和 `@SIZE@` 由脚本按编译戳、架构和 `du -sk` 的真实体积替换。

依赖分两段得出。`libc6`、`libgcc-s1`、`libstdc++6`、`libfontconfig1` 连版本下界都是 `dpkg-shlibdeps` 在发布输出上算出来的：自带运行时、Skia、HarfBuzz 里只有这几个原生库按 ELF 名链到系统库上。剩下的必须手写，因为 Avalonia 和 Skia 是在托管侧 `dlopen` 平台库的，`readelf -d` 只列得出一堆 `libdl`/`libpthread`，`dpkg-shlibdeps` 也就看不见它们——看不见不等于不需要，窗口建不起来就是因为漏了。清单是照着托管程序集里出现的 soname 逐个核出来的：`libX11.so.6`、`libXext.so.6`、`libXi.so.6`、`libXrandr.so.2`、`libXinerama.so.1`、`libXcursor.so.1`、`libGL.so.1`，加上 Skia 直接链上的 `libfontconfig.so.1`。`libx11-xcb` 和 `libxcb` 不用写，`libgl1` 会把 Mesa 那一栈（`libGLX`、`libglx-mesa0`、`libdrm`、`libllvm`）连同它们一起传递进来。Vulkan 也不写：`libvulkan.so.1` 只在真的去建 Vulkan surface 时才会加载，而这个应用走 Skia 的 GL 后端。

`libICE`/`libSM` 和 GTK3 放在 Recommends。文件选择框优先走 XDG 门户，没有门户的会话才回落到 `Avalonia.X11` 自带的 GTK 实现，所以 GTK 是「装了更好」而不是必须；前两个 soname X11 后端自己也引用。Ubuntu 24.04 的 time_t 迁移把 GTK 改名成 `libgtk-3-0t64`，Debian 那边还叫 `libgtk-3-0`，于是写成 `libgtk-3-0t64 | libgtk-3-0`，两边都能装上。libc/libstdc++ 的下界取自构建机（Ubuntu 24.04）上 dpkg-shlibdeps 的结果，也就是 Ubuntu 22.04 与 Debian 12 起步。

AppImage 和 tar.gz 不声明任何依赖，把这些库留给宿主的桌面环境：装 deb 的机器上 apt 会补齐，能跑 AppImage 的桌面本来就带着。

deb 在干净容器里实测过两遍：`apt-get install ./electronicpointer_*.deb` 一次带 Recommends（171 个包，GTK3 和 Mesa 都在内），一次 `--no-install-recommends`（52 个包）。两次都没有 unmet dependencies，`apt-get check` 干净，`/usr/bin/electronicpointer --version` 都出版本号，Xvfb 下启动后工具栏落在屏幕底部中间（`860x166+370+802`）。

`appimagetool` 是现场下载的。CI 的 runner 上没有 FUSE 设备，所以脚本把它 `--appimage-extract` 解开之后直接执行 `squashfs-root/AppRun`，而不是把它挂载起来。

AppImage 那一侧还有两条 AppDir 约定：`.desktop` 菜单项和 `Icon=` 指向的 PNG 必须同时出现在 AppDir 根目录，appimagetool 只从那里找，`usr/share/applications` 里那份是给装进系统的 deb 用的。`appimagetool` 另外要求系统里有 `file` 命令，CI 检测到缺失时会先装上，lint job 还会用 `desktop-file-validate` 校验菜单项，重复的条目组或写错的字段码在那里就会失败。

## macOS

```bash
pwsh packaging/publish.ps1 -Rid osx-arm64
bash packaging/macos/package.sh osx-arm64
```

| 产物 | 说明 |
| --- | --- |
| `ElectronicPointer-1.0.20260928-osx-arm64.app` | 直接拖进 `/Applications` 的包 |
| `ElectronicPointer-1.0.20260928-osx-arm64.dmg` | 装着上面那个 `.app` 的磁盘映像 |

`.app` 里的 `Contents/MacOS` 就是原封不动的发布输出。.NET 会从这个目录旁边解析 `libSkiaSharp.dylib` 和 Avalonia 的原生库，所以搬家或者改链接只会把本来能用的东西弄坏。

Code signing 和公证是可选的，靠四个环境变量：

```bash
APPLE_SIGNING_IDENTITY='Developer ID Application: XXX'   # 设了才会 codesign
APPLE_ID='xxx@example.com'                                # 下面三个都设了才会 notarize
APPLE_TEAM_ID='XXXXXXXXXX'
APPLE_APP_PASSWORD='xxxx-xxxx-xxxx-xxxx'
```

一个都不设也能打出包，只是会被 Gatekeeper 拦下来。未签名的包在目标机器上过一次这个命令就能跑：

```bash
xattr -cr /Applications/ElectronicPointer.app
```

## Windows

```powershell
pwsh packaging/publish.ps1 -Rid win-x64
pwsh packaging/windows/package.ps1 -Rid win-x64        # 便携 zip + MSIX + NSIS
pwsh packaging/windows/package.ps1 -Rid win-x86
pwsh packaging/windows/package.ps1 -Rid win-arm64
```

| 产物 | 说明 |
| --- | --- |
| `ElectronicPointer-1.0.20260928-win-x64.zip` | 便携版，解压双击 |
| `ElectronicPointer-1.0.20260928-win-x64.msix` | 安装版，带开始菜单项和卸载入口 |
| `ElectronicPointer-1.0.20260928-win-x64-setup.exe` | NSIS 安装包，每机一份，带组件选择 |

MSIX 不是打包工程生成的，而是 `makeappx` 把发布输出加上 `AppxManifest.xml` 和 `Assets/` 直接打成包。这样整条 Windows 流水线只依赖 Windows PowerShell 和 Windows SDK，不需要 Visual Studio。

包里的 `Identity Name` 加上 `Application Id` 拼出来是 `Mutantcat.ElectronicPointer.App`，和程序自己上报的 AUMID 是同一个字符串，自启动那一套要靠它对齐。

MSIX 的 `Version` 是四段式，每段上限 65535，装不下 `20260928`，所以脚本用「距 2020-01-01 的天数」当 build 段（`1.0.20260928` → `1.0.2462.0`）：排序和日期戳一致，在「设置 - 应用」里看也还是可读的。`windows/assets/` 下的瓦片图标和 Linux 的 hicolor 图标同源。

签名参数：

```powershell
pwsh packaging/windows/package.ps1 -Rid win-x64 `
    -CertificatePath sign.pfx -CertificatePassword 'xxx' `
    -TimestampUrl 'http://timestamp.digicert.com'
```

不带证书也能打包，但没签名的 MSIX 装不上去，便携版也会被 SmartScreen 拦。签名时会自动从证书里取 `Publisher`，因为 `makeappx` 记录的主题必须和 `signtool` 证明的主题一致。

### NSIS 安装包

`installer.nsi` 用 `makensis` 编，输入还是同一棵发布树，所以它不需要 Visual Studio、不需要打包工程，机器上装一份官方原版 NSIS 就够；托管镜像自己不带，两个 workflow 都是先 `choco install nsis` 再进打包步骤。装法是每机一份：`$PROGRAMFILES` 下一个 `ElectronicPointer` 目录，开始菜单一个文件夹，桌面快捷方式和开机自启做成可选组件，卸载入口跟着开始菜单一起建。界面是中文的，升级时靠卸载注册表键认旧装，不会重复问一遍装到哪。

左下角的品牌字是 `电子教鞭 ElectronicPointer <版本号>`，不是 NSIS 默认的那串，做法就是把 `BrandingText` 写掉。

**三个架构共用同一个 `x86-unicode` 目标。** 32 位的安装程序里装 64 位或 ARM64 的载荷没有任何问题：安装目录走 `$PROGRAMFILES64`，注册表走 `SetRegView 64`，卸载键因此不会落在 `Wow6432Node` 里跟 32 位那份打架。之所以不直接出 amd64/arm64 的安装程序，是因为 NSIS 官方只发 x86 系的 stub，CI 装的也是官方原版包，要编别的目标得先从源码编 stub。Git for Windows、Notepad++ 都是这么做的。`package.ps1` 里留了一个 `Test-NsisTarget` 守卫，万一某台机器的 NSIS 被裁过，会跳过安装包而不是让 makensis 报一句看不懂的错。

签名顺序是先签装进包里的 `ElectronicPointer.exe`、再签 `setup.exe` 本身：NSIS 的载荷封在文件里，签名只能落在最后的附加数据上，所以顺序反了就白签。不带证书时脚本会现场生成一个 `CN=Mutantcat Working Group` 的自签名证书并用它签所有产物，用户第一次会被问一次「信不信这个发布者」；想要完全不要签名，加 `-SkipSigning`。

只出安装包、不要另外两个产物：`-SkipMsix`；反过来只要 zip 和 MSIX：`-SkipNsis`。

### 图标

唯一的图标源是仓库根目录的 `icon.png`。`packaging/tools/generate-icons.ps1` 从它派生所有提交进仓库的图标：Linux hicolor 七个尺寸、MSIX 的瓦片图、NSIS 用的多分辨率 `.ico`。换了图之后跑一次这个脚本，三个平台的包就都换好了。

## 版本号

版本号是 `1.0.<yyyymmdd>`，即「小版本 + 构建日期」。`AssemblyVersion` 固定为 `1.0.0.0`，因为日期戳每天都在变，而 CLR 只允许四段数字，把日期塞进程序集版本会让每次构建的强名称都不同；`InformationalVersion` 带日期戳，`--version` 读的是后者。

本地构建不传 `-DateStamp`，脚本用当天日期。出了 tag 之后一定要传，`-p:DateStamp` 保证同一个 tag 无论什么时候重新打包，版本号都一样。

## 平台限制

**Linux + Wayland**：全局快捷键和屏幕捕获都取决于合成器。X11 会话下一切照旧；Wayland 会话下某些合成器不转发热键，应用捕获不到，工具栏操作仍然可用。

**macOS 权限**：第一次做屏幕捕获时系统会弹权限窗。应用完全在沙箱外运行，不需要额外授权，但系统偏好设置里得允许它控制电脑，辅助功能权限同理。

**Windows 未签名**：`.msix` 不签名装不上，便携版会被 SmartScreen 警告。两者都只是提示，不影响功能。

## 现状

三个 RID（`win-x64`、`win-x86`、`win-arm64`）的 `windows/package.ps1` 链路（`publish.ps1` → 便携 zip → MSIX → NSIS 安装包）都在 Windows 上完整跑通过：`makeappx` 打出的包里 236 个条目、`Identity`、`Assets` 都对；安装包编出来以后，标题、左下角品牌字、中文组件页和目录页、卸载入口都实测看过，`signtool` 也验过。`win-x64` 和 `win-x86` 两个安装包是真的装出来的，`win-arm64` 走的是同一条链路和同一个 `x86-unicode` 目标，差别只在载荷。

CI 的 `build` job 把六个 RID（`win-x64`、`win-x86`、`win-arm64`、`linux-x64`、`osx-x64`、`osx-arm64`）在三个真机镜像上全都跑到过：`windows-latest`、`ubuntu-latest`、`macos-latest`（具体镜像号随 GitHub 更新而变，写死没有意义）上 restore → build → test（110 个）→ publish → 打包 → 校验。`lint` job 对全部脚本做 `bash -n`、`shellcheck` 和打包契约检查，那只是静态把关，下面写的产出是真实执行的结果。
- Windows：三个架构的便携 zip、MSIX、NSIS 安装包都真的打出来并签了名（没有证书时是现场生成的 `CN=Mutantcat Working Group` 自签名证书），三个产物逐一断言存在。
- Linux：`linux/package.sh` 在 runner 上真实执行，deb、AppImage、tar.gz 三个产物逐一断言存在；AppImage 还用 `--appimage-extract` 解开（runner 没有 FUSE，挂不起来），直接跑镜像里的 `AppRun --version`，「解压即用」是验过的，不是写在文档上的。deb 的依赖链另在本地干净容器（Ubuntu 24.04）里装过：带 Recommends 与 `--no-install-recommends` 各一次，都没有未满足依赖，装好之后从 `/usr/bin/electronicpointer` 启动，工具栏位置与 Xvfb 屏幕尺寸对得上。
- macOS：`macos/package.sh` 在 runner 上真实执行，两个架构的 `.app` 和 `.dmg` 都出了；CI 把 dmg 挂载起来，验过里面的 `Applications` 是指向 `/Applications` 的软链、验过 bundle 带 ad-hoc 签名，arm64 的那一份还从挂载的镜像里直接启动过。
- `win-arm64` 和 `osx-x64` 在 runner 上没有对应硬件（x64 Windows 跑不了 ARM64 程序，arm64 的 macOS 镜像不带 Rosetta），这两个 payload 照常发布、打包、校验产物，只是不启动，`smoke` 步骤对它们自动跳过；`win-x86` 靠 WoW64 能启动，冒烟照旧。

`release.yml` 只由 tag 触发，链路与 `ci.yml` 同源，还没有真实触发记录；正式发版由 `v1.0.<yyyymmdd>` 的 tag 触发，别擅自打。
