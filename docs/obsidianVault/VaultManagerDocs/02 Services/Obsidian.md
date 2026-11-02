---
type: index
status: active
priority: high
tags: [service, core]
aliases: [VaultManager.Core.Obsidian]
summary: "Reads Obsidian's own state: vault registry, enabled list, Restricted mode, versions, secrets and the workspace scan."
related:
  - "[[Vault Manager Architecture]]"
  - "[[Changes]]"
  - "[[Restricted mode read from browser storage]]"
  - "[[Registration waits while Obsidian runs]]"
created: 2026-11-02
updated: 2026-11-02
---

# Obsidian

## Purpose

Everything Core knows about Obsidian's files and process. It reads the disk on each scan and keeps no state of its own, because the disk is the truth about a vault.

## Responsibilities

- Read and extend Obsidian's vault list, `%APPDATA%\obsidian\obsidian.json`.
- Read and change one vault's `community-plugins.json`, refusing a file it could not read.
- Read Restricted mode from browser storage.
- Compare plugin versions as numbers and find the newest copy.
- Find settings that hold a live secret.
- Scan all vaults into one snapshot for the window.

## Dependencies

`Foundation` for atomic writes, shared reads and `ReadResult`. Newtonsoft.Json.

## Key Types

| Type | What it does |
| :-- | :-- |
| `VaultLayout` | Paths inside a vault: `.obsidian`, `plugins`, `main.js`, `manifest.json`, `data.json` |
| `IObsidianProcess` | Whether Obsidian runs. The real one looks for a process named `Obsidian` |
| `VaultRegistry` | `Read` returns entries. `Register` adds a vault, keeps every other key, and returns the existing id if the path is already listed |
| `CommunityPluginList` | The enabled list. `Set` throws `ListUnreadable` rather than rewrite a file it could not read |
| `CommunityPluginsReader` | Whether community plugins are on, from browser storage: `On`, `Off` (Restricted mode), `NeverEnabled` (no key seen), `Unknown` |
| `PluginManifest` | Reads `manifest.json`; a bad file gives null, and a field of the wrong type reads as absent |
| `PluginVersion` | Numeric compare: 2.15.2 is newer than 2.8.3. A pre-release sorts below its release |
| `SecretScanner` | Walks JSON and returns key names only |
| `WorkspaceScanner` | Produces `WorkspaceSnapshot` from `VaultState` and `PluginRow` |

## Details

- **What open means.** A vault is open only when `open` is true in `obsidian.json` and Obsidian also runs. The flag alone also marks the vault that was open when the app last closed. With Obsidian running and the list unreadable, `GateVault` counts every vault as open, because nothing proves it closed.
- **What `NeverEnabled` means.** No `enable-plugin-<vault id>` key was seen in a store that could be read. A compacted LevelDB file may compress a key so that it looks absent, so this is likely, not a fact. The window words it that way: `plugins seem to be off` on a row of the Plugins home, and `Community plugins seem to be turned off here` on the vault page, with the same hedge in Russian. The home row is shared with `Off`, so it hedges there too, while the vault page says `Community plugins are turned off here` for `Off`.
- **What a secret is.** The key name must match words such as token, password or api key, and the value must be a string of at least 8 characters, or a list or object holding one at any depth. So `"apiKeys": ["sk-..."]` and `"token": {"value": "sk-..."}` are found, while `rememberPassword` as a flag, `tokenizeUrls` and `"tokens": [1, 2, 3]` are not secrets. Unparsable settings cannot be cleared as safe and report `(unreadable)`.
- **Odd manifests.** A manifest is someone else's file. A field of the wrong type reads as absent instead of crashing a scan. A plugin whose `version` reads as absent is not a candidate for the newest copy, and the table shows it as broken.
- **What a scan finds.** `PluginRow` carries `NewestVersion` and `SourceVaultPath` (the newest copy, never the first vault), `Drifted`, and `SameVersionDifferentFiles` (same version with a different `main.js` hash).
- **One vault after another.** The scan is sequential on purpose, because external drives here drop under parallel load.
- **Offline vaults stay.** A vault whose folder is gone stays in the list with `IsOnline` false.
- **Display names.** Two folders with one name are told apart by their parent: `PC (CopyPc)`.

## Notable Decisions

- [[Restricted mode read from browser storage]]
- [[Registration waits while Obsidian runs]]

## Links

- [[Changes]] writes through these types.
- [[Sync]] and [[Profiles]] use `SecretScanner`.
