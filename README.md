# MedResource

AtomicNotes keeps an Obsidian-style vault and per-user activity statistics in SQLite.

A regular user can read only their own activity. An administrator can read every user's activity and compare them. The role is a column on `users` (`User` or `Admin`). Queries load that role from the database for the signed-in user; they do not trust a role sent by the caller.

## Conflict decisions

| Topic | Decision |
|---|---|
| `MaxTreeDepth` | **15** (`AppConstants.MaxTreeDepth`), enforced by the `vault_items.depth` check |
| Import status | **Committed** (`Prepared`, `Staged`, `Committed`, `Failed`). `Completed` is rejected by the database |
| Settings path | `ISettingsService.SettingsFilePath`. Backup packs that file |
| Settings API | **`LoadAsync` / `SaveAsync`** |
| `BaseViewModel` | namespace **`AtomicNotes.WPF.ViewModels`** |
| Tehran clock | `TehranClockService`: `UtcToTehran` / `yyyy-MM-dd`, `PeriodicTimer`, `Start`, `StopAsync`, **`RestartAsync`** |
| Scheduler | `AutoBackupScheduler.RestartAsync` |

Schema version is 4: baseline vault and import tables, users and sessions, the `role` column, then `user_sessions` and `user_daily_stats`.
