#!/usr/bin/env python3
"""Restore the matching Village build atlas from its protected CDN package."""
import argparse
import hashlib
import io
import json
import os
from pathlib import Path
import struct
import tempfile
import urllib.error
import urllib.request
from urllib.parse import urlsplit
from zipfile import ZipFile

ROOT = Path(__file__).resolve().parents[2]
MANIFEST = Path('Valour/Client/wwwroot/tilesets/exterior-tileset-0.json')
ATLAS = Path('Valour/Client/wwwroot/media/villages/library-atlas.png')
MAX_BYTES = 64 * 1024 * 1024


def validate(png, manifest):
    if hashlib.sha256(png).hexdigest() != manifest['imageSha256'].lower():
        raise ValueError('Village atlas fingerprint does not match this checkout.')
    if len(png) < 24 or png[:8] != b'\x89PNG\r\n\x1a\n' or png[12:16] != b'IHDR':
        raise ValueError('Village atlas is not a PNG.')
    if struct.unpack('>II', png[16:24]) != (manifest['atlas']['width'], manifest['atlas']['height']):
        raise ValueError('Village atlas dimensions do not match this checkout.')


class NoRedirects(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def decode(data):
    if len(data) < 44 or data[:8] != b'VLTEX001':
        raise ValueError('Village CDN response is not a protected atlas.')
    length, checksum, state = struct.unpack('<III', data[8:20])
    if length > 16 * 1024 * 1024 or len(data) != length + 20:
        raise ValueError('Protected Village atlas length is invalid.')
    png = bytearray(length)
    actual = 2166136261
    for i, value in enumerate(data[20:]):
        state ^= (state << 13) & 0xffffffff
        state ^= state >> 17
        state ^= (state << 5) & 0xffffffff
        png[i] = value ^ (state & 255)
        actual = ((actual ^ png[i]) * 16777619) & 0xffffffff
    if actual != checksum:
        raise ValueError('Protected Village atlas checksum is invalid.')
    return bytes(png)


def download(manifest):
    fingerprint = manifest['imageSha256'].lower()
    if len(fingerprint) != 64 or any(c not in '0123456789abcdef' for c in fingerprint):
        raise ValueError('Village manifest fingerprint is invalid.')
    url = os.environ.get('VILLAGE_ATLAS_URL') or (
        'https://public-cdn.valour.gg/valour-public/villages/atlas/' + fingerprint + '.vtex.bin')
    parsed = urlsplit(url)
    if parsed.scheme != 'https' or not parsed.hostname or parsed.username or parsed.password:
        raise ValueError('VILLAGE_ATLAS_URL must be an HTTPS protected-atlas URL.')
    try:
        opener = urllib.request.build_opener(NoRedirects)
        request = urllib.request.Request(url, headers={'User-Agent': 'Valour-CI/1.0'})
        with opener.open(request, timeout=120) as response:
            data = response.read(MAX_BYTES + 1)
    except (urllib.error.URLError, ValueError, OSError):
        raise ValueError('Village atlas download failed. Publish the matching fingerprinted .vtex.bin to the CDN or set VILLAGE_ATLAS_URL; redirects are not accepted.') from None
    if len(data) > MAX_BYTES:
        raise ValueError('Village CDN response exceeds the 64 MiB limit.')
    return decode(data)


def restore(root, archive=None):
    manifest = json.loads((root / MANIFEST).read_text())
    target = root / ATLAS
    if archive is None and target.exists():
        validate(target.read_bytes(), manifest)
        print('Existing Village atlas matches this checkout.')
        return
    if archive is not None:
        if archive.stat().st_size > MAX_BYTES:
            raise ValueError('Private Village ZIP exceeds the 64 MiB limit.')
        data = archive.read_bytes()
    else:
        png = download(manifest)
        data = None
    if data is not None:
        with ZipFile(io.BytesIO(data)) as package:
            required = [MANIFEST.as_posix(), ATLAS.as_posix()]
            for name in required:
                if package.namelist().count(name) != 1 or package.getinfo(name).file_size > MAX_BYTES:
                    raise ValueError('Private Village ZIP has missing, duplicate or oversized build inputs.')
            if json.loads(package.read(required[0])) != manifest:
                raise ValueError('Private Village ZIP manifest does not match this checkout. Restore the matching release package.')
            png = package.read(required[1])
    validate(png, manifest)
    # Read only the two expected entries; never extract paths or overwrite source metadata.
    target.parent.mkdir(parents=True, exist_ok=True)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(dir=target.parent, delete=False) as stream:
            temporary = Path(stream.name)
            stream.write(png)
        temporary.replace(target)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)
    print('Restored and verified the private Village atlas for this checkout.')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--archive', type=Path, help='Use a local private release ZIP instead of downloading.')
    parser.add_argument('--root', type=Path, default=ROOT)
    args = parser.parse_args()
    try:
        restore(args.root.resolve(), args.archive)
    except Exception as error:
        if isinstance(error, ValueError):
            raise SystemExit(str(error)) from None
        raise SystemExit('Could not restore private Village build inputs. Check the package and filesystem permissions.') from None


if __name__ == '__main__':
    main()
