# 1.x → 2.0 Migration Guide

Taskbar Groups 2.0 can read every 1.x install. You do not need to do anything
for this to happen: the first time the 2.0 executable starts, it looks for
legacy data, archives it, imports it, and leaves the originals alone.

## Where 1.x data was

```
<data folder>\config\<Group_Name>\ObjectData.xml
<data folder>\config\<Group_Name>\GroupIcon.ico
<data folder>\config\<Group_Name>\GroupImage.png
<data folder>\config\<Group_Name>\Icons\*.png
<data folder>\Shortcuts\*.lnk
<data folder>\JITComp\*
```

`<data folder>` was wherever the exe sat (portable 1.x), or
`%LOCALAPPDATA%\TaskbarGroups`. 2.0 looks in both.

## What the migrator does, in order

1. **Finds** each group folder under `config/` that contains an
   `ObjectData.xml`. Folders without one are ignored.
2. **Archives** the entire legacy tree to
   `Backups/legacy-config-<timestamp>.zip`. If that archive cannot be written,
   nothing is imported; the report says why.
3. **Reads** every `ObjectData.xml`. A file that cannot be parsed is skipped
   and recorded; the rest of the tree still imports.
4. **Classifies** each shortcut. 1.x stored only an `isWindowsApp` bit, which
   is why folder shortcuts once crashed the icon extractor: a folder is not an
   executable. 2.0 infers the real type (`Exe`, `Lnk`, `Url`, `Bat`, `Cmd`,
   `Ps1`, `Folder`, `Uwp`) from the path, so folders launch as folders.
5. **Writes** groups, shortcuts, themes and open-all flags into the new
   SQLite database, preserving names, order, arguments, working directories
   and the legacy allow-open-all setting. The legacy per-group name with
   underscores is kept as `LegacyName` so the folder can be found again.
6. **Records** the run in `MigrationHistory` and writes
   `Logs/migration-report.json` with counts, skipped items, and every legacy
   property that has no 2.0 equivalent.

## What is not migrated, and why

These are never silently dropped; each one is in
`migration-report.json` under `unsupportedFields`:

- The legacy `Icons/` cache. It is re-extracted on demand; copying it would
  only carry stale icons into the new cache.
- The legacy `JITComp/` profiles. Not used by 2.0.
- A group's `GroupImage.png` is kept if present, but the popup icon is
  re-rendered from `GroupIcon.ico` on first open.

## What is never touched

- Anything under `config/`. The migrator does not write, move or delete files
  there. The original `ObjectData.xml` stays byte-identical.
- Your pinned taskbar entries. They point at `Shortcuts/*.lnk`, which the new
  build still produces with the same naming scheme
  (`TaskbarGroups/Shortcuts/<GroupName>.lnk`), so the pins keep working.

## Re-running

Migration is idempotent. A group whose name already exists is kept as-is and
not duplicated. If the first run failed halfway, the next start retries and
the report records the new outcome.

## Rolling back

Delete the 2.0 data directory and run the 1.x exe again. The original
`config/` tree is intact, and `Backups/legacy-config-*.zip` is still there if
you want to poke at what was read.
