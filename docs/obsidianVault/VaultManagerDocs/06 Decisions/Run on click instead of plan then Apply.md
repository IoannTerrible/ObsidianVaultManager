---
type: decision
status: active
priority: high
tags: [adr, window, safety]
aliases: []
summary: "An action runs on click through the executor, with a copy saved first and Undo in view, instead of a plan reviewed and then applied."
related:
  - "[[Changes]]"
  - "[[Store]]"
  - "[[Vault Manager Architecture]]"
  - "[[Registration waits while Obsidian runs]]"
created: 2026-11-02
updated: 2026-11-02
---

# Run on click instead of plan then Apply

## Context

The first full version of the window collected actions into a list and wrote nothing until a press of Apply. The owner found the window too complicated.

## Decision

An action runs on click through the executor, with a copy saved first and Undo in view. `DESIGN.md` keeps this as the rule "Act, then offer Undo".

## Alternatives Considered

| Option | Why rejected |
| :-- | :-- |
| Keep the plan-then-Apply step | `DESIGN.md` records it as removed for being needless work |

## Consequences

- **Trade-off:** there is no list to review before writing. A click writes.
- Waiting lines had to survive a restart, so they are now kept in `pending_changes`. See [[Store]] and Waiting lines in [[Vault Manager Architecture]].
- A waiting line finishes by itself when its gate opens, possibly long after the click. The executor checks the gate again before it writes, and a plugin or settings line whose target moved on reports `NeedsReview` and overwrites nothing.
- The safety now rests on Undo, and Undo has edges:
  - A copy is saved for plugin swaps and settings overwrites. Other lines keep an undo record of what to put back.
  - `CreateVault` and `RegisterVault` have no undo. After a new vault the log says `New vaults are not undone. Delete the folder if you do not need it.`
  - Answering a conflict with `Keep target` has no undo record. See [[Sync]].
  - Only the newest 20 operations can be undone, and Undo refuses a line whose target moved on.
- The Changes panel appears only when something waits, failed, or an operation made in this session can be undone. An older undo is offered in the `⋯` menu as `Undo last change (time)`, and lines that worked go to the log.

## Links

- `DESIGN.md`
- [[Changes]]
- [[Vault Manager Architecture]]
