"""Retarget Quaternius UAL1 Source onto the existing Officer/Ghost bind skeleton.

Run through Build-FpsLocomotion.ps1. Source blends are read with scripts disabled;
no source rig, controls, meshes or textures are included in the client pack.
"""
from __future__ import annotations

import argparse
import json
import math
from pathlib import Path
import shutil
import sys

import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
sys.path.insert(0, str(Path(__file__).resolve().parent / 'vendor'))
import build_fps_modern_assets as modern
from blender_assetto_corsa_tools.exporter.ksanim_writer import KSAnimWriter
from validate_fps_modern_assets import inspect_ksanim, validate_modern_asset_set

CLIPS = {
    'walk_forward': 'Walk_Loop',
    'jog_forward': 'Jog_Fwd_Loop',
    # Use measured root travel: source "Left" moves +X, runtime right is +X.
    'jog_forward_left': 'Jog_Fwd_R_Loop',
    'strafe_left': 'Jog_Right_Loop',
    'jog_backward_left': 'Jog_Bwd_R_Loop',
    'walk_backward': 'Jog_Bwd_Loop',
    'jog_backward_right': 'Jog_Bwd_L_Loop',
    'strafe_right': 'Jog_Left_Loop',
    'jog_forward_right': 'Jog_Fwd_L_Loop',
    'sprint': 'Sprint_Loop',
}
MAPPING = {
    'Hips_01': 'pelvis', 'Spine_02': 'spine_01', 'Spine1_03': 'spine_02',
    'Spine2_04': 'spine_03', 'Neck_05': 'neck_01', 'Head_06': 'Head',
    'LeftUpLeg_062': 'thigh_l', 'LeftLeg_063': 'calf_l',
    'LeftFoot_064': 'foot_l', 'LeftToeBase_065': 'ball_l',
    'RightUpLeg_057': 'thigh_r', 'RightLeg_058': 'calf_r',
    'RightFoot_059': 'foot_r', 'RightToeBase_060': 'ball_r',
}
SOURCE_URL = 'https://quaternius.itch.io/universal-animation-library'


def sample_source(path):
    bpy.ops.wm.open_mainfile(filepath=str(path), load_ui=False, use_scripts=False)
    rig = bpy.data.objects['Armature']
    rig.animation_data_create()
    for track in rig.animation_data.nla_tracks:
        track.mute = True
    rest = {name: rig.data.bones[name].matrix_local.copy() for name in MAPPING.values()}
    source_leg = sum(rig.data.bones[n].length for n in ('thigh_l', 'calf_l'))
    samples = {}
    for clip, name in CLIPS.items():
        action = bpy.data.actions[name]
        rig.animation_data.action = action
        rig.animation_data.action_slot = action.slots[0]
        bag = action.layers[0].strips[0].channelbag(action.slots[0])
        for group in bag.groups:
            group.mute = False
        for curve in bag.fcurves:
            curve.mute = False
        start, end = map(int, action.frame_range)
        poses, roots = [], []
        for frame in range(start, end + 1):
            bpy.context.scene.frame_set(frame)
            bpy.context.view_layer.update()
            root = rig.pose.bones['root'].matrix.copy()
            roots.append(root.translation.copy())
            poses.append({n: root.inverted() @ rig.pose.bones[n].matrix for n in rest})
        travel = roots[-1] - roots[0]
        distance = math.hypot(travel.x, travel.y)
        if distance <= 0.1:
            raise ValueError(f'Missing root-motion distance in {name}')
        samples[clip] = {'poses': poses, 'distance': distance,
                         'direction': math.degrees(math.atan2(travel.x, -travel.y)),
                         'duration': (end - start) / bpy.context.scene.render.fps,
                         'sourceAction': name}
    return rest, source_leg, samples


def load_target(reference):
    bpy.ops.wm.open_mainfile(filepath=str(reference), load_ui=False, use_scripts=False)
    rig = bpy.data.objects['ASRC_MODERN_OPERATOR_RIG']
    rig.animation_data_clear()
    rig.data.pose_position = 'POSE'
    modern.make_operator_pose_callback(rig, 'aim_idle', 31)(0)
    bpy.context.view_layer.update()
    return rig


def retarget(rig, rest, source_leg, sampled):
    target = {source: modern.bone(rig, suffix) for suffix, source in MAPPING.items()}
    if any(b is None for b in target.values()):
        raise ValueError('Canonical operator skeleton is incomplete')
    basis = {b.name: b.matrix_basis.copy() for b in rig.pose.bones}
    aim_rotations = {n: b.matrix.to_quaternion() for n, b in target.items()}
    aim_positions = {n: b.matrix.translation.copy() for n, b in target.items()}
    leg_scale = sum(target[n].length for n in ('thigh_l', 'calf_l')) / source_leg
    scale_m = leg_scale * rig.matrix_world.to_scale().x
    hips = target['pelvis']
    output = {}
    for clip, sample in sampled.items():
        frames = []
        for pose in sample['poses']:
            for b in rig.pose.bones:
                b.matrix_basis = basis[b.name].copy()
            bpy.context.view_layer.update()
            for name, b in target.items():
                # World-space rest deltas avoid assuming matching bone roll or axes.
                rotation = pose[name].to_quaternion() @ rest[name].to_quaternion().inverted()
                if name in ('spine_03', 'neck_01', 'Head'):
                    # Keep the rifle aimed ahead while the pelvis/lower spine move.
                    rotation = Quaternion().slerp(rotation, 0.35 if clip == 'sprint' else 0.12)
                    rotation = rotation @ aim_rotations[name]
                else:
                    rotation = rotation @ b.bone.matrix_local.to_quaternion()
                inherited = b.parent.matrix @ b.parent.bone.matrix_local.inverted() @ b.bone.matrix_local
                position = inherited.translation
                if b == hips:
                    position = b.bone.head_local + (pose[name].translation - rest[name].translation) * leg_scale
                elif name == 'spine_03':
                    # An aimed upper body absorbs part of the unarmed torso sway;
                    # otherwise the fully extended support hand loses the foregrip.
                    position = aim_positions[name].lerp(position, 0.35)
                b.matrix = Matrix.LocRotScale(position, rotation, Vector((1, 1, 1)))
                bpy.context.view_layer.update()
            # Both solved arms inherit the same chest motion. This preserves the
            # baked two-hand grip and finger curls without importing unarmed swings.
            frames.append({b.name: b.matrix_basis.copy() for b in rig.pose.bones})
        # Source endpoint noise must not create a visible pop at cycle wrap.
        frames[-1] = {n: m.copy() for n, m in frames[0].items()}
        output[clip] = {'frames': frames, 'duration': sample['duration'],
                        'stride': sample['distance'] * scale_m,
                        'direction': sample['direction'],
                        'sourceAction': sample['sourceAction']}
    return output


def apply_frame(rig, frame):
    for b in rig.pose.bones:
        b.matrix_basis = frame[b.name]
    bpy.context.view_layer.update()


def bake_rifle_grip(rig, clips):
    """Keep existing weapon attachments aligned while the torso moves beneath them."""
    modern.make_operator_pose_callback(rig, 'aim_idle', 31)(0)
    bpy.context.view_layer.update()
    hands = [modern.bone(rig, s) for s in ('RightHand_037', 'LeftHand_013')]
    reference = {b.name: b.matrix.copy() for b in hands}
    constraints, helpers, stretch = [], [], []
    for side, suffix, pole_position, angle in (
            ('Right', 'RightForeArm_036', (-38, -10, 128), -math.pi),
            ('Left', 'LeftForeArm_012', (38, -20, 128), 0)):
        forearm = modern.bone(rig, suffix)
        hand = next(b for b in hands if side in b.name)
        target = bpy.data.objects.new('ASRC_GRIP_TARGET_' + side, None)
        pole = bpy.data.objects.new('ASRC_GRIP_POLE_' + side, None)
        for ob in (target, pole):
            bpy.context.scene.collection.objects.link(ob)
            helpers.append(ob)
        target.location = rig.matrix_world @ reference[hand.name].translation
        pole.location = rig.matrix_world @ Vector(pole_position)
        constraint = forearm.constraints.new('IK')
        constraint.target, constraint.pole_target = target, pole
        constraint.chain_count, constraint.pole_angle = 2, angle
        constraint.use_tail = True
        constraint.use_stretch = True
        for b in (forearm.parent, forearm):
            stretch.append((b, b.ik_stretch))
            b.ik_stretch = 0.05
        constraints.append((forearm, constraint))
    try:
        for clip in clips.values():
            clip['maximumGripErrorMeters'] = 0
            for i, frame in enumerate(clip['frames']):
                apply_frame(rig, frame)
                for hand in hands:
                    # Preserve wrist orientation and the authored curled fingers.
                    hand.matrix = Matrix.LocRotScale(hand.matrix.translation,
                        reference[hand.name].to_quaternion(), Vector((1, 1, 1)))
                    bpy.context.view_layer.update()
                    error = (hand.matrix.translation - reference[hand.name].translation).length
                    error *= rig.matrix_world.to_scale().x
                    clip['maximumGripErrorMeters'] = max(clip['maximumGripErrorMeters'], error)
                    if error > 0.005:
                        raise ValueError(f'Unreachable rifle grip: {clip["sourceAction"]} {i} {hand.name} {error}')
                # Bake evaluated IK results; matrix_basis alone omits constraints.
                clip['frames'][i] = {b.name: b.bone.convert_local_to_pose(
                    b.matrix, b.bone.matrix_local,
                    parent_matrix=b.parent.matrix if b.parent else Matrix.Identity(4),
                    parent_matrix_local=b.parent.bone.matrix_local if b.parent else Matrix.Identity(4),
                    invert=True) for b in rig.pose.bones}
                if any(max(abs(v - 1) for v in clip['frames'][i][b.name].to_scale()) > 0.08
                       for b, _ in stretch):
                    raise ValueError(f'Excessive rifle-grip arm stretch: {clip["sourceAction"]}/{i}')
            clip['frames'][-1] = {n: m.copy() for n, m in clip['frames'][0].items()}
    finally:
        for owner, constraint in constraints:
            owner.constraints.remove(constraint)
        for ob in helpers:
            bpy.data.objects.remove(ob, do_unlink=True)
        for b, value in stretch:
            b.ik_stretch = value


def evaluate_geometry(rig, clips):
    proof = {}
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH'
              and any(m.type == 'ARMATURE' and m.object == rig for m in o.modifiers)]
    for name, clip in clips.items():
        bounds = []
        for i in range(len(clip['frames'])):
            apply_frame(rig, clip['frames'][i])
            points = []
            graph = bpy.context.evaluated_depsgraph_get()
            for ob in meshes:
                evaluated = ob.evaluated_get(graph)
                mesh = evaluated.to_mesh()
                p = np.empty(len(mesh.vertices) * 3, dtype=np.float32)
                mesh.vertices.foreach_get('co', p)
                world = np.asarray(evaluated.matrix_world)
                points.append(p.reshape(-1, 3) @ world[:3, :3].T + world[:3, 3])
                evaluated.to_mesh_clear()
            p = np.concatenate(points)
            if not np.isfinite(p).all() or np.abs(p).max() > 3.5:
                raise ValueError(f'Invalid retargeted geometry: {name}/{i}')
            bounds.append({'frame': i, 'minimum': p.min(axis=0).tolist(),
                           'maximum': p.max(axis=0).tolist()})
        proof[name] = bounds
    return proof


def render_proof(rig, clips, directory, model):
    scene = bpy.context.scene
    for ob in list(scene.objects):
        if ob.type in {'CAMERA', 'LIGHT'}:
            bpy.data.objects.remove(ob, do_unlink=True)
    camera = bpy.data.objects.new('LocomotionCamera', bpy.data.cameras.new('LocomotionCamera'))
    scene.collection.objects.link(camera)
    scene.camera = camera
    camera.data.type = 'ORTHO'
    camera.data.ortho_scale = 2.25
    camera.location = (3.4, -3.7, 1.6)
    camera.rotation_euler = (Vector((0, 0, 0.9)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.display.shading.light = 'STUDIO'
    scene.display.shading.color_type = 'MATERIAL'
    scene.display.shading.show_shadows = True
    scene.display.shading.show_cavity = True
    scene.render.resolution_x = 560
    scene.render.resolution_y = 560
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = 'PNG'
    directory.mkdir(exist_ok=True)
    for name in ('walk_forward', 'jog_forward', 'strafe_left', 'sprint'):
        for phase in (0, 0.25, 0.5, 0.75):
            frame = round(phase * (len(clips[name]['frames']) - 1))
            apply_frame(rig, clips[name]['frames'][frame])
            scene.render.filepath = str(directory / f'{model}-{name}-{int(phase*100):02}.png')
            bpy.ops.render.render(write_still=True)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--source', required=True, type=Path)
    parser.add_argument('--reference', required=True, type=Path)
    parser.add_argument('--ghost-reference', required=True, type=Path)
    parser.add_argument('--output-dir', required=True, type=Path)
    parser.add_argument('--cache-dir', required=True, type=Path)
    parser.add_argument('--proof-only', action='store_true')
    parser.add_argument('--render', action='store_true')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    cache = args.cache_dir.resolve()
    cache.mkdir(parents=True, exist_ok=True)
    source = args.source.resolve()
    provenance = {'name': 'Quaternius Universal Animation Library', 'edition': 'Source',
                  'version': '3.0 (root-motion edition)', 'url': SOURCE_URL,
                  'license': 'CC0-1.0', 'sourceFile': source.name,
                  'sha256': modern.sha256(source),
                  'licenseSha256': modern.sha256(source.with_name('License.txt'))}
    (cache / 'source-provenance.json').write_text(json.dumps(provenance, indent=2) + '\n')
    rest, source_leg, samples = sample_source(source)
    rig = load_target(args.reference.resolve())
    clips = retarget(rig, rest, source_leg, samples)
    initial = evaluate_geometry(rig, clips)
    hips = modern.bone(rig, 'Hips_01')
    for name, clip in clips.items():
        # A constant per-clip sole correction preserves authored flight phases.
        lift = -min(f['minimum'][2] for f in initial[name])
        if abs(lift) > 0.15:
            raise ValueError(f'Excessive ground correction for {name}: {lift}')
        local_lift = hips.bone.matrix_local.to_3x3().inverted() @ Vector((0, 0, lift / rig.matrix_world.to_scale().x))
        for frame in clip['frames']:
            frame[hips.name].translation += local_lift
        clip['groundCorrection'] = lift
    bake_rifle_grip(rig, clips)
    proof = {'officer': evaluate_geometry(rig, clips)}
    if args.render:
        render_proof(rig, clips, cache / 'previews', 'officer')
    stage = cache / 'assets'
    stage.mkdir(exist_ok=True)
    for name, clip in clips.items():
        with (stage / f'asrc_modern_operator_{name}.ksanim').open('wb') as stream:
            KSAnimWriter(stream, bpy.context, [rig], 0, len(clip['frames']) - 1,
                         frame_callback=lambda f, c=clip: apply_frame(rig, c['frames'][f])).write()
    upper = modern.bone(rig, 'Spine_02')
    upper_tracks = [upper.name, *(b.name for b in upper.children_recursive)]
    for name in ('fire', 'reload'):
        count = modern.OPERATOR_CLIPS[name][0]
        callback = modern.make_operator_pose_callback(rig, name, count)
        with (stage / f'asrc_modern_operator_{name}.ksanim').open('wb') as stream:
            KSAnimWriter(stream, bpy.context, [rig], 0, count - 1,
                         frame_callback=callback, track_names=upper_tracks).write()
    rig = load_target(args.ghost_reference.resolve())
    proof['ghost'] = evaluate_geometry(rig, clips)
    if args.render:
        render_proof(rig, clips, cache / 'previews', 'ghost')
    (cache / 'geometry-proof.json').write_text(json.dumps(proof, indent=2) + '\n')
    print('LOCOMOTION', json.dumps({n: {'frames': len(c['frames']), 'stride': c['stride']}
                                  for n, c in clips.items()}), flush=True)
    if args.proof_only:
        return
    output = args.output_dir.resolve()
    generated = {f'asrc_modern_operator_{name}.ksanim' for name in (*clips, 'fire', 'reload')}
    for p in output.iterdir():
        if p.is_file() and p.name not in generated:
            shutil.copy2(p, stage / p.name)
    manifest_path = stage / 'asrc-modern-assets.json'
    manifest = json.loads(manifest_path.read_text())
    manifest['sources']['locomotion'] = provenance
    catalog = {}
    for p in sorted(stage.glob('asrc_modern_operator_*.ksanim')):
        name = p.stem.removeprefix('asrc_modern_operator_')
        tracks = inspect_ksanim(p)
        count = len(next(iter(tracks.values())))
        catalog[name] = {'file': p.name, 'durationSeconds': (count - 1) / 30,
                         'trackCoverage': 'upperBody' if name in ('fire', 'reload') else 'fullBody',
                         'trackCount': len(tracks)}
        if name in clips:
            catalog[name].update(strideMeters=round(clips[name]['stride'], 6), loop=True,
                                 directionDegrees=round(clips[name]['direction'], 3),
                                 groundCorrectionMeters=round(clips[name]['groundCorrection'], 6),
                                 maximumGripErrorMeters=round(clips[name]['maximumGripErrorMeters'], 6),
                                 sourceAction=clips[name]['sourceAction'])
    manifest['operatorAnimations'] = catalog
    manifest['operator']['clips'] = list(catalog)
    manifest['operators']['officer']['clips'] = list(catalog)
    manifest['files'] = {p.name: modern.sha256(p) for p in sorted(stage.iterdir())
                         if p.is_file() and p.name != manifest_path.name}
    manifest['validation']['quaterniusLocomotionValidated'] = True
    manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
    validate_modern_asset_set(stage)
    for name in sorted(generated | {manifest_path.name}):
        shutil.copy2(stage / name, output / name)
    print(f'Validated locomotion installed in {output}', flush=True)


if __name__ == '__main__':
    main()
