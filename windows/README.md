# Quota Arc for Windows

Quota Arc pins a small black notch to a screen edge, showing how much of each
coding assistant's usage limit you have burned.

This build covers **Claude Code**, **Cursor**, **Codex**, and **Antigravity**.
GLM, Grok, and OpenCode stay on the Mac app for now.

## Requirements

- Windows 10 1809 or later
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Build and run

From the repository root:

```powershell
dotnet build windows/QuotaArc.sln
dotnet run --project windows/QuotaArc/QuotaArc.csproj
```

Sample rings instead of live readings:

```powershell
$env:QUOTAARC_DEMO = "1"
dotnet run --project windows/QuotaArc/QuotaArc.csproj
```

Tests:

```powershell
dotnet test windows/QuotaArc.sln
```

A Release exe lands at `windows/QuotaArc/bin/Release/net8.0-windows/QuotaArc.exe`.

## Installer

Build a Start Menu installer (MSI) and a portable zip. The MSI is self-contained,
so the laptop does not need the .NET SDK:

```powershell
powershell -File windows/installer/build.ps1
```

The files land in `windows/dist/`:

- `QuotaArc-1.6.1.msi` - double-click to install (Program Files, Start Menu, desktop shortcut). Installing again replaces the copy already on the machine. The last page has **Run Quota Arc**.
- `QuotaArc-1.6.1-portable-win-x64.zip` - unzip and run `QuotaArc.exe`

Pushes to `main` attach the Windows installer and the Mac disk image to
[Releases](https://github.com/vaiibhavkale/Quota-Arc/releases).

The Start Menu, desktop, exe, tray, and Apps list all use `assets/windows/QuotaArc.ico`,
built from `assets/windows/QuotaArc-logo.jpg`.

## What it matches

- Stack-space layout (`along` / `across`) with `NotchPlacement` as the only
  mapping onto screen coordinates
- Inverse-rounded pill welded to the chosen edge, following the work area so a
  bottom notch sits on the taskbar
- Unfold / fold springs, staggered cells, tooltip glide, settings orb
- Click-through overlay except over the notch, tooltip, and orb
- Right-click: keep open, refresh now, quit
- Opening the notch re-reads live usage (same 15s floor as Mac 1.6.0)

## 1.6.0 customization

Settings matches the Mac 1.6.0 knobs, on the existing dark Quota Arc window:

- Reorder connected rings (Move up / Move down)
- Displays: one notch or a notch on every screen
- Pin that single notch to a named display, or follow the foreground window
- Accent colour for the positive ring state (warnings stay amber/red)
- Reset time as a date or a countdown
- Alt-drag the pill along its edge (remembered per edge)
- Threshold alerts at 80% and 100%, mute per provider
- When a session ends: peek the notch, optional sound, how long it stays open
- Sub-1% usage shows `<0.1%` instead of `0%`

GitHub Copilot and Gemini API rings stay on the Mac app for now.

## Windows-specific sources

| Mac | Windows |
|---|---|
| Dock `visibleFrame` | Taskbar work area |
| Keychain | Credential Manager, then local files |
| Menu bar / Dock icon | Tray / taskbar / hidden |
| Sparkle | No auto-update in this build |

Claude's token is read from Credential Manager (`Claude Code-credentials`) or
`%USERPROFILE%\.claude\.credentials.json`. Cursor uses
`%APPDATA%\Cursor\User\globalStorage\state.vscdb`. Codex uses
`%USERPROFILE%\.codex\auth.json`. Antigravity uses Credential Manager
(`gemini:antigravity`) or `%USERPROFILE%\.gemini\antigravity\oauth.json`.

There is no hardware-notch merge on Windows. Session activity for Cursor,
Codex, and Antigravity is "that process is running"; Claude still reads its
session JSON files.

Settings persist under `HKCU\Software\QuotaArc`.
