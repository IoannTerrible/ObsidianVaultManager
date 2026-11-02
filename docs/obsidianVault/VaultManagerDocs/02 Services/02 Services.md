---
type: index
status: active
priority: high
tags: [services]
aliases: []
summary: "Folder note for the areas of src/VaultManager.Core."
related:
  - "[[Vault Map]]"
  - "[[Vault Manager Architecture]]"
created: 2026-11-02
updated: 2026-11-02
---

# 02 Services

One note per area of `src/VaultManager.Core`. There are no backend services: Vault Manager is a desktop app, and these are its rules.

## Notes

```dataview
TABLE WITHOUT ID file.link AS "Note", summary AS "About", status AS "Status", updated AS "Updated"
FROM "02 Services"
WHERE file.name != this.file.name
SORT file.name ASC
```

%% Begin Waypoint %%
%% End Waypoint %%

## Key documents

- [[Obsidian]] - reading Obsidian's own state: registry, enabled list, Restricted mode, versions, secrets, scan
- [[Changes]] - change lines, apply, snapshot, undo
- [[Profiles]] - vault recipes as files, audit, building a vault
- [[Sync]] - three-way settings compare
- [[Catalog]] - the only door to the network
- [[Store]] - the SQLite database

`Foundation` and `Host.cs` are covered in [[Vault Manager Architecture]], and `Demo` in [[Demo Mode]].

## Status

- All six areas have a note, written from the code as of v0.6.0.0.
- `src/VaultManager.App` has no note of its own. Its queue, waiting lines and screens are in [[Vault Manager Architecture]], its conflict answers in [[Sync]], `--demo` in [[Demo Mode]], `--smoke` in [[Release]], and its look in `DESIGN.md`.
