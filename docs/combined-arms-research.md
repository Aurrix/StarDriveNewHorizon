# Combined Arms research migration

The base research tree is replaced with the locally installed Combined Arms tree:
336 technologies across 11 roots. Imported 271 research/category icons and 507
changed or new research text entries. The source mod remains intact.

All tree links, text IDs and icon references resolve. Background mappings cover
all eleven categories. Bands have 28-pixel separation and a gentle rightward fade;
research icons preserve their aspect ratio in cards, search, categories and queue.
The category root card remains hidden.

This is a research migration, not a complete Combined Arms gameplay conversion.
The audit found 295 missing module assets, 27 buildings, one troop and 109 hulls.
Those unlocks need follow-up integration before the whole tree is playable.
Missing hull previews now use placeholders without dereferencing a null hull.
Old saves and vanilla-specific unlock tests may reference removed technology UIDs;
use a new game for migration review. Save compatibility has not been established.

The detailed dependency inventory and removed UIDs are in
[the import report](combined-arms-research-import.json).
The pre-import technology XML, text and overwritten icons were backed up to `output/backups/research-before-combined-arms-20261004-215721.zip`.

`python tools/import_combined_arms_research.py` audits the installed source;
`--apply` archives the current data and replaces the tree. The report is regenerated
on each run, so preserve the original migration report when auditing again.

Validation: all five ResearchScreenTests passed, including renders at 1920x1080, 1280x720 and 1024x768. Reviewed the Colonization render. Gameplay unlock tests were not run; the outstanding dependency inventory prevents claiming a complete gameplay conversion.

[Current Colonization preview](concepts/research-combined-arms.png)
