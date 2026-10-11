# Tendedero for Windows

<p align="center">
  <strong>Screenshots, hung out to dry.</strong><br/>
  A Windows port of <a href="https://github.com/alejandrobujan/tendedero">alejandro buján's macOS app</a>, built with WPF and .NET 9.
</p>

<p align="center">
  <a href="#download">Download&nbsp;&rsaquo;</a>
  &nbsp;&nbsp;
  <a href="#build-from-source">Build from source&nbsp;&rsaquo;</a>
</p>

---

## Out of sight. Within reach.

Every screenshot you take hangs on a line just above your screen.
Rest the pointer at the top edge and it glides down. Move away and it's gone.

| | |
|:--|:--|
| **Click** | Copy the image. |
| **Press and hold** | Open it in your image editor. |
| **Double click** | Open it at full size. |
| **Drag into an app** | Send a copy. It stays on the line. |
| **Drag into a folder** | Keep it there. It leaves the line. |
| **Drag to the recycle bin, or click the ✕** | Let it go. |
| **Rest the pointer at the top edge** | Bring the line down. |
| **Move the pointer away** | Tuck it back up. |
| <kbd>Ctrl</kbd> + <kbd>Alt</kbd> + <kbd>T</kbd> | Show or hide the line. |

## Your desktop. Finally clear.

Hand Tendedero your screenshots and they skip the Desktop entirely.
Each capture hangs the instant you take it, and only what you drag out is kept.
Same shortcuts. Same muscle memory. Just less mess.

## Private by design.

No account. No network. No analytics.
Tendedero runs entirely on your PC, and your screenshots never leave it.

## Languages

Tendedero for Windows ships with **five languages** out of the box and
follows the system UI language automatically. You can also pick a language
from the tray menu → **Language**.

| Code | Language | Native name |
|:----:|:---------|:------------|
| `en` | English  | English     |
| `es` | Español  | Español     |
| `zh` | 中文     | 中文        |
| `ja` | 日本語   | 日本語      |
| `fr` | Français | Français    |

To add a new translation, drop a `Strings.<culture>.resx` file in
`Resources/` and add the culture code to `SatelliteResourceLanguages` in
`Tendedero.csproj`. See [Contributing](#contributing) below.

## Download

Grab the latest `Tendedero-win-x64.exe` from the
[Releases](../../releases/latest) page. It's a self-contained single-file
executable — no .NET runtime install required. Just double-click and go.

| | |
|:--|:--|
| **Platform** | Windows 10 1809+ / Windows 11, x64 |
| **Size** | ~160 MB (self-contained) |
| **Runtime** | None required |
| **License** | MIT for the code. The name and icon are not included. |

## Build from source

```powershell
git clone https://github.com/<your-fork>/tendedero-windows.git
cd tendedero-windows
dotnet build -c Release
```

To produce a self-contained single-file EXE:

```powershell
dotnet publish -c Release -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o publish
```

The EXE appears in `publish/Tendedero.exe`.

Requires the **.NET 9 SDK** with the WPF workload
(`Microsoft.NET.Sdk.WindowsDesktop` / `UseWPF`).

## Tech specs

| | |
|:--|:--|
| **Compatibility** | Windows 10 1809 or later, x64 |
| **Languages** | English, Spanish, 中文, 日本語, Français |
| **Built with** | C# 13, WPF, .NET 9, WinForms (tray) |
| **Network access** | None |
| **Price** | Free |
| **License** | MIT |

## Project structure

| File | Role |
|:--|:--|
| `App.xaml.cs` | Startup, services wiring, tray, hotkey, watchers |
| `UI/LineWindow.cs` | Transparent topmost strip, slide in/out, mouse tracking |
| `UI/PeggedCard.cs` | One photo: glass frame, clip, swing/breeze animations |
| `UI/TrayIcon.cs` | System tray menu (WinForms `NotifyIcon`) |
| `UI/Layout.cs` | Math: parabolic rope, card spacing, sizes |
| `Services/ScreenshotWatcher.cs` | `FileSystemWatcher` on the Screenshots folder |
| `Services/ClipboardWatcher.cs` | Hooks copied images from the clipboard |
| `Services/HotKey.cs` | Global `Ctrl+Alt+T` via `RegisterHotKey` |
| `Services/FullScreen.cs` | Hides the line while an app is full-screen |
| `Services/Inbox.cs` | Where captured screenshots are stored |
| `Services/Settings.cs` | JSON-backed persisted settings |
| `Services/I18n.cs` | Culture selection, resource lookup |
| `Resources/Strings.*.resx` | Localized UI strings |

## Where screenshots are watched

By default Tendedero watches `%USERPROFILE%\Pictures\Screenshots`, the folder
Windows writes to when you press <kbd>Win</kbd> + <kbd>PrintScreen</kbd>.
You can add up to 3 extra folders from **Also watch folders → Add Folder…**
in the tray menu. When **Handle screenshots** is on, captures land in
`%APPDATA%\Tendedero\Screenshots` instead and the system folder is ignored.

## Contributing

Pull requests for new languages are welcome. The translation surface is
small (about 25 strings) and lives in `Resources/Strings.<culture>.resx`.
Open the English `Strings.resx` as a reference, copy it to
`Strings.<two-letter-code>.resx`, translate the `<value>` elements, and add
the culture to `SatelliteResourceLanguages` in `Tendedero.csproj`. The tray
menu will pick it up automatically.

## Credits

The original macOS app was designed and built by
[Alejandro Buján](https://alejandrobujan.com). This Windows port is a
community contribution and is not affiliated with the original project.
The code is MIT licensed; the Tendedero name and icon are not, so forks
need their own — see [LICENSE](LICENSE).

## License

MIT — see [LICENSE](LICENSE).
