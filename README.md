<img src="./.github/logo.png" width="80" />

# TinyHand for Windows

A tiny hand on a stick that points at things on your Windows screen.

This is a Windows 10/11 port of [TinyHand](https://github.com/nilbuild/tinyhand) by
[Kamran Ahmed](https://github.com/kamranahmedse), originally a macOS app. The original
repo is linked as a git submodule in [`upstream/`](./upstream) so its latest code is always
one command away (see [Keeping up with the original](#keeping-up-with-the-original)).

<img src="./.github/banner.jpg" alt="TinyHand: a tiny hand on a stick" />

## Install

Download `TinyHand-win-Setup.exe` from the [latest release](https://github.com/OmiChrome/tinyhand-windows/releases/latest)
and run it. TinyHand installs for your user only (no admin needed), opens at sign-in, and updates itself.

Windows may show a SmartScreen warning because the installer isn't code-signed yet:
choose **More info → Run anyway**.

## Use

| | |
|---|---|
| Click the tray icon, or **Ctrl+Alt+H** | Turn the pointer on or off |
| Right-click the tray icon | Menu and Settings |
| Click | Tap (nothing underneath gets clicked) |
| Hold and drag | Draw on the screen |
| Hold **Alt** and click | Click for real |
| **Ctrl+Alt+X** | Clear the drawing |
| **Esc** | Clear everything and turn the pointer off |

While the pointer is on, a small toolbar at the bottom of the screen lets you pick one of
36 hands or a laser dot, change the ink colour, mute the tap sound, or exit. You can also make
your own hand from any image in **Settings → Pointers → Make your own**.

Tip: Windows 11 hides new tray icons in the overflow menu (^). Drag TinyHand onto the taskbar
to keep it visible.

## How it works on Windows

| macOS original | Windows port |
|---|---|
| AppKit overlay window + Core Animation | Click-through layered window with a DirectComposition swap chain, drawn with Direct2D on the GPU |
| `CGEventTap` (needs Accessibility) | Low-level mouse hook (`WH_MOUSE_LL`), no permission needed |
| `CGDisplayHideCursor` | Every system cursor swapped for a blank one, restored on exit (a watchdog restores it even if TinyHand is killed) |
| Menu bar item | Notification area (tray) icon |
| Carbon hotkeys | `RegisterHotKey` |
| Sparkle | [Velopack](https://velopack.io) with GitHub Releases |
| SwiftUI settings | WPF with the Windows 11 Fluent theme |

Known differences:

- Clicks on windows of apps running as Administrator can't be intercepted unless TinyHand
  also runs as Administrator (a Windows security rule).
- Apps that draw their own custom cursor can still show it while the pointer is on.
- On some keyboard layouts Ctrl+Alt acts as AltGr; change the shortcuts in Settings if they clash.

## Build

Requirements: Windows 10 2004+ or Windows 11, the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
git clone --recurse-submodules https://github.com/OmiChrome/tinyhand-windows
cd tinyhand-windows
dotnet run --project src/TinyHand
```

Settings live in `%APPDATA%\TinyHand`, logs in `%LOCALAPPDATA%\TinyHand.Data\TinyHand.log`.

### Make an installer

```powershell
./scripts/pack.ps1          # builds Releases/TinyHand-win-Setup.exe with Velopack
```

Releases are built by GitHub Actions when a `v*` tag is pushed.

## Keeping up with the original

```powershell
./scripts/sync-upstream.ps1   # pulls the latest nilbuild/tinyhand into upstream/ and lists what changed
```

The Windows code lives in `src/`; the hand images and sounds in `assets/` come from the original.

## License

MIT. See [LICENSE](LICENSE). Original app © Kamran Ahmed.
