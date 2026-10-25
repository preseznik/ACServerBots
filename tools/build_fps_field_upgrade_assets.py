"""Convert approved Tripo GLBs to bottom-centred, metre-scale CSP KN5 props.

Run in Blender --background --python ... -- --munitions GLB --trophy GLB.
The GLBs and task records are retained separately; no paid conversion is used.
"""
import argparse
import json
from pathlib import Path
import sys
import tempfile

import bpy
from mathutils import Vector

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tools'))
sys.path.insert(0, str(ROOT / 'tools/vendor'))
import build_fps_desert_eagle_assets as common
import blender_assetto_corsa_tools as ac_tools
from blender_assetto_corsa_tools import exporter


def build(slug, source, output, previews, work):
    common.reset_scene()
    bpy.ops.import_scene.gltf(filepath=str(source))
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == 'MESH']
    if not meshes:
        raise RuntimeError(f'No meshes in {source}')
    points = [obj.matrix_world @ Vector(c) for obj in meshes for c in obj.bound_box]
    low = Vector(tuple(min(p[i] for p in points) for i in range(3)))
    high = Vector(tuple(max(p[i] for p in points) for i in range(3)))
    size = high - low
    # Blender imports glTF Y-up into Blender Z-up; the shared writer exports AC Y-up.
    factor = 0.60 / (max(size.x, size.y) if slug == 'munitions_box' else size.z)
    center = Vector(((low.x + high.x) / 2, (low.y + high.y) / 2, low.z))
    material_map = {}
    for obj in meshes:
        world = obj.matrix_world.copy()
        obj.parent = None
        for vertex in obj.data.vertices:
            vertex.co = (world @ vertex.co - center) * factor
        obj.matrix_world.identity()
        for index, original in enumerate(list(obj.data.materials)):
            if original.name not in material_map:
                textures = {}
                shader = next((n for n in original.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
                if shader is None or not shader.inputs['Base Color'].is_linked:
                    raise RuntimeError(f'Missing base texture: {original.name}')
                image = shader.inputs['Base Color'].links[0].from_node.image
                diffuse = work / f'{slug}_{len(material_map)}.png'
                image.filepath_raw = str(diffuse)
                image.file_format = 'PNG'
                image.save()
                textures['txDiffuse'] = (diffuse, 1024, False)
                material_map[original.name] = common.create_material(
                    f'ASRC_{slug.upper()}_{len(material_map)}', 'ksPerPixel', textures, work)
            obj.data.materials[index] = material_map[original.name]
        obj.name = f'ASRC_{slug.upper()}_{obj.name}'
    for obj in list(bpy.context.scene.objects):
        if obj not in meshes:
            common.remove_object(obj)
    common.purge_unused_data()
    bpy.context.view_layer.update()
    triangles = sum(len(p.vertices) - 2 for obj in meshes for p in obj.data.polygons)
    if triangles > 6500:
        raise RuntimeError(f'Prop exceeds budget: {triangles} triangles')
    target = output / f'asrc_{slug}.kn5'
    common.export_kn5(target, exporter, True)
    metrics = {'source': str(source), 'triangles': triangles, 'sizeMetres': list(size * factor),
               'materials': len(material_map), 'kn5Bytes': target.stat().st_size}
    (previews / f'{slug}.json').write_text(json.dumps(metrics, indent=2) + '\n')
    # Render the actual normalized meshes/materials, after KN5 export (no camera in KN5).
    bpy.ops.object.camera_add(location=(0.95, -1.25, 0.92))
    camera = bpy.context.object
    camera.rotation_euler = (Vector((0, 0, 0.25)) - camera.location).to_track_quat('-Z', 'Y').to_euler()
    camera.data.type = 'ORTHO'
    camera.data.ortho_scale = 0.95
    scene = bpy.context.scene
    scene.camera = camera
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 24
    scene.render.resolution_x = scene.render.resolution_y = 640
    scene.render.resolution_percentage = 100
    scene.render.film_transparent = True
    for location, energy, light_size in [((2,-3,4), 450, 3), ((-3,-1,2), 250, 2)]:
        bpy.ops.object.light_add(type='AREA', location=location)
        light = bpy.context.object
        light.data.energy, light.data.shape, light.data.size = energy, 'DISK', light_size
        light.rotation_euler = (-light.location).to_track_quat('-Z', 'Y').to_euler()
    scene.render.image_settings.file_format = 'PNG'
    scene.render.filepath = str(previews / f'{slug}.png')
    bpy.ops.render.render(write_still=True)
    print(json.dumps(metrics))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--munitions', type=Path, required=True)
    parser.add_argument('--trophy', type=Path, required=True)
    parser.add_argument('--output', type=Path, default=ROOT / 'AssettoServer.RaceControl.Core/Assets/Fps')
    parser.add_argument('--previews', type=Path, default=ROOT / '.artifacts/fps-field-upgrades/converted')
    options = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    options.output.mkdir(parents=True, exist_ok=True)
    options.previews.mkdir(parents=True, exist_ok=True)
    ac_tools.register()
    with tempfile.TemporaryDirectory(prefix='asrc-field-props-') as temporary:
        for slug, path in [('munitions_box', options.munitions), ('trophy_system', options.trophy)]:
            build(slug, path.resolve(), options.output, options.previews, Path(temporary))


if __name__ == '__main__':
    main()
