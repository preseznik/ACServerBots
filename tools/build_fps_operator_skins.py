"""Generate selectable operator skins and portraits from the production rigs.

Run after build_fps_ghost_assets.py; source blends and KN5 geometry stay unchanged.
"""
import argparse
import json
from pathlib import Path
import sys

import bpy
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_fps_ghost_assets as ghost
from validate_fps_modern_assets import validate_modern_asset_set


def diffuse_node(prefix, group):
    material = bpy.data.materials[prefix + group]
    return next(node for node in material.node_tree.nodes
                if node.type == 'TEX_IMAGE' and node.image and 'txDiffuse' in node.name)


def desert_texture(source, destination, tint):
    width, height = source.size
    pixels = np.empty(width * height * 4, dtype=np.float32)
    source.pixels.foreach_get(pixels)
    rgba = pixels.reshape((height, width, 4))
    rgb = rgba[:, :, :3]
    luminance = rgb @ np.array([0.2126, 0.7152, 0.0722], dtype=np.float32)
    # Lift Nightwar's dark fabric into sand while retaining its seams and wear.
    shade = np.clip(np.sqrt(np.maximum(0, luminance)) * 1.35, 0.10, 1.0)
    rgba[:, :, :3] = np.clip(shade[:, :, None] * np.array(tint)
                            + (rgb - luminance[:, :, None]) * 0.05, 0, 1)
    image = bpy.data.images.new(destination.stem, width=width, height=height, alpha=True)
    image.pixels.foreach_set(rgba.ravel())
    image.filepath_raw = str(destination)
    image.file_format = 'PNG'
    image.save()
    return image


def build(output, cache):
    manifest_path = output / 'asrc-modern-assets.json'
    manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
    for model, blend, prefix in [('officer', 'officer-reference.blend', 'ASRC_OFFICER_'),
                                 ('ghost', 'ghost-production.blend', 'ASRC_GHOST_')]:
        bpy.ops.wm.open_mainfile(filepath=str(cache / blend), load_ui=False, use_scripts=False)
        rig = bpy.data.objects['ASRC_MODERN_OPERATOR_RIG']
        ghost.reset_pose(rig)
        original = {group: diffuse_node(prefix, group).image for group in ('UNIFORM', 'GEAR')}
        entry = manifest['operators'][model]
        skins = [{'id': 0, 'name': 'Nightwar' if model == 'ghost' else 'Standard issue',
                  'portrait': f'asrc_operator_{model}.png'},
                 {'id': 1, 'name': 'Blue-grey',
                  'uniform': entry['teamSkins']['team2Uniform'],
                  'gear': entry['teamSkins']['team2Gear'],
                  'portrait': f'asrc_operator_{model}_bluegrey.png'}]
        if model == 'ghost':
            for group, tint in [('UNIFORM', (0.78, 0.61, 0.39)), ('GEAR', (0.50, 0.36, 0.20))]:
                desert_texture(original[group], output / f'asrc_modern_ghost_desert_{group.lower()}.png', tint)
            skins.append({'id': 2, 'name': 'Desert tan',
                          'uniform': 'asrc_modern_ghost_desert_uniform.png',
                          'gear': 'asrc_modern_ghost_desert_gear.png',
                          'portrait': 'asrc_operator_ghost_desert.png'})
        for skin in skins[1:]:
            for group in ('UNIFORM', 'GEAR'):
                diffuse_node(prefix, group).image = bpy.data.images.load(str(output / skin[group.lower()]), check_existing=True)
            ghost.render_pose(rig, output / skin['portrait'], textured=True, portrait=True)
        entry['skins'] = skins
    manifest['files'] = {path.name: ghost.modern.sha256(path) for path in sorted(output.iterdir())
                         if path.suffix.lower() in {'.kn5', '.ksanim', '.png'}}
    manifest_path.write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    validate_modern_asset_set(output)
    print('Operator skins and portraits validated; KN5 geometry and animations unchanged.', flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--output-dir', type=Path, required=True)
    parser.add_argument('--cache-dir', type=Path, required=True)
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    build(args.output_dir.resolve(), args.cache_dir.resolve())
