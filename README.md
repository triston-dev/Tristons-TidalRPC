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

- 🎵 **Now-playing → Discord** — title, artist, and a live progress bar derived from the SMTC timeline.
- 🎯 **Tidal only** — matches Tidal explicitly by its `SourceAppUserModelId` (`com.squirrel.TIDAL.TIDAL`) so other media apps can't hijack your presence.
- 🖼️ **Album art** — resolves a hosted cover-art URL (iTunes Search API, then Deezer as a fallback), cached per track. Falls back to a bundled logo asset.
- ⏸️ **Paused-aware** — shows a distinct paused indicator instead of a running timer, and clears presence when Tidal is closed or stopped.
- 🔌 **Discord-optional** — if Discord isn't running yet, it connects automatically once you launch it.
- 🧰 **Tray menu** — toggle RPC, hide presence during ads, run at Windows startup, check for updates, About, and Quit. All toggles persist between runs.
- 🪶 **Background-friendly** — no console window, single-instance guard, clean shutdown, and a diagnostic log file.

---

## Prerequisites

- **Windows 10 (build 19041 / version 2004) or newer** — required for the SMTC APIs.
- **[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)** to build (an end user running a self-contained publish needs nothing installed).
- The **Tidal desktop app** and the **Discord desktop app**.

---

## Build & run

```powershell
# from the repository root
dotnet build "Tristons TidalRPC.sln" -c Release

# run it
dotnet run --project "src\Tristons TidalRPC.csproj" -c Release
```

The app starts silently in the system tray (bottom-right notification area).
Right-click the tray icon for the menu; double-click it to see the current track.

> **Note:** The first thing the app does on startup is log the AUMIDs of every
> SMTC session to the log file (see below), so you can always confirm the exact
> string it's matching Tidal against.

### Confirming the Tidal AUMID (the SMTC probe)

A tiny standalone probe lives in [`probe/`](probe/). It enumerates every media
session and prints each `SourceAppUserModelId` plus its current metadata — handy
for verifying that Tidal is readable before wiring up Discord:

```powershell
dotnet run --project "probe\SmtcProbe.csproj" -c Release
```

On this project's reference machine Tidal reports **`com.squirrel.TIDAL.TIDAL`**.
(Tidal also reports an empty *album* over SMTC, which is why album art is looked
up by **artist + title**.)

---

## Setting / changing the Discord Application ID

The presence is published under a Discord "application," identified by its ID.
The ID is a clearly-marked constant at the top of the presence manager:

**[`src/DiscordPresenceManager.cs`](src/DiscordPresenceManager.cs)**

```csharp
// =========================================================================
//  Discord Application ID — change this to point at your own Discord app.
// =========================================================================
public const string DiscordApplicationId = "1521933416495321321";
```

To use your own:

1. Go to the **[Discord Developer Portal](https://discord.com/developers/applications)** → **New Application**.
2. Copy the **Application ID** and paste it over the constant above.
3. *(Optional, for the fallback logo)* Under **Rich Presence → Art Assets**, upload:
   - an image named **`tidal_logo`** (the large-image fallback when cover art can't be resolved), and
   - small status icons named **`play`** and **`pause`**.
   These asset key names are also constants at the top of `DiscordPresenceManager.cs`.

> Hosted cover-art URLs are passed straight through as the large image (modern
> Discord clients proxy external image URLs), so per-track art works without
> uploading anything. The uploaded `tidal_logo` asset is only the fallback.

---

## Producing a self-contained single-file exe

Build a single `.exe` that runs on a machine with **no .NET installed**, with the
tray icon embedded:

```powershell
dotnet publish "src\Tristons TidalRPC.csproj" `
  -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true
```

The result is at:

```
src\bin\Release\net8.0-windows10.0.19041.0\win-x64\publish\Tristons TidalRPC.exe
```

Double-click it (or drop it in your Startup folder / enable **Run at Windows
startup** from the tray menu). The app icon and metadata (author **triston-dev**,
product **Triston's TidalRPC**) are baked into the file's properties.

---

## Where things are stored

| What | Location |
| --- | --- |
| Config (toggles) | `%APPDATA%\Tristons TidalRPC\config.json` |
| Diagnostic log | `%APPDATA%\Tristons TidalRPC\log.txt` |
| Run-at-startup entry | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run` → `Tristons TidalRPC` |

Set `"Verbose": true` in `config.json` for detailed (DEBUG-level) logging.

---

## Project layout

```
Tristons TidalRPC.sln
src/
  Tristons TidalRPC.csproj      # WinExe, net8.0-windows10.0.19041.0
  Program.cs                    # entry point, single-instance mutex, exception handling
  TrayApp.cs                    # NotifyIcon + context menu + orchestration
  SmtcListener.cs               # SMTC session manager, Tidal AUMID match, debounced events
  DiscordPresenceManager.cs     # discord-rpc-csharp wrapper (App ID constant lives here)
  AlbumArtResolver.cs           # iTunes / Deezer cover-art lookup + cache
  TrackInfo.cs                  # immutable now-playing snapshot
  Config.cs                     # JSON settings in %APPDATA%
  Logger.cs                     # file logger with a verbosity switch
  StartupManager.cs             # HKCU Run-key helper
  Assets/app.ico                # embedded tray / application icon
probe/
  SmtcProbe.csproj              # standalone AUMID/metadata probe
  Program.cs
```

---

## How it works (the non-obvious bits)

- **SMTC** (`Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager`)
  is the same system that powers the Windows media flyout. We subscribe to
  `SessionsChanged` and, on the Tidal session, to `MediaPropertiesChanged`,
  `PlaybackInfoChanged`, and `TimelinePropertiesChanged` — no tight polling.
  Bursts of events (rapid skips/seeks) are **debounced** into a single update.
- WinRT change events fire on thread-pool threads; everything is marshalled back
  onto the WinForms UI thread so WinRT session objects are only touched from one
  thread.
- Discord **timestamps** are computed from the SMTC timeline: the reported
  position is extrapolated to "now" using `LastUpdatedTime`, so the progress bar
  stays accurate across seeks. Paused tracks omit timestamps entirely.

---

## Author / Credits

Created by **triston-dev** — <https://github.com/triston-dev>

Built on:

- [discord-rpc-csharp](https://github.com/Lachee/discord-rpc-csharp) (Discord IPC / Rich Presence)
- Windows **System Media Transport Controls** (`Windows.Media.Control`)
- Cover art via the **iTunes Search API** and **Deezer API**

This is an original, independent implementation.

## License

[MIT](LICENSE) © 2026 triston-dev
