"""Render the actual old/new KSANIM files at their respective runtime cadences."""
import argparse
import math
from pathlib import Path
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
from validate_fps_modern_assets import inspect_ksanim
from build_fps_locomotion import load_target


def pose(rig, tracks, phase):
    for name, frames in tracks.items():
        index = phase * (len(frames) - 1)
        a, b = frames[int(index)], frames[min(int(index) + 1, len(frames) - 1)]
        t = index % 1
        qa = Quaternion((a[3], a[0], -a[2], a[1]))
        qb = Quaternion((b[3], b[0], -b[2], b[1]))
        p = Vector((a[4], -a[6], a[5])).lerp(Vector((b[4], -b[6], b[5])), t)
        s = Vector((a[7], a[9], a[8])).lerp(Vector((b[7], b[9], b[8])), t)
        local = Matrix.LocRotScale(p, qa.slerp(qb, t), s)
        bone = rig.pose.bones[name]
        bone.matrix = bone.parent.matrix @ local if bone.parent else local
        bpy.context.view_layer.update()


def main():
    import json
    parser = argparse.ArgumentParser()
    parser.add_argument('--reference-directory', type=Path, required=True)
    parser.add_argument('--baseline-directory', type=Path, required=True)
    parser.add_argument('--asset-directory', type=Path, required=True)
    parser.add_argument('--output-directory', type=Path, required=True)
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    for name, value in vars(args).items():
        setattr(args, name, value.resolve())
    catalog = json.loads((args.asset_directory / 'asrc-modern-assets.json').read_text())['operatorAnimations']
    for model, reference in [('officer', 'officer-reference.blend'), ('ghost', 'ghost-production.blend')]:
        rig = load_target(args.reference_directory / reference)
        scene = bpy.context.scene
        for ob in list(scene.objects):
            if ob.type in {'CAMERA', 'LIGHT'}:
                bpy.data.objects.remove(ob, do_unlink=True)
        camera = bpy.data.objects.new('ComparisonCamera', bpy.data.cameras.new('ComparisonCamera'))
        scene.collection.objects.link(camera)
        scene.camera = camera
        camera.data.type = 'ORTHO'
        camera.data.ortho_scale = 2.25
        camera.location = (3.4, -3.7, 1.6)
        camera.rotation_euler = (Vector((0, 0, 0.9)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
        bpy.ops.mesh.primitive_plane_add(size=200, location=(0, 0, -0.003))
        scene.render.engine = 'BLENDER_WORKBENCH'
        scene.display.shading.light = 'STUDIO'
        scene.display.shading.color_type = 'MATERIAL'
        scene.display.shading.show_shadows = True
        scene.display.shading.show_cavity = True
        scene.render.film_transparent = False
        scene.render.resolution_x = scene.render.resolution_y = 480
        scene.render.resolution_percentage = 100
        scene.render.image_settings.file_format = 'PNG'
        for clip, speed in [('walk_forward', 2.4), ('jog_forward', 6), ('strafe_left', 6), ('sprint', 9)]:
            for version, directory in [('before', args.baseline_directory), ('after', args.asset_directory)]:
                name = clip if version == 'after' else ('sprint' if speed > 4.8 else clip)
                tracks = inspect_ksanim(directory / f'asrc_modern_operator_{name}.ksanim')
                cycles = speed / catalog[clip]['strideMeters'] if version == 'after' else max(0.75, speed * 0.35)
                destination = args.output_directory / f'{model}-{clip}-{version}'
                destination.mkdir(parents=True, exist_ok=True)
                for frame in range(60):
                    pose(rig, tracks, (frame / 30 * cycles) % 1)
                    scene.render.filepath = str(destination / f'{frame:03}.png')
                    bpy.ops.render.render(write_still=True)


if __name__ == '__main__':
    main()
