---
type: index
status: active
priority: low
tags: [legacy]
aliases: []
summary: "Pre-vault documents, preserved as they were. Empty so far."
related:
  - "[[Vault Map]]"
  - "[[v0.6.0.0 - Rebuild into Vault Manager]]"
created: 2026-11-02
updated: 2026-11-02
---

# Legacy

Pre-vault documents, preserved as they were for reference.

## Notes

```dataview
TABLE WITHOUT ID file.link AS "Note", summary AS "About", status AS "Status", updated AS "Updated"
FROM "Legacy"
WHERE file.name != this.file.name
SORT updated DESC
```

%% Begin Waypoint %%
%% End Waypoint %%

## Status

- Empty: no older documents have been brought in.
- The app before the rebuild, `ObsidianScanner`, is still in the repo as code: `ObsidianScanner/` and `ObsidianScanner.slnx`. See [[v0.6.0.0 - Rebuild into Vault Manager]].
