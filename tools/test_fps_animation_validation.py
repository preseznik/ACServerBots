"""Reject damaged animation tracks independently of Blender and the manifest hashes."""
import copy
import json
from pathlib import Path
import struct
import tempfile
import unittest

from validate_fps_modern_assets import _validate_animation_family, inspect_ksanim


class AnimationValidationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.assets = Path(__file__).resolve().parent.parent / 'AssettoServer.RaceControl.Core/Assets/Fps/Modern'
        cls.catalog = json.loads((cls.assets / 'asrc-modern-assets.json').read_text())['operatorAnimations']
        cls.tracks = inspect_ksanim(cls.assets / 'asrc_modern_operator_jog_forward.ksanim')

    def validate_modified(self, mutate, name='jog_forward'):
        tracks = copy.deepcopy(inspect_ksanim(self.assets / f'asrc_modern_operator_{name}.ksanim'))
        tracks = {k: [list(f) for f in v] for k, v in tracks.items()}
        mutate(tracks)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / f'asrc_modern_operator_{name}.ksanim'
            with path.open('wb') as f:
                f.write(struct.pack('<II', 2, len(tracks)))
                for key, frames in tracks.items():
                    encoded = key.encode()
                    f.write(struct.pack('<I', len(encoded)) + encoded + struct.pack('<I', len(frames)))
                    for frame in frames:
                        f.write(struct.pack('<10f', *frame))
            _validate_animation_family([path], True, set(self.tracks), self.catalog)

    def test_root_travel_rejected(self):
        with self.assertRaisesRegex(ValueError, 'actor root'):
            self.validate_modified(lambda t: t['_rootJoint'][10].__setitem__(4, 5))

    def test_hip_root_motion_rejected(self):
        with self.assertRaisesRegex(ValueError, 'root travel'):
            self.validate_modified(lambda t: t['mixamorig:Hips_01'][10].__setitem__(4, 100))

    def test_loop_pop_rejected(self):
        with self.assertRaisesRegex(ValueError, 'discontinuous'):
            self.validate_modified(lambda t: t['mixamorig:LeftLeg_063'][-1].__setitem__(0, 0.5))

    def test_static_ankles_rejected(self):
        def freeze(t):
            t['mixamorig:LeftFoot_064'] = [t['mixamorig:LeftFoot_064'][0]] * 29
        with self.assertRaisesRegex(ValueError, 'static ankles'):
            self.validate_modified(freeze)

    def test_action_overlay_cannot_target_hips(self):
        def add_hips(t):
            t['mixamorig:Hips_01'] = [list(self.tracks['mixamorig:Hips_01'][0])] * 7
        with self.assertRaisesRegex(ValueError, 'upper-body'):
            self.validate_modified(add_hips, 'fire')

    def test_unknown_bones_rejected(self):
        def unknown(t):
            t['Quaternius_Control'] = t.pop('mixamorig:Hips_01')
        with self.assertRaisesRegex(ValueError, 'missing KN5 nodes'):
            self.validate_modified(unknown)

    def test_nonfinite_tracks_rejected(self):
        with self.assertRaisesRegex(ValueError, 'Invalid KSANIM frames'):
            self.validate_modified(lambda t: t['mixamorig:Hips_01'][4].__setitem__(4, float('nan')))


if __name__ == '__main__':
    unittest.main()
