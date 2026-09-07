import base64
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
PACKER = ROOT / 'Valour/BuildTools/VillageAtlasPacker/bin/Debug/net11.0/VillageAtlasPacker.dll'


class AtlasPackagingTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        folder = Path(self.temporary.name)
        vector = json.loads((ROOT / 'Valour/Tests/Fixtures/village-atlas-vector.json').read_text())
        self.png = folder / 'private.png'
        self.png.write_bytes(base64.b64decode(vector['png']))
        self.expected = base64.b64decode(vector['protected'])
        self.output = folder / 'atlas.vtex.bin'
        self.manifest = folder / 'tileset.json'
        image = '/_content/Valour.Client/media/villages/library-atlas.vtex.bin?v=fixture'
        self.data = {'image': image, 'imageSha256': hashlib.sha256(self.png.read_bytes()).hexdigest(),
                     'atlas': {'width': 1, 'height': 1}, 'wallSets': [{'Image': image}]}
        self.manifest.write_text(json.dumps(self.data))

    def run_packer(self):
        return subprocess.run(['dotnet', str(PACKER), str(self.manifest), str(self.png), str(self.output)],
                              capture_output=True, text=True)

    def test_parallel_builds_produce_the_same_package_without_temporary_files(self):
        with ThreadPoolExecutor(max_workers=4) as workers:
            results = list(workers.map(lambda _: self.run_packer(), range(4)))
        for result in results:
            self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.output.read_bytes(), self.expected)
        self.assertEqual(list(self.output.parent.glob('*.tmp')), [])

    def test_tampered_output_is_regenerated_from_verified_input(self):
        self.output.write_bytes(b'corrupted')
        result = self.run_packer()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(self.output.read_bytes(), self.expected)

    def test_wrong_hash_dimensions_and_readable_official_urls_are_rejected(self):
        cases = [
            {'imageSha256': '0' * 64},
            {'atlas': {'width': 2, 'height': 1}},
            {'image': '/_content/Valour.Client/media/villages/library-atlas.png'},
            {'wallSets': [{'Image': '/unprotected.png'}]},
        ]
        for override in cases:
            with self.subTest(override=override):
                self.manifest.write_text(json.dumps({**self.data, **override}))
                self.assertNotEqual(self.run_packer().returncode, 0)
                self.assertFalse(self.output.exists())


if __name__ == '__main__':
    unittest.main()
