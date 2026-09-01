"""Strict, Blender-independent validation for generated Modern FPS assets."""

from __future__ import annotations

from dataclasses import dataclass
import hashlib
import json
import math
from pathlib import Path
import struct
from typing import Any


@dataclass(frozen=True)
class Kn5Summary:
    triangles: int
    materials: int
    material_names: tuple[str, ...]
    shaders: tuple[str, ...]
    material_blend_modes: tuple[int, ...]
    material_depth_modes: tuple[int, ...]
    skinned_meshes: int
    rigid_meshes: int
    bones: int
    texture_dimensions: tuple[tuple[str, int, int], ...]
    node_names: tuple[str, ...]
    mesh_rendering: tuple[tuple[str, str, bool], ...]
    mesh_bounds: tuple[tuple[str, int, tuple[float, float, float],
                             tuple[float, float, float]], ...]


class Reader:
    def __init__(self, data: bytes, source: str):
        self.data = data
        self.source = source
        self.offset = 0

    def read(self, size: int) -> bytes:
        end = self.offset + size
        if size < 0 or end > len(self.data):
            raise ValueError(f"Unexpected end of {self.source} at byte {self.offset}")
        result = self.data[self.offset:end]
        self.offset = end
        return result

    def unpack(self, fmt: str):
        size = struct.calcsize("<" + fmt)
        return struct.unpack("<" + fmt, self.read(size))

    def uint(self) -> int:
        return self.unpack("I")[0]

    def integer(self) -> int:
        return self.unpack("i")[0]

    def byte(self) -> int:
        return self.unpack("B")[0]

    def boolean(self) -> bool:
        return self.unpack("?")[0]

    def floats(self, count: int) -> tuple[float, ...]:
        return self.unpack(f"{count}f")

    def string(self) -> str:
        size = self.uint()
        return self.read(size).decode("utf-8")

    def finish(self) -> None:
        if self.offset != len(self.data):
            raise ValueError(
                f"Unexpected trailing bytes in {self.source}: {len(self.data) - self.offset}")


def _png_dimensions(blob: bytes, name: str) -> tuple[int, int]:
    magic = b"\x89PNG\r\n\x1a\n"
    if len(blob) < 24 or blob[:8] != magic or blob[12:16] != b"IHDR":
        raise ValueError(f"KN5 texture is not a PNG: {name}")
    return struct.unpack(">II", blob[16:24])


def inspect_kn5(path: Path) -> Kn5Summary:
    reader = Reader(path.read_bytes(), path.name)
    if reader.read(6) != b"sc6969" or reader.uint() != 5:
        raise ValueError(f"Unsupported KN5 header: {path.name}")

    textures: list[tuple[str, int, int]] = []
    texture_count = reader.integer()
    if texture_count < 0 or texture_count > 64:
        raise ValueError(f"Invalid KN5 texture count in {path.name}: {texture_count}")
    for _ in range(texture_count):
        if reader.integer() != 1:
            raise ValueError(f"Inactive KN5 texture in {path.name}")
        name = reader.string()
        blob = reader.read(reader.uint())
        width, height = _png_dimensions(blob, name)
        if width > 2048 or height > 2048:
            raise ValueError(f"Texture exceeds 2K budget: {name} ({width}x{height})")
        textures.append((name, width, height))

    material_names: list[str] = []
    shaders: list[str] = []
    material_blend_modes: list[int] = []
    material_depth_modes: list[int] = []
    material_count = reader.integer()
    if material_count <= 0 or material_count > 16:
        raise ValueError(f"Invalid KN5 material count in {path.name}: {material_count}")
    for _ in range(material_count):
        material_names.append(reader.string())
        shaders.append(reader.string())
        material_blend_modes.append(reader.byte())
        reader.boolean()
        material_depth_modes.append(reader.integer())
        for _ in range(reader.uint()):
            reader.string()
            values = reader.floats(10)
            if not all(math.isfinite(value) for value in values):
                raise ValueError(f"Non-finite KN5 material property in {path.name}")
        for _ in range(reader.uint()):
            reader.string()
            reader.uint()
            reader.string()

    triangles = 0
    skinned_meshes = 0
    rigid_meshes = 0
    bone_names: set[str] = set()
    node_names: set[str] = set()
    mesh_rendering: list[tuple[str, str, bool]] = []
    mesh_bounds: list[tuple[str, int, tuple[float, float, float],
                            tuple[float, float, float]]] = []

    def read_node() -> None:
        nonlocal triangles, skinned_meshes, rigid_meshes
        node_class = reader.uint()
        node_name = reader.string()
        child_count = reader.uint()
        reader.boolean()
        if not node_name:
            raise ValueError(f"Unnamed KN5 node in {path.name}")
        if node_name in node_names:
            raise ValueError(f"Duplicate KN5 node name in {path.name}: {node_name}")
        node_names.add(node_name)
        if node_class == 1:
            matrix = reader.floats(16)
            if not all(math.isfinite(value) for value in matrix):
                raise ValueError(f"Non-finite node matrix for {node_name}")
            for _ in range(child_count):
                read_node()
            return
        if node_class not in {2, 3} or child_count != 0:
            raise ValueError(f"Invalid KN5 node class/children for {node_name}")
        reader.boolean()
        reader.boolean()
        transparent = reader.boolean()
        mesh_bones: list[str] = []
        if node_class == 3:
            skinned_meshes += 1
            for _ in range(reader.uint()):
                bone_name = reader.string()
                matrix = reader.floats(16)
                if not bone_name or not all(math.isfinite(value) for value in matrix):
                    raise ValueError(f"Invalid skin bind matrix for {node_name}")
                mesh_bones.append(bone_name)
                bone_names.add(bone_name)
            if not mesh_bones or len(mesh_bones) != len(set(mesh_bones)):
                raise ValueError(f"Invalid skin bone table for {node_name}")
        else:
            rigid_meshes += 1
        vertex_count = reader.uint()
        if vertex_count < 3 or vertex_count > 65_536:
            raise ValueError(f"Invalid KN5 vertex count for {node_name}: {vertex_count}")
        minimum = [math.inf, math.inf, math.inf]
        maximum = [-math.inf, -math.inf, -math.inf]
        for _ in range(vertex_count):
            base = reader.floats(11)
            if not all(math.isfinite(value) for value in base):
                raise ValueError(f"Non-finite vertex in {node_name}")
            for axis in range(3):
                minimum[axis] = min(minimum[axis], base[axis])
                maximum[axis] = max(maximum[axis], base[axis])
            if node_class == 3:
                weights = reader.floats(4)
                indices = reader.floats(4)
                if any(value < 0 or not math.isfinite(value) for value in weights) \
                        or abs(sum(weights) - 1) > 0.001:
                    raise ValueError(f"Invalid normalized skin weights in {node_name}")
                for weight, index in zip(weights, indices):
                    if weight > 0 and (not index.is_integer() or index < 0
                                       or index >= len(mesh_bones)):
                        raise ValueError(f"Invalid skin bone index in {node_name}: {index}")
        mesh_bounds.append((node_name, node_class, tuple(minimum), tuple(maximum)))
        index_count = reader.uint()
        if index_count % 3 != 0:
            raise ValueError(f"Non-triangular KN5 index count in {node_name}")
        indices = reader.unpack(f"{index_count}H")
        if indices and max(indices) >= vertex_count:
            raise ValueError(f"Out-of-range KN5 vertex index in {node_name}")
        triangles += index_count // 3
        material_id = reader.uint()
        if material_id >= material_count:
            raise ValueError(f"Out-of-range KN5 material in {node_name}")
        if node_class == 3 and shaders[material_id] != "ksSkinnedMesh":
            raise ValueError(
                f"Skinned mesh {node_name} uses non-skinned shader {shaders[material_id]}")
        mesh_rendering.append((node_name, material_names[material_id], transparent))
        reader.uint()
        lod_in, lod_out = reader.floats(2)
        if not (math.isfinite(lod_in) and math.isfinite(lod_out) and lod_out >= lod_in):
            raise ValueError(f"Invalid KN5 LOD range in {node_name}")
        if node_class == 2:
            sphere = reader.floats(4)
            if not all(math.isfinite(value) for value in sphere):
                raise ValueError(f"Invalid KN5 bounds in {node_name}")
            reader.boolean()

    read_node()
    reader.finish()
    return Kn5Summary(triangles, material_count, tuple(material_names), tuple(shaders),
                      tuple(material_blend_modes), tuple(material_depth_modes),
                      skinned_meshes, rigid_meshes, len(bone_names), tuple(textures),
                      tuple(sorted(node_names)), tuple(mesh_rendering), tuple(mesh_bounds))


def inspect_ksanim(path: Path) -> dict[str, tuple[tuple[float, ...], ...]]:
    reader = Reader(path.read_bytes(), path.name)
    if reader.uint() != 2:
        raise ValueError(f"Unsupported KSANIM version: {path.name}")
    tracks: dict[str, tuple[tuple[float, ...], ...]] = {}
    for _ in range(reader.uint()):
        name = reader.string()
        if not name or name in tracks:
            raise ValueError(f"Invalid KSANIM track name in {path.name}: {name!r}")
        frames = tuple(reader.floats(10) for _ in range(reader.uint()))
        if not frames or not all(math.isfinite(value) for frame in frames for value in frame):
            raise ValueError(f"Invalid KSANIM frames for {name} in {path.name}")
        tracks[name] = frames
    reader.finish()
    return tracks


def _validate_animation_family(paths: list[Path], root_lock: bool,
                               compatible_nodes: set[str]) -> None:
    expected_tracks: tuple[str, ...] | None = None
    for path in paths:
        tracks = inspect_ksanim(path)
        names = tuple(tracks)
        missing = set(names) - compatible_nodes
        if missing:
            raise ValueError(
                f"KSANIM targets missing KN5 nodes in {path.name}: {sorted(missing)}")
        if expected_tracks is None:
            expected_tracks = names
        elif names != expected_tracks:
            raise ValueError(f"Incompatible KSANIM tracks: {path.name}")
        if root_lock:
            root_name = next((name for name in names if name.endswith("Hips_01")), None)
            if root_name is None:
                raise ValueError(f"Operator KSANIM has no hips/root track: {path.name}")
            frames = tracks[root_name]
            origin_x, origin_z = frames[0][4], frames[0][6]
            if any(abs(frame[4] - origin_x) > 1e-5 or abs(frame[6] - origin_z) > 1e-5
                   for frame in frames):
                raise ValueError(f"Operator KSANIM contains planar root motion: {path.name}")


def _validate_rifle_ready_pose(path: Path) -> None:
    tracks = inspect_ksanim(path)
    required = (
        "mixamorig:LeftArm_011", "mixamorig:RightArm_035",
        "mixamorig:LeftHand_013", "mixamorig:RightHand_037",
        "mixamorig:LeftHandIndex1_022", "mixamorig:RightHandIndex1_046",
    )
    missing = [name for name in required if name not in tracks]
    if missing:
        raise ValueError(f"Operator rifle-ready pose is missing tracks: {missing}")

    def rotation_magnitude(name: str) -> float:
        frame = tracks[name][0]
        return math.sqrt(sum(value * value for value in frame[:3]))

    # These bounds deliberately reject the source rest/T-pose. Both upper arms
    # must swing into the two-handed IK solution and the hand/index tracks must
    # contain the baked grip instead of open rest-pose fingers.
    if any(rotation_magnitude(name) < 0.5 for name in required[:2]):
        raise ValueError("Operator aim-idle upper arms remain in the T-pose")
    if any(rotation_magnitude(name) < 0.25 for name in required[2:]):
        raise ValueError("Operator aim-idle hands do not contain the rifle grip")


def _validate_death_pose(path: Path) -> None:
    tracks = inspect_ksanim(path)
    required = (
        "mixamorig:Spine2_04", "mixamorig:LeftArm_011", "mixamorig:RightArm_035",
        "mixamorig:LeftLeg_063", "mixamorig:RightLeg_058",
    )
    missing = [name for name in required if name not in tracks]
    if missing:
        raise ValueError(f"Operator death pose is missing tracks: {missing}")

    def angular_change(name: str) -> float:
        start = tracks[name][0][:4]
        finish = tracks[name][-1][:4]
        dot = abs(sum(a * b for a, b in zip(start, finish)))
        return 2 * math.acos(max(-1.0, min(1.0, dot)))

    changes = {name: angular_change(name) for name in required}
    if changes[required[0]] < 0.35:
        raise ValueError("Operator death pose has no meaningful torso collapse")
    if any(changes[name] < 0.55 for name in required[1:3]):
        raise ValueError("Operator death pose does not release both arms")
    if any(changes[name] < 0.65 for name in required[3:]):
        raise ValueError("Operator death pose does not buckle both knees")


def _validate_stance_poses(directory: Path) -> None:
    aim = inspect_ksanim(directory / "asrc_modern_operator_aim_idle.ksanim")
    crouch = inspect_ksanim(directory / "asrc_modern_operator_crouch_idle.ksanim")
    crouch_move = inspect_ksanim(directory / "asrc_modern_operator_crouch_move.ksanim")
    prone = inspect_ksanim(directory / "asrc_modern_operator_prone_idle.ksanim")
    prone_crawl = inspect_ksanim(directory / "asrc_modern_operator_prone_crawl.ksanim")
    hips = "mixamorig:Hips_01"
    knees = ("mixamorig:LeftLeg_063", "mixamorig:RightLeg_058")
    hands = ("mixamorig:LeftHand_013", "mixamorig:RightHand_037")

    def angular_difference(first: tuple[float, ...], second: tuple[float, ...]) -> float:
        dot = abs(sum(a * b for a, b in zip(first[:4], second[:4])))
        return 2 * math.acos(max(-1.0, min(1.0, dot)))

    for label, tracks in (("crouch", crouch), ("crouch move", crouch_move),
                          ("prone", prone), ("prone crawl", prone_crawl)):
        required = (hips, *knees, *hands)
        missing = [name for name in required if name not in tracks]
        if missing:
            raise ValueError(f"Operator {label} pose is missing tracks: {missing}")

    # The exporter converts the source rig's Y-up translation to KN5 Z and
    # retains the source centimetre units in KSANIM tracks.
    crouch_drop = abs(crouch[hips][0][6] - aim[hips][0][6])
    prone_drop = abs(prone[hips][0][6] - aim[hips][0][6])
    if crouch_drop < 45 or prone_drop < 40 or abs(prone_drop - crouch_drop) < 2:
        raise ValueError("Operator crouch/prone poses are not grounded at distinct heights")
    if any(angular_difference(aim[name][0], crouch[name][0]) < 0.9 for name in knees):
        raise ValueError("Operator crouch pose does not bend both knees")
    if angular_difference(aim[hips][0], prone[hips][0]) < 1.2:
        raise ValueError("Operator prone pose does not rotate the torso onto the floor")
    if any(angular_difference(aim[name][0], prone[name][0]) < 0.35 for name in hands):
        raise ValueError("Operator prone pose does not retain a stance-specific rifle grip")

    def cycle_change(tracks: dict[str, tuple[tuple[float, ...], ...]],
                     names: tuple[str, ...]) -> float:
        quarter = len(next(iter(tracks.values()))) // 4
        return max(angular_difference(tracks[name][0], tracks[name][quarter])
                   for name in names)

    if cycle_change(crouch_move, knees) < 0.08:
        raise ValueError("Operator crouch-move clip has no locomotion cycle")
    if cycle_change(prone_crawl, ("mixamorig:LeftArm_011",
                                  "mixamorig:RightArm_035")) < 0.05:
        raise ValueError("Operator prone-crawl clip has no crawl cycle")


def validate_modern_asset_set(directory: Path) -> dict[str, Any]:
    manifest_path = directory / "asrc-modern-assets.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    if manifest.get("schemaVersion") != 1 or manifest.get("theme") != "Modern":
        raise ValueError("Modern asset manifest schema/theme mismatch")
    if manifest.get("redistributionRightsConfirmedByUser") is not True:
        raise ValueError("Modern asset redistribution rights are not recorded")
    if manifest["sources"].get("m4a1Used") is not False:
        raise ValueError("Deferred M4A1 source must not be present")

    files = {path.name: path for path in directory.iterdir()
             if path.suffix.lower() in {".kn5", ".ksanim"}}
    recorded = manifest.get("files", {})
    if set(files) != set(recorded):
        raise ValueError("Modern manifest file list does not match generated assets")
    for name, path in files.items():
        actual = hashlib.sha256(path.read_bytes()).hexdigest()
        if actual != recorded[name]:
            raise ValueError(f"Modern asset hash mismatch: {name}")

    operator = inspect_kn5(directory / manifest["operator"]["file"])
    viewmodel = inspect_kn5(directory / manifest["viewmodel"]["file"])
    pickup = inspect_kn5(directory / manifest["pickup"]["file"])
    for label, summary, metadata, triangle_limit, material_limit in (
            ("operator", operator, manifest["operator"], 40_000, 4),
            ("viewmodel", viewmodel, manifest["viewmodel"], 30_000, 3)):
        if summary.triangles != metadata["triangles"] or summary.materials != metadata["materials"]:
            raise ValueError(
                f"{label} KN5 statistics do not match the manifest: "
                f"parsed={summary.triangles}/{summary.materials}, "
                f"manifest={metadata['triangles']}/{metadata['materials']}")
        if summary.triangles > triangle_limit or summary.materials > material_limit:
            raise ValueError(f"{label} exceeds the shipping budget")
        if summary.skinned_meshes == 0 or summary.bones <= 0:
            raise ValueError(f"{label} does not contain a valid skinned mesh")
    pickup_metadata = manifest["pickup"]
    if pickup.triangles != pickup_metadata["triangles"] \
            or pickup.materials != pickup_metadata["materials"]:
        raise ValueError("pickup KN5 statistics do not match the manifest")
    if pickup.triangles > 6_000 or pickup.materials != 1 \
            or pickup.rigid_meshes != 1 or pickup.skinned_meshes != 0:
        raise ValueError("pickup is not a one-material rigid model within budget")
    if pickup.shaders != ("ksPerPixelMultiMap",):
        raise ValueError("pickup does not use the expected rigid multimap shader")
    pickup_mesh = pickup.mesh_bounds[0]
    pickup_extent = tuple(maximum - minimum
                          for minimum, maximum in zip(pickup_mesh[2], pickup_mesh[3]))
    pickup_center = tuple((minimum + maximum) * 0.5
                          for minimum, maximum in zip(pickup_mesh[2], pickup_mesh[3]))
    if max(pickup_extent) < 0.5 or sum(axis > 0.02 for axis in pickup_extent) < 2:
        raise ValueError("pickup rifle has invalid or trivial bounds")
    if any(abs(axis) > 0.05 for axis in pickup_center):
        raise ValueError(f"pickup rifle is not centered around its scene root: {pickup_center}")
    weapon_meshes = [mesh for mesh in viewmodel.mesh_bounds
                     if mesh[0].startswith("ASRC_CARBINE_VIEWMODEL_WEAPON")
                     and mesh[1] == 3]
    optic_meshes = [mesh for mesh in viewmodel.mesh_bounds
                    if mesh[0].startswith("ASRC_CARBINE_VIEWMODEL_OPTIC")
                    and mesh[1] == 3]
    if viewmodel.skinned_meshes < 2 or len(weapon_meshes) != 1:
        raise ValueError("viewmodel does not contain its required skinned rifle mesh")
    if len(optic_meshes) != 1 or "ASRC_CARBINE_OPTIC" not in viewmodel.material_names:
        raise ValueError("viewmodel does not contain its required separate optic lens")
    if manifest["viewmodel"].get("redDotCoreDiameterPixels") != 14 \
            or manifest["viewmodel"].get("redDotTextureSizePixels") != 512:
        raise ValueError("viewmodel does not contain the compact generated red-dot reticle")
    optic_material_index = viewmodel.material_names.index("ASRC_CARBINE_OPTIC")
    optic_rendering = [mesh for mesh in viewmodel.mesh_rendering
                       if mesh[0].startswith("ASRC_CARBINE_VIEWMODEL_OPTIC")]
    if viewmodel.material_blend_modes[optic_material_index] != 1 \
            or viewmodel.material_depth_modes[optic_material_index] != 1 \
            or len(optic_rendering) != 1 or not optic_rendering[0][2]:
        raise ValueError("viewmodel optic lens is not configured for transparent rendering")
    weapon_minimum, weapon_maximum = weapon_meshes[0][2], weapon_meshes[0][3]
    weapon_extent = tuple(maximum - minimum
                          for minimum, maximum in zip(weapon_minimum, weapon_maximum))
    if max(weapon_extent) < 0.5 or sum(axis > 0.02 for axis in weapon_extent) < 2:
        raise ValueError("viewmodel skinned rifle mesh has invalid or trivial bounds")

    operator_animations = sorted(directory.glob("asrc_modern_operator_*.ksanim"))
    viewmodel_animations = sorted(directory.glob("asrc_modern_carbine_*.ksanim"))
    if len(operator_animations) != 20 or len(viewmodel_animations) != 6:
        raise ValueError("Modern animation set is incomplete")
    _validate_animation_family(operator_animations, root_lock=True,
                               compatible_nodes=set(operator.node_names))
    _validate_rifle_ready_pose(directory / "asrc_modern_operator_aim_idle.ksanim")
    _validate_stance_poses(directory)
    _validate_death_pose(directory / "asrc_modern_operator_death.ksanim")
    _validate_animation_family(viewmodel_animations, root_lock=False,
                               compatible_nodes=set(viewmodel.node_names))
    return {
        "operator": operator,
        "viewmodel": viewmodel,
        "pickup": pickup,
        "viewmodelWeaponSkinnedMeshes": len(weapon_meshes),
        "viewmodelOpticSkinnedMeshes": len(optic_meshes),
        "manifest": manifest,
    }


if __name__ == "__main__":
    import argparse

    parser = argparse.ArgumentParser()
    parser.add_argument("directory")
    args = parser.parse_args()
    result = validate_modern_asset_set(Path(args.directory).resolve())
    print(json.dumps({
        "operatorTriangles": result["operator"].triangles,
        "operatorMaterials": result["operator"].materials,
        "operatorBones": result["operator"].bones,
        "viewmodelTriangles": result["viewmodel"].triangles,
        "viewmodelMaterials": result["viewmodel"].materials,
        "viewmodelBones": result["viewmodel"].bones,
        "viewmodelWeaponSkinnedMeshes": result["viewmodelWeaponSkinnedMeshes"],
        "viewmodelOpticSkinnedMeshes": result["viewmodelOpticSkinnedMeshes"],
        "pickupTriangles": result["pickup"].triangles,
        "pickupMaterials": result["pickup"].materials,
    }, indent=2))
