"""Import the installed CS:S Dust II into an isolated, reproducible Blender scene."""
from __future__ import annotations

import argparse
import collections
import hashlib
import importlib.util
import json
import re
import struct
import sys
from pathlib import Path

import bpy


def digest(path):
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--game-root", required=True)
    parser.add_argument("--sourceio-pin", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    game = Path(args.game_root).resolve()
    output = Path(args.output).resolve()
    output.mkdir(parents=True, exist_ok=True)
    source_file = game / "cstrike/maps/de_dust2.bsp"
    pin = json.loads(Path(args.sourceio_pin).read_text(encoding="utf-8-sig"))
    sourceio = Path(pin["source"])
    spec = importlib.util.spec_from_file_location(
        "SourceIO", sourceio / "__init__.py", submodule_search_locations=[str(sourceio)]
    )
    module = importlib.util.module_from_spec(spec)
    sys.modules["SourceIO"] = module
    spec.loader.exec_module(module)
    from SourceIO.blender_bindings.bindings import register
    from SourceIO.library.shared.content_manager import ContentManager
    from SourceIO.library.utils.tiny_path import TinyPath
    from SourceIO.library.source1.vmt import VMT
    from SourceIO.blender_bindings.material_loader.material_loader import ShaderRegistry
    from SourceIO.blender_bindings.material_loader.shaders.source1_shader_base import Source1ShaderBase

    register()
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    data = source_file.read_bytes()
    if data[:4] != b"VBSP":
        raise ValueError("Expected the installed Source 1 de_dust2.bsp")
    lumps = [struct.unpack_from("<4i", data, 8 + 16 * i) for i in range(64)]

    def lump(index):
        offset, size, _, compression = lumps[index]
        if compression:
            raise ValueError(f"Unexpected compressed source lump {index}; audit before import")
        return data[offset:offset + size]

    entities = [dict(re.findall(r'"([^"\n]*)"\s*"([^"\n]*)"', block))
                for block in re.findall(r"\{([^{}]*)\}", lump(0).decode("utf-8", errors="replace"), re.S)]
    materials = sorted({item.decode("utf-8") for item in lump(43).split(b"\0") if item})
    model_paths = {entity["model"].lower() for entity in entities
                   if entity.get("model", "").lower().endswith(".mdl")}
    game_lump = lump(35)
    static_count = 0
    static_props = []
    for i in range(struct.unpack_from("<i", game_lump)[0]):
        ident, _, version, offset, size = struct.unpack_from("<IHHii", game_lump, 4 + i * 16)
        if ident != int.from_bytes(b"prps", "little"):
            continue
        raw = data[offset:offset + size]
        count = struct.unpack_from("<i", raw)[0]
        dictionary = [raw[4 + n * 128:4 + (n + 1) * 128].split(b"\0")[0].decode().lower()
                      for n in range(count)]
        model_paths.update(dictionary)
        pos = 4 + count * 128
        leaf_count = struct.unpack_from("<i", raw, pos)[0]
        static_count = struct.unpack_from("<i", raw, pos + 4 + leaf_count * 2)[0]
        pos += 8 + leaf_count * 2
        if version != 6 or len(raw) - pos != static_count * 64:
            raise ValueError("Audit this static-prop layout before conversion; expected CS:S v6")
        for n in range(static_count):
            start = pos + n * 64
            values = struct.unpack_from("<6f3H2Bi", raw, start)
            static_props.append({"index": n, "model": dictionary[values[6]],
                                 "origin": values[:3], "angles": values[3:6],
                                 "solid": values[9], "flags": values[10], "skin": values[11]})

    bpy.ops.sourceio.bsp(filepath=str(source_file), scale=0.0254,
                        import_textures=True, use_bvlg=False,
                        load_props=True, load_static_props=True,
                        load_lights=False, load_info=False,
                        load_decals=True, load_triggers=False,
                        load_logic=False, import_cubemaps=False,
                        discover_resources=True, steam_app_id="240")
    content = ContentManager()
    unresolved = []
    for resource in [*("materials/" + name.lower() + ".vmt" for name in materials),
                     *sorted(model_paths)]:
        if content.find_file(TinyPath(resource)) is None:
            unresolved.append(resource)

    bpy.context.scene.use_instances = False
    bpy.context.scene.replace_entity = False
    bpy.context.scene.import_materials = True
    bpy.context.scene.import_physics = True
    bpy.context.scene.use_bvlg = False
    bpy.ops.object.select_all(action="DESELECT")
    placeholders = [obj for obj in bpy.data.objects
                    if obj.get("entity_data") and obj["entity_data"].get("prop_path")]
    # SourceIO parents visual meshes but omits unrigged PHY meshes in this path.
    # Load each instance separately so those meshes inherit its exact transform.
    for obj in placeholders:
        obj["source_model"] = str(obj["entity_data"]["prop_path"])
        obj["source_solid"] = (static_props[int(obj.name.rsplit("_", 1)[1])]["solid"]
                               if obj.name.startswith("static_prop_") else 6)
        before = {item.as_pointer() for item in bpy.data.objects}
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        bpy.ops.sourceio.load_placeholder()
        for item in bpy.data.objects:
            if item.as_pointer() not in before and item.type == "MESH" and item.parent is None \
                    and any("_PHYSICS" in c.name for c in item.users_collection):
                item.parent = obj
        bpy.ops.object.select_all(action="DESELECT")
    unresolved_props = [obj.name for obj in placeholders
                        if obj.get("entity_data") and not obj["entity_data"].get("imported", False)]
    # Some alternate MDL skins are left as empty named materials. Resolve their
    # authored VMTs; an ambiguous or absent match is an import failure.
    repaired_materials = []
    used_materials = {slot.material for obj in bpy.data.objects if obj.type == "MESH"
                      and not any("_PHYSICS" in c.name for c in obj.users_collection)
                      for slot in obj.material_slots if slot.material}
    for mat in sorted(used_materials, key=lambda item: item.name):
        if mat.get("source_loaded"):
            continue
        name = str(mat.get("full_path", mat.name)).lower()
        candidates = sorted({str(path) for path, _ in content.glob("materials/*" + name + ".vmt")})
        if len(candidates) != 1:
            raise RuntimeError(f"Cannot uniquely resolve material {mat.name}: {candidates}")
        path = TinyPath(candidates[0])
        vmt = VMT(content.find_file(path), path, content)
        mat.node_tree.nodes.clear()
        Source1ShaderBase.use_bvlg(False)
        ShaderRegistry.source1_create_nodes(content, mat, vmt, {})
        mat["full_path"] = candidates[0].removeprefix("materials/").removesuffix(".vmt")
        repaired_materials.append({"material": mat.name, "vmt": candidates[0]})
    bpy.context.view_layer.update()
    bpy.ops.file.pack_all()
    scene_file = output / "dust2-source.blend"
    bpy.ops.wm.save_as_mainfile(filepath=str(scene_file))
    objects = []
    for obj in bpy.data.objects:
        record = {"name": obj.name, "type": obj.type,
                  "parent": obj.parent.name if obj.parent else None,
                  "collections": [c.name for c in obj.users_collection]}
        if obj.type == "MESH":
            record.update(vertices=len(obj.data.vertices), polygons=len(obj.data.polygons),
                          materials=[mat.name if mat else None for mat in obj.data.materials])
        if obj.get("entity_data"):
            record["entity_data"] = obj["entity_data"].to_dict()
        objects.append(record)
    report = {
        "track_id": "css_dust2", "edition": "Counter-Strike: Source",
        "game_root": str(game), "bsp": str(source_file), "bsp_sha256": digest(source_file),
        "bsp_version": struct.unpack_from("<i", data, 4)[0],
        "sourceio_commit": pin["commit"], "blender": bpy.app.version_string,
        "scale_metres_per_source_unit": 0.0254,
        "vpk_indexes": [{"path": str(path), "sha256": digest(path)}
                        for path in sorted(game.glob("*/*_dir.vpk"))],
        "entity_classes": dict(collections.Counter(e.get("classname", "") for e in entities)),
        "source_entities": entities, "world_materials": materials,
        "source_models": sorted(model_paths), "static_prop_count": static_count,
        "static_props": static_props, "repaired_materials": repaired_materials,
        "unresolved_direct_resources": unresolved, "unresolved_props": unresolved_props,
        "objects": objects, "images": [{"name": im.name, "size": list(im.size)} for im in bpy.data.images],
        "scene": str(scene_file), "status": "Imported source scene; not an AC track or gameplay validation.",
    }
    (output / "source-import.json").write_text(json.dumps(report, indent=2, default=str) + "\n", encoding="utf-8")
    print(json.dumps({key: report[key] for key in ("track_id", "static_prop_count", "unresolved_direct_resources",
                                                   "unresolved_props", "scene")}, indent=2))
    if unresolved or unresolved_props:
        raise RuntimeError("Incomplete import; see source-import.json")


if __name__ == "__main__":
    main()
