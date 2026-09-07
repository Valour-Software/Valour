import json
import tempfile
import unittest
from pathlib import Path
from PIL import Image, ImageDraw
from library_art import closed_crop, normalize_object, digest, load_objects, check_lock, verify_object_pixels


class WholeObjectTests(unittest.TestCase):
    def cabinet(self):
        image = Image.new('RGBA', (64, 80))
        draw = ImageDraw.Draw(image)
        draw.rectangle((18, 10, 45, 57), fill='#8d604b')
        draw.rectangle((20, 12, 43, 22), fill='#ddbd84')
        draw.rectangle((20, 24, 43, 54), fill='#8cd7e0')
        return image

    def test_glass_cabinet_header_cannot_be_cut_off(self):
        with self.assertRaisesRegex(ValueError, 'cuts through artwork'):
            closed_crop(self.cabinet(), [16, 26, 32, 32], 'living.cabinet.glass')
        complete = closed_crop(self.cabinet(), [16, 8, 32, 64], 'living.cabinet.glass')
        self.assertEqual(normalize_object(complete, 'cabinet').size, (32, 48))

    def test_out_of_bounds_does_not_silently_add_blank_squares(self):
        with self.assertRaisesRegex(ValueError, 'Out-of-bounds'):
            closed_crop(self.cabinet(), [48, 0, 32, 32], 'desk')

    def test_alpha_padding_is_removed_without_losing_source_pixels(self):
        original = self.cabinet()
        normalized = normalize_object(original, 'cabinet')
        self.assertEqual(original.crop(original.getbbox()).tobytes(), normalized.crop(normalized.getbbox()).tobytes())
        self.assertEqual(normalized.getbbox()[3], normalized.height)

    def test_empty_object_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'Empty artwork'):
            normalize_object(Image.new('RGBA', (32, 32)), 'desk')

    def test_missing_atlas_square_fails_pixel_validation(self):
        approved = normalize_object(self.cabinet(), 'cabinet')
        atlas = Image.new('RGBA', (64, 80)); atlas.paste(approved, (16, 16))
        definitions = {'cabinet': {'X': 1, 'Y': 1, 'Width': 2, 'Height': 3}}
        proofs = {'cabinet': {'size': [32, 48], 'rgbaSha256': digest(approved)}}
        verify_object_pixels(atlas, definitions, proofs)
        ImageDraw.Draw(atlas).rectangle((16, 16, 31, 31), fill=(0, 0, 0, 0))
        with self.assertRaisesRegex(ValueError, 'pixels differ'):
            verify_object_pixels(atlas, definitions, proofs)

    def test_truncated_definition_fails_even_with_nonempty_art(self):
        image = normalize_object(self.cabinet(), 'cabinet')
        with self.assertRaisesRegex(ValueError, 'Truncated object dimensions'):
            verify_object_pixels(image, {'cabinet': {'X': 0, 'Y': 0, 'Width': 2, 'Height': 2}},
                                 {'cabinet': {'size': [32, 48], 'rgbaSha256': digest(image)}})

    def test_assembly_requires_every_cell_and_nonoverlapping_parts(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); Image.new('RGBA', (16, 32), 'red').save(root / 'part.png')
            entry = {'key': 'sofa', 'footprint': [2, 1], 'parts': [
                {'file': 'part.png', 'x': 0, 'y': 0}, {'file': 'part.png', 'x': 32, 'y': 0}]}
            curation = {'objects': [entry]}
            with self.assertRaisesRegex(ValueError, 'Missing assembly cells'):
                load_objects(curation, root)
            entry['parts'][1]['x'] = 0
            with self.assertRaisesRegex(ValueError, 'Overlapping assembly parts'):
                load_objects(curation, root)
            entry['parts'][1]['x'] = 16
            objects, _ = load_objects(curation, root)
            self.assertEqual(objects['sofa'].size, (32, 32))

    def test_source_change_requires_a_new_review(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / 'lock.json'
            path.write_text(json.dumps({'desk': {'rgbaSha256': 'approved'}}))
            with self.assertRaisesRegex(ValueError, 'Source or recipe changed'):
                check_lock({'desk': {'rgbaSha256': 'changed'}}, path)


if __name__ == '__main__':
    unittest.main()
