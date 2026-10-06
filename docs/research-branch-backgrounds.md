# Research branch backgrounds

Research now displays illustrated branch bands for every base-game root category.
The category root card and its outgoing connector lines are omitted from the
canvas; the category name remains in the header and sidebar. Actual technologies
and their prerequisite connections remain unchanged.

## Original artwork coverage (before Combined Arms migration)

| Category | Illustrated themes |
| --- | --- |
| Colonization | Agriculture/habitation, science/development, industry/mining |
| Socio-Logistics | Economy/diplomacy, supply/infrastructure, military organization |
| Starship Construction | Hulls/shipyards, warp engineering, fleet support |
| Ship Defenses | Armor, shields and point defense |
| Physics | Reactors/power, propulsion/subspace, ion technology |
| Ballistics | Kinetic weapons, missiles/guidance |
| Energy | Lasers and directed energy |
| Secret | Ancient/alien technology, only when discovered |

Thirteen production textures live in `game/Content/Textures/ResearchMenu/Backgrounds/`,
alongside the existing observatory texture. They were generated using built-in
imagegen. Each final generation prompt is retained in
`docs/concepts/research-branch-<texture-name>.prompt.txt`.

Backgrounds use aspect-preserving cropping, a dark overlay and separate labels.
Bands move with their nodes and are clipped to the research canvas. The top search
and bottom queue remain fixed. Large trees still require vertical scrolling and,
at small resolutions, horizontal panning; the preview concept was not a promise
that every technology would fit on one screen.

## Mod support

`game/Content/ResearchBranchStyles.yaml` defines the visual mappings. A mod may
provide `ResearchBranchStyles.yaml` in its content directory to replace the base
mapping, with texture overrides using the normal mod content paths.

Each entry has `Root` (category UID), `Label`, `Texture` (path relative to Textures,
without extension), and `Seeds` (technology UIDs). File order controls band order.
Visible descendants inherit the nearest mapped seed's theme; another explicitly
mapped seed starts its own theme. Ties follow mapping order, and a shared node is
placed once. Only technologies already present in the visible tree participate.

Unrecognized roots retain the standard layout/background. Unmapped visible nodes
within a partly mapped category appear in a neutral Other technologies band.
Missing artwork uses a neutral background, and absent or malformed configuration
falls back to the standard view. Mapping affects presentation only, with no changes
to save format, research costs, unlocks, prerequisites, or discovery rules.

The base tree now uses Combined Arms research. See [migration notes](combined-arms-research.md). Unknown-root, missing-seed and hidden-technology fallback behavior remains covered by tests.

## Validation and previews

Before the Combined Arms migration, 14 research UI and technology-unlock tests passed, including category coverage,
texture availability, seed boundaries, hidden nodes, unchanged prerequisites,
root-column removal, scrolling to the final branch, queue controls and search.
Renders are captured at 1920x1080, 1280x720 and 1024x768; full-HD renders of each
discovered category are in `output/imagegen/research-category-*.png`.

- [Colonization](concepts/research-branches-implemented.png)
- [Colonization, scrolled to industry](concepts/research-branches-industry.png)
- [Starship Construction](concepts/research-branches-shipyards.png)
- [Physics](concepts/research-branches-physics.png)
