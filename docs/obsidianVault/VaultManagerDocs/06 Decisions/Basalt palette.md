---
type: decision
status: active
priority: medium
tags: [adr, design, theme]
aliases: []
summary: "Vault Manager's look is built from volcanic rock: a cold basalt ground and one sulfur accent."
related:
  - "[[Vault Manager Architecture]]"
created: 2026-11-02
updated: 2026-11-02
---

# Basalt palette

## Context

Tech Tea Studio has no single palette. Each product grows its look from its own subject and keeps its values in one token file: Chronos is a game, the launcher looks like the Mojang launcher, Turbine is an instrument panel. Vault Manager is about obsidian.

## Decision

The look is built from volcanic rock: a cold basalt ground and one sulfur accent, `#E2C13F`. All values live in `src/VaultManager.App/Themes/Basalt.axaml`, and `DESIGN.md` explains them.

Sulfur means selection and the single main action on a screen, such as Update all. Row buttons stay neutral. Green, copper and red are state only. No colour is used for decoration.

## Alternatives Considered

| Option | Why rejected |
| :-- | :-- |
| Violet | Taken by Chronos. Obsidian's own brand is violet too, which would merge this app with Chronos |
| Magenta | Taken by Omni Client |
| Green | Taken by Minecraft Launcher |
| Orange | Taken by Turbine |

## Consequences

- All text keeps at least 4.5:1 on all four surfaces, and tests compute this.
- `edge` keeps 3:1 on `well`, `base` and `plate` but only 2.6:1 on `raised`, so fields do not sit on `raised`.
- The palette lives twice: in `Basalt.axaml` and in the table in `DESIGN.md`. Contrast and no-hex tests read the theme file; the table is kept by hand.
- Colour is never the only signal, so every state has a glyph. `StateGlyph` draws them as vector shapes of one size, because font glyphs render unevenly.
- Only a dark theme exists. `Basalt.axaml` is shaped so a light variant can be added.

## Links

- `DESIGN.md`
- [[Vault Manager Architecture]]
