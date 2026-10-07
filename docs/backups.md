# Backups, Export & Restore

## What a backup is

One ZIP file. Inside:

```
TaskbarGroups-backup-<timestamp>[-reason].zip
├── backup.json                 manifest: app version, schema version, counts
├── TaskbarGroups.db            consistent SQLite snapshot
├── TaskbarGroups.export.json   full export of every group, workspace, setting
└── Icons/*.png                 the icon cache at backup time
```

The database snapshot is taken with SQLite's online backup API, so it is
consistent even if the app is running and WAL pages are outstanding. If the
online API fails, the file itself is copied instead, with the same result.

## Creating a backup

From the app: Diagnostics → Backup → "Create backup". Backups land in
`Backups/`. Each one is validated at creation time, so a corrupt backup fails
immediately rather than at restore time.

The setting `AutomaticBackupIntervalHours` controls the rolling backup the
app takes on exit; `BackupRotationCount` keeps the newest N and deletes the
rest. `PruneOldBackups` never deletes the newest one.

## Restoring

Restore never overwrites the live database blindly:

1. The current `TaskbarGroups.db` is copied to
   `Backups/pre-restore-<timestamp>.db`.
2. The archive is validated. If it has no database and no export, it is
   rejected.
3. If the archive has a JSON export, that export is replayed into the live
   database. The archive's database file is not copied over it, because the
   export is the safer of the two to apply.
4. If the archive has no export, its database file replaces the live one.
5. The archive's `Icons/*.png` are extracted into the icon cache.

A restore that fails leaves the pre-restore copy in `Backups/`, so the
previous state is one file copy away.

## Export vs backup

Export is the same document the backup embeds, as a standalone
`TaskbarGroups.export.json` file. Use export to move data between machines or
app versions; use backup to snapshot one installation.

The JSON document is versioned (`schemaVersion`). This build reads schema 2.
A file with a higher version is read as far as possible and reported, rather
than throwing.

## Legacy migration backup

Before migration reads a 1.x `config/` tree, the whole tree is archived to
`Backups/legacy-config-<timestamp>.zip`. If that archive cannot be written,
migration stops and reports the error; nothing is imported and nothing is
modified.
