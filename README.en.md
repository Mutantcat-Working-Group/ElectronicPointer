<div align=center>
<img src="./icon.png" style="width:100px;" width="100"/>
<h2>ElectronicPointer</h2>
</div>

A cross-platform desktop pointer and annotation tool · [中文](./README.md)

### 1. Overview

- One codebase, three desktops: ElectronicPointer floats a transparent canvas above any window on Windows, macOS and Linux, so you can write with a pen, highlight with a marker and erase without breaking the flow of your lecture.
- Pen, highlighter, eraser, lasso selection, multi-page boards and undo/redo all live in a single cross-platform core, so the feel of writing and every shortcut are identical on all three systems.
- Built by Mutantcat Working Group, open source under the GNU General Public License v3.0, application id `org.mutantcat.electronicpointer`.

Core values:

- Strictly layered, one-way dependencies: App (Avalonia shell) → Platform (Windows/macOS/Linux) → Platform.Abstractions → Rendering → Core. Platform quirks are absorbed by the platform layer, so behaviour is written once and works everywhere.
- At startup the app probes what the host actually allows: click-through overlay, always-on-top, hiding from the task switcher, global hotkeys, screen capture, multiple displays, presentation detection, handwriting recognition and launch at login. Anything unavailable is greyed out with a Chinese explanation instead of failing later.
- One board document (`.epboard`) opens on every platform.

### 2. Features

#### Drawing

- Four tools: pen `Alt+P`, highlighter `Alt+H`, eraser `Alt+E`, lasso select `Alt+S`.
- Ink palette plus a thickness slider whose range follows the tool: pen 1-24px, highlighter 8-48px, eraser 4-96px.
- Pressure sampling feeds the stroke geometry, so a heavier stroke is a wider one.

#### Boards

- Multi-page boards: previous page `Ctrl+PageUp`, next page `Ctrl+PageDown`, new page `Ctrl+N`, shown as `n / m` on the toolbar.
- Undo `Ctrl+Z`, redo `Ctrl+Shift+Z`, clear the current page `Ctrl+Shift+Del`; button hints name the exact stroke the next undo would remove.
- Freeze screen: capture the current screen as a background pinned behind the canvas and annotate over it.
- Recognize ink: turn the selected strokes into text and put it on the clipboard, through a replaceable recognizer.
- Lasso selection moves the selected strokes as a group.

#### Desktop integration

- Click-through toggle `Ctrl+Alt+T`: when on, mouse events reach the application behind the canvas; when off, you write.
- The canvas stays on top and can be hidden from the task bar, the Dock and the window switcher.
- Thirteen global shortcuts, with `Ctrl` folded to `Cmd` on macOS.
- Per-display placement across multiple monitors, and automatic click-through when a full screen slideshow is detected.
- The settings window offers launch at login, open/save for board documents, and exporting the current page as an image.

#### Visible state

- The settings window lists the platform capability table (supported / unavailable, plus the reason), the configuration directory, the version and the application id.
- The configuration directory follows each platform's convention: `%AppData%\Mutantcat\ElectronicPointer` on Windows, `~/Library/Application Support/org.mutantcat.electronicpointer` on macOS and `~/.config/org.mutantcat.electronicpointer` on Linux.

### 3. Install and Download

Grab your platform from [Releases](https://github.com/Mutantcat-Working-Group/ElectronicPointer/releases). Versions look like `1.0.20260929`, that is the minor version plus the build date.

| Platform | Artifact | Notes |
| --- | --- | --- |
| Windows | `ElectronicPointer-1.0.20260929-win-x64-setup.exe` | NSIS installer, Chinese UI, x64/x86/arm64 |
| Windows | `ElectronicPointer-1.0.20260929-win-x64.msix` | Installed build with Start menu entry and uninstaller |
| Windows | `ElectronicPointer-1.0.20260929-win-x64.zip` | Portable, unzip and run |
| macOS | `ElectronicPointer-1.0.20260929-osx-arm64.dmg` | Disk image with an Applications symlink, x64 and arm64 |
| Linux | `electronicpointer_1.0.20260929_amd64.deb` | Debian/Ubuntu package with desktop entry and icons |
| Linux | `ElectronicPointer-1.0.20260929-x86_64.AppImage` | Single file, no root needed |
| Linux | `electronicpointer-1.0.20260929-linux-x64.tar.gz` | Plain directory tree |

1. If Gatekeeper stops the first launch on macOS, run `xattr -cr /Applications/ElectronicPointer.app` once.
2. The Windows installers are self-signed, so SmartScreen asks once whether you trust the publisher.
3. On Linux an X11 session is fully supported; some capabilities depend on the compositor under Wayland, so use an X11 session when they are unavailable.
4. The Linux deb needs the X11 client libraries, fontconfig and libGL, and apt pulls them in; the AppImage and the tarball install nothing and expect those libraries to be present already, which any desktop environment takes care of.

### 4. Quick Start

1. After launch a toolbar appears at the bottom centre of the screen with the pen already in hand, ready to write.
2. `Alt+P` / `Alt+H` / `Alt+E` / `Alt+S` switch between pen, highlighter, eraser and select.
3. Turn on click-through (`Ctrl+Alt+T`) when you need to reach the window behind the canvas, then turn it off to write again.
4. Move between pages with `Ctrl+PageUp` / `Ctrl+PageDown`, start a fresh page with `Ctrl+N`.
5. Use freeze screen to pin slides or a web page as the background and mark them up.
6. `Ctrl+Z` undoes a mistake, `Ctrl+Tab` hides the toolbar when you do not want it on screen, `Ctrl+Q` quits.

### 5. Platform Notes and File Format

**Windows** is the most complete host: PowerPoint integration goes through COM and can read the current slide number. Three install shapes are shipped: a per-machine NSIS installer, an MSIX with a Start menu entry, and a portable zip.

**macOS** capability is decided by runtime authorisation: the system asks for screen recording the first time you capture, and controlling the computer needs to be allowed under System Settings > Privacy & Security > Accessibility. The app does not run in a sandbox, and `Ctrl` shortcuts are displayed as `Cmd`.

**Linux** is judged by session, not by kernel: under X11 click-through, always-on-top, global hotkeys and capture all work; under Wayland those four depend on whether the compositor forwards hotkeys and allows capture, while ink, tools, pages and undo stay unaffected.

**File format** `.epboard` is JSON holding the format version, the application id and version that wrote it, the active page and every stroke on every page; a `.json` extension is accepted as well. Older files keep loading on newer versions, and a format bump is always deliberate.

**What cannot be identical** across platforms is written down in [docs/cross-platform-design.md](./docs/cross-platform-design.md), together with why the old InkCanvasForClass could not be carried over (WPF, Windows Ink, VSTO and Office COM, a .NET Framework native recognition library) and what replaced each of them.

### 6. Progress

- [X] Cross-platform core: Core, Rendering, Platform.Abstractions, Platform.Windows, Platform.MacOS, Platform.Linux, App
- [X] Toolbar groups: tools, ink, thickness, pages, editing, board, files
- [X] Thirteen global shortcuts, with Ctrl folded to Cmd on macOS
- [X] Multi-page boards, undo/redo, lasso select and move
- [X] `.epboard` read/write and image export
- [X] Freeze screen and the handwriting recognition entry point
- [X] Platform capability probing with graceful degradation
- [X] Launch at login toggle
- [X] Packaging: NSIS/MSIX/zip on Windows, dmg on macOS, deb/AppImage/tar.gz on Linux
- [X] GitHub Actions build and release pipeline
- [ ] Verified runs on real macOS and Linux desktops (first executed by the release workflow)
- [ ] A bundled handwriting recognizer (the interface is in place, engines are replaceable)
- [ ] UI for rebinding shortcuts
