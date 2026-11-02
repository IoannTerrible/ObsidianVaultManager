---
type: index
status: active
priority: medium
tags: [service, core]
aliases: [VaultManager.Core.Sync]
summary: "SettingsSync: a three-way compare of two vaults' settings against the hash they last agreed on."
related:
  - "[[Vault Manager Architecture]]"
  - "[[Changes]]"
  - "[[Store]]"
created: 2026-11-02
updated: 2026-11-02
---

# Sync

## Purpose

Compares the settings of two vaults against the hash they last agreed on, so the choice between copying and leaving a file comes from history instead of a blind overwrite. It syncs settings between vaults on one PC. It is not for syncing notes between devices.

## Responsibilities

- List candidate files by category.
- Decide per file what should happen.
- Turn a decision into a `CopySettingsFile` line. The write itself happens in [[Changes]].

## Dependencies

[[Store]] for sync bases. [[Obsidian]] for `SecretScanner` and `VaultLayout`.

## Key Types

`SettingsSync` (`Preview`, `Decide`, `ToChange`), `SettingsCategory`, `SyncDecision`, `SyncEntry`.

## Details

### Categories

| Category | Files under `.obsidian` |
| :-- | :-- |
| `Hotkeys` | `hotkeys.json` |
| `Editor` | `app.json` |
| `CorePlugins` | `core-plugins.json` |
| `Appearance` | `appearance.json`, `themes/`, `snippets/` |
| `PluginSettings` | `plugins/<id>/data.json`, only where the plugin exists in the target. Never `obsidian-git`, whose settings point at one repository |

The Settings sync screen starts with every category ticked except `PluginSettings`: plugin settings hold keys and vault-specific paths, so they travel only when asked for.

### Decisions

Checked in this order:

| Situation | Decision |
| :-- | :-- |
| Source and target are the same | `Same` |
| Source holds a live secret | `BlockedSecret` |
| Target has no such file | `Copy` |
| They differ and have no base | `Conflict`: a person has to pick |
| Target still equals the base, so only the source moved | `Copy` |
| Source still equals the base, so only the target moved | `KeepTarget`: this is its own edit |
| Both moved | `Conflict` |

### Answering in the window

The screen opens from a vault page, through `Copy settings from...` in its menu. That vault is the fixed target, the person picks the source, and `Back` returns to the page.

`SettingsSyncViewModel` shows the decisions from Core and asks only where a person must choose. Only online vaults can be picked, and a Compare answer for vaults no longer selected is thrown away.

| Decision | What the line offers |
| :-- | :-- |
| `Copy` | A checkbox, ticked at the start |
| `Conflict` | `Take source` and `Keep target`, with neither chosen at the start |
| `Same`, `KeepTarget`, `BlockedSecret` | Nothing to choose |

The action button reads `Copy {0} files`, or `Save choices` when the only answers are `Keep target`. It acts on each answer at once, and after a copy the table is compared again so it shows what is true now:

- **Take source** runs a `CopySettingsFile` like any `Copy`. It can be undone, and it sets the base when it lands.
- **Keep target** writes no line. It sets the base to the source's hash in `sync_bases`, with no undo record. The vault's files stay untouched. The next Compare reads `KeepTarget` until the source moves, and then both have moved, so the file is a `Conflict` again.
- **No answer** runs nothing, and the conflict is asked again at the next Compare.

### Behaviour worth knowing

- Deletions do not travel.
- The base is written only when a copy lands. `Same` does not write one, so two vaults that agree with no earlier sync still have no base, and a later difference is a `Conflict`.
- A base is stored per ordered pair `(source, target, path)` and keyed by vault path, not vault id. Syncing the other way round starts with no base, and moving a vault loses its bases.
- Only the last agreed hash is kept. The changelog's "sync history" is this one value, not a list.
- Undo restores the file and the sync history together. The undo record keeps the base from before the copy (`PreviousSyncBase`), and `RestoreSyncBase` puts it back or deletes it if there was none. Otherwise the next Compare would read the restored file as the target's own edit. An undo refused because the file changed since the line ran leaves both alone.
- `RunSettings` checks the hashes and the secret scan again when the line runs.

## Notable Decisions

None recorded yet. The secret rule comes from the "Plugin settings stay behind by default" guardrail in `CLAUDE.md`.

## Links

- [[Changes]]
- [[Store]]
