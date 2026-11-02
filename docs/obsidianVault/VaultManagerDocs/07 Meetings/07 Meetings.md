---
type: index
status: draft
priority: low
tags: [meetings]
aliases: []
summary: "Folder note for meeting notes."
related:
  - "[[Vault Map]]"
created: 2026-11-02
updated: 2026-11-02
---

# 07 Meetings

Meeting notes. One note per meeting, named `YYYY-MM-DD <topic>`. Use the [[Meeting]] template.

## Notes

```dataview
TABLE WITHOUT ID file.link AS "Note", summary AS "About", status AS "Status", updated AS "Updated"
FROM "07 Meetings"
WHERE file.name != this.file.name
SORT updated DESC
```

%% Begin Waypoint %%
%% End Waypoint %%

## Status

- No meeting notes yet.
