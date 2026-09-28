> 这里是从仓库根搬过来的旧 Windows 实现，只作参考，不参与构建。

原版 InkCanvasForClass 是基于 WPF 的 Windows 专属电子教鞭。跨平台重构时它没有被升级，而是整体搬到了这里，原因写在 [../docs/cross-platform-design.md](../docs/cross-platform-design.md)：WPF、Windows Ink 墨迹引擎、VSTO 与 Office COM、.NET Framework 4.7.2 + x86 的手写识别原生库，这几样都跨不出去，于是分别被换成了 Avalonia、自写的墨迹模型、平台进程监视和可替换的识别引擎。

留下来的价值是交互细节和算法：`IccInkCanvas` 里的实时笔锋、区域擦除的判定、漫游（平移画布）的处理，这些思路在 `src/ElectronicPointer.Core` 里有对应的新实现，两边对着看比读文档快。

目录内容：

| 目录 | 说明 |
| --- | --- |
| `InkCanvasForClass` | 旧主程序，WPF |
| `InkCanvasForClass.IccInkCanvas` | 魔改的 InkCanvas 控件，笔锋与多页白板都在这儿 |
| `InkCanvasForClass.IccInkCanvas.Demo` | 上面那个控件的 Demo |
| `InkCanvasForClass.IACoreHelper` | IACore 手写识别库的封装（.NET Framework 4.7.2 + x86） |
| `InkCanvasForClass.PowerPoint.InteropHelper` | PowerPoint COM 联动封装 |
| `InkCanvasForClass.PowerPoint.VstoPlugin` | VSTO 插件，解决 Office 与 WPS 抢占 COM 的问题 |
| `InkCanvasForClassX` | 中途废弃的重写尝试 |
| `Ink Canvas.sln` | 旧解决方案，同上，不参与构建 |
| `icc*.png` | 旧 README 用的插图和图标 |

当前工程是 [`ElectronicPointer.sln`](../ElectronicPointer.sln)，源码在 `src/`、测试在 `tests/`、打包脚本在 `packaging/`。
