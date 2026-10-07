# Troubleshooting

## The app will not start

Check `Logs\app-*.log` in the data directory. The most common cause is a
database from a much newer build; 2.0 refuses to open it rather than risking
your data. Restore the latest backup or remove the file to start clean.

## A group popup opens on the wrong monitor

Set the group's monitor preference in the group editor. `FollowCursor` opens
on the monitor under the mouse, `Primary` on the primary display, and
`Specific` on the named monitor. Multi-monitor placement is computed from
physical rectangles returned by Windows, so on some mixed-DPI setups the
popup can be offset by one monitor until you click it twice; this is usually
the OS reporting a stale work area and is fixed by re-opening the group.

## Icons show the placeholder

The target no longer exists, the shell cannot read its icon, or the icon
cache folder is unreadable. Right-click the item, force a refresh, and check
`Logs`. If a specific extension shows placeholders for everything, that
extension's file-type handler is broken; reassociating it in Windows fixes
it.

## My pinned taskbar entries stopped working

Pins point at `Shortcuts\<Group>.lnk`. If the data folder moved and you did
not re-pin, those links are stale. Re-pin from the new location, or, for a
portable copy, keep the folder where it was and move the whole folder instead
of just its contents.

## Migration did not import my 1.x groups

Look at `Logs\migration-report.json`. The usual causes:

- `config/` lives somewhere other than the exe folder or
  `%LOCALAPPDATA%\TaskbarGroups`. Set the portable marker or move the folder.
- `ObjectData.xml` files are malformed. Each failure is listed under
  `entries` with the group name and the exception.
- The legacy backup archive could not be written. The report's `backup`
  stage holds the error. Fix the path and start the app again.

## My shortcut to a Store app says "missing"

The package's AppUserModelID changed after a Store update. Re-add the item
from discovery; the classification still recognises `shell:AppsFolder\...`
targets and `*!App` AppUserModelIDs.

## Antivirus / SmartScreen

The self-contained publish output can be flagged on first run. The installer
is not signed with an EV certificate; if SmartScreen blocks it, choose
"More info → Run anyway". This will remain so until the project has a code
signing budget, which is a project-funding decision, not a technical one.

## Uninstalling

`uninstall.ps1` removes the app and shortcuts and keeps your data. Delete the
data directory manually if you want a clean slate; deleting it does not
uninstall the app.
