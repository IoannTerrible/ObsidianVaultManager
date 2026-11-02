---
type: runbook
status: active
priority: medium
tags: [runbook, demo, testing]
aliases: ["--demo"]
summary: "The sealed world behind --demo: throwaway vaults with every state the window has to show, no network, plus the state gallery."
related:
  - "[[Vault Manager Architecture]]"
  - "[[Release]]"
created: 2026-11-02
updated: 2026-11-02
---

# Demo Mode

## Purpose

Try the app, or look at a visual change, without touching anything real. `--demo` is sealed: everything lives in a temp folder, and it never reaches the real vaults or the network.

## Preconditions

Do not start the app on the owner's desktop to check something. Use the headless gallery below.

## Steps

From source:

```bash
dotnet run --project src/VaultManager.App/VaultManager.App.csproj -f net10.0 -- --demo
```

From a published build:

```bash
VaultManager-v0.6.0.0-win-x64.exe --demo
```

To write the state gallery to a folder for a visual change:

```bash
VM_SCREENSHOTS=/d/some/folder dotnet test tests/VaultManager.Tests/VaultManager.Tests.csproj
```

## How it is sealed

`Demo/DemoWorkspace.cs` in Core replaces all three seams of `CoreHost`:

| Seam | Demo value |
| :-- | :-- |
| `AppPaths` | `DemoWorkspace.Create(root)` builds a registry, a browser store and vaults inside `root`, and returns paths inside it |
| `IObsidianProcess` | `DemoProcess`. `IsRunning` starts true |
| `ICatalogClient` | `DemoCatalog` answers from memory. Its downloads write fake plugin files |

Because `IsRunning` starts true, the open vault's lines wait, and so does `RegisterVault`, for as long as it stays true. A link in the title bar reads `Obsidian: running` or `Obsidian: closed`, with the tip `Switch`, and flips it. The waiting lines are then checked at once, so they finish with no further click.

## Which folder it uses

`AppSession.NewDemoRoot` makes a new root on each start: `vault-manager-demo-<guid>` under the system temp folder (`Path.GetTempPath()`, normally `%TEMP%`). The folder is deleted when the app exits. If a file is still in use, the delete gives up and the folder stays under temp.

| Inside the root | Holds |
| :-- | :-- |
| `obsidian` | The demo registry and browser store |
| `Vaults` | The demo vaults |
| `data` | The database, profiles and snapshots |
| `temp` | `TMP` and `TEMP` point here for the run, because Core stages downloads under the system temp folder |
| `exports` | Exported profiles |

The window stays inside the root in three more ways:

- `DemoFilePicker` answers from inside the root, because a native picker can walk out of it.
- `RecordingShell` follows no links, and the log says `Demo: would open {0}`.
- A new vault with a parent outside the root is refused (`OutsideDemo`).

## What the demo world shows

| Vault or plugin | State it exists for |
| :-- | :-- |
| `KnowledgeSnippets` | Open vault: changes wait |
| `Archive` | Listed, but its drive is absent: offline |
| `Damaged` | A cut `community-plugins.json`: the app must refuse to touch it |
| `Family-restricted` | Community plugins off in the browser store: Restricted mode |
| `Future` | No browser store entry: `NeverEnabled`, worded as seeming to be off |
| `Notes` under `CopyPc` | Two folders named `Notes`, told apart by parent |
| `HyperionDocs`, `ChronosDocs` | Studio layouts with the eight studio plugins |
| Excalidraw | Drift: 2.17.2, 2.15.2, 2.8.3 |
| Beautitab | Same version 1.6.1 with different files |
| AI Helper in `Family` | `data.json` with a fake key: blocked by the secret scan |
| Calendar in `KnowledgeSnippets` | Installed but disabled |

## Verification

Nothing outside the root folder is read or written. Tests use real temp folders and never touch the real `%APPDATA%` or the network.

## Links

- [[Vault Manager Architecture]]
- [[Catalog]]
- [[Release]]
