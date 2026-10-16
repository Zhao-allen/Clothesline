<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/hero.jpg">
  <img src="docs/hero.jpg" alt="Clothesline. Screenshots, hung out to dry. Four screenshots in glass frames hang from a thin line at the top of the screen.">
</picture>

<h3 align="center">A Windows port of <a href="https://tendedero.app/?lang=en">Tendedero</a>&nbsp;&rsaquo;</h3>

<p align="center">
  Free and open source. For Windows 10 and 11.
  <br>
  <a href="https://github.com/Zhao-allen/Clothesline/releases/latest">Download&nbsp;&rsaquo;</a>
  &nbsp;&nbsp;
  <a href="#build-from-source">Build from source&nbsp;&rsaquo;</a>
  <br><br>
  English&nbsp;·&nbsp;<a href="docs/i18n/README.zh-Hans.md">简体中文</a>&nbsp;·&nbsp;<a href="docs/i18n/README.es.md">Español</a>&nbsp;·&nbsp;<a href="docs/i18n/README.ja.md">日本語</a>&nbsp;·&nbsp;<a href="docs/i18n/README.fr.md">Français</a>
</p>

<br>

## Out of sight. Within reach.

Every screenshot you take hangs on a line just above your screen.
Press <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>T</kbd> and it glides down. Move away and it's gone.

<p align="center">
  <img src="docs/demo.jpg" alt="The pointer rests against the top edge, the line slides down with screenshots hanging from it, and the line tucks away when the pointer leaves." width="80%">
</p>

<br>
<br>

## A gesture for everything.

<p align="center">
  <img src="docs/bento.jpg" alt="Click to copy. Right click to mark up. Drag to share. Let it go." width="100%">
</p>

<br>
<br>

| | |
|:--|:--|
| Click | Copy the image. |
| Right click | A menu: copy, edit, save, take down. |
| Double click | Open it in the markup editor. |
| Drag into an app | Send a copy. It stays on the line. |
| Drag into a folder | Keep it there. It leaves the line. |
| Delete | Move it to the Recycle Bin. |
| <kbd>Win</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd> | Take a region capture. It hangs the moment you let go. |
| <kbd>Ctrl</kbd>+<kbd>Alt</kbd>+<kbd>T</kbd> | Show or hide the line. |

Clothesline never takes screenshots itself. It watches the shortcuts Windows
already gives you — <kbd>Win</kbd>+<kbd>Shift</kbd>+<kbd>S</kbd> for a region,
<kbd>Win</kbd>+<kbd>PrintScreen</kbd> for the whole screen — and the moment a
capture lands it hangs on the line. Same shortcuts. Same muscle memory.
Just a place for them to hang.

<br>

## Your desktop. Finally clear.

Hand Clothesline your screenshots and they skip the Desktop entirely.
No clutter. No five-second wait. Each capture hangs the instant you take it,
and only what you drag out is kept.

Your previous screenshot settings are remembered and put back when Clothesline
quits, and it stays out of the way while an app is in full screen.

<br>

## Copied images too.

Copy an image from anywhere — a browser, a chat window, the Snipping Tool — and
it hangs on the line like any other. Turn on **Hang copied images** in the tray
menu and the shirt fills in while it is on. Only images: anything copied along
with text or a file is left alone.

<br>

## Private by design.

No account. No network. No analytics.
Clothesline runs entirely on your PC, and your screenshots never leave it.

<br>

## Tech Specs

| | |
|:--|:--|
| **Compatibility** | Windows 10 and 11, x64. Per-monitor DPI aware. |
| **Size** | ~160 MB installed, runtime included |
| **Languages** | English, Chinese (Simplified), Spanish, Japanese, French |
| **Built with** | C# 13, WPF and WinForms on .NET 9 |
| **Network access** | None |
| **Price** | Free |
| **License** | MIT for the code. The name and icon are not included. |

<br>

## Install

Download `Clothesline-Setup.exe` from the
[latest release](https://github.com/Zhao-allen/Clothesline/releases/latest)
and run it. The installer picks its own language, lets you choose where it
goes, and carries the .NET runtime with it — nothing else to install.

Prefer not to install? The portable build is a folder: unzip
`Clothesline-win-x64.zip` anywhere and start `Clothesline.exe`.

<br>

## Build from source

```bat
git clone https://github.com/Zhao-allen/Clothesline.git
cd Clothesline
build.bat
```

Requires the .NET 9 SDK with the WPF workload. The script builds a
self-contained x64 package under `build\`, so the result runs on a machine
with no .NET installed at all.

<br>

<details>
<summary>Inside the app</summary>
<br>

| File | Role |
|:--|:--|
| `App.xaml.cs` | Startup, the tray icon, the two watchers |
| `UI/LineWindow.xaml.cs` | The transparent strip along the top of the screen |
| `UI/Layout.cs` | The rope, and where each photo hangs |
| `UI/PeggedCard.cs` | One photo: frame, clothespin, swing and drop |
| `Models/Line.cs` | What is hanging, and what you can do with it |
| `Services/ScreenshotWatcher.cs` | Notices new screenshots |
| `Services/ClipboardWatcher.cs` | Notices images copied to the clipboard |
| `Services/Inbox.cs` | Takes over the screenshot folder and puts it back |
| `UI/AnnotationWindow.cs` | The markup editor |
| `Services/FullScreen.cs` | Knows when to stay hidden |
| `UI/TrayIcon.cs` | The tray icon and its menu |
| `Services/Diag.cs` | The log that makes a silent failure readable |

Translations live in `Resources`, one `.resx` per language, and
`Services/I18n.cs` picks between them.

</details>

<br>

---

<p align="center">
  <sub>The code is MIT licensed. The Clothesline name and icon are not, so forks need their own. See <a href="LICENSE">LICENSE</a>.</sub>
  <br>
  <sub>Ported from Alejandro Buján's <a href="https://tendedero.app">Tendedero</a> for macOS. An independent Windows port, not affiliated with the original project.</sub>
</p>
