"""Convert the supplied Ghost Nightwar source to the shared FPS operator rig.

Run with Blender --background --factory-startup --disable-autoexec --python.
The original blend is only read. Intermediate blends and inspection renders live
in the build cache, never in the distributed client asset directory.
"""
from __future__ import annotations

import argparse
from collections import defaultdict
import hashlib
import json
import math
from pathlib import Path
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
import build_fps_modern_assets as modern


GHOST_FILE = "asrc_modern_ghost_carbine.kn5"
GHOST_UNIFORM = "asrc_modern_ghost_team2_uniform.png"
GHOST_GEAR = "asrc_modern_ghost_team2_gear.png"
GROUPS = ("HEAD", "UNIFORM", "GEAR")
HEAD_OBJECTS = {"Headmask", "Helmet", "Helmet.001", "Helmet.002", "Helmet.003"}
BODY_OBJECTS = {"Body", "Gloves"}
MAIN_BUDGETS = {
    "Body": 15_500, "Gloves": 3_000, "Headmask": 6_000,
    "Helmet": 2_000, "Helmet.001": 1_200, "Helmet.002": 100, "Helmet.003": 150,
}


def log(message):
    print(f"GHOST: {message}", flush=True)


def select(objects):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.hide_set(False)
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]


def reset_pose(rig):
    rig.animation_data_clear()
    rig.data.pose_position = "REST"
    for bone in rig.pose.bones:
        bone.matrix_basis.identity()
    bpy.context.view_layer.update()


def reference_rig(options, exporter, writer, cache):
    reference = cache / "officer-reference.blend"
    signature = hashlib.sha256((Path(modern.__file__).read_text()
        + str(Path(options.officer_zip).stat().st_mtime_ns)
        + str(Path(options.carbine_fbx).stat().st_mtime_ns)).encode()).hexdigest()
    stamp = cache / "officer-reference.sha256"
    if not reference.exists() or not stamp.exists() or stamp.read_text() != signature:
        destination = cache / "reference-assets"
        destination.mkdir(exist_ok=True)
        modern.build_officer(Path(options.officer_zip), Path(options.carbine_fbx),
                             destination, cache, exporter, writer)
        reset_pose(bpy.data.objects["ASRC_MODERN_OPERATOR_RIG"])
        bpy.ops.wm.save_as_mainfile(filepath=str(reference), compress=True)
        stamp.write_text(signature)
    else:
        bpy.ops.wm.open_mainfile(filepath=str(reference), load_ui=False, use_scripts=False)
    rig = bpy.data.objects["ASRC_MODERN_OPERATOR_RIG"]
    reset_pose(rig)
    return rig


def bone_frame(head, tail, forward):
    """Anatomical frame independent of Rigify/Mixamo bone roll conventions."""
    y = (tail - head).normalized()
    z = forward - y * forward.dot(y)
    if z.length < 0.1:
        up = Vector((0, 0, 1))
        z = up - y * up.dot(y)
    z.normalize()
    x = y.cross(z).normalized()
    result = Matrix((x, y, z)).transposed().to_4x4()
    result.translation = head
    return result


def mapping(source_name):
    """Source deform chain -> canonical bone plus the complete source segment."""
    side = "L" if ".L" in source_name else "R"
    prefix = "Left" if side == "L" else "Right"
    parts = {
        "DEF-shoulder.": (prefix + "Shoulder", f"DEF-shoulder.{side}", None),
        "DEF-upper_arm.": (prefix + "Arm", f"DEF-upper_arm.{side}", f"DEF-upper_arm.{side}.001"),
        "DEF-forearm.": (prefix + "ForeArm", f"DEF-forearm.{side}", f"DEF-forearm.{side}.001"),
        "DEF-hand.": (prefix + "Hand", f"DEF-hand.{side}", None),
        "DEF-thigh.": (prefix + "UpLeg", f"DEF-thigh.{side}", f"DEF-thigh.{side}.001"),
        "DEF-shin.": (prefix + "Leg", f"DEF-shin.{side}", f"DEF-shin.{side}.001"),
        "DEF-foot.": (prefix + "Foot", f"DEF-foot.{side}", None),
        "DEF-toe.": (prefix + "ToeBase", f"DEF-toe.{side}", None),
    }
    for start, result in parts.items():
        if source_name.startswith(start):
            return result
    for source, target in (("thumb", "Thumb"), ("f_index", "Index"),
                            ("f_middle", "Middle"), ("f_ring", "Ring"),
                            ("f_pinky", "Pinky")):
        if source_name.startswith("DEF-" + source + "."):
            number = int(source_name.split(".")[1])
            return (prefix + "Hand" + target + str(number), source_name, None)
    if source_name.startswith("DEF-palm."):
        return (prefix + "Hand", f"DEF-hand.{side}", None)
    spine = {
        "DEF-spine": ("Hips", "DEF-spine", None),
        "DEF-spine.001": ("Spine", "DEF-spine.001", None),
        "DEF-spine.002": ("Spine1", "DEF-spine.002", None),
        "DEF-spine.003": ("Spine2", "DEF-spine.003", None),
        "DEF-spine.004": ("Neck", "DEF-spine.004", "DEF-spine.005"),
        "DEF-spine.005": ("Neck", "DEF-spine.004", "DEF-spine.005"),
    }
    if source_name in spine:
        return spine[source_name]
    if source_name.startswith("DEF-pelvis"):
        return spine["DEF-spine"]
    if source_name.startswith("DEF-breast"):
        return spine["DEF-spine.003"]
    # The masked character does not need facial animation.
    return ("Head", "DEF-spine.006", None)


def rigid_source_bone(obj):
    if obj.name in HEAD_OBJECTS:
        return "DEF-spine.006"
    if obj.name == "Gear 2":
        return "DEF-hand.L"
    if obj.parent_type == "BONE" and "thigh" in obj.parent_bone:
        return "DEF-thigh.L" if ".L" in obj.parent_bone else "DEF-thigh.R"
    return "DEF-spine.003"


def retarget(source, target, objects):
    reset_pose(source)
    reset_pose(target)
    target_bones = {b.name.split(":")[-1].rsplit("_", 1)[0]: b for b in target.data.bones}
    source_head = source.matrix_world @ source.data.bones["DEF-spine.006"].head_local
    target_head = target.matrix_world @ target_bones["Head"].head_local
    radial_scale = target_head.z / source_head.z
    transforms = {}
    for src in source.data.bones:
        if not src.use_deform:
            continue
        name, first, last = mapping(src.name)
        dst = target_bones[name]
        start = source.matrix_world @ source.data.bones[first].head_local
        end = source.matrix_world @ source.data.bones[last or first].tail_local
        dst_start = target.matrix_world @ dst.head_local
        dst_end = target.matrix_world @ dst.tail_local
        # glTF's synthetic leaf tails are display hints (some are 1.6 m long),
        # not anatomical endpoints. Never stretch a head/fingertip to those tails.
        longitudinal = (dst_end - dst_start).length / (end - start).length
        if name == "Head" or name.endswith("3") or "ToeBase" in name:
            longitudinal = radial_scale
        scale = Matrix.Diagonal((radial_scale, longitudinal, radial_scale, 1.0))
        transforms[src.name] = (dst.name,
            bone_frame(dst_start, dst_end, Vector((0, -1, 0))) @ scale
            @ bone_frame(start, end, Vector((1, 0, 0))).inverted())
    for obj in objects:
        source_world = obj.matrix_world.copy()
        skinned = any(m.type == "ARMATURE" and m.object == source for m in obj.modifiers)
        group_names = {g.index: g.name for g in obj.vertex_groups}
        weights = []
        for vertex in obj.data.vertices:
            influences = [(group_names[g.group], g.weight) for g in vertex.groups
                          if group_names[g.group] in transforms and g.weight > 1e-6] if skinned else []
            if not influences:
                influences = [(rigid_source_bone(obj), 1.0)]
            total = sum(w for _, w in influences)
            original = source_world @ vertex.co
            position = Vector((0, 0, 0))
            merged = defaultdict(float)
            for name, weight in influences:
                target_name, transform = transforms[name]
                position += (transform @ original) * (weight / total)
                merged[target_name] += weight / total
            vertex.co = position
            selected = sorted(merged.items(), key=lambda pair: (-pair[1], pair[0]))[:4]
            total = sum(w for _, w in selected)
            weights.append([(name, w / total) for name, w in selected])
        obj.modifiers.clear()
        obj.vertex_groups.clear()
        obj.parent = target
        obj.parent_type = "OBJECT"
        obj.matrix_world = Matrix.Identity(4)
        groups = {name: obj.vertex_groups.new(name=name)
                  for name in sorted({n for row in weights for n, _ in row})}
        for index, row in enumerate(weights):
            for name, weight in row:
                groups[name].add([index], weight, "REPLACE")
        modifier = obj.modifiers.new("ASRC_OPERATOR_RIG", "ARMATURE")
        modifier.object = target
    bpy.context.view_layer.update()
    return radial_scale


def prepare_geometry(source_path, rig):
    existing = set(bpy.data.objects)
    with bpy.data.libraries.load(str(source_path), link=False) as (src, dst):
        dst.objects = [name for name in src.objects
                       if not name.startswith("WGT-") and name != "smd_bone_vis"]
    imported = [obj for obj in bpy.data.objects if obj not in existing]
    for obj in imported:
        if not obj.users_collection:
            bpy.context.scene.collection.objects.link(obj)
    source = next(o for o in imported if o.type == "ARMATURE")
    meshes = [o for o in imported if o.type == "MESH"
              and not o.name.startswith("WGT-")
              and o.name not in {"Eyelashes", "smd_bone_vis"} and o.data.polygons]
    for obj in meshes:
        obj.data.uv_layers.active.name = "SourceUV"
        for material in obj.data.materials:
            if material is None or not material.node_tree:
                continue
            uv = material.node_tree.nodes.get("ASRC_SOURCE_UV")
            if uv is None:
                uv = material.node_tree.nodes.new("ShaderNodeUVMap")
                uv.name = "ASRC_SOURCE_UV"
                uv.uv_map = "SourceUV"
            for node in material.node_tree.nodes:
                if node.type == "TEX_IMAGE" and not node.inputs["Vector"].is_linked:
                    material.node_tree.links.new(uv.outputs["UV"], node.inputs["Vector"])
    # Retarget before reduction so small influences survive the rest-pose change.
    scale = retarget(source, rig, meshes)
    other_total = sum(modern.triangle_count(o) for o in meshes if o.name not in MAIN_BUDGETS)
    for obj in meshes:
        budget = MAIN_BUDGETS.get(obj.name, max(60, int(5_800
            * modern.triangle_count(obj) / other_total)))
        count = modern.triangle_count(obj)
        if count > budget:
            select([obj])
            modifier = obj.modifiers.new("ASRC_GHOST_REDUCTION", "DECIMATE")
            modifier.ratio = (budget - 2) / count
            bpy.ops.object.modifier_move_up(modifier=modifier.name)
            bpy.ops.object.modifier_apply(modifier=modifier.name)
        obj["ghostGroup"] = "HEAD" if obj.name in HEAD_OBJECTS else (
            "UNIFORM" if obj.name in BODY_OBJECTS else "GEAR")
    for obj in imported:
        if obj not in meshes and obj.name in bpy.data.objects:
            modern.remove_object(obj)
    for obj in list(bpy.context.scene.objects):
        if obj.type == "MESH" and obj not in meshes and obj.name != "ASRC_CARBINE_WORLD":
            modern.remove_object(obj)
    modern.purge_unused_data()
    log(f"Retargeted {len(meshes)} meshes; {sum(modern.triangle_count(o) for o in meshes)} triangles; scale {scale:.6f}")
    return meshes


def preview_materials():
    for material in bpy.data.materials:
        if not material.use_nodes:
            continue
        nodes = material.node_tree.nodes
        shader = next((n for n in nodes if n.type == "BSDF_PRINCIPLED"), None)
        if shader is None:
            continue
        for node in list(nodes):
            if node.type == "TEX_IMAGE" and node.image and "txDiffuse" in node.name:
                material.node_tree.links.new(node.outputs["Color"], shader.inputs["Base Color"])


def render_pose(rig, path, pose="aim_idle", textured=False, portrait=False):
    scene = bpy.context.scene
    rig.data.pose_position = "POSE"
    frames = modern.OPERATOR_CLIPS[pose][0]
    modern.make_operator_pose_callback(rig, pose, frames)(frames - 1 if pose == "death" else 0)
    bpy.context.view_layer.update()
    scene.render.engine = "BLENDER_EEVEE" if textured else "BLENDER_WORKBENCH"
    scene.render.use_compositing = False
    scene.render.use_sequencer = False
    scene.render.resolution_x = 640
    scene.render.resolution_y = 800
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.film_transparent = portrait
    scene.display.shading.light = "STUDIO"
    scene.display.shading.color_type = "SINGLE"
    scene.display.shading.single_color = (0.5, 0.55, 0.6)
    scene.display.shading.show_cavity = True
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.exposure = 0
    scene.world.color = (0.15, 0.15, 0.15)
    camera = bpy.data.objects.new("AssessmentCamera", bpy.data.cameras.new("AssessmentCamera"))
    scene.collection.objects.link(camera)
    scene.camera = camera
    camera.data.type = "ORTHO"
    camera.data.ortho_scale = 2.35
    camera.data.clip_end = 100
    camera.location = Vector((2.7, -4, 2.3))
    camera.rotation_euler = (Vector((0, 0, 0.94)) - camera.location).to_track_quat("-Z", "Y").to_euler()
    lights = []
    if textured:
        preview_materials()
        for position, energy, size in (((1, -3, 4), 180, 4), ((-3, -1, 2), 120, 3)):
            light = bpy.data.objects.new("AssessmentLight", bpy.data.lights.new("AssessmentLight", "AREA"))
            scene.collection.objects.link(light)
            light.location = position
            light.rotation_euler = (Vector((0, 0, 1)) - light.location).to_track_quat("-Z", "Y").to_euler()
            light.data.energy = energy
            light.data.shape = "DISK"
            light.data.size = size
            lights.append(light)
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)
    for obj in [camera, *lights]:
        modern.remove_object(obj)
    reset_pose(rig)


def bake_group(objects, group, cache):
    obj = modern.join_objects(objects, "ASRC_GHOST_" + group)
    select([obj])
    atlas = obj.data.uv_layers.new(name="AtlasUV")
    obj.data.uv_layers.active = atlas
    atlas.active_render = True
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.008)
    bpy.ops.object.mode_set(mode="OBJECT")
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 1
    scene.cycles.device = "CPU"
    scene.render.bake.margin = 8
    scene.render.bake.use_selected_to_active = False
    scene.render.bake.use_clear = True
    scene.render.bake.normal_space = "TANGENT"
    materials = set(obj.data.materials)
    original_outputs = {}
    targets = {}
    for material in materials:
        nodes = material.node_tree.nodes
        output = next(n for n in nodes if n.type == "OUTPUT_MATERIAL")
        original_outputs[material] = output.inputs["Surface"].links[0].from_socket
        target = nodes.new("ShaderNodeTexImage")
        target.name = "ASRC_BAKE_TARGET"
        nodes.active = target
        targets[material] = target
    paths = {}
    for channel in ("diffuse", "normal", "roughness", "specular"):
        image = bpy.data.images.new(f"GHOST_{group}_{channel}", width=2048, height=2048, alpha=True)
        image.colorspace_settings.name = "sRGB" if channel == "diffuse" else "Non-Color"
        for material in materials:
            nodes, links = material.node_tree.nodes, material.node_tree.links
            targets[material].image = image
            nodes.active = targets[material]
            output = next(n for n in nodes if n.type == "OUTPUT_MATERIAL")
            shader = next(n for n in nodes if n.type == "BSDF_PRINCIPLED")
            if channel == "normal":
                links.new(original_outputs[material], output.inputs["Surface"])
            else:
                emission = nodes.get("ASRC_BAKE_EMISSION") or nodes.new("ShaderNodeEmission")
                emission.name = "ASRC_BAKE_EMISSION"
                socket = shader.inputs[{"diffuse": "Base Color", "roughness": "Roughness",
                                        "specular": "Specular IOR Level"}[channel]]
                for link in list(emission.inputs["Color"].links):
                    links.remove(link)
                if socket.is_linked:
                    links.new(socket.links[0].from_socket, emission.inputs["Color"])
                else:
                    value = socket.default_value
                    emission.inputs["Color"].default_value = value if channel == "diffuse" else (value, value, value, 1)
                links.new(emission.outputs[0], output.inputs["Surface"])
        log(f"Baking {group} {channel}")
        bpy.ops.object.bake(type="NORMAL" if channel == "normal" else "EMIT")
        image.filepath_raw = str(cache / f"ghost_{group}_{channel}.png")
        image.file_format = "PNG"
        image.save()
        paths[channel] = Path(image.filepath_raw)
        for target in targets.values():
            target.image = None
        bpy.data.images.remove(image)
    # Match the existing operator's RGB specular / alpha glossiness map contract.
    specular = bpy.data.images.load(str(paths["specular"]), check_existing=False)
    roughness = bpy.data.images.load(str(paths["roughness"]), check_existing=False)
    specular.colorspace_settings.name = roughness.colorspace_settings.name = "Non-Color"
    pixels = np.empty(2048 * 2048 * 4, dtype=np.float32)
    rough = np.empty_like(pixels)
    specular.pixels.foreach_get(pixels)
    roughness.pixels.foreach_get(rough)
    pixels[3::4] = 1 - rough[0::4]
    specular.pixels.foreach_set(pixels)
    specular.filepath_raw = str(cache / f"ghost_{group}_maps.png")
    specular.file_format = "PNG"
    specular.save()
    paths["maps"] = Path(specular.filepath_raw)
    material = modern.create_material("ASRC_GHOST_" + group, "ksSkinnedMesh", {
        "txDiffuse": (paths["diffuse"], False), "txNormal": (paths["normal"], True),
        "txMaps": (paths["maps"], True),
    }, cache)
    modern.assign_single_material(obj, material)
    obj.data.uv_layers.remove(obj.data.uv_layers["SourceUV"])
    return obj, paths


def validate_poses(rig, meshes, cache):
    """Evaluate actual skinned geometry at the start/middle/end of every clip."""
    rig.data.pose_position = "POSE"
    proof = {}
    for name, (count, _) in modern.OPERATOR_CLIPS.items():
        callback = modern.make_operator_pose_callback(rig, name, count)
        samples = []
        for frame in sorted({0, count // 2, count - 1}):
            bpy.context.scene.frame_set(frame)
            callback(frame)
            bpy.context.view_layer.update()
            points = []
            graph = bpy.context.evaluated_depsgraph_get()
            for obj in meshes:
                evaluated = obj.evaluated_get(graph)
                mesh = evaluated.to_mesh()
                coordinates = np.empty(len(mesh.vertices) * 3, dtype=np.float32)
                mesh.vertices.foreach_get("co", coordinates)
                local = coordinates.reshape((-1, 3))
                matrix = np.asarray(evaluated.matrix_world)
                points.append(local @ matrix[:3, :3].T + matrix[:3, 3])
                evaluated.to_mesh_clear()
            vertices = np.concatenate(points)
            if not np.isfinite(vertices).all() or np.abs(vertices).max() > 3.5:
                raise ValueError(f"Ghost has invalid/exploded skinned geometry in {name}/{frame}")
            samples.append({"frame": frame, "minimum": vertices.min(axis=0).tolist(),
                            "maximum": vertices.max(axis=0).tolist()})
        proof[name] = samples
    reset_pose(rig)
    (cache / "ghost-pose-validation.json").write_text(json.dumps(proof, indent=2) + "\n")
    log("All 20 shared clips passed evaluated-geometry checks (60 sampled poses)")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", required=True)
    parser.add_argument("--officer-zip", required=True)
    parser.add_argument("--carbine-fbx", required=True)
    parser.add_argument("--output-dir", required=True)
    parser.add_argument("--cache-dir", required=True)
    parser.add_argument("--geometry-only", action="store_true")
    options = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    cache = Path(options.cache_dir).resolve()
    cache.mkdir(parents=True, exist_ok=True)
    output = Path(options.output_dir).resolve()
    output.mkdir(parents=True, exist_ok=True)
    sys.path.insert(0, str(Path(__file__).resolve().parent / "vendor"))
    import blender_assetto_corsa_tools as ac_tools
    from blender_assetto_corsa_tools import exporter
    from blender_assetto_corsa_tools.exporter.ksanim_writer import KSAnimWriter
    ac_tools.register()
    rig = reference_rig(options, exporter, KSAnimWriter, cache)
    render_pose(rig, cache / "officer-idle.png")
    if not options.geometry_only:
        render_pose(rig, output / "asrc_operator_officer.png", textured=True, portrait=True)
    meshes = prepare_geometry(Path(options.source), rig)
    for pose in ("aim_idle", "crouch_idle", "prone_idle", "death"):
        render_pose(rig, cache / f"ghost-{pose}.png", pose)
    if options.geometry_only:
        log("Geometry proof complete; no shipping assets generated")
        return
    generated = []
    grouped_meshes = {group: [o for o in meshes if o["ghostGroup"] == group] for group in GROUPS}
    for group in GROUPS:
        mesh, textures = bake_group(grouped_meshes[group], group, cache)
        generated.append(mesh)
        if group in {"UNIFORM", "GEAR"}:
            modern.create_team2_texture(textures["diffuse"], output / (
                GHOST_UNIFORM if group == "UNIFORM" else GHOST_GEAR),
                (0.30, 0.38, 0.46) if group == "UNIFORM" else (0.22, 0.28, 0.34))
    for obj in generated:
        select([obj])
        bpy.ops.object.vertex_group_limit_total(limit=4)
        bpy.ops.object.vertex_group_normalize_all(lock_active=False)
    modern.purge_unused_data()
    validate_poses(rig, generated, cache)
    reset_pose(rig)
    modern.export_kn5(output / GHOST_FILE, exporter, {
        "ASRC_GHOST_" + group: "ksSkinnedMesh" for group in GROUPS
    } | {"ASRC_CARBINE_WORLD": "ksSkinnedMesh"})
    render_pose(rig, output / "asrc_operator_ghost.png", textured=True, portrait=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(cache / "ghost-production.blend"), compress=True)
    triangles = sum(modern.triangle_count(o) for o in bpy.context.scene.objects if o.type == "MESH")
    metadata = {
        "file": GHOST_FILE, "triangles": triangles, "materials": 4, "bones": 68,
        "clips": list(modern.OPERATOR_CLIPS), "sharedAnimations": "officer",
        "source": "FPS/Characters/Ghost_IURgXpX/Ghost.blend",
        "sourceSha256": modern.sha256(Path(options.source)),
        "redistributionRightsConfirmedByUser": False,
        "teamSkins": {"team1": "embedded original uniform and gear",
                      "team2Uniform": GHOST_UNIFORM, "team2Gear": GHOST_GEAR},
    }
    if triangles > 40_000 or (output / GHOST_FILE).stat().st_size > 60_000_000:
        raise RuntimeError("Ghost exceeds the shipping geometry or file-size budget")
    (cache / "ghost-metadata.json").write_text(json.dumps(metadata, indent=2) + "\n")
    manifest_path = output / "asrc-modern-assets.json"
    if manifest_path.exists():
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        manifest["operators"] = {"officer": {"id": 0, **manifest["operator"]},
                                 "ghost": {"id": 1, **metadata}}
        manifest["files"] = {p.name: modern.sha256(p) for p in sorted(output.iterdir())
                             if p.suffix.lower() in {".kn5", ".ksanim", ".png"}}
        manifest["validation"]["status"] = "pending"
        manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
        from validate_fps_modern_assets import validate_modern_asset_set
        validate_modern_asset_set(output)
        manifest["validation"].update(status="passed", ghostSharedSkeletonValidated=True)
        manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    log(json.dumps(metadata))


if __name__ == "__main__":
    main()
