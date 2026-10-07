# Architecture

```
TaskbarGroups.sln
├── src/
│   ├── TaskbarGroups.Core/          net8.0, no Windows/UI deps
│   │   ├── Enums/                   ShortcutType, GroupTheme, PlacementAnchor, ...
│   │   ├── Models/                  Group, ShortcutItem, Workspace, MonitorInfo, ...
│   │   ├── Interfaces/              IGroupRepository, ILauncherResolver, IIconCache, ...
│   │   ├── Validation/              GroupNameValidator, LinkResolver hook, ...
│   │   └── Constants/AppContract.cs AppUserModelID scheme, folder names
│   │
│   ├── TaskbarGroups.Windows/       net8.0-windows10.0.19041.0
│   │   ├── Monitor/                 MonitorService + PopupPlacementCalculator
│   │   ├── Shell/                   ShellLinkReader, ShellIconExtractor, IShellGate
│   │   ├── Dpi/                     DPI-aware bitmap scaling
│   │   ├── WindowManagement/        WindowManager, WindowPlacementCalculator
│   │   ├── Startup/                 StartupService (registry Run key)
│   │   └── Taskbar/                 TaskbarService (shim .lnk writing)
│   │
│   ├── TaskbarGroups.Launchers/      Target classification, scripted-item detection
│   ├── TaskbarGroups.Icons/          IconCache, shell extraction, fallback icons
│   ├── TaskbarGroups.Data/           Database (SQLite schema), repositories,
│   │                                 LegacyXml reader, BackupService, ExportService
│   ├── TaskbarGroups.Groups/         LegacyMigrator, SmartGroupService
│   ├── TaskbarGroups.Workspaces/     WorkspaceService (launch then place)
│   ├── TaskbarGroups.Discovery/      App, browser, Steam, UWP, PWA locators
│   ├── TaskbarGroups.Diagnostics/    DiagnosticsService health checks
│   ├── TaskbarGroups.Updates/        UpdateService (offline-first, manifest-gated)
│   └── TaskbarGroups.App/            WinForms shell: Program, ServiceContainer,
│                                     forms, user controls, live settings
│
└── tests/                            One project per layer, xUnit + Microsoft.NET.Test.Sdk
```

## Dependency direction

`Core` is the bottom. Nothing in it references a UI technology, a database,
COM interop or the Windows API; a `Rectangle` is `System.Drawing.Rectangle`
and the icon contracts return `Bitmap`, so `System.Drawing.Common` is the only
package. Every concrete capability (SQLite, COM, the shell, monitors) lives in
a higher project and implements an interface declared in `Core`.

The composition root is `ServiceContainer.Create()` in the App project. It
builds paths, logging, the database, repositories, icon cache, monitor and
taskbar services, launcher resolution, and finally runs the legacy migration.
A failed migration is logged and reported; the app still starts.

## Key abstractions

| Interface | Role |
|---|---|
| `IGroupRepository` / `IWorkspaceRepository` | SQLite persistence for groups and workspaces |
| `ILauncherResolver` | Decide how to start a target (exe, lnk, url, folder, UWP, script) |
| `IIconCache` | PNG cache, content-addressed by icon inputs; never null, never throws |
| `IWindowManager` | Enumerate and place top-level windows |
| `ITaskbarService` | Write the per-group shim `.lnk` the taskbar pins point at |
| `IBackupService` | Create, validate, restore and prune backup ZIPs |
| `IDiagnosticsService` | Build the health report |
| `IApplicationDiscoveryService` | Enumerate candidate shortcuts from disk and packages |
| `IMonitorService` | enumerate monitors, work areas, DPI |
| `IAppLogger` | Rolling text logs |

## Data flow for a group launch

1. The taskbar entry's `.lnk` targets `TaskbarGroups.exe <GroupName>`.
2. `Program` parses the argument, opens the group popup on the monitor under
   the cursor (via `MonitorService` + `PopupPlacementCalculator`, not by
   guessing).
3. Clicking an item hands its `ShortcutItem` to `ILauncherResolver`, which
   classifies the target (exe,lnk, url, folder, UWP, script, Store) and
   starts it with the right process kind. Working directory and arguments
   travel with the item, so a shortcut never starts inside the Taskbar
   Groups folder.
4. `Ctrl+Enter` goes through the same resolver for every enabled item when
   the group allows open-all.

## Icon pipeline

`IIconCache.GetIcon(item, request)` →
- compute a SHA-256 of every input that affects the image (target, type,
  requested size),
- serve the cached PNG from `Icons/` when the fingerprint still matches,
- extract via the Windows shell (`SHGetFileInfo` / `ExtractIconEx`) on a
  miss, serialised through a gate because the shell APIs are not thread-safe,
- fall back to a neutral placeholder if extraction fails, and never cache
  the placeholder so the next attempt retries.

The extractor is serialised because an unsynchronised fan-out around
`SHGetFileInfo` fails on roughly 3 % of calls (12 of 400 at 16 threads);
with the gate it is 0 of 400.

## Popup placement

`PopupPlacementCalculator` is a pure function of
(taskbar rect, monitor rects, popup size, work areas, DPI) so the whole
monitor/DPI matrix is testable without a display. `MonitorService` supplies
the rectangles; the app never multiplies pixels itself.

## Storage

SQLite with WAL, foreign keys on, busy timeout. Groups, shortcuts, workspaces,
workspace items, window placements, icons, settings, themes and migration
history are tables. Enums and timestamps are stored as text so a raw dump is
readable; booleans as 1/0.

A backup is one ZIP containing the consistent database snapshot
(`BackupService` uses the SQLite online backup API), a full JSON export, the
icon cache, and a manifest. Restore validates, copies the live database aside
first, then replays the export (the database file is only overwritten wholesale
when the archive contains no export at all).
