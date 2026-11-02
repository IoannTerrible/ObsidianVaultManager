---
type: index
status: active
priority: medium
tags: [service, core, network]
aliases: [VaultManager.Core.Catalog]
summary: "The only door to the network: the official plugin catalog, version checks and release downloads, on a click only."
related:
  - "[[Vault Manager Architecture]]"
  - "[[Changes]]"
  - "[[Store]]"
created: 2026-11-02
updated: 2026-11-02
---

# Catalog

## Purpose

The only door to the network. Nothing here runs by itself: the owner's connection is expensive, so every call follows a click, sizes are shown first, and local copies come before downloads. No other code in Core takes an `HttpClient`, and there is no telemetry.

## Responsibilities

- Ask the size of the catalog before fetching it, about 3 MB.
- Fetch the official catalog and cache it.
- Ask each installed plugin's repository for its latest version.
- List release file sizes and download a release.

## Dependencies

[[Store]] for the catalog cache and online versions. [[Obsidian]] for `PluginManifest` and `VaultLayout`. `Foundation` for atomic writes.

## Key Types

| Type | What it does |
| :-- | :-- |
| `ICatalogClient` | The seam. `DemoCatalog` answers from memory |
| `GitHubCatalogClient` | The real client. `Configure` sets a two-minute timeout and a `VaultManager` user agent |
| `OnlineChecker` | Refreshes the cache, then asks about installed plugins only, one small file each |
| `OnlineCheck` | `Versions`, `NotInCatalog`, `Failed` |
| `ReleaseFile` | Name and size |

## Details

| Call | Request |
| :-- | :-- |
| Catalog size | `HEAD` on `obsidianmd/obsidian-releases`, `community-plugins.json` |
| Catalog | `GET` of the same file |
| Latest version | `manifest.json` at `HEAD` of the plugin's repository |
| Release sizes | `HEAD` on each release asset |
| Download | `main.js` and `manifest.json` are required, `styles.css` is optional |

- Repository names come from a downloaded file and end up in URLs, so `IsRepoName` checks them.
- No answer is not "up to date": a failed lookup counts in `Failed` and writes nothing.
- A plugin not in the catalog is reported in `NotInCatalog`, which backs the mark the changelog describes.
- `DownloadPlugin` is only ever queued from the official catalog.
- A download is checked before it lands: its manifest must carry the expected plugin id and `ToVersion`, or the line fails with `VerifyFailed` and the vault stays as it was. A timeout or an HTTP error fails the line with `DownloadFailed`.

### Worth checking

- `LatestVersionAsync` reads the manifest at the repository head, while a download uses a release tag. The two can differ if a repository's head is ahead of its releases. A release whose manifest says another version is refused when the line runs. A tag with no such release should fail at the size question, from reading the code.

## Notable Decisions

None recorded yet. The rule comes from the "Nothing outside a click reaches the network" guardrail in `CLAUDE.md`. See also [[Copies not symlinks]] for why local copies come first.

## Links

- [[Changes]] runs downloads.
- [[Store]] holds `catalog` and `online_versions`.
