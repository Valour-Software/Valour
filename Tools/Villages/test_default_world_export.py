import importlib.util
import json
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('world_export', Path(__file__).with_name('export-default-world.py'))
export = importlib.util.module_from_spec(spec)
spec.loader.exec_module(export)


class DefaultWorldExportTests(unittest.TestCase):
    def setUp(self):
        self.world = json.loads((ROOT / 'Valour/Database/Seeds/Villages/default-world-v1.json').read_text())

    def test_reexport_is_byte_stable(self):
        exported = export.write_snapshot(export.portable_snapshot(self.world))
        self.assertEqual(exported, (ROOT / 'Valour/Database/Seeds/Villages/default-world-v1.json').read_text())

    def test_runtime_ids_ownership_and_unlinked_channels_are_removed(self):
        expected = export.portable_snapshot(self.world)
        for kind in export.FIELDS:
            for item in self.world[kind]:
                for key in ('Id', 'MapId', 'ParentBuildingId', 'InteriorMapId', 'PlotId'):
                    if key in item:
                        item[key] += 9000000000000
                item.update(PlanetId=12345, OwnerMemberId=54321, SaleId='private', Planet={'Name': 'private'}, ArchivedAt=None, Version=87)
        self.world['ChannelTypes']['99999'] = 7
        self.assertEqual(expected, export.portable_snapshot(self.world))

    def test_dangling_links_and_duplicate_ids_are_rejected(self):
        self.world['Maps'][1]['ParentBuildingId'] = -50
        with self.assertRaises(KeyError):
            export.portable_snapshot(self.world)
        self.world['Maps'][1]['ParentBuildingId'] = self.world['Buildings'][0]['Id']
        self.world['Objects'][0]['Id'] = self.world['Maps'][0]['Id']
        with self.assertRaises(ValueError):
            export.portable_snapshot(self.world)


if __name__ == '__main__':
    unittest.main()
