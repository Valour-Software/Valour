#!/usr/bin/env python3
"""Validate the generated atlas and optionally render a catalog contact sheet."""
import argparse
import hashlib
import json
from pathlib import Path
from PIL import Image, ImageDraw
from library_art import load_objects, check_lock, verify_object_pixels, require

ROOT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--contact-sheet', type=Path)
    parser.add_argument('--interiors', type=Path, help='Also verify all recipes against the purchased original files.')
    parser.add_argument('--reference-dir', type=Path, help='Write private whole-object images for browser pixel regression tests.')
    args = parser.parse_args()
    manifest = json.loads((ROOT / 'Valour/Client/wwwroot/tilesets/exterior-tileset-0.json').read_text())
    art = ROOT / 'Valour/Client/wwwroot/media/villages/library-atlas.png'
    atlas = Image.open(art).convert('RGBA')
    require(atlas.size == (manifest['atlas']['width'], manifest['atlas']['height']), 'Atlas dimensions differ from the manifest')
    require(hashlib.sha256(art.read_bytes()).hexdigest() == manifest.get('imageSha256'), 'Atlas PNG does not match the manifest fingerprint')
    keys = set()
    curation = json.loads((Path(__file__).parent / 'curation.json').read_text())
    lock_path = Path(__file__).parent / 'curation.lock.json'
    proofs = json.loads(lock_path.read_text())
    require(set(proofs) == {entry['key'] for entry in curation['objects']}, 'Curated objects do not match the reviewed lock')
    for entry in curation['objects']:
        recipe_hash = hashlib.sha256(json.dumps(entry, sort_keys=True).encode()).hexdigest()
        require(recipe_hash == proofs[entry['key']]['recipeSha256'], f'Recipe changed without rebuilding and reviewing: {entry["key"]}')
    if args.interiors:
        objects, proofs = load_objects(curation, args.interiors)
        check_lock(proofs, lock_path)
        if args.reference_dir:
            args.reference_dir.mkdir(parents=True, exist_ok=True)
            for key, image in objects.items():
                image.save(args.reference_dir / f'{key}.png')
    elif args.reference_dir:
        parser.error('--reference-dir requires --interiors')
    verify_object_pixels(atlas, {d['Key']: d for d in manifest['definitions']}, proofs)
    added = {item['key'] for item in curation['objects']} | {item[0] for item in curation['floors']}
    previews = []
    for item in manifest['definitions']:
        key = item['Key']
        assert key not in keys, f'Duplicate definition: {key}'
        keys.add(key)
        x, y, width, height = [item[field] * manifest['tileSize'] for field in ('X', 'Y', 'Width', 'Height')]
        assert x >= 0 and y >= 0 and x + width <= atlas.width and y + height <= atlas.height, key
        crop = atlas.crop((x, y, x + width, y + height))
        assert crop.getbbox(), f'Empty art: {key}'
        assert len(item.get('Collision', [])) == item['Width'] * item['Height'], f'Collision size: {key}'
        for axis in ('Width', 'Height'):
            assert 0 <= item.get(f'Footprint{axis}', 0) <= item[axis], f'Footprint: {key}'
        if key.startswith('floor.'):
            assert crop.getchannel('A').getextrema() == (255, 255), f'Transparent floor: {key}'
        if key in added:
            previews.append((key, item['Name'], crop))
    assert added <= keys, f'Missing curated items: {added - keys}'
    for wall in manifest['wallSets']:
        x, y = wall['OriginX'], wall['OriginY']
        assert wall['Layout'] in ('RoomBuilder', 'Blob47')
        assert wall['Image'] == manifest['image']
        assert x >= 0 and y >= 0 and x + wall['Columns'] * wall['TileSize'] <= atlas.width
        assert y + wall['Rows'] * wall['TileSize'] <= atlas.height
    if args.contact_sheet:
        args.contact_sheet.parent.mkdir(parents=True, exist_ok=True)
        for start in range(0, len(previews), 42):
            page = previews[start:start + 42]
            sheet = Image.new('RGBA', (1200, ((len(page) + 5) // 6) * 150), '#25343d')
            draw = ImageDraw.Draw(sheet)
            for index, (key, name, crop) in enumerate(page):
                scale = min(3, 106 / max(crop.size))
                crop = crop.resize((int(crop.width * scale), int(crop.height * scale)), Image.Resampling.NEAREST)
                x, y = (index % 6) * 200, (index // 6) * 150
                sheet.alpha_composite(crop, (x + (200 - crop.width) // 2, y + 6))
                draw.text((x + 4, y + 116), name, fill='white')
                draw.text((x + 4, y + 132), key, fill='#a6bcc9')
            path = args.contact_sheet if start == 0 else args.contact_sheet.with_stem(f'{args.contact_sheet.stem}-{start // 42 + 1}')
            sheet.save(path)
    print(json.dumps({'definitions': len(keys), 'wallStyles': len(manifest['wallSets']),
        'curatedDefinitions': len(added), 'approvedWholeObjects': len(proofs), 'atlasBytes': art.stat().st_size,
        'atlasSha256': hashlib.sha256(art.read_bytes()).hexdigest()}, indent=2))


if __name__ == '__main__':
    main()
