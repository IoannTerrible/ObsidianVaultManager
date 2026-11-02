---
type: index
status: active
priority: low
tags: [inbox]
aliases: []
summary: "Unsorted notes. Move them out promptly."
related:
  - "[[Vault Map]]"
created: 2026-11-02
updated: 2026-11-02
---

# Inbox

Drop-zone for notes that do not yet have a home. Anything that lives here for more than a session should move to the right folder.

## Notes

```dataview
TABLE WITHOUT ID file.link AS "Note", summary AS "About", status AS "Status", updated AS "Updated"
FROM "Inbox"
WHERE file.name != this.file.name
SORT updated DESC
```

%% Begin Waypoint %%
%% End Waypoint %%

## Status

- Empty.
