#!/usr/bin/env python3
"""Make a published village snapshot portable and suitable for a release seed."""
import argparse
import json
from pathlib import Path


FIELDS = {
    'Maps': 'Id MapType Name ParentBuildingId Width Height TileSize SpawnX SpawnY TilesetKey AmbientColor',
    'Buildings': 'Id MapId InteriorMapId PlotId ChannelId Name Description X Y Width Height DoorX DoorY SpriteKey VoiceMode ForSale Price',
    'Plots': 'Id MapId Name X Y Width Height EditMode ForSale Price',
    'Objects': 'Id MapId DefinitionKey X Y Rotation ZIndex BlocksMovement',
    'Chunks': 'Id MapId ChunkX ChunkY LayerData CollisionData',
}


def portable_snapshot(source):
    result = {kind: [{key: item[key] for key in fields.split() if item.get(key) is not None}
                     for item in source[kind]] for kind, fields in FIELDS.items()}
    result['Maps'].sort(key=lambda item: (item['MapType'], item['Id']))
    map_order = {item['Id']: index for index, item in enumerate(result['Maps'])}
    for kind in ('Buildings', 'Plots', 'Objects'):
        result[kind].sort(key=lambda item: (map_order[item['MapId']], item.get('ZIndex', 0), item['Y'], item['X'], item['Id']))
    result['Chunks'].sort(key=lambda item: (map_order[item['MapId']], item['ChunkY'], item['ChunkX']))
    items = [item for kind in FIELDS for item in result[kind]]
    ids = {item['Id']: index for index, item in enumerate(items, 1)}
    if len(ids) != len(items):
        raise ValueError('Snapshot entity IDs must be unique.')
    referenced_channels = sorted({item['ChannelId'] for item in result['Buildings'] if 'ChannelId' in item})
    channels = {old: index for index, old in enumerate(referenced_channels, 1)}
    result['ChannelTypes'] = {str(channels[old]): source['ChannelTypes'][str(old)] for old in referenced_channels}
    for item in items:
        for key in ('Id', 'MapId', 'ParentBuildingId', 'InteriorMapId', 'PlotId'):
            if key in item:
                item[key] = ids[item[key]]
        if 'ChannelId' in item:
            item['ChannelId'] = channels[item['ChannelId']]
    return result


def write_snapshot(snapshot):
    lines = ['{']
    for kind in FIELDS:
        entries = snapshot[kind]
        lines.append(f'  "{kind}": [' if entries else f'  "{kind}": [],')
        for index, item in enumerate(entries):
            lines.append('    ' + json.dumps(item, ensure_ascii=False) + (',' if index + 1 < len(entries) else ''))
        if entries:
            lines.append('  ],')
    lines.append('  "ChannelTypes": ' + json.dumps(snapshot['ChannelTypes']))
    return '\n'.join([*lines, '}', ''])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('source', type=Path, help='Exported village_template.published_json')
    parser.add_argument('output', type=Path, help='A new, versioned seed JSON file')
    args = parser.parse_args()
    if args.output.exists():
        parser.error('Use a new output filename; published migration seeds are immutable.')
    snapshot = portable_snapshot(json.loads(args.source.read_text()))
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(write_snapshot(snapshot))
    print(f'Exported {len(snapshot["Maps"])} maps, {len(snapshot["Buildings"])} buildings and {len(snapshot["Objects"])} objects to {args.output}')


if __name__ == '__main__':
    main()
