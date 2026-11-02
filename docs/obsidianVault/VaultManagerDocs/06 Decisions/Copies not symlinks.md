---
type: decision
status: active
priority: high
tags: [adr, plugins, safety]
aliases: []
summary: "A plugin reaches a vault as a real copy of its files, never as a link to a shared folder."
related:
  - "[[Changes]]"
  - "[[Obsidian]]"
created: 2026-11-02
updated: 2026-11-02
---

# Copies not symlinks

## Context

Vault Manager brings one plugin into many vaults. A shared folder linked into each vault is the obvious way to keep them in line, and it is what symlink tools do.

## Decision

A plugin reaches a vault as a real copy of its files: from another vault on this PC, or from a release in the official catalog. The app creates no links.

The guardrail in `CLAUDE.md` gives two reasons:

- Symlink tools report admin rights, cloud folders and shared `data.json` problems.
- Studio vaults live in git with their plugins.

## Alternatives Considered

| Option | Why rejected |
| :-- | :-- |
| Symlinks to a shared plugin folder | The problems above, as reported by tools that do this. Nothing in the repo records reproducing them here |

## Consequences

- Copies can drift apart, so the app has to find it: drift is what the Plugins home reports, and `SameVersionDifferentFiles` warns when one version has different files.
- The newest copy is the source, compared as numbers. First-vault-wins used to spread old versions.
- Each vault keeps its own `data.json`. Plugin settings stay behind by default, because they hold keys and vault-specific paths.
- Each vault pays for the disk space of its own copy.
- A copy from another vault uses no traffic, which is why local copies come before downloads on the owner's expensive connection.
- A copy can be built beside the old folder, verified by hash and swapped in, and the old folder can be saved first for Undo.

## Links

- [[Changes]]
- [[Obsidian]]
- [[Catalog]]
