# Migration

Taskbar Groups 2.0 runs the legacy migration automatically on first start.
Nothing in this folder needs to be run by hand.

What happens, and how to inspect or roll back a migration, is documented in
[docs/migration.md](../docs/migration.md). The report the migrator writes for
every run is at `<data folder>\Logs\migration-report.json`, and the safety
archive of the 1.x tree is at
`<data folder>\Backups\legacy-config-*.zip`.
