#!/usr/bin/env python3
"""Pack the licensed Village library; original sheets are never copied to the repository."""
import argparse
import hashlib
import json
import subprocess
from pathlib import Path
from PIL import Image
from urllib.parse import parse_qsl, urlencode, urlsplit, urlunsplit
from zipfile import ZipFile, ZIP_DEFLATED
from library_art import load_objects, check_lock, verify_object_pixels, require

ROOT = Path(__file__).resolve().parents[2]
MANIFEST = ROOT / 'Valour/Client/wwwroot/tilesets/exterior-tileset-0.json'
ART = ROOT / 'Valour/Client/wwwroot/media/villages/library-atlas.png'
IMAGE_URL = '/_content/Valour.Client/media/villages/library-atlas.vtex.bin'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--exteriors', required=True, type=Path)
    parser.add_argument('--interiors', required=True, type=Path)
    parser.add_argument('--update-lock', action='store_true', help='Record changed recipes for visual review; never use in release automation.')
    parser.add_argument('--release-zip', type=Path, help='Write a private build-input archive, including the asset licenses.')
    args = parser.parse_args()
    manifest = json.loads(MANIFEST.read_text())
    curation = json.loads((Path(__file__).parent / 'curation.json').read_text())
    objects, proofs = load_objects(curation, args.interiors)
    lock_path = Path(__file__).parent / 'curation.lock.json'
    if not args.update_lock:
        check_lock(proofs, lock_path)
    sheets, images, offset = [], [], 0
    sources = [('exterior', args.exteriors / 'Modern_Exteriors_16x16/Modern_Exteriors_Complete_Tileset.png')]
    sources += [(key, args.interiors / path) for key, path in curation['sources'].items()]
    offsets = {}
    for key, path in sources:
        data = path.read_bytes()
        image = Image.open(path).convert('RGBA')
        offsets[key] = offset // 16
        sheets.append({'fileName': path.name, 'sha256': hashlib.sha256(data).hexdigest(),
                       'width': image.width, 'height': image.height, 'offsetX': 0, 'offsetY': offset})
        images.append((image, offset))
        offset += (image.height + 15) // 16 * 16
    object_positions = {}
    groups = sorted({entry['group'] for entry in curation['objects']})
    for group in groups:
        entries = [entry for entry in curation['objects'] if entry['group'] == group]
        x, y, row_height, positions = 16, 16, 0, []
        for entry in entries:
            art = objects[entry['key']]
            require(art.width <= 480, f'Object is too wide for its source board: {entry["key"]}')
            if x + art.width + 16 > 512:
                x, y, row_height = 16, y + row_height + 16, 0
            positions.append((entry['key'], art, x, y))
            object_positions[entry['key']] = (x // 16, (offset + y) // 16)
            x += art.width + 16
            row_height = max(row_height, art.height)
        board = Image.new('RGBA', (512, y + row_height + 16))
        for key, art, x, y in positions:
            board.paste(art, (x, y))
        sheets.append({'fileName': f'{group} objects', 'sha256': hashlib.sha256(board.tobytes()).hexdigest(),
                       'width': board.width, 'height': board.height, 'offsetX': 0, 'offsetY': offset})
        images.append((board, offset))
        offset += board.height
    width = max(image.width for image, _ in images)
    source = Image.new('RGBA', (width, offset))
    for image, y in images:
        source.paste(image, (0, y))
    manifest['source'] = {'width': width, 'height': offset, 'fileName': 'Village library', 'sheets': sheets, 'sha256': hashlib.sha256(source.tobytes()).hexdigest()}
    definitions = {d['Key']: d for d in manifest['definitions']}
    curated_keys = set(objects) | {entry[0] for entry in curation['floors']}
    for key in set(manifest.get('curationKeys', [])) - curated_keys:
        definitions.pop(key, None)
    manifest['curationKeys'] = sorted(curated_keys)
    for entry in curation['objects']:
        key, name = entry['key'], entry['name']
        x, y = object_positions[key]
        w, h = objects[key].width // 16, objects[key].height // 16
        fw, fh = entry['footprint']
        collision = ['empty'] * (w * h)
        if entry.get('blocking', True):
            for cy in range(h - fh, h):
                for cx in range(fw):
                    collision[cy * w + cx] = 'solid'
        definitions[key] = {'Key': key, 'Name': name, 'Kind': 'Sprite', 'GroupKey': entry['group'],
            'X': x, 'Y': y, 'SourceX': x, 'SourceY': y,
            'Width': w, 'Height': h, 'FootprintWidth': fw, 'FootprintHeight': fh, 'Collision': collision,
            'PlacementLayer': entry.get('layer', 'Furniture'), 'SupportsItems': entry.get('supportsItems', False)}
    for key, name, x, y in curation['floors']:
        definitions[key] = {'Key': key, 'Name': name, 'Kind': 'Tile', 'GroupKey': 'Floors',
            'X': x, 'Y': offsets['floors'] + y, 'SourceX': x, 'SourceY': offsets['floors'] + y,
            'Width': 1, 'Height': 1, 'Collision': ['empty'], 'TerrainKey': key,
            'TerrainRole': 'Base', 'TerrainDirection': 'None', 'TerrainAgainst': '', 'TerrainWeight': 1}
    for key, cells in curation.get('collisionOverrides', {}).items():
        definition = definitions[key]
        for cell in cells:
            definition['Collision'][cell['y'] * definition['Width'] + cell['x']] = cell['state']
    manifest['definitions'] = list(definitions.values())
    terrains = {t['Key']: t for t in manifest['terrains']}
    for key, name, *_ in curation['floors']:
        terrains[key] = {'Key': key, 'Name': name, 'Priority': 30}
    manifest['terrains'] = list(terrains.values())
    manifest['wallSets'] = [{'Key': key, 'Name': name, 'Layout': 'RoomBuilder', 'Image': IMAGE_URL,
        'TileSize': 16, 'OriginX': x, 'OriginY': offsets['walls'] * 16 + y,
        'SourceOriginX': x, 'SourceOriginY': offsets['walls'] * 16 + y,
        'Columns': 8, 'Rows': 7, 'FrameCount': 47, 'PreviewFrame': 19,
        'TopColor': top, 'FaceColor': face} for key, name, x, y, top, face in curation['walls']]
    module = (ROOT / 'Valour/Client/wwwroot/ts/VillageTilesetPacking.js').as_uri()
    js = f'''import {{ createTilesetPackPlan, compileTilesetManifest }} from {json.dumps(module)};
let input = ''; for await (const chunk of process.stdin) input += chunk;
const {{ manifest, url }} = JSON.parse(input);
const plan = createTilesetPackPlan(manifest, manifest.source.width, manifest.source.height);
process.stdout.write(JSON.stringify({{plan, result: compileTilesetManifest(manifest, plan, url, manifest.source.sha256)}}));'''
    packed = json.loads(subprocess.run(['node', '--input-type=module', '-e', js],
        input=json.dumps({'manifest': manifest, 'url': IMAGE_URL}), text=True, capture_output=True, check=True).stdout)
    plan = packed['plan']
    atlas = Image.new('RGBA', (plan['atlasWidth'], plan['atlasHeight']))
    for r in plan['regions']:
        x, y, w, h = (r[k] for k in ('sourceX', 'sourceY', 'width', 'height'))
        tile = source.crop((x, y, x + w, y + h))
        ax, ay = r['atlasX'], r['atlasY']
        atlas.paste(tile, (ax, ay))
        # Match the browser packer's one-pixel extrusion inside the transparent tile gutter.
        atlas.paste(tile.crop((0, 0, w, 1)), (ax, ay - 1))
        atlas.paste(tile.crop((0, h - 1, w, h)), (ax, ay + h))
        atlas.paste(tile.crop((0, 0, 1, h)), (ax - 1, ay))
        atlas.paste(tile.crop((w - 1, 0, w, h)), (ax + w, ay))
        for dx, sx in [(-1, 0), (w, w - 1)]:
            for dy, sy in [(-1, 0), (h, h - 1)]:
                atlas.putpixel((ax + dx, ay + dy), tile.getpixel((sx, sy)))
    ART.parent.mkdir(parents=True, exist_ok=True)
    result = packed['result']['manifest']
    verify_object_pixels(atlas, {d['Key']: d for d in result['definitions']}, proofs)
    atlas.save(ART, optimize=True)
    result['imageSha256'] = hashlib.sha256(ART.read_bytes()).hexdigest()
    url = urlsplit(IMAGE_URL)
    query = dict(parse_qsl(url.query)); query['v'] = result['imageSha256'][:16]
    result['image'] = urlunsplit(url._replace(query=urlencode(query)))
    for wall in result['wallSets']:
        wall['Image'] = result['image']
    result['credits'] = [{'name': 'Modern Exteriors & Modern Interiors', 'author': 'LimeZu', 'url': 'https://limezu.itch.io/'}]
    MANIFEST.write_text(json.dumps(result, indent=2) + '\n')
    if args.update_lock:
        lock_path.write_text(json.dumps(proofs, indent=2) + '\n')
    if args.release_zip:
        args.release_zip.parent.mkdir(parents=True, exist_ok=True)
        temporary = args.release_zip.with_suffix('.zip.tmp')
        with ZipFile(temporary, 'w', ZIP_DEFLATED) as package:
            for path in (ART, MANIFEST):
                package.write(path, path.relative_to(ROOT).as_posix())
            package.write(args.interiors / 'LICENSE.txt', 'licenses/Modern_Interiors_LICENSE.txt')
            package.write(args.exteriors / 'Modern_Exteriors_License.pdf', 'licenses/Modern_Exteriors_License.pdf')
            package.writestr('README.txt',
                'Private Valour Village build inputs\n\n'
                'Art: Modern Interiors and Modern Exteriors by LimeZu (https://limezu.itch.io/).\n'
                'Extract the Valour/ folder at the repository root before building.\n'
                'The app build verifies the PNG and generates library-atlas.vtex.bin.\n'
                'The readable PNG is excluded from the published app. Keep this ZIP private.\n'
                'Do not publish this archive as an asset download or commit it to a public repository.\n'
                'The included original licenses govern the artwork. In-game attribution links to LimeZu.\n'
                'See Docs/VillageTilesets.md for packaging and verification.\n')
        temporary.replace(args.release_zip)
    print(f'{len(result["definitions"])} definitions, {len(result["wallSets"])} wall styles; atlas {atlas.width}×{atlas.height}, {ART.stat().st_size:,} bytes')

if __name__ == '__main__':
    main()
