# Triston's TidalRPC

[![build](https://github.com/triston-dev/Tristons-TidalRPC/actions/workflows/build.yml/badge.svg)](https://github.com/triston-dev/Tristons-TidalRPC/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

**Discord Rich Presence for the [Tidal](https://tidal.com) desktop app — sourced directly from the Windows System Media Transport Controls (SMTC).**

By **triston-dev** — <https://github.com/triston-dev>

A lightweight Windows tray utility that watches what Tidal is playing and mirrors
it to your Discord profile as Rich Presence: track title, artist, album art, and
an accurate progress bar. No SteelSeries / GameSense, no local web server — it
reads now-playing data straight from Windows itself.

---

## Features

- 🎵 **Now-playing → Discord** — title, artist, and a live progress bar (time played / total).
- 🎯 **Tidal only** — matches Tidal explicitly, so other media apps can't hijack your presence.
- 🖼️ **Album art** — fetches hosted cover art automatically, cached per track.
- ⏸️ **Paused-aware** — shows a distinct paused indicator, and clears when Tidal is closed or stopped.
- 🔌 **Discord-optional** — connects automatically once Discord is running.
- 🪶 **Background-friendly** — no console window, single-instance guard, clean shutdown.

---

## Requirements

- **Windows 10 (build 19041 / version 2004) or newer** — required for the SMTC APIs.
- The **Tidal desktop app** and the **Discord desktop app**.
- **[.NET 8](https://dotnet.microsoft.com/download/dotnet/8.0)** — only needed if you run from source (below).

---

## Install & run

Run **`Tristons TidalRPC.exe`** — there's no installer and no window; it starts
straight in your system tray (bottom-right notification area).

Prefer to run from source? From the repository root:

```powershell
dotnet run --project "src\Tristons TidalRPC.csproj" -c Release
```

- **Right-click** the tray icon for the menu.
- **Double-click** it to see the current track.

---

## Tray menu

- **Rich Presence enabled** — master on/off switch for the Discord presence.
- **Hide presence during ads** — blanks your presence while a Tidal ad is playing.
- **Run at Windows startup** — launch automatically when you sign in.
- **Check for updates** — opens the project page.
- **About** — author and project link.
- **Quit** — exits and clears your Discord presence.

All toggles are remembered between runs.

---

## Where your settings live

| What | Location |
| --- | --- |
| Config (toggles) | `%APPDATA%\Tristons TidalRPC\config.json` |
| Diagnostic log | `%APPDATA%\Tristons TidalRPC\log.txt` |
| Run-at-startup entry | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → `Tristons TidalRPC` |

Set `"Verbose": true` in `config.json` if you ever need detailed logging.

---

## Author / Credits

Created by **triston-dev** — <https://github.com/triston-dev>

Built on:

- [discord-rpc-csharp](https://github.com/Lachee/discord-rpc-csharp) (Discord Rich Presence)
- Windows **System Media Transport Controls** (`Windows.Media.Control`)
- Cover art via the **iTunes Search API** and **Deezer API**

This is an original, independent implementation.

## License

[MIT](LICENSE) © 2026 triston-dev
