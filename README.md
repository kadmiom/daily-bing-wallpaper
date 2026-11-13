# Daily Bing Wallpaper

A tiny Windows app (one ~24 KB `.exe`, no runtime to install) that sets your desktop
background to today's Bing image from
[github.com/niumoo/bing-wallpaper](https://github.com/niumoo/bing-wallpaper).

## Build

```
build.cmd
```

Uses the C# compiler that ships with .NET Framework — no SDK, NuGet packages or other
tooling. It writes:

- `dist\DailyBingWallpaper.exe` — the app
- `dist\DailyBingWallpaper-Setup.exe` — the installer, with the app embedded

## Install

Run `dist\DailyBingWallpaper-Setup.exe` and press **Install**. It installs for the
current user only — no administrator rights — to
`%LOCALAPPDATA%\Programs\DailyBingWallpaper\`, and adds desktop and Start menu
shortcuts.

Silent install for other machines: `DailyBingWallpaper-Setup.exe /S`

Requires .NET Framework 4.8, included with Windows 10 (1809+) and Windows 11.

## Use

- **Set wallpaper** downloads today's 4K image once and applies it.
- **Refresh at sign-in** adds a one-shot startup entry.
- **Daily at HH:mm** registers a Windows Task Scheduler task for machines left on for
days; unticking removes it.

Only the current image is kept in `%LOCALAPPDATA%\DailyBingWallpaper`; earlier images
are deleted after a new one is applied.

## Uninstall

`Uninstall.exe` in the install folder, or Settings → Apps → Daily Bing Wallpaper.
It removes the shortcuts, the Programs entry, and its sign-in/task entries. The last
wallpaper image stays on disk.
