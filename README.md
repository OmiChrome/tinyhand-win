<div align="center">

<img src="./.github/logo.png" width="96" alt="TinyHand logo" />

# TinyHand

[![Latest release](https://img.shields.io/github/v/release/OmiChrome/tinyhand-win?display_name=tag&sort=semver&cacheSeconds=3600)](https://github.com/OmiChrome/tinyhand-win/releases/latest)
[![Platform: Windows](https://img.shields.io/badge/platform-Windows-lightgrey)](https://github.com/OmiChrome/tinyhand-win/releases/latest)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](./LICENSE)

A tiny hand on a stick to point at things on your screen.<br />
Windows port of [nilbuild/tinyhand](https://github.com/nilbuild/tinyhand).

<img src="./.github/banner.jpg" alt="TinyHand: a tiny hand for Windows" />

[![Download for Windows](https://img.shields.io/badge/Download_for_Windows-0078D4?style=for-the-badge&logo=windows&logoColor=white)](https://github.com/OmiChrome/tinyhand-win/releases/latest/download/TinyHand-win-Setup.exe)

Works on Windows 10 and 11 (64-bit).

## How to use

| | |
|---|---|
| Click the tray icon or press **Ctrl+Alt+H** | Turn it on or off |
| Right-click the tray icon | Open the menu and settings |
| Click | Tap without clicking what's underneath |
| Click and drag | Draw on the screen |
| Hold **Alt** and click | Click normally |
| **Ctrl+Alt+X** | Clear your drawing |
| **Esc** | Clear everything and turn it off |

When it's on, a small toolbar at the bottom of the screen lets you<br />
switch between 36 hands and a laser dot, change the ink colour, mute the sound, or close it.

## Build it yourself

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download), then run:

`git clone --recurse-submodules https://github.com/OmiChrome/tinyhand-win`<br />
`dotnet run --project tinyhand-win/src/TinyHand`

The original app lives in the `tinyhand` folder.<br />
Run `scripts/sync-upstream.ps1` to pull its latest changes.<br />
Change `VERSION` on `main` to publish a new release.

## Credits

Based on [TinyHand](https://github.com/nilbuild/tinyhand) by [nilbuild](https://github.com/nilbuild).<br />
The hand images and sounds come from the original project.

## License

MIT. See [LICENSE](LICENSE).

</div>
