"""Whole-object source recipes and pixel validation for the private Village atlas."""
import hashlib
import json
from pathlib import Path
from PIL import Image

TILE = 16


def require(condition, message):
    if not condition:
        raise ValueError(message)


def digest(image):
    return hashlib.sha256(image.convert('RGBA').tobytes()).hexdigest()


def closed_crop(image, rect, key):
    x, y, w, h = rect
    require(all(isinstance(n, int) for n in rect) and w > 0 and h > 0 and
            x >= 0 and y >= 0 and x + w <= image.width and y + h <= image.height,
            f'Out-of-bounds source rectangle: {key}')
    alpha = image.getchannel('A')
    for px in range(x, x + w):
        for inside, outside in [(y, y - 1), (y + h - 1, y + h)]:
            if alpha.getpixel((px, inside)) and 0 <= outside < image.height:
                require(not any(alpha.getpixel((nx, outside)) for nx in range(max(0, px - 1), min(image.width, px + 2))),
                        f'Selection cuts through artwork at {px},{inside}: {key}')
    for py in range(y, y + h):
        for inside, outside in [(x, x - 1), (x + w - 1, x + w)]:
            if alpha.getpixel((inside, py)) and 0 <= outside < image.width:
                require(not any(alpha.getpixel((outside, ny)) for ny in range(max(0, py - 1), min(image.height, py + 2))),
                        f'Selection cuts through artwork at {inside},{py}: {key}')
    return image.crop((x, y, x + w, y + h))


def normalize_object(image, key, canvas=(0, 0)):
    bounds = image.getchannel('A').getbbox()
    require(bounds is not None, f'Empty artwork: {key}')
    crop = image.crop(bounds)
    require(all(isinstance(n, int) and n >= 0 and n % TILE == 0 for n in canvas), f'Invalid object canvas: {key}')
    result = Image.new('RGBA', (max(canvas[0], ((crop.width + TILE - 1) // TILE) * TILE),
                                max(canvas[1], ((crop.height + TILE - 1) // TILE) * TILE)))
    result.paste(crop, ((result.width - crop.width) // 2, result.height - crop.height))
    # Transparent RGB values have no visual meaning and differ across PNG encoders.
    pixels = bytearray(result.tobytes())
    for index in range(0, len(pixels), 4):
        if pixels[index + 3] == 0:
            pixels[index:index + 3] = b'\0\0\0'
    return Image.frombytes('RGBA', result.size, bytes(pixels))


def load_objects(curation, interiors):
    objects, proofs, cache = {}, {}, {}

    def read(relative):
        path = (interiors / relative).resolve()
        require(path.is_relative_to(interiors.resolve()), f'Source is outside the art pack: {relative}')
        if relative not in cache:
            cache[relative] = Image.open(path).convert('RGBA')
        return cache[relative]

    for entry in curation['objects']:
        key = entry['key']
        require(key not in objects, f'Duplicate object recipe: {key}')
        sources = []
        if 'file' in entry:
            image = read(entry['file'])
            sources.append({'file': entry['file'], 'size': list(image.size), 'rgbaSha256': digest(image)})
        elif 'parts' in entry:
            parts = [(read(part['file']), part) for part in entry['parts']]
            require(bool(parts), f'Empty assembly: {key}')
            require(all(part['x'] >= 0 and part['y'] >= 0 and part['x'] % TILE == 0 and part['y'] % TILE == 0 for _, part in parts),
                    f'Assembly must align complete parts to the tile grid: {key}')
            image = Image.new('RGBA', (max(part['x'] + art.width for art, part in parts),
                                       max(part['y'] + art.height for art, part in parts)))
            occupied = set()
            for art, part in parts:
                cells = {(x, y) for y in range(part['y'], part['y'] + art.height, TILE)
                         for x in range(part['x'], part['x'] + art.width, TILE)}
                require(not occupied.intersection(cells), f'Overlapping assembly parts: {key}')
                occupied.update(cells)
                image.paste(art, (part['x'], part['y']))
                sources.append({'file': part['file'], 'size': list(art.size), 'rgbaSha256': digest(art)})
            require(len(occupied) == image.width * image.height // TILE ** 2, f'Missing assembly cells: {key}')
        else:
            relative = curation['sources'][entry['sheet']]
            original = read(relative)
            image = closed_crop(original, entry['rect'], key)
            sources.append({'file': relative, 'rect': entry['rect'], 'rgbaSha256': digest(image)})
        image = normalize_object(image, key, entry.get('canvas', (0, 0)))
        fw, fh = entry['footprint']
        require(0 < fw <= image.width // TILE and 0 < fh <= image.height // TILE, f'Invalid footprint: {key}')
        layer = entry.get('layer', 'Furniture')
        require(layer in ('Furniture', 'Surface', 'Floor'), f'Invalid placement layer: {key}')
        require(layer == 'Furniture' or not entry.get('blocking', True), f'{layer} objects must be nonblocking: {key}')
        require(not entry.get('supportsItems') or layer == 'Furniture', f'Invalid supporting surface: {key}')
        objects[key] = image
        proofs[key] = {'size': list(image.size), 'rgbaSha256': digest(image), 'sources': sources,
                       'recipeSha256': hashlib.sha256(json.dumps(entry, sort_keys=True).encode()).hexdigest()}
    return objects, proofs


def check_lock(proofs, path):
    require(path.exists(), 'The reviewed object lock is missing. Generate a review with --update-lock.')
    approved = json.loads(path.read_text())
    require(proofs.keys() == approved.keys(), 'Object recipes changed; regenerate and review the catalog with --update-lock.')
    for key, proof in proofs.items():
        require(proof == approved[key], f'Source or recipe changed for {key}; regenerate and review the catalog with --update-lock.')


def verify_object_pixels(atlas, definitions, proofs):
    for key, proof in proofs.items():
        require(key in definitions, f'Missing approved object: {key}')
        item = definitions[key]
        width, height = item['Width'] * TILE, item['Height'] * TILE
        require([width, height] == proof['size'], f'Truncated object dimensions: {key}')
        x, y = item['X'] * TILE, item['Y'] * TILE
        require(x >= 0 and y >= 0 and x + width <= atlas.width and y + height <= atlas.height, f'Atlas bounds: {key}')
        require(digest(atlas.crop((x, y, x + width, y + height))) == proof['rgbaSha256'], f'Atlas pixels differ from the complete approved object: {key}')
