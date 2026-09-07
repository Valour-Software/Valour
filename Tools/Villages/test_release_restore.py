import base64
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import MagicMock, patch
from zipfile import ZipFile

spec = importlib.util.spec_from_file_location('restore', Path(__file__).with_name('restore-release-assets.py'))
restore = importlib.util.module_from_spec(spec)
spec.loader.exec_module(restore)


class RestoreTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        vector = json.loads((restore.ROOT / 'Valour/Tests/Fixtures/village-atlas-vector.json').read_text())
        self.vector = vector
        self.png = base64.b64decode(vector['png'])
        self.manifest = {'imageSha256': hashlib.sha256(self.png).hexdigest(), 'atlas': {'width': 1, 'height': 1}}
        path = self.root / restore.MANIFEST
        path.parent.mkdir(parents=True)
        path.write_text(json.dumps(self.manifest))

    def test_restores_download_without_changing_manifest(self):
        with patch.object(restore, 'download', return_value=self.png):
            restore.restore(self.root)
        self.assertEqual((self.root / restore.ATLAS).read_bytes(), self.png)
        self.assertEqual(json.loads((self.root / restore.MANIFEST).read_text()), self.manifest)

    def test_rejects_wrong_download_before_writing(self):
        with patch.object(restore, 'download', return_value=b'wrong'):
            with self.assertRaisesRegex(ValueError, 'fingerprint'):
                restore.restore(self.root)
        self.assertFalse((self.root / restore.ATLAS).exists())

    def test_existing_atlas_is_verified_without_network(self):
        target = self.root / restore.ATLAS
        target.parent.mkdir(parents=True)
        target.write_bytes(self.png)
        with patch.object(restore, 'download', side_effect=AssertionError('network')):
            restore.restore(self.root)

    def test_wrong_manifest_in_zip_cannot_overwrite_checkout(self):
        archive = self.root / 'release.zip'
        with ZipFile(archive, 'w') as package:
            package.writestr(restore.MANIFEST.as_posix(), '{}')
            package.writestr(restore.ATLAS.as_posix(), self.png)
        with self.assertRaisesRegex(ValueError, 'manifest'):
            restore.restore(self.root, archive)
        self.assertEqual(json.loads((self.root / restore.MANIFEST).read_text()), self.manifest)

    def test_protected_format_matches_shared_vector(self):
        self.assertEqual(restore.decode(base64.b64decode(self.vector['protected'])), self.png)

    def test_download_uses_fingerprinted_url_and_ci_user_agent(self):
        opener = MagicMock()
        opener.open.return_value.__enter__.return_value.read.return_value = base64.b64decode(self.vector['protected'])
        with patch.dict(restore.os.environ, {}, clear=True), patch.object(restore.urllib.request, 'build_opener', return_value=opener):
            self.assertEqual(restore.download(self.manifest), self.png)
        request = opener.open.call_args.args[0]
        self.assertEqual(request.full_url, 'https://public-cdn.valour.gg/valour-public/villages/atlas/' + self.manifest['imageSha256'] + '.vtex.bin')
        self.assertEqual(request.get_header('User-agent'), 'Valour-CI/1.0')

    def test_rejects_insecure_override(self):
        with patch.dict(restore.os.environ, {'VILLAGE_ATLAS_URL': 'http://example.com/atlas'}):
            with self.assertRaisesRegex(ValueError, 'HTTPS'):
                restore.download(self.manifest)

    def test_corruption_is_rejected(self):
        data = bytearray(base64.b64decode(self.vector['protected']))
        data[-1] ^= 1
        with self.assertRaisesRegex(ValueError, 'checksum'):
            restore.decode(data)
        with self.assertRaisesRegex(ValueError, 'length'):
            restore.decode(data[:-1])


if __name__ == '__main__':
    unittest.main()
