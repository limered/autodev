# Issue tracker

This project uses **local markdown files** for issue tracking.

Tickets are stored as individual markdown files under:

```
.scratch/<feature-slug>/issues/<NN>-<slug>.md
```

Tickets are numbered in dependency order (blockers first). Each ticket file contains a "Blocked by" section referencing the tickets that gate it.

Use the `to-tickets` skill to break work into tickets and write them to this layout.
