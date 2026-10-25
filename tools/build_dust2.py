"""Convert the audited CS:S scene to an isolated AC FPS track, using Nuketown's contract."""
from __future__ import annotations

import argparse
import collections
import hashlib
import json
import math
import sys
from pathlib import Path

import bpy
import bmesh
import numpy as np
from mathutils import Matrix, Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
from dust2_geometry import movement_boxes, read_brushes

VERSION = "0.1.1"


def ac(point):
    return [float(point[0]), float(point[2]), -float(point[1])]


def mesh_object(name, triangles, material, uv=None, normals=None, colors=None):
    mesh = bpy.data.meshes.new(name)
    coordinates = [tuple(p) for tri in triangles for p in tri]
    mesh.from_pydata(coordinates, [], [tuple(range(i, i + 3)) for i in range(0, len(coordinates), 3)])
    mesh.materials.append(material)
    mesh.update()
    if uv:
        # Compiled Source meshes contain collapsed UV triangles on small caps.
        # Give those a sub-texel UV basis so the AC tangent frame is defined.
        repaired = 0
        for i in range(0, len(uv), 3):
            a, b, c = np.array(uv[i:i + 3])
            if abs(np.linalg.det(np.array([b - a, c - a]))) < 1e-12:
                uv[i:i + 3] = [a, a + (1e-4, 0), a + (0, 1e-4)]
                repaired += 1
        layer = mesh.uv_layers.new(name="UVMap")
        layer.data.foreach_set("uv", np.asarray(uv).ravel())
    else:
        mesh.uv_layers.new(name="UVMap")
    if normals:
        mesh.normals_split_custom_set(normals)
        for polygon in mesh.polygons:
            polygon.use_smooth = True
    if colors:
        for name_, data in colors.items():
            layer = mesh.color_attributes.new(name=name_, type="FLOAT_COLOR", domain="CORNER")
            layer.data.foreach_set("color", np.asarray(data).ravel())
    obj = bpy.data.objects.new(name, mesh)
    obj["repaired_uv_triangles"] = repaired if uv else 0
    bpy.context.scene.collection.objects.link(obj)
    return obj


def marker(name, source_point, yaw=0):
    obj = bpy.data.objects.new(name, None)
    bpy.context.scene.collection.objects.link(obj)
    obj.location = source_point
    obj.rotation_euler.z = yaw
    return obj


def prepare_geometry(report):
    world = next(e for e in report["source_entities"] if e.get("classname") == "worldspawn")
    low = np.array([float(x) * .0254 for x in world["world_mins"].split()])
    high = np.array([float(x) * .0254 for x in world["world_maxs"].split()])
    def inside(point):
        return all(low[i] - 6 <= point[i] <= high[i] + 6 for i in (0, 1))
    grouped = {}
    physics, world_triangles = [], []
    owners = {}
    ignored_faces = 0
    originals = list(bpy.data.objects)
    depsgraph = bpy.context.evaluated_depsgraph_get()
    for obj in originals:
        if obj.type not in ("MESH", "CURVE"):
            continue
        owner = obj
        while owner and "source_solid" not in owner:
            owner = owner.parent
        physical = any("_PHYSICS" in c.name for c in obj.users_collection)
        evaluated = obj.evaluated_get(depsgraph)
        mesh = bpy.data.meshes.new_from_object(evaluated, preserve_all_data_layers=True, depsgraph=depsgraph)
        slots = [slot.material for slot in obj.material_slots]
        mesh.transform(obj.matrix_world)
        mesh.calc_loop_triangles()
        if owner:
            owned = owners.setdefault(owner.name, {"model": owner["source_model"],
                "solid": int(owner["source_solid"]), "visual": [], "physics": []})
        for tri in mesh.loop_triangles:
            coords = [tuple(mesh.vertices[i].co) for i in tri.vertices]
            a, b, c = np.array(coords)
            cross_length = np.linalg.norm(np.cross(b - a, c - a))
            longest_edge = max(np.linalg.norm(b - a), np.linalg.norm(c - a), np.linalg.norm(c - b))
            if cross_length < max(1e-8, longest_edge * .0005):
                ignored_faces += 1
                continue
            if not inside(np.mean(coords, axis=0)):
                ignored_faces += 1
                continue
            if physical:
                if owner and owner["source_solid"]:
                    owned["physics"].append(coords)
                continue
            material = slots[tri.material_index] if tri.material_index < len(slots) else None
            path = str(material.get("full_path", material.name)).lower() if material else ""
            if path.startswith("tools"):
                ignored_faces += 1
                continue
            if material is None or not material.get("source_loaded"):
                raise ValueError(f"Unresolved visual material: {obj.name}: {path}")
            if owner:
                owned["visual"].append(coords)
            params = material.get("vmt_parameters", {})
            if not owner and obj.type == "MESH" and not path.startswith("decals/") \
                    and str(params.get("$decal", "0")) != "1":
                world_triangles.append(coords)
            item = grouped.setdefault(material, {"triangles": [], "uv": [], "normals": [], "colors": {}})
            item["triangles"].append(coords)
            for loop in tri.loops:
                item["uv"].append(tuple(mesh.uv_layers.active.data[loop].uv) if mesh.uv_layers else (0, 0))
                item["normals"].append(tuple(mesh.corner_normals[loop].vector))
            for node in material.node_tree.nodes:
                if node.type == "VERTEX_COLOR":
                    color = mesh.color_attributes.get(node.layer_name) if node.layer_name else mesh.color_attributes.active_color
                    if color is None:
                        raise ValueError(f"Missing source blend colour: {obj.name}/{material.name}")
                    values = item["colors"].setdefault(node.layer_name or "SourceBlend", [])
                    for loop in tri.loops:
                        index = loop if color and color.domain == "CORNER" else mesh.loops[loop].vertex_index
                        values.append(tuple(color.data[index].color) if color else (1, 1, 1, 1))
        bpy.data.meshes.remove(mesh)
    prop_audit = []
    for name, item in owners.items():
        if not item["solid"] or not item["visual"]:
            continue
        triangles = item["physics"] or item["visual"]
        visual = np.array(item["visual"]).reshape(-1, 3)
        proxy = np.array(triangles).reshape(-1, 3)
        # Reject oversized/misplaced PHY shells, retaining a recorded visual-mesh
        # fallback. Large props can have very different triangle-weighted means.
        rejected_phy = bool(item["physics"]) and bool(np.any(proxy.min(axis=0) < visual.min(axis=0) - 1)
                                                     or np.any(proxy.max(axis=0) > visual.max(axis=0) + 1))
        if rejected_phy:
            triangles = item["visual"]
        physics.extend(triangles)
        prop_audit.append({"source": name, "model": item["model"], "solid": item["solid"],
                           "collision": "authored PHY" if item["physics"] and not rejected_phy else "visual fallback",
                           "rejected_phy_bounds": rejected_phy,
                           "triangles": len(triangles)})
    for obj in originals:
        bpy.data.objects.remove(obj, do_unlink=True)
    visuals = [mesh_object(f"VIS_{i:03d}", material=mat, **data)
               for i, (mat, data) in enumerate(grouped.items())]
    clips, brush_count = [], 0
    # As with Nuketown, exposed map surfaces become collision. Raw convex brush
    # interiors would create invisible floors inside solid masonry in this engine.
    solid = world_triangles + physics
    for index, contents, planes, points, sides in read_brushes(Path(report["bsp"])):
        if not inside(points.mean(axis=0)):
            continue
        if contents & 0x10000:
            brush_count += 1
            tolerance = .25 if points[:, 2].min() < 3.3 else .75
            for box in movement_boxes(planes, points, tolerance):
                clips.append({"brush": index, **box})
    if len(clips) > 256:
        raise ValueError(f"{len(clips)} movement boxes exceed current server limit")
    print(f"GEOMETRY: {len(visuals)} materials, {len(solid)} solid triangles, {len(clips)} clip boxes", flush=True)
    return visuals, solid, clips, low, high, {"source_clip_brushes": brush_count,
        "world_surface_triangles": len(world_triangles),
        "discarded_tool_or_skybox_faces": ignored_faces, "prop_collision": prop_audit}


def bake_blend(obj, directory):
    mat = obj.data.materials[0]
    tree = mat.node_tree
    shader = next(n for n in tree.nodes if n.type == "BSDF_PRINCIPLED")
    output = next(n for n in tree.nodes if n.type == "OUTPUT_MATERIAL")
    source = shader.inputs["Base Color"].links[0].from_socket
    emission = tree.nodes.new("ShaderNodeEmission")
    tree.links.new(source, emission.inputs["Color"])
    tree.links.new(emission.outputs[0], output.inputs["Surface"])
    original_uv = obj.data.uv_layers.active
    original_uv_name = original_uv.name
    for node in tree.nodes:
        if node.type == "UVMAP":
            node.uv_map = original_uv_name
        elif node.type == "VERTEX_COLOR" and not node.layer_name:
            node.layer_name = "SourceBlend"
    uv = obj.data.uv_layers.new(name="ACBake")
    # The material collector emits independent triangles. Join shared vertices
    # before unwrapping, otherwise thousands of tiny islands waste the atlas and
    # produce blurred, visibly triangular ground. Preserve authored corner normals.
    normal_attribute = obj.data.attributes.new(name="BakeSourceNormal", type="FLOAT_VECTOR", domain="CORNER")
    normal_attribute.data.foreach_set("vector", np.array([n.vector[:] for n in obj.data.corner_normals]).ravel())
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True); bpy.context.view_layer.objects.active = obj
    obj.data.uv_layers.active = uv
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.remove_doubles(threshold=1e-6, use_sharp_edge_from_normals=True)
    # Break the map's large, winding floor into compact charts without changing
    # its triangles. A single connected chart would leave courtyards/buildings as
    # empty holes in the atlas and needlessly lower texture detail.
    bm = bmesh.from_edit_mesh(obj.data)
    regions = {face: tuple(math.floor(v / 16) for v in face.calc_center_median()) for face in bm.faces}
    edges = [edge for edge in bm.edges if len({regions[face] for face in edge.link_faces}) > 1]
    bmesh.ops.split_edges(bm, edges=edges)
    bmesh.update_edit_mesh(obj.data)
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=.005)
    bpy.ops.object.mode_set(mode="OBJECT")
    normal_attribute = obj.data.attributes["BakeSourceNormal"]
    obj.data.normals_split_custom_set([value.vector[:] for value in normal_attribute.data])
    obj.data.attributes.remove(normal_attribute)
    # SourceIO's UV node remains bound to the authored UVMap during the bake.
    image = bpy.data.images.new("D2_" + mat.name.lower() + ".png", width=4096, height=4096, alpha=False)
    target = tree.nodes.new("ShaderNodeTexImage"); target.image = image
    tree.nodes.active = target
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"; scene.cycles.samples = 1
    scene.render.bake.margin = 8
    bpy.ops.object.bake(type="EMIT")
    image.filepath_raw = str(directory / image.name); image.file_format = "PNG"
    image.save(); image.pack()
    tree.nodes.remove(emission)
    tree.links.new(shader.outputs[0], output.inputs["Surface"])
    # KN5 supports a single UV set. The combined authored layers are now one atlas.
    obj.data.uv_layers.remove(obj.data.uv_layers[original_uv_name])
    obj.data.uv_layers["ACBake"].name = "UVMap"
    obj.data.calc_loop_triangles()
    uv_data = obj.data.uv_layers.active.data
    uv_area, area = [], []
    for tri in obj.data.loop_triangles:
        a, b, c = (np.array(uv_data[i].uv) for i in tri.loops)
        uv_area.append(abs(np.linalg.det(np.array([b - a, c - a]))) / 2)
        area.append(tri.area)
    density = np.sqrt(np.array(uv_area) / np.maximum(area, 1e-12)) * 4096
    obj["blend_uv_coverage"] = float(sum(uv_area))
    obj["blend_texels_per_meter_p10"] = float(np.percentile(density, 10))
    obj["blend_texels_per_meter_median"] = float(np.median(density))
    packed_uv = np.array([item.uv[:] for item in uv_data])
    if not (.35 <= sum(uv_area) <= 1.001) or np.percentile(density, 10) < 30 \
            or packed_uv.min() < -1e-5 or packed_uv.max() > 1.00001:
        raise ValueError(f"Insufficient blended texture density: {mat.name}: coverage={sum(uv_area)}, p10={np.percentile(density, 10)}")
    return image


def convert_materials(visuals, output):
    audit = []
    for obj in visuals:
        mat = obj.data.materials[0]
        params = mat["vmt_parameters"].to_dict()
        nodes = mat.node_tree.nodes
        diffuse = nodes.get("$basetexture")
        normal = nodes.get("$bumpmap")
        if not diffuse or not diffuse.image:
            raise ValueError(f"No authored diffuse image for {mat.name}")
        image = diffuse.image
        normal_image = normal.image if normal else None
        blend = "$basetexture2" in params
        if blend:
            image = bake_blend(obj, output)
            # The baked colour atlas uses a new UV layout; do not attach an
            # unbaked repeating normal with the wrong coordinates.
            normal_image = None
        alpha_test = str(params.get("$alphatest", "0")) == "1"
        transparent = str(params.get("$translucent", "0")) == "1" or str(params.get("$decal", "0")) == "1"
        shader = "ksPerPixelAlpha" if transparent else "ksPerPixelNM" if normal_image else "ksPerPixel"
        mat.assettoCorsa.shaderName = shader
        mat.assettoCorsa.alphaBlendMode = "1" if transparent else "0"
        mat.assettoCorsa.alphaTested = alpha_test
        mat.assettoCorsa.depthMode = "1" if transparent else "0"
        nodes.clear()
        shader_node = nodes.new("ShaderNodeBsdfPrincipled")
        shader_node.inputs["Roughness"].default_value = .85
        material_output = nodes.new("ShaderNodeOutputMaterial")
        mat.node_tree.links.new(shader_node.outputs[0], material_output.inputs["Surface"])
        texture = nodes.new("ShaderNodeTexImage"); texture.image = image
        texture.assettoCorsa.shaderInputName = "txDiffuse"; texture.show_texture = True
        mat.node_tree.links.new(texture.outputs["Color"], shader_node.inputs["Base Color"])
        if alpha_test or transparent:
            mat.node_tree.links.new(texture.outputs["Alpha"], shader_node.inputs["Alpha"])
        if normal_image:
            texture = nodes.new("ShaderNodeTexImage"); texture.image = normal_image
            texture.assettoCorsa.shaderInputName = "txNormal"
            normal_node = nodes.new("ShaderNodeNormalMap")
            mat.node_tree.links.new(texture.outputs["Color"], normal_node.inputs["Color"])
            mat.node_tree.links.new(normal_node.outputs[0], shader_node.inputs["Normal"])
        audit.append({"material": mat.name, "source_vmt": mat["full_path"], "shader": shader,
                      "diffuse": image.name, "normal": normal_image.name if normal_image else None,
                      "blended_colour_baked": blend, "alpha_test": alpha_test, "transparent": transparent,
                      **({"uv_coverage": obj["blend_uv_coverage"],
                          "texels_per_meter_p10": obj["blend_texels_per_meter_p10"],
                          "texels_per_meter_median": obj["blend_texels_per_meter_median"]} if blend else {})})
    return audit


def sanitize_tangents(obj):
    mesh = obj.data
    mesh.calc_tangents()
    bad = [p for p in mesh.polygons if any(mesh.loops[i].tangent.length_squared < 1e-12 for i in p.loop_indices)]
    if bad:
        normals = [tuple(loop.normal) for loop in mesh.loops]
        for polygon in bad:
            for index in polygon.loop_indices:
                normals[index] = tuple(polygon.normal)
            first = mesh.uv_layers.active.data[polygon.loop_start].uv.copy()
            for index, delta in zip(polygon.loop_indices, ((0, 0), (1e-4, 0), (0, 1e-4))):
                mesh.uv_layers.active.data[index].uv = first + Vector(delta)
        mesh.normals_split_custom_set(normals)
        mesh.update()
        mesh.free_tangents()
        mesh.calc_tangents()
        remaining = [p for p in mesh.polygons if any(mesh.loops[i].tangent.length_squared < 1e-12 for i in p.loop_indices)]
        if remaining:
            if any(p.area > .0001 for p in remaining):
                detail = [{"area": p.area, "positions": [tuple(mesh.vertices[i].co) for i in p.vertices],
                           "uv": [tuple(mesh.uv_layers.active.data[i].uv) for i in p.loop_indices]}
                          for p in remaining if p.area > .0001]
                raise ValueError(f"Unresolved tangent in {obj.name}: {detail}")
            indices = {p.index for p in remaining}
            bm = bmesh.new(); bm.from_mesh(mesh); bm.faces.ensure_lookup_table()
            bmesh.ops.delete(bm, geom=[bm.faces[i] for i in indices], context="FACES")
            bm.to_mesh(mesh); bm.free(); mesh.calc_tangents()
            if any(loop.tangent.length_squared < 1e-12 for loop in mesh.loops):
                raise ValueError(f"Unresolved tangent after degenerate face removal in {obj.name}")
    obj["repaired_normal_triangles"] = len(bad)


def smooth_stairs(solid, ramps):
    """Replace collision inside measured tread footprints; visuals stay authored."""
    audit = []
    for ramp in ramps:
        x0, x1 = ramp["x"]; y0, y1 = ramp["y"]; h0, h1 = ramp["height"]
        def height(x, y):
            if ramp["axis"] == "turn":
                t = math.atan2(max(0, x - x0), max(0, y1 - y)) / (math.pi / 2)
            else:
                t = (x - x0) / (x1 - x0) if ramp["axis"] == "x" else (y - y0) / (y1 - y0)
            return h0 + (h1 - h0) * t + .015
        retained, removed = [], 0
        for triangle in solid:
            v = np.asarray(triangle)
            # Only remove geometry entirely inside the measured stair footprint
            # and below its top. Surrounding wall/ceiling triangles are retained.
            if (v[:, 0].min() >= x0 - .015 and v[:, 0].max() <= x1 + .015
                    and v[:, 1].min() >= y0 - .015 and v[:, 1].max() <= y1 + .015
                    and v[:, 2].min() >= min(h0, h1) - .025
                    and v[:, 2].max() <= max(h0, h1) + .025):
                removed += 1
            else:
                retained.append(triangle)
        # A fine tessellation follows the quarter-turn stair without a diagonal
        # bridge across the surrounding masonry.
        subdivisions = 16 if ramp["axis"] == "turn" else 1
        xs = np.linspace(x0, x1, subdivisions + 1); ys = np.linspace(y0, y1, subdivisions + 1)
        for x, nx in zip(xs, xs[1:]):
            for y, ny in zip(ys, ys[1:]):
                a = (x, y, height(x, y)); b = (nx, y, height(nx, y))
                c = (nx, ny, height(nx, ny)); d = (x, ny, height(x, ny))
                retained.extend(((a, b, c), (a, c, d)))
        solid = retained
        audit.append({"name": ramp["name"], "replaced_triangles": removed, "ramp_triangles": subdivisions ** 2 * 2})
    return solid, audit


def export(path, keep, collision, exporter):
    names, material_names, image_names, texture_names = [], [], [], []
    keep = set(keep)
    used = {slot.material for obj in keep if obj.type == "MESH" for slot in obj.material_slots}
    for obj in list(bpy.data.objects):
        if obj not in keep:
            names.append((obj, obj.name)); obj.name = "__" + obj.name
    for mat in list(bpy.data.materials):
        if mat not in used:
            material_names.append((mat, mat.name)); mat.name = "__" + mat.name
            if mat.node_tree:
                for node in mat.node_tree.nodes:
                    if node.type == "TEX_IMAGE":
                        texture_names.append((node, node.name)); node.name = "__" + node.name
    # Exporter scans all object textures independently of object visibility.
    images = {node.image for mat in used if mat and mat.node_tree
              for node in mat.node_tree.nodes if node.type == "TEX_IMAGE"}
    for image in list(bpy.data.images):
        if image not in images:
            image_names.append((image, image.name)); image.name = "__" + image.name
    settings = {"nodes": {"*": {"lodIn": 0, "lodOut": 1500, "visible": True,
                                "renderable": not collision, "castShadows": not collision}},
                "materials": {mat.name: {"properties": {
                    "ksAmbient": {"valueA": .35}, "ksDiffuse": {"valueA": .65},
                    "ksSpecular": {"valueA": .08}, "ksSpecularEXP": {"valueA": 18},
                    "ksAlphaRef": {"valueA": .5}, "fresnelC": {"valueA": .1},
                    "fresnelEXP": {"valueA": 5}}} for mat in used}}
    try:
        bpy.context.view_layer.update()
        warnings = []
        with path.open("wb") as stream:
            exporter.KN5FileWriter(stream, bpy.context, settings, warnings, root_node_name="DUST2",
                                  forward_axis="-Y", transform_tangent_space=not collision).write()
        return warnings
    finally:
        for obj, name in names + material_names + image_names + texture_names:
            obj.name = name


def render_preview(root, track, low, high):
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"; scene.cycles.samples = 24; scene.cycles.use_denoising = True
    scene.render.resolution_x = 1600; scene.render.resolution_y = 1100; scene.render.resolution_percentage = 100
    scene.world = bpy.data.worlds.new("Dust II daylight"); scene.world.use_nodes = True
    scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value = .7
    sun = bpy.data.lights.new("Preview sun", "SUN"); sun.energy = 3; sun.angle = .15
    light = bpy.data.objects.new("Preview sun", sun); scene.collection.objects.link(light)
    light.rotation_euler = (math.radians(28), math.radians(-15), math.radians(-30))
    camera = bpy.data.cameras.new("Preview camera")
    cam = bpy.data.objects.new("Preview camera", camera); scene.collection.objects.link(cam); scene.camera = cam
    target = Vector((low + high) / 2); target.z = 0
    for name, offset, ortho in (("overview", (100, -130, 115), False), ("topdown", (0, 0, 160), True)):
        cam.location = target + Vector(offset)
        cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
        camera.type = "ORTHO" if ortho else "PERSP"; camera.lens = 46; camera.ortho_scale = 180
        scene.render.filepath = str(root / (name + ".png")); bpy.ops.render.render(write_still=True)
    (track / "ui/preview.png").write_bytes((root / "overview.png").read_bytes())


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--layout")
    parser.add_argument("--skip-renders", action="store_true")
    args = parser.parse_args(sys.argv[sys.argv.index("--") + 1:])
    layout = json.loads(Path(args.layout).read_text()) if args.layout else None
    source = Path(args.source).resolve(); root = Path(args.output).resolve()
    track = root / "content/tracks/css_dust2"
    for sub in ("data", "ui", "extension"):
        (track / sub).mkdir(parents=True, exist_ok=True)
    report = json.loads((source / "source-import.json").read_text())
    if hashlib.sha256(Path(report["bsp"]).read_bytes()).hexdigest() != report["bsp_sha256"]:
        raise ValueError("Source BSP changed after import")
    bpy.ops.wm.open_mainfile(filepath=str(source / "dust2-source.blend"))
    sys.path.insert(0, str(Path(__file__).resolve().parent / "vendor"))
    import blender_assetto_corsa_tools as addon
    addon.register()
    from blender_assetto_corsa_tools import exporter
    visuals, solid, clips, low, high, geometry_audit = prepare_geometry(report)
    solid, geometry_audit["stair_adaptation"] = smooth_stairs(solid, layout.get("collisionRamps", []) if layout else [])
    material_audit = convert_materials(visuals, root)
    for obj in visuals:
        sanitize_tangents(obj)
    for mat in list(bpy.data.materials):
        if mat not in {obj.data.materials[0] for obj in visuals}:
            bpy.data.materials.remove(mat)
    collision_mat = bpy.data.materials.new("D2_COLLISION"); collision_mat.use_nodes = True
    collision_mat.node_tree.nodes.clear()
    collision_mat.assettoCorsa.shaderName = "ksPerPixel"
    roads, walls = [], []
    for tri in solid:
        v = np.asarray(tri)
        normal = np.cross(v[1] - v[0], v[2] - v[0]); length = np.linalg.norm(normal)
        if length > 1e-9:
            (roads if normal[2] / length >= math.cos(math.pi / 4) - 1e-5 else walls).append(tri)
    # Remote hidden car support, following the Nuketown FPS carrier convention.
    pad = [[(x, y, -.1) for x, y in [(230, -15), (310, -15), (310, 15)]],
           [(x, y, -.1) for x, y in [(230, -15), (310, 15), (230, 15)]]]
    colliders = [mesh_object("1ROAD_D2", roads + pad, collision_mat), mesh_object("1WALL_D2", walls, collision_mat)]
    for obj in colliders:
        obj.hide_render = True
    markers = []
    for i, point in enumerate(((low[0], low[1], 0), (high[0], low[1], 0),
                               (high[0], high[1], 0), (low[0], high[1], 0))):
        markers.append(marker(f"FPS_BOUNDARY_{i}", point))
    for i, clip in enumerate(clips):
        center = np.array(clip["center"]); axes = np.array(clip["half_axes"])
        markers.append(marker(f"FPS_MOVEBOX_{i}_CENTER", center))
        for label, delta in (("X", axes[0]), ("Y", axes[2]), ("Z", -axes[1])):
            markers.append(marker(f"FPS_MOVEBOX_{i}_{label}", center + delta))
    if layout:
        spawns = layout["spawns"]
    else:
        starts = [e for e in report["source_entities"] if e.get("classname", "").startswith("info_player_")]
        # Balanced source starts bootstrap preparation; the validated layout can
        # replace these with distributed arena spawns without reimporting Source.
        chosen = [e for kind in ("info_player_terrorist", "info_player_counterterrorist")
                  for e in [s for s in starts if s["classname"] == kind][::3]][:16]
        spawns = [ac([float(x) * .0254 for x in e["origin"].split()]) +
                  [float(e.get("angles", "0 0 0").split()[1]) + 90] for e in chosen]
    for i, (x, y, z, yaw) in enumerate(spawns):
        markers.append(marker(f"AC_START_{i}", (x, -z, y), math.radians(yaw)))
        pit = (236 + (i % 8) * 9, -9 + (i // 8) * 12, .05)
        markers.append(marker(f"AC_PIT_{i}", pit))
        markers.append(marker(f"AC_CREW_{i}", (pit[0] + 1.8, pit[1], pit[2])))
    markers.append(marker("AC_HOTLAP_START_0", (250, 0, .05)))
    for i, y in enumerate((-12, 12)):
        markers += [marker(f"AC_TIME_{i}_L", (231, y, .05)), marker(f"AC_TIME_{i}_R", (309, y, .05))]
    bpy.ops.wm.save_as_mainfile(filepath=str(root / "dust2-ac.blend"))
    visual_warnings = export(track / "css_dust2.kn5", visuals + markers, False, exporter)
    collision_warnings = export(track / "css_dust2_collision.kn5", colliders, True, exporter)
    (track / "models.ini").write_text("[MODEL_0]\nFILE=css_dust2.kn5\nPOSITION=0,0,0\nROTATION=0,0,0\n\n[MODEL_1]\nFILE=css_dust2_collision.kn5\nPOSITION=0,0,0\nROTATION=0,0,0\n")
    surfaces = ""
    for i, key in enumerate(("ROAD", "WALL")):
        surfaces += f"[SURFACE_{i}]\nKEY={key}\nFRICTION=1\nDAMPING=0\nWAV=\nWAV_PITCH=1\nFF_EFFECT=NULL\nDIRT_ADDITIVE=0\nIS_VALID_TRACK={1-i}\nBLACK_FLAG_TIME=0\nSIN_HEIGHT=0\nSIN_LENGTH=0\nIS_PITLANE=0\nVIBRATION_GAIN=0\nVIBRATION_LENGTH=0\n\n"
    (track / "data/surfaces.ini").write_text(surfaces)
    (track / "data/lighting.ini").write_text("[LIGHTING]\nSUN_PITCH_ANGLE=55\nSUN_HEADING_ANGLE=-35\n")
    (track / "data/crew.ini").write_text("[HEADER]\nSIDE=1\n")
    (track / "extension/ext_config.ini").write_text(f"[ABOUT]\nAUTHOR=Valve; local FPS conversion\nVERSION={VERSION}\n[GRASS_FX]\nENABLED=0\n")
    (track / "ui/ui_track.json").write_text(json.dumps({"name": "Dust II (CS:S FPS)", "version": VERSION,
        "description": "Local Counter-Strike: Source Dust II conversion for Race Control FPS mode.",
        "author": "Valve; local FPS conversion", "tags": ["FPS", "Counter-Strike: Source", "prototype"],
        "country": "Fictional", "city": "Dust II", "length": "136 x 132 m", "width": "varies",
        "pitboxes": str(len(spawns)), "run": "FPS arena"}, indent=2) + "\n")
    audit = {"track_id": "css_dust2", "version": VERSION, "source_bsp_sha256": report["bsp_sha256"],
        "sourceio_commit": report["sourceio_commit"], "blender": bpy.app.version_string,
        "source_scale": .0254, "axes": "Source/Blender XYZ -> AC X,Z,-Y (once)",
        "materials": material_audit, "collision": geometry_audit, "spawns": spawns,
        "movement_boxes": clips, "visual_triangles": sum(len(o.data.polygons) for o in visuals),
        "road_triangles": len(roads), "wall_triangles": len(walls),
        "repaired_collapsed_uv_triangles": sum(o["repaired_uv_triangles"] for o in visuals),
        "repaired_normal_triangles": sum(o["repaired_normal_triangles"] for o in visuals),
        "visual_export_warnings": visual_warnings, "collision_export_warnings": collision_warnings,
        "runtime_validated": False,
        "limitations": ["Physics props are frozen at their authored positions.",
            "Source player clips are upright-box covers; maximum plane overreach is 0.25 m near ground and 0.75 m on high roofs.",
            "Two world blend materials retain baked colour; their blended normal detail is omitted.",
            "Source baked lighting, particles, sprites, audio and environment reflections are not ported."]}
    (track / "conversion-report.json").write_text(json.dumps(audit, indent=2) + "\n")
    (root / "layout.json").write_text(json.dumps({"spawns": spawns}, indent=2) + "\n")
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(root / "dust2-ac.blend"))
    if not args.skip_renders:
        render_preview(root, track, low, high)
    print(f"STAGED: {track}", flush=True)


if __name__ == "__main__":
    main()
