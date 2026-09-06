# Village Tilesets

Valour uses one canonical tileset package: one manifest and one runtime atlas.
There is no separate authoring manifest or legacy runtime format.

## Coordinates

Definitions retain two positions in the canonical manifest:

- `X` and `Y` are tile coordinates in the packed runtime atlas.
- `SourceX` and `SourceY` are tile coordinates in the virtual source sheet used by the editor.

Wall sets use the same rule in pixels:

- `OriginX` and `OriginY` address the packed runtime atlas.
- `SourceOriginX` and `SourceOriginY` address the virtual source sheet.

The game reads runtime coordinates. The tileset editor reconstructs the virtual
source sheet by copying the referenced runtime rectangles back to their source
coordinates. Pixels that were never referenced remain transparent.

## Multiple source sheets

The editor accepts one or more original source images in a single upload. It
stacks them into a tile-aligned virtual source sheet and records each original
sheet's dimensions, offset, filename, and SHA-256 fingerprint in
`source.sheets`. This lets one package contain walls, floors, and furniture from
separate purchased sheets without introducing another packaging pipeline.

Original source images remain browser-local. Building a package creates:

1. A trimmed PNG atlas containing only referenced definitions and complete wall
   blocks.
2. A canonical `.tileset.json` manifest containing both coordinate spaces.

The atlas uses deterministic tile-aligned shelf packing. Exact duplicate source
rectangles share one runtime region, and each packed region has a transparent
tile gutter with an extruded one-pixel edge to prevent sampling bleed.

## Existing exterior tileset

`exterior-tileset-0.json` was migrated in place to the canonical format. Its
initial atlas is an identity atlas (`atlas.packed` is `false`), so all existing
runtime coordinates and the current CDN image remain unchanged. Publishing it
through the editor later changes the atlas and runtime coordinates but preserves
the source coordinates, keys, collision masks, terrains, brushes, and walls.
