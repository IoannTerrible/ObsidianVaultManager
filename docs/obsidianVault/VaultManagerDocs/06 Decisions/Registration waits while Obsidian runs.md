---
type: decision
status: active
priority: high
tags: [adr, obsidian, safety, unverified]
aliases: []
summary: "A write to Obsidian's vault list waits until no Obsidian process runs, because what a running Obsidian does with that write is not established."
related:
  - "[[Obsidian]]"
  - "[[Changes]]"
created: 2026-11-02
updated: 2026-11-02
---

# Registration waits while Obsidian runs

## Context

A new vault takes two lines: `CreateVault` makes the folder, and `RegisterVault` adds it to `%APPDATA%\obsidian\obsidian.json` so Obsidian lists it. The first touches only a new folder. The second writes a file that belongs to a running application.

## Decision

`RegisterVault` waits while any Obsidian process runs, with `Waiting` and `ObsidianRunning`, whichever vaults are open. `CreateVault` has no such gate, so the folder can exist while its registration waits.

`CLAUDE.md` gives the reason and the instruction: what a running Obsidian does with an external write to `obsidian.json` has not been established. Do not change this without testing it.

## Alternatives Considered

| Option | Why rejected |
| :-- | :-- |
| Write while Obsidian runs | What Obsidian does with the write is unknown. Nothing records that it was tried |

## Consequences

- A person may have to close Obsidian before a new vault appears in its list.
- A registration has no undo record. Neither does `CreateVault`.
- `Register` keeps every other key in the file, adds no vault twice, and refuses a list it could not read (`RegistryUnreadable`).
- The line does not follow a failed `CreateVault` in the same run, and it needs an existing `.obsidian` (`SourceMissing`), so a folder that is not a vault is never listed.
- A waiting registration is kept in `pending_changes`, so it survives a restart and lands by itself once no Obsidian process runs. The created folder stays meanwhile, and removing the line leaves the folder. See Waiting lines in [[Vault Manager Architecture]].

## Unverified

- **The running case itself.** This is the open question. The comment on `VaultRegistry.Register` says the app keeps this file in memory, and no test or note records a check of that.
- **The closed case against a real Obsidian.** Tests run on temp folders with a fake process. They pin that the line waits, lands afterwards, keeps other keys and is not added twice. Nothing recorded shows a real Obsidian accepting an entry of a random 16-hex id with `path` and `ts` and no `open`.
- **Starting Obsidian during the write.** The gate is read once, before the write. Nothing guards the moment between. The line now runs by itself within a timer tick of Obsidian closing, so a quick reopen could meet that moment with no click involved.
- **Whether Obsidian undoes the entry later.** For vault config, Vault Extension Sync documents that Obsidian rewrites it on close, which is why writes into an open vault wait. Whether `obsidian.json` behaves the same is not recorded.

## Tasks

- [ ] Test all of the above against a real Obsidian, without risking the owner's real vault list, before changing this.

## Links

- [[Obsidian]]
- [[Changes]]
