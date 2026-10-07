# Taskbar Groups 2.0

A smart taskbar launcher and Windows workspace manager.

> Ground-up modernisation of the original
> [Taskbar Groups](https://github.com/tjackenpacken/taskbar-groups) project by
> tjackenpacken, kept MIT-licensed and backward-compatible with 1.x data.

## What it does

- **Groups.** Collect shortcuts into a named group with its own icon, width,
  colour and opacity, and pin a single taskbar entry that opens a popup with
  the whole set.
- **Workspaces.** Open a set of applications and arrange their windows on the
  right monitors and halves of the screen in one click.
- **Discovery.** Scan installed applications, browsers, Steam libraries and
  Microsoft Store packages and turn what you find into groups.
- **Diagnostics.** A health check page that says why something is broken and
  what would fix it, instead of a log file and silence.
- **Backups.** One ZIP per backup, validated before it can be restored, and a
  pre-restore copy so a bad restore is never terminal.
- **Migration.** Imports a 1.x `config/` tree automatically, after archiving
  it, without touching the originals.

## Requirements

- Windows 10 (build 17763) or Windows 11, x64.
- No .NET runtime install is required: the published builds are
  self-contained.

## Download

Two editions, both from `artifacts/`:

| Edition | ZIP | Where data lives |
|---|---|---|
| Installed | `TaskbarGroups-2.0.0-win-x64.zip` | `%LOCALAPPDATA%\TaskbarGroups` |
| Portable | `TaskbarGroups-2.0.0-portable-win-x64.zip` | `Data/` beside the exe |

The portable ZIP differs only by a `portable.txt` marker file next to
`TaskbarGroups.exe`.

## Install

Portable: extract anywhere and run `TaskbarGroups.exe`.

Installed:

```powershell
unzip TaskbarGroups-2.0.0-win-x64.zip
cd TaskbarGroups
powershell -ExecutionPolicy Bypass -File ..\installer\install.ps1 -DesktopShortcut
```

`install.ps1` copies the app to `%LOCALAPPDATA%\Programs\TaskbarGroups`,
creates a Start Menu shortcut, and optionally a desktop shortcut and startup
entry. `uninstall.ps1` removes the app and shortcuts but keeps your data.

## Using it

1. Create a group, give it a name and an icon.
2. Add shortcuts by typing, browsing, drag-and-drop, or discovery.
3. Save. Exit the group editor.
4. In `Shortcuts\`, right-click the group's `.lnk` and choose
   **Pin to taskbar**. Clicking the pinned icon opens the group popup.
5. Number keys 1–0 open items, **Ctrl+Enter** opens all of them when the
   group allows it.

## Data layout

| Installed mode | Portable mode |
|---|---|
| `%LOCALAPPDATA%\TaskbarGroups\` | `<exe folder>\Data\` |

Inside the data directory:

| Path | Purpose |
|---|---|
| `TaskbarGroups.db` | SQLite database (groups, shortcuts, workspaces, settings, icon index, migration history) |
| `Icons/` | Icon cache (PNG, content-addressed) |
| `Backups/` | Backup ZIPs |
| `Logs/` | Rolling text logs and `migration-report.json` |
| `config/` | Your 1.x data, migrated once and never modified |
| `Shortcuts/` | The pinned `.lnk` shims the taskbar actually points at |
| `Temp/` | Staging space for migrations and restores |

The legacy `config/` folder is read by the migrator on first run, archived to
`Backups/legacy-config-*.zip`, and never written to again. Everything the app
needs to keep working is in SQLite.

## Building from source

```powershell
dotnet build TaskbarGroups.sln -c Release
dotnet test TaskbarGroups.sln -c Release
powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
powershell -ExecutionPolicy Bypass -File scripts\pack-portable.ps1
powershell -ExecutionPolicy Bypass -File scripts\pack-install.ps1
```

Requires the .NET 8 SDK (`net8.0-windows10.0.19041.0`). The SDK is on
`PATH` in CI images and from `dotnet`/`dotnet.ps1` locally.

## Known limitations

- **Taskbar pin automation.** Windows 11 does not expose a public API for
  pinning; the app still creates the shim `.lnk` files in `Shortcuts/` and the
  user pins once. A future release may add a documented, supported pinning
  path if one appears.
- **Microsoft Store (`shell:AppsFolder\...`) shortcuts** are launched through
  `explorer.exe` and cannot be monitored for a window title in the way a real
  executable can, so workspace placement is best-effort for those items.
- **Portable mode** writing a group pins from a path under `Temp/`; moving the
  whole portable folder invalidates pinned taskbar entries until they are
  re-pinned.

## Documentation

- [docs/migration.md](docs/migration.md) — what happens to 1.x data
- [docs/architecture.md](docs/architecture.md) — the project layout and why
- [docs/troubleshooting.md](docs/troubleshooting.md) — common problems and fixes
- [docs/backups.md](docs/backups.md) — backup/restore/export formats

## License

MIT. See [LICENSE](LICENSE). Original project copyright tjackenpacken; this
modernisation is a derivative work under the same terms.
