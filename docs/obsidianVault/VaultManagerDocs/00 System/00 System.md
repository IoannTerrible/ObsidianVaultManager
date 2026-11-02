---
type: index
status: active
priority: medium
tags: [system, meta]
aliases: []
summary: "Folder note for vault system files: Vault Map and templates."
related:
  - "[[Vault Map]]"
created: 2026-11-02
updated: 2026-11-02
---

# 00 System

Meta-files for the vault itself, not project content. Start with [[Vault Map]].

## Notes

```dataview
TABLE WITHOUT ID file.link AS "Note", summary AS "About", status AS "Status", updated AS "Updated"
FROM "00 System"
WHERE file.name != this.file.name AND !contains(file.folder, "Templates")
SORT updated DESC
```

%% Begin Waypoint %%
%% End Waypoint %%

## Key documents

- [[Vault Map]] - where things live and which conventions apply
- `Templates/` - Templater templates: [[Note]], [[Decision]], [[Meeting]], [[Runbook]], [[Release Summary]], [[Service]]

## Status

- Templates stay out of the table above because they are not project content.
