---
type: index
status: active
priority: medium
tags: [service, core]
aliases: [VaultManager.Core.Profiles]
summary: "Vault recipes as plain files: validator, store, import and export, audit against a vault, and VaultBuilder."
related:
  - "[[Vault Manager Architecture]]"
  - "[[Changes]]"
  - "[[Profiles as files, everything else in SQLite]]"
created: 2026-11-02
updated: 2026-11-02
---

# Profiles

## Purpose

A profile is a vault recipe kept as a plain file, so it can live in git, be sent to someone or be edited by hand. It never holds plugin settings or anything from a note.

## Responsibilities

- Define the format and check every profile as untrusted input.
- Store profiles in one folder, plus one read-only built-in.
- Import and export without overwriting.
- Audit a vault against a profile.
- Write a profile's parts into a folder.

## Dependencies

[[Obsidian]] for `SecretScanner`, `VaultLayout` and `CommunityPluginList`. `Foundation` for atomic writes.

## Key Types

| Type | What it does |
| :-- | :-- |
| `Profile` | `Format`, `Id`, `Name`, `Description`, `Plugins`, `Folders`, `FolderNotes`, `VaultMap`, `Settings`, `Templates` |
| `ProfileValidator` | Returns a `ProfileProblem`. An imported file decides where files get written |
| `ProfileFile` | `.vaultprofile.json`, 1 MB at most, `TypeNameHandling` off |
| `ProfileStore` | List, save, delete, duplicate, import, export |
| `ProfileAudit` | Missing, present and other plugins; missing and present folders; a missing vault map |
| `VaultBuilder` | `Create` for a new vault, `AddParts` for an existing one |

## Details

### What the validator refuses

- A newer format than this build, or a missing name or id.
- A null `Plugins`, `Folders`, `Settings` or `Templates`, reported as `NotJson`: a file can say null where the type promises a list.
- A profile id outside the plugin id pattern, also reported as `NotJson`: the id becomes a file name, so `../../evil` never gets that far.
- A plugin id outside `^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$`, or a null entry in the list.
- A folder that is absolute, climbs with `..`, uses a reserved Windows name, starts with `.obsidian`, or is null.
- A settings file other than `app.json`, `appearance.json`, `core-plugins.json`, `hotkeys.json` and `graph.json`, or one with a null value.
- A template that is not a safe `.md` name, or one with a null value.
- Settings holding a live secret, inside lists and objects too. See [[Obsidian]].

### Store

- The built-in `tech-tea-studio` is an embedded resource. It cannot be saved or deleted, only duplicated.
- An import never overwrites: a taken name gets a number and a new id.
- A broken file in the folder is skipped so it does not hide the others. Import reports problems, while a hand-edited file shows up by its absence.

### Building

- `AddParts` only adds, and returns what it created so an undo removes only that. `AddProfileParts` does not touch plugins or settings. If it fails partway, it removes the files and folders it made and rethrows, because a failed line offers no undo.
- `Create` first checks that every plugin source it was given still exists, and fails with `SourceMissing` before creating anything: a vanished drive must not make the vault a silent half. It then writes settings, copies plugins, writes the enabled list and calls `AddParts`.
- If `Create` fails while filling the folder, it clears what it built and leaves an empty folder if one was there before, so a retry does not fail as "already holds files".
- A profile plugin with no source given is still skipped with no error, and it does not reach the enabled list. The New vault screen names these before the line is queued: `Not on this PC, so left out: {0}`.
- Plugin settings stay behind on purpose: they can hold keys and paths of the source vault.

### Built-in profile

Eight plugins (Dataview, Folder notes, Metadata Menu, Git, Linter, Omnisearch, Templater, Waypoint), the studio folders, folder notes, a vault map and five templates. It repeats the global studio rules by hand. `The_built_in_profile_matches_the_studio_rules` pins the plugin list.

### In the window

The Profiles screen gives one plain result per vault, such as `1 folder missing`, `matches` or `not using this profile`. A click opens the details and the action, and the editor is behind `Edit`. The Plugins home and the vault page use the same wording for what is short.

## Notable Decisions

- [[Profiles as files, everything else in SQLite]]

## Worth checking

From reading the code, not run. `ReservedNames` lists `COM1` to `COM4` and `LPT1` to `LPT3`, so a folder named `COM5` or `LPT4` passes the validator, though Windows reserves those names too.

## Links

- [[Changes]] runs `AddProfileParts` and `CreateVault`. A `RegisterVault` does not follow a failed `CreateVault`.
- [[Store]] holds which profile a vault was assigned, in `vault_profiles`.
