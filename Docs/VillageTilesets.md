# Village Tilesets

A village tileset package contains a manifest and a runtime PNG atlas. The
manifest records the definitions used for drawing, collision, and editing.

## Coordinates

Definitions retain two positions in the package manifest:

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

Use **Source sheet** to browse a complete board or **Jump to asset** to select a
named object. On touch screens, drag with one finger to select a rectangle and
use two fingers to pan or zoom without changing the selection. In narrow panes,
the artwork and definition forms stack vertically; scroll down to the package
build and export controls.

Original source images remain browser-local. Building a package creates:

1. A trimmed PNG atlas containing only referenced definitions and complete wall
   blocks.
2. A `.tileset.json` manifest containing both coordinate spaces.

The atlas uses deterministic tile-aligned shelf packing. Exact duplicate source
rectangles share one runtime region, and each packed region has a transparent
tile gutter with an extruded one-pixel edge to prevent sampling bleed.

## Curated library

`exterior-tileset-0.json` is the shared package for both outdoor and indoor
maps. It contains 257 definitions, eleven terrain styles, three manual brushes and
six wall styles.
There are 170 curated furniture objects and seven floor finishes. The furniture
library covers kitchens, bathrooms, bedrooms, living rooms, offices, art studios,
music rooms and recreation spaces. The builder has
separate categories for these rooms; source navigation groups complete objects
into compact boards instead of one source entry per tiny PNG.

The runtime atlas is 2048 × 1024 and approximately 178 KiB. The source sheets are
combined only while packing; the full purchased sheets are never shipped.
`Tools/Villages/curation.json` records whole-object PNGs, explicit assemblies,
selected source rectangles and ground footprints. Artwork is centered and
bottom-aligned in complete 16-pixel cells without discarding any visible pixels.
Office chairs include four authored directions. `pack-library.py` calls the same `VillageTilesetPacking.js` planner
and compiler as the browser editor, then copies the referenced pixels into the
planned atlas with one-pixel edge and corner extrusion.

Build the library with Python 3, Pillow and Node.js:

```sh
python3 Tools/Villages/pack-library.py \
  --exteriors /path/to/modernexteriors-win \
  --interiors /path/to/moderninteriors-win
python3 Tools/Villages/verify-library.py \
  --interiors /path/to/moderninteriors-win \
  --contact-sheet TestResults/village-browser/catalog.png
python3 Tools/Villages/test_library_art.py
```

The packer rejects source selections that cross an object's opaque boundary,
out-of-bounds rectangles, empty objects, incomplete or overlapping assemblies,
invalid footprints, and invalid placement layers. It compares each recipe and
its source pixels with `curation.lock.json`. The verifier compares every packed
object's entire RGBA image with that approved reference, as well as definition
bounds, collision masks, footprints, opaque floors and wall-block bounds.

When adding artwork, prefer the pack's complete object PNGs. Files in a Singles
folder can still be modular parts: inspect each candidate before choosing it.
Use an explicit `parts` recipe for sofas, screens or other modular objects and
include every cap, center and side. Preserve published definition keys and
ground footprints when repairing artwork so placed objects keep their identity.
The `canvas` option can preserve an existing larger ground footprint.

Run the packer with `--update-lock` only while preparing a curation change. Review
every page of the contact sheet, then run the normal packer, verifier and browser
library suite before committing the recipes and lock together. Normal release
packing never updates the lock. A fingerprint confirms approved pixels are
unchanged; visual review is still required to establish that an object is complete.
The staff editor also checks new or resized sprite selections for empty art and
cuts through object edges before saving. Tile definitions remain available for
intentional modular terrain pieces.

For browser verification against the actual purchased originals, generate private
references and run the application renderer:

```sh
python3 Tools/Villages/verify-library.py \
  --interiors /path/to/moderninteriors-win \
  --reference-dir TestResults/village-browser/asset-review/references
node Valour/Tests/Browser/villages-library.mjs
```

This compares all 170 placed objects with their source images at four scales
(680 pixel comparisons), and saves a gallery rendered by the game. Browser
export/import tests also check dimensions, footprints, layers, support flags and
every displayed atlas pixel. Canvas PNG encoding may normalize invisible RGB
and round semi-transparent RGB; exported images must retain their rendered color
and alpha and remain stable on subsequent round trips.

The generated `Valour/Client/wwwroot/media/villages/library-atlas.png` is a private
build input, excluded from Git and from the app's static assets. Add
`--release-zip artifacts/villages-release-assets.zip` to the packing command to
bundle the PNG, matching manifest and original licenses. Keep that ZIP private,
for example in R2, and extract its `Valour/` directory at the repository root
before building in CI. The manifest and PNG in the package must match the release.

The app build runs `BuildTools/VillageAtlasPacker`. It verifies the PNG's SHA-256
and dimensions against the manifest and produces `library-atlas.vtex.bin` in the
same directory. Only that binary is published. The official manifest and wall
sets reference its normal `/_content/Valour.Client/` URL, with the PNG fingerprint
in the version query. A Release build rejects a missing or mismatched PNG and an
official manifest that points at the readable PNG. The server embeds the same
tileset manifest at build time for collision and catalog metadata.

`VillageAtlasProtection.ts` decodes the versioned binary in memory and checks its
length and checksum. The renderer, builder previews and staff editor share a
document-scoped blob URL. Decoding preserves the original PNG bytes and requires
no network key service or Web Crypto. The binary adds twenty bytes to the PNG.
The service worker includes it in the app's static asset cache. Ordinary PNG,
WebP and local image imports keep their existing loading path, and the staff
editor can export its edited library as a normal PNG and manifest.

After building, `python3 Tools/Villages/test_atlas_packaging.py` checks the packer
with a generated single-pixel fixture: parallel builds, damaged output,
mismatched fingerprints and dimensions, and readable official-image URLs.
The JavaScript tests use the same format vector as the C# encoder tests.

This is reversible obfuscation intended to discourage casual copying. Browser
code can recover the image, and rendered pixels can be captured. It is not DRM,
access control, or a guarantee of license compliance. The licenses for
[Modern Interiors](https://limezu.itch.io/moderninteriors) and
[Modern Exteriors](https://limezu.itch.io/modernexteriors) allow use in projects,
require credit, and prohibit redistribution of the assets. Keep the source art
and build-input ZIP private; do not offer them as public asset downloads. The
in-game credit links to LimeZu. Questions about the license covering a particular
product or editor use should be confirmed with the licensor.

## Furniture placement metadata

`FootprintWidth` and `FootprintHeight` describe the ground occupied by a sprite,
which can be smaller than its drawn height. When both values are positive, they are clamped to the art's dimensions.
Otherwise `VillageObjectGeometry` uses its key-specific footprint or a one-tile
default. Collision masks remain aligned to the full sprite and are
sampled from its bottom-aligned ground footprint.

`PlacementLayer` is `Furniture` by default. `Floor` places nonblocking rugs at
Z index -10, above terrain (-100/-101) and beneath furnishings. `Surface` places
accessories at Z index 5 and requires their entire footprint to be on an item
with `SupportsItems: true`. Rugs may overlap furnishings but not other rugs;
accessories may overlap their supporting desk or table but not each other.
Moving or removing a supporting item requires moving its accessories first.
Standing art is centered horizontally on its footprint and anchored at its
bottom. Surface items are raised half a tile, inset from tabletop edges, and
depth-sorted after their supporting furniture. Previews and hit tests use the
same bounds.

These fields are editable in the staff tileset definition editor and survive
cloning and package compilation.

## Wall artwork layouts

Logical wall keys always use the same 47-shape topology, and the server owns
neighbor resolution. `Layout: Blob47` addresses a row-major authored shape table.
`Layout: RoomBuilder` identifies LimeZu's 8 × 7 blocks of caps, faces, bases and
vertical strips. Those blocks are not a blob table: the runtime composes the
wall footprint from the logical neighbors. Its six-pixel caps connect across
cell boundaries, and exposed front edges use an intact 26-pixel authored face
with a baseboard. The result is cached per style and shape. Every source pixel
keeps its proportions; neither vertical strips nor face end caps are stretched
or repeated as complete tiles. Both layouts retain packed and source origins
and have a connected colored fallback while textures load.

Run `Valour/Tests/Browser/villages-rendering.mjs` for a full office and all six
styles at corners, junctions, and a solid block. It uses the application renderer
and asserts that adjoining cap edges agree and vertical borders stay continuous.
