# Pirate UI artwork

Generated with the built-in imagegen tool. No CLI/API fallback was used.

The approved concept is `pirates-approved-concept.png`. Runtime text, meters, tabs, faction selection and market actions are rendered by the game; they are not baked into artwork.

## Production assets and final prompts

### `game/Content/Textures/Underworld/Corsairs.png`

Use case: stylized-concept. Production game artwork for StarDrive pirate faction CORSAIRS, matching a black-metal and brass pirate brokerage UI. Wide cinematic landscape: massive weathered industrial pirate fortress built into an asteroid, angular salvaged battleship prominently in the middle foreground, smaller raiding ships, distant planet, amber orange furnace lights and warm nebula. Rich detailed realistic painterly science fiction, ominous privateer atmosphere, dark charcoal hulls with restrained brass highlights. Composition supports both wide banner and central square crop; main ship clearly identifiable at thumbnail size. Full bleed artwork only, absolutely no text, letters, interface, borders, watermarks or logos.

### `game/Content/Textures/Underworld/Draugar.png`

Use case: stylized-concept. Production game artwork for StarDrive pirate faction DRAUGAR, matching a black-metal and brass pirate brokerage interface but with distinct cold violet identity. Wide cinematic landscape: ominous dark alien pirate flagship with skeletal angular architecture, long blade-shaped prow, ribbed black hull, pale silver edges and violet engine lights, accompanied by smaller raiders emerging from a purple nebula. Behind it a forbidding asteroid stronghold with tall spires, distant eclipsed planet. Rich realistic painterly science fiction, threatening mysterious atmosphere. Composition supports wide banner and central square crop; main ship readable at thumbnail size. Full bleed artwork only. No text, letters, UI, borders, watermark, logos, human faces. Original spacecraft designs.

### `game/Content/Textures/Underworld/Terminal.png`

Use case: stylized-concept. Asset type: full-screen production background texture for a premium science fiction pirate brokerage UI. Wide 16:9 landscape, straight front-on orthographic view. Nearly black weathered charcoal metal and black ceramic panels, extremely subtle etched astronomical navigation chart lines, fine scratches, tarnished brass beveled perimeter frame with restrained amber highlights and small intricate mechanical corner details. Center 90 percent stays uniformly very dark, quiet and low contrast for legible UI overlays; decoration concentrated in outermost border. Sophisticated functional starship console, sinister luxury pirate trading terminal. Single continuous background panel, no inner windows or UI subdivisions, no buttons, no gauges, no icons, absolutely no letters or text, no ships or people, no watermarks. Full bleed rectangular texture.

## Danger and growth

Danger uses the existing faction level, with five bands: levels 1–4 Low, 5–8 Guarded, 9–12 Elevated, 13–16 High, 17–20 Extreme. Level zero is Dormant. It is a global power indicator, not a new aggression calculation. Client standing and protection are shown separately. Growth shows current investment toward the next level, capped at 100%; level 20 displays maximum strength.

Both screens receive snapshots from the simulation thread. The galaxy sidebar's skull-and-crossbones icon opens **Pirates**. Diplomacy has faction contact cards with previous/next controls when more than two known factions exist, and no separate market button. The Pirates screen has a scrollable roster with no two-faction cap.

## Adding faction artwork

Set `<PirateArtwork>Underworld/MyFaction</PirateArtwork>` in the faction's race XML and provide `Content/Textures/Underworld/MyFaction.png`. The path has no extension and is relative to `Content/Textures`. Custom factions use their empire color for accents. Missing artwork falls back to `Encounters/pirates3`; older saves retain the vanilla Corsair/Draugar art through canonical-name fallbacks. Factions do not need custom art to appear in the roster or diplomacy pages. Unknown and defeated factions are excluded.
