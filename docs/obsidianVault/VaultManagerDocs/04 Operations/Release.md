---
type: runbook
status: active
priority: high
tags: [runbook, release, ci]
aliases: []
summary: "How a version is bumped, tested by CI and published as a self-contained Windows exe from a v* tag."
related:
  - "[[Demo Mode]]"
  - "[[v0.6.0.0 - Rebuild into Vault Manager]]"
created: 2026-11-02
updated: 2026-11-02
---

# Release

## Purpose

Publish Vault Manager as one self-contained Windows x64 exe on the GitHub Releases page. The release branch is `product`.

## Preconditions

- `VERSION` holds the version. `Directory.Build.props` reads it and feeds every assembly.
- Every part of a version is one digit, carried like an odometer: `0.6.9.9` then `0.7.0.0`. `DemoAndVersionTests` guard the file.
- `CHANGELOG.md` has an entry for the version.
- Agents commit only. They do not push, and that includes tags. Pushing is the owner's step.

## Steps

1. Bump `VERSION`.
2. Commit as `v<VERSION> <Short description>`: one line, at most 72 characters.
3. The owner pushes to `product`, and `ci.yml` runs.
4. The owner tags and pushes the tag, which starts `release.yml`:

```bash
git tag v0.6.0.0
```

```bash
git push origin v0.6.0.0
```

## What the workflows do

### ci.yml

Runs on push and pull request to `product`, and by hand.

- **Test** on `windows-latest` with .NET 10.
- **Build** of `VaultManager.App` on `ubuntu-latest` for each of net6.0, net8.0, net9.0 and net10.0. Each job also installs SDK 10, because the sources use C# 12.

### release.yml

Runs on a `v*` tag, or by hand with a version input. On `windows-latest`:

1. Resolve the version. It must equal `VERSION`, or the job fails.
2. Run the tests in Release.
3. Publish net10.0, win-x64, self-contained, single file, compressed, with native libraries inside.
4. Smoke test: start the exe with `--smoke` and fail on a non-zero exit code.
5. Rename to `VaultManager-v<version>-win-x64.exe`.
6. Create the GitHub Release with generated notes.

### The --smoke run

A published build has no window to look at in a check, so `Program.Smoke` in `src/VaultManager.App/Program.cs` proves the parts a single file can lose. It opens no window and touches nothing of the user's. It works in `vault-manager-smoke-<guid>` under the system temp folder and removes it afterwards. `--smoke` wins if `--demo` is given too.

| Exit code | Meaning |
| :-- | :-- |
| 0 | All checks passed |
| 2 | A value written to a temp SQLite database did not read back |
| 3 | The English or Russian text of one key is missing, or both are the same, so the Russian satellite is not inside |
| 4 | After Avalonia setup, `Font.Label` is not found or has no glyph for the Cyrillic letter U+0416 |

Exceptions are not caught, so a native library that fails to load ends the process with a non-zero code too.

## Verification

- The Release page has the exe under the versioned name.
- The exe starts with no .NET installed, and `--demo` runs. See [[Demo Mode]].

## If It Fails

- **Tag and `VERSION` differ:** the job stops at step 1. Fix the version rather than the check.
- **Tests, publish or smoke test fail:** nothing is published, because creating the release is the last step. The tag stays.

## Worth checking

- The workflow does not check that the tag sits on `product`.

## Links

- [[v0.6.0.0 - Rebuild into Vault Manager]]
- [[Demo Mode]]
