---
type: index
status: active
priority: high
tags: [service, core]
aliases: [VaultManager.Core.Changes]
summary: "ChangeItem records and ChangeExecutor: gate, apply, snapshot and undo, line by line."
related:
  - "[[Vault Manager Architecture]]"
  - "[[Store]]"
  - "[[Obsidian]]"
  - "[[Copies not symlinks]]"
created: 2026-11-02
updated: 2026-11-02
---

# Changes

## Purpose

Every write to a vault goes through here. A line either lands whole or leaves the vault as it was, and each line reports for itself, so one failed drive does not hide what happened to the rest.

## Responsibilities

- Describe each possible write as a `ChangeItem` record that touches nothing until run.
- Gate each line on the state of its vault.
- Refuse a line when the source or target changed since it was queued.
- Save a copy first and record what it takes to put the line back.
- Refuse an undo that would destroy newer work.

## Dependencies

[[Obsidian]], [[Profiles]] (`VaultBuilder`), [[Catalog]] (downloads), [[Store]] (undo records, sync bases), `Foundation`.

## Key Types

`ChangeItem`, `ChangeItemJson`, `ChangeExecutor`, `ChangeResult`, `ChangeStatus`, `ChangeNote`, `UndoRecord`, `CreatedFile`, `ApplyReport`.

## Details

### Line kinds

| Line | Writes | Undo |
| :-- | :-- | :-- |
| `CopyPlugin` | Plugin folder from another vault, enabled in the list | Restores the snapshot and the listed state, or removes a new plugin |
| `DownloadPlugin` | Plugin folder from a GitHub release, enabled in the list | Same |
| `SetPluginEnabled` | `community-plugins.json` | Restores the listed state, with no check for a later change |
| `CopySettingsFile` | One file under `.obsidian`, then the sync base | Restores the snapshot or deletes a new file, and restores the sync base |
| `AddProfileParts` | Folders, folder notes, vault map, templates. Existing notes stay | Deletes what it created and nobody edited since; folders only if empty |
| `CreateVault` | A new vault folder. Refused if the folder holds anything or a given plugin source is gone | None |
| `RegisterVault` | Entry in `obsidian.json`. Needs an existing `.obsidian` (`SourceMissing`) | None |

A `RegisterVault` does not follow a failed `CreateVault` for that path in the same run: it fails with the create's failure, so someone else's folder never becomes a vault.

### Results

| Status | When |
| :-- | :-- |
| `Done` | Landed. `SettingsSkippedSecret` notes that settings stayed behind because they held a key |
| `Waiting` | Gate said `VaultOffline`, `VaultOpen` or `ObsidianRunning` |
| `NeedsReview` | `TargetChanged` or `SourceChanged` since the line was queued |
| `Failed` | Any other refusal, `WriteFailed` on an I/O error, `DownloadFailed` on an HTTP error or timeout, `RecoveryNeeded`, `Unexpected` |

`ApplyAsync` saves undo records in a `finally`, so lines that already ran stay undoable after a cancel. An `HttpClient` timeout is not a cancel: it arrives as `TaskCanceledException` with none requested, its line fails with `DownloadFailed`, and the run goes on. The window passes no token to `ApplyAsync`, so only tests cancel a run today.

`Gate` is public, so the window can ask about a waiting line without running it. [[Vault Manager Architecture]] says how a `Waiting` line finishes by itself.

### Stored form

`ChangeItemJson` is the form a waiting line takes in `pending_changes`. Each record is written by hand under its own `kind` name, so the text can never choose a type and renaming a class cannot strand a line. `TryParse` returns null for damaged text, an unknown kind or a profile the validator refuses, and such a line is not run.

### Plugin install

1. Compare the target's `main.js` hash with the one queued; refuse on a difference.
2. Refuse if the enabled list is unreadable.
3. Snapshot the existing folder.
4. Fill a staging folder. A copy skips `.git*` and `node_modules`, and is verified by the `main.js` hash. A download must carry a manifest with the expected plugin id and version, or it fails with `VerifyFailed`.
5. Keep the target's `data.json` unless the line asks to copy settings and the source holds no key.
6. Carry over everything but `BuildFiles`: top-level `main.js`, `manifest.json`, `styles.css` and `data.json`. A plugin's history or a developer's `.git` stays, unless the new build has a file at that path. An old `styles.css` the new build lacks is gone.
7. Move the old folder aside, move the new one in, enable it in the list. On failure, the old folder goes back.

Plugin settings stay behind by default: they hold keys and vault-specific paths.

### When the old folder cannot go back

If a swap fails and `TryPutBack` fails too, the line fails with `RecoveryNeeded`. Staging stays at `.obsidian/.vault-manager-staging/<guid>/`, with the old copy in `old`, and the failure text names that folder. The snapshot stays too: with no other undo record in the run nothing deletes it, and otherwise it lives until that operation's Undo finishes or it is pruned. The window has no repair step, so recovery is by hand.

### Undo

`UndoLast` walks the newest operation's lines, newest first, under the same gate as a run.

| Outcome | Reasons |
| :-- | :-- |
| Kept for the next Undo | `VaultOffline`, `VaultOpen`, `ListUnreadable`, `RecoveryNeeded`, `WriteFailed` |
| Refused and dropped, disk untouched | `TargetChanged`, `SourceMissing` (snapshot gone) |

When no line remains, the operation is marked undone and its snapshot folder is deleted.

- **Plugin.** `main.js` must still match `AfterHash`. A snapshot comes back through the same staged swap as an install, and a `data.json` changed since the line ran (`AfterDataHash`) is carried over it. Other files written since then are lost. A plugin with no snapshot was new, so its folder is deleted, and a `data.json` changed since is first copied to `snapshots/undone/<time>-<id>-data.json`. Nothing prunes that folder, and the window does not mention the copy.
- **Settings.** The file must still match `AfterHash`. The sync base then goes back too (`RestoreSyncBase`). See [[Sync]].
- **Parts.** Each `CreatedFile` has a hash. An unchanged file is deleted and an edited one stays. If anything was kept, the line reports `TargetChanged` after removing the rest.
- **Toggle.** It has no after-state, so it sets the listed state back whatever happened since.

## Notable Decisions

- [[Copies not symlinks]]
- [[Registration waits while Obsidian runs]]
- [[Run on click instead of plan then Apply]]

## Worth checking

From reading the code, not run.

- A line released from waiting keeps its `pending_changes` row until its result arrives. If the app ends in between, the line is read back as waiting and runs again. Lines with a hash should report `NeedsReview` then, and a line that wrote without saving its result would have no undo record.
- A `RecoveryNeeded` undo is kept for a retry, but its target folder is then gone. The next Undo should read `TargetChanged`, drop the line and delete the snapshot folder. Staging still holds the files.
- No test drives `RecoveryNeeded` through a swap, only `TryPutBack` alone. The `HttpClient` timeout path has no test either.

## Links

- [[Vault Manager Architecture]]
- [[Sync]] produces `CopySettingsFile` lines.
