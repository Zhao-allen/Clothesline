<picture>
  <source media="(prefers-color-scheme: dark)" srcset="../hero.jpg">
  <img src="../hero.jpg" alt="Clothesline。截图晾在绳上。四张带玻璃框的截图挂在屏幕顶端的一条细线上。">
</picture>

<h3 align="center">macOS 应用 <a href="https://tendedero.app/?lang=zh">Tendedero</a> 的 Windows 移植版&nbsp;&rsaquo;</h3>

<p align="center">
  免费开源。支持 Windows 10 与 11。
  <br>
  <a href="https://github.com/Zhao-allen/Clothesline/releases/latest">下载&nbsp;&rsaquo;</a>
  &nbsp;&nbsp;
  <a href="#从源码构建">从源码构建&nbsp;&rsaquo;</a>
  <br><br>
  <a href="../README.md">English</a>&nbsp;·&nbsp;简体中文&nbsp;·&nbsp;<a href="README.es.md">Español</a>&nbsp;·&nbsp;<a href="README.ja.md">日本語</a>&nbsp;·&nbsp;<a href="README.fr.md">Français</a>
</p>

<br>

## 看不见，但伸手可及。

你截的每一张图，都挂在屏幕顶端的一条晾衣绳上。
按 <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>T</kbd>，绳子垂下来；鼠标移开，它就收回去了。

<p align="center">
  <img src="../demo.jpg" alt="指针停在屏幕顶端，绳子垂下挂着几张截图，指针离开后绳子收回。" width="80%">
</p>

<br>
<br>

## 每个动作都有对应手势。

<p align="center">
  <img src="../bento.jpg" alt="单击复制。右键标注。拖出去分享。放手即可。" width="100%">
</p>

<br>
<br>

| | |
|:--|:--|
| 单击 | 复制这张图。 |
| 右键 | 弹出菜单：复制、编辑、保存、取下。 |
| 双击 | 打开标注编辑器。 |
| 拖到应用里 | 送一份副本过去，图上还留在绳上。 |
| 拖到文件夹里 | 留在那里，离开绳子。 |
| 删除 | 移入回收站。 |
| <kbd>Win</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd> | 区域截图，松手即挂上绳。 |
| <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>T</kbd> | 显示或隐藏晾衣绳。 |

Clothesline 自己从不截图。它只是盯着 Windows 本来就有的快捷键——
<kbd>Win</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd> 截区域，
<kbd>Win</kbd>+<kbd>PrintScreen</kbd> 截全屏——截图一落地就挂上绳。
同样的快捷键，同样的肌肉记忆，只是多了个挂的地方。

<br>

## 桌面，终于清净了。

把截图交给 Clothesline，它们就完全跳过桌面。
不堆文件，不等五秒。每一张在拍下的瞬间就挂上绳，只有你主动拖出去的才会留下。

你原来的截图设置会被记住，Clothesline 退出时自动还原；应用全屏时它也会自动让开。

<br>

## 复制的图片也照收。

从任何地方复制一张图——浏览器、聊天窗口、截图工具——它都会像普通截图一样挂上绳。
在托盘菜单里打开「挂载复制的图片」，T 恤图标就会亮起来。只收图片：凡是和文字或文件一起复制的，一律放过。

<br>

## 天生隐私。

没有账号。没有联网。没有统计。
Clothesline 完全跑在你自己的电脑上，截图从不离开它。

<br>

## 技术规格

| | |
|:--|:--|
| **兼容性** | Windows 10 与 11，x64，支持多显示器不同 DPI |
| **体积** | 安装后约 160 MB（含运行时） |
| **语言** | 英语、简体中文、西班牙语、日语、法语 |
| **技术栈** | C# 13、WPF 与 WinForms，基于 .NET 9 |
| **网络访问** | 无 |
| **价格** | 免费 |
| **许可证** | 代码采用 MIT。名称与图标不包含在内。 |

<br>

## 安装

从[最新发布](https://github.com/Zhao-allen/Clothesline/releases/latest)下载
`Clothesline-Setup.exe` 并运行。安装程序会自己选语言、让你选安装位置，
并把 .NET 运行时一起带上——不需要额外装任何东西。

不想安装？便携版就是一个文件夹：把 `Clothesline-win-x64.zip` 解压到任意位置，
双击 `Clothesline.exe` 即可。

<br>

## 从源码构建

```bat
git clone https://github.com/Zhao-allen/Clothesline.git
cd Clothesline
build.bat
```

需要 .NET 9 SDK（含 WPF 工作负载）。脚本会在 `build\` 下产出一个自包含的 x64 包，
在没装 .NET 的机器上也能直接跑。

<br>

<details>
<summary>程序内部</summary>
<br>

| 文件 | 职责 |
|:--|:--|
| `App.xaml.cs` | 启动、托盘图标、两个监视器 |
| `UI/LineWindow.xaml.cs` | 屏幕顶端那条透明窗口 |
| `UI/Layout.cs` | 绳子的形状，以及每张图挂在哪 |
| `UI/PeggedCard.cs` | 一张图：相框、衣夹、摆动与落下 |
| `Models/Line.cs` | 绳上挂着什么，能对它做什么 |
| `Services/ScreenshotWatcher.cs` | 留意新出现的截图 |
| `Services/ClipboardWatcher.cs` | 留意复制到剪贴板的图片 |
| `Services/Inbox.cs` | 接管截图文件夹，退出时还原 |
| `UI/AnnotationWindow.cs` | 标注编辑器 |
| `Services/FullScreen.cs` | 知道什么时候该藏起来 |
| `UI/TrayIcon.cs` | 托盘图标与菜单 |
| `Services/Diag.cs` | 让静默失败变得可读的日志 |

翻译放在 `Resources` 里，每种语言一个 `.resx`，由 `Services/I18n.cs` 选择。

</details>

<br>

---

<p align="center">
  <sub>代码采用 MIT 许可。Clothesline 的名称与图标不包含在内，衍生项目需要另做。见 <a href="../LICENSE">LICENSE</a>。</sub>
  <br>
  <sub>移植自 Alejandro Buján 的 macOS 应用 <a href="https://tendedero.app">Tendedero</a>。本项目是独立的 Windows 移植版，与原项目无隶属关系。</sub>
</p>
