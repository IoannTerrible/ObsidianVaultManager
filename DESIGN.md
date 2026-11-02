# DESIGN.md

How Vault Manager looks and why. Read it before a visual change. All values live in `src/VaultManager.App/Themes/Basalt.axaml`, and this file explains them rather than repeating more than it must.

## Where the look comes from

Tech Tea Studio has no single palette. Each product grows its look from its own subject and keeps its values in one token file: Chronos is a game, the launcher looks like the Mojang launcher, Turbine is an instrument panel. Vault Manager is about obsidian, so it is built from volcanic rock: a cold basalt ground and one sulfur accent.

The hues of the other products are taken: violet (Chronos), magenta (Omni Client), green (Minecraft Launcher), orange (Turbine). Obsidian's own brand is violet too, which would merge this app with Chronos.

## Palette

| Token | Value | Use |
| :-- | :-- | :-- |
| `well` | `#0A0B0D` | Recessed: tables, lists, log, fields |
| `base` | `#101214` | Window ground |
| `plate` | `#181B1E` | Raised: panels, headers |
| `raised` | `#23272B` | Buttons, the selected row |
| `line` | `#2C3136` | Dividers |
| `edge` | `#60676E` | Control borders |
| `text` | `#E7E5DC` | Main text |
| `soft` | `#AEAEA4` | Secondary text |
| `dim` | `#90918A` | Captions |
| `sulfur` | `#E2C13F` | Selection and the main action |
| `on-sulfur` | `#17150A` | Text on sulfur |
| `match` | `#7DB56A` | Same as the newest copy |
| `drift` | `#E5904F` | Older version, waiting |
| `blocked` | `#E86A5B` | Blocked, failed |
| `info` | `#7FB2C9` | Offline drive |

All text keeps at least 4.5:1 on all four surfaces. `edge` keeps 3:1 on `well`, `base` and `plate`, and only 2.6:1 on `raised`, so fields do not sit on `raised`. Tests compute this.

## Rules

- **Two surfaces.** Plate stands above the window, well is cut into it. There is no third. From Turbine.
- **Colour means one thing.** Sulfur is selection and the single main action on a screen, such as Update all. Row buttons stay neutral. Green, copper and red are state only. No colour for decoration.
- **A glyph with every state.** Colour is never the only signal. `StateGlyph` draws them as vector shapes of one size, because font glyphs render unevenly.
- **Three voices of type.** Condensed caps name parts of the window, monospace shows values, sans carries sentences. Sizes 12, 13, 16. All three faces ship Cyrillic.
- **No stock look.** A new control is drawn in the panel's material; bare Fluent is a regression. From Turbine.
- **The window does not jump.** Zones have fixed sizes and long content scrolls inside. From Chronos.
- **Show only what is needed.** A zone with nothing to say is not drawn, a row is one line, and details open on click. Added after the owner found the first full version too complicated.
- **Act, then offer Undo.** An action runs on click, because a copy is saved first and Undo stays in view. An earlier plan-then-Apply step was removed as needless work.
- **The voice of a label on a machine.** Short, no exclamation marks, no long dashes. Values and reasons, never True and False.

## The window

```
rail: Plugins, Profiles                  | header: one sentence, Update all, refresh, menu
      Vaults, with + New vault           | main area, scrolls inside
      Changes, only when needed          | log: the latest event, one line, opens on click
```

- **Plugins opens on one list** of one-line rows, most serious first, without section headers. A click opens a row's details and its `Keep as is`. The full table is a link away and shows a version only where it is behind.
- **A vault's page** answers what this vault has. Adding a plugin is a button with a filter rather than a permanent list, because a list of what could be added says nothing about the vault. Copying settings starts here too.
- **Profiles says one plain result per vault**, such as `1 folder missing`. The editor is behind `Edit`.
- **Changes appears in the corner only when something waits, failed, or can be undone from this session.** Successful lines go to the log. An older undo stays in the `⋯` menu, because undo records live for weeks and would keep the panel up forever.
- **Update all carries no count.** The header counts plugins and the action counts copies, so a number on the button read as a mismatch.
- **Wording is exact about what was checked.** Comparison happens between your vaults, so the app avoids `Up to date` until an online check has run.

## Where one value lives twice

| Value | Copies | Guard |
| :-- | :-- | :-- |
| Palette | `Basalt.axaml`, the table above | Contrast and no-hex tests read the theme file; this table is by hand |
| Strings | `Strings.resx`, `Strings.ru.resx` | Localization tests compare keys and placeholders |
| Studio profile | `tech-tea-studio.json`, the global rules for vaults | By hand; `The_built_in_profile_matches_the_studio_rules` pins the plugin list |

## Open questions

- Only a dark theme exists. `Basalt.axaml` is shaped so a light variant can be added.
