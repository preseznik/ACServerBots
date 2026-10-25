"""Exact depot lettering rasterized into one atlas, with one quad per sign.

Blender loads DecalAtlas; the bundled Pillow Python runs this same file's small
--rasterize command. Generated wheel art is never read or modified by this code.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import subprocess


ATLAS_SIZE = 2048
ATLAS_NAME = "LS_SIGN_ATLAS.png"
MATERIAL_NAME = "LS_SIGN_ATLAS"


def _srgb(value):
    value = max(0.0, min(1.0, float(value)))
    return round(255 * (12.92 * value if value <= .0031308 else 1.055 * value ** (1 / 2.4) - .055))


def _pack(records):
    """Deterministic shelf packing; retain a four-pixel gutter around each tile."""
    requested = []
    for record in records:
        lines = max(1, len(record["text"].splitlines()))
        width, height = record["width"], record["height"]
        density = max(360, (lines * 36 + 16) / height)
        scale = min(1, 1536 / (width * density), 640 / (height * density))
        requested.append((max(48, math.ceil(width * density * scale)),
                          max(48, math.ceil(height * density * scale))))
    for scale in (1.0, .9, .8, .7, .6, .5):
        sizes = [(max(24, math.ceil(w * scale)), max(24, math.ceil(h * scale))) for w, h in requested]
        order = sorted(range(len(records)), key=lambda i: (-sizes[i][1], -sizes[i][0], i))
        positions = {}
        x = y = row_height = 0
        for index in order:
            width, height = sizes[index]
            if x + width + 8 > ATLAS_SIZE:
                x, y, row_height = 0, y + row_height, 0
            if y + height + 8 > ATLAS_SIZE:
                break
            positions[index] = [x + 4, y + 4, width, height]
            x += width + 8
            row_height = max(row_height, height + 8)
        if len(positions) == len(records):
            return positions
    raise ValueError("Sign atlas exceeds 2048 square pixels; split the content deliberately.")


def rasterize(manifest_path, output_path):
    """Draw code-authored text and borders only; never processes wheel artwork."""
    from PIL import Image, ImageDraw, ImageFont

    document = json.loads(Path(manifest_path).read_text(encoding="utf-8"))
    image = Image.new("RGB", (ATLAS_SIZE, ATLAS_SIZE), (18, 21, 22))
    draw = ImageDraw.Draw(image)
    font_path = document["fontPath"]
    for record in document["signs"]:
        x, y, width, height = record["textureRect"]
        background, foreground = tuple(record["backgroundRgb"]), tuple(record["textRgb"])
        # Edge colour extends into the gutter to reduce mip bleeding.
        draw.rectangle((x - 4, y - 4, x + width + 3, y + height + 3), fill=background)
        border = tuple(round(a * .55 + b * .45) for a, b in zip(background, foreground))
        inset = max(2, round(min(width, height) * .035))
        draw.rectangle((x + inset, y + inset, x + width - inset - 1, y + height - inset - 1),
                       outline=border, width=max(1, round(min(width, height) * .012)))
        text = record["text"]
        if not text:
            record["rasterFontPixels"] = 0
            continue
        margin = max(5, inset * 2)
        line_count = max(1, len(text.splitlines()))
        font_size = max(8, int((height - margin * 2) / (line_count * 1.2)))
        while font_size >= 5:
            font = ImageFont.truetype(font_path, font_size)
            spacing = max(1, round(font_size * .18))
            bounds = draw.multiline_textbbox((0, 0), text, font=font, spacing=spacing, align="center")
            if bounds[2] - bounds[0] <= width - 2 * margin and bounds[3] - bounds[1] <= height - 2 * margin:
                break
            font_size -= 1
        if font_size < 5:
            raise ValueError("Sign text cannot fit its atlas tile: " + record["label"])
        tx = x + (width - bounds[2] + bounds[0]) / 2 - bounds[0]
        ty = y + (height - bounds[3] + bounds[1]) / 2 - bounds[1]
        draw.multiline_text((tx, ty), text, fill=foreground, font=font, spacing=spacing, align="center")
        record["rasterFontPixels"] = font_size
    image.save(output_path, format="PNG", optimize=True)
    document["sha256"] = hashlib.sha256(Path(output_path).read_bytes()).hexdigest()
    Path(manifest_path).write_text(json.dumps(document, indent=2) + "\n", encoding="utf-8")


class DecalAtlas:
    def __init__(self, author, outputcache, raster_python=None, font_path=None):
        import bpy

        self.a = author
        self.bpy = bpy
        self.output = Path(outputcache)
        self.output.mkdir(parents=True, exist_ok=True)
        self.records = []
        self.objects = []
        self.raster_python = Path(raster_python or os.environ.get("LAST_SHIFT_RASTER_PYTHON", "") or
            Path(os.environ.get("USERPROFILE", "")) / ".cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe")
        self.font_path = Path(font_path or os.environ.get("LAST_SHIFT_SIGN_FONT", "C:/Windows/Fonts/arialbd.ttf"))
        mat = bpy.data.materials.new(MATERIAL_NAME)
        mat.use_nodes = True
        mat.diffuse_color = (1, 1, 1, 1)
        mat["tile_metres"] = (1.0, 1.0)
        mat["ac_shader"] = "ksPerPixel"
        mat["ac_specular"] = .08
        mat["ac_specular_exp"] = 12
        shader = mat.node_tree.nodes.get("Principled BSDF")
        shader.inputs["Roughness"].default_value = .72
        placeholder = bpy.data.images.new("__LS_SIGN_ATLAS_PENDING", width=2, height=2, alpha=False)
        placeholder.pixels = [1.0, 1.0, 1.0, 1.0] * 4
        self.texture = mat.node_tree.nodes.new("ShaderNodeTexImage")
        self.texture.name = MATERIAL_NAME + "_diffuse"
        self.texture.image = placeholder
        self.texture.interpolation = "Linear"
        self.texture.extension = "EXTEND"
        self.texture.assettoCorsa.shaderInputName = "txDiffuse"
        mat.node_tree.nodes.active = self.texture
        mat.node_tree.links.new(self.texture.outputs["Color"], shader.inputs["Base Color"])
        self.material = mat
        author.MATS["sign_atlas"] = mat

    def panel(self, label, text, center, width, height, face="south", mat="sign_blue",
              text_mat="paint", text_size=None, yaw=0):
        import bmesh
        from mathutils import Vector

        if width <= 0 or height <= 0:
            raise ValueError("Sign dimensions must be positive.")
        if face not in ("south", "east", "north", "west"):
            raise ValueError("Unsupported sign face: " + face)
        cardinal_yaw = {"south": 0, "east": 90, "north": 180, "west": 270}[face]
        normal = {"south": (0, 0, -1), "east": (1, 0, 0),
                  "north": (0, 0, 1), "west": (-1, 0, 0)}[face]
        cosine, sine = math.cos(math.radians(yaw)), math.sin(math.radians(yaw))
        normal = (normal[0]*cosine+normal[2]*sine, 0, -normal[0]*sine+normal[2]*cosine)
        depth, mount_offset = .055, .020
        backing_position = tuple(center[i] + mount_offset * normal[i] for i in range(3))
        backing = self.a.box(label + " backing", backing_position, (width, height, depth),
                             mat, solid=False, yaw=yaw-cardinal_yaw)
        old_name = backing.name
        backing.name = f"LS_DECAL_BACKING_{len(self.records):04d}"
        for semantic in reversed(self.a.SEMANTICS):
            if semantic["node"] == old_name:
                semantic["node"] = backing.name
                break
        # The bitmap is the board's only front surface. Retaining a second face
        # a few millimetres behind it produces depth fighting at game distances.
        local_normal = backing.rotation_euler.to_matrix().transposed() @ Vector(self.a.xyz(normal))
        body = bmesh.new()
        try:
            body.from_mesh(backing.data)
            front = [polygon for polygon in body.faces if polygon.normal.dot(local_normal) > .999]
            if len(front) != 1:
                raise ValueError("Expected one sign backing front polygon: " + label)
            bmesh.ops.delete(body, geom=front, context="FACES_ONLY")
            body.to_mesh(backing.data)
        finally:
            body.free()
        backing.data.update()
        backing["sign_backing_front_removed"] = True
        # right cross up is the outward normal; UVs read left-to-right from outside.
        right = (normal[2], 0, -normal[0])
        front_offset = mount_offset + depth / 2
        origin = tuple(center[i] + front_offset * normal[i] for i in range(3))
        vertices = []
        for u, v in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            point = tuple(origin[i] + u * width * .5 * right[i] + (v * height * .5 if i == 1 else 0)
                          for i in range(3))
            vertices.append(self.a.xyz(point))
        node_name = f"LS_DECAL_{len(self.records):04d}"
        mesh = self.bpy.data.meshes.new(node_name)
        mesh.from_pydata(vertices, [], [(0, 1, 2, 3)])
        mesh.update()
        mesh.uv_layers.new(name="UVMap")
        obj = self.bpy.data.objects.new(node_name, mesh)
        self.bpy.context.collection.objects.link(obj)
        obj["detail_label"] = label + " bitmap face"
        obj["decal_atlas"] = ATLAS_NAME
        obj.data.materials.append(self.material)
        self.a.VISUALS.append(obj)
        record = dict(node=node_name, label=label, text=str(text), material=self.material.name,
                      atlas=ATLAS_NAME, vertices=4, triangles=2, width=width, height=height,
                      position=list(center), face=face, yaw=yaw, normal=list(normal), textSizeHint=text_size,
                      frontPosition=list(origin), frontOffset=front_offset,
                      backingNode=backing.name, backingPosition=list(backing_position),
                      backingDepth=depth, mountOffset=mount_offset, backingFrontRemoved=True,
                      backgroundRgb=[_srgb(c) for c in self.a.MATS[mat].diffuse_color[:3]],
                      textRgb=[_srgb(c) for c in self.a.MATS[text_mat].diffuse_color[:3]])
        self.records.append(record)
        self.objects.append(obj)
        return obj

    def finalize(self):
        if not self.raster_python.is_file():
            raise FileNotFoundError("Set LAST_SHIFT_RASTER_PYTHON to a Python executable with Pillow.")
        if not self.font_path.is_file():
            raise FileNotFoundError("Set LAST_SHIFT_SIGN_FONT to a readable TrueType font.")
        positions = _pack(self.records)
        for index, record in enumerate(self.records):
            x, y, width, height = positions[index]
            record["textureRect"] = [x, y, width, height]
            u0, u1 = (x + .5) / ATLAS_SIZE, (x + width - .5) / ATLAS_SIZE
            v0, v1 = 1 - (y + height - .5) / ATLAS_SIZE, 1 - (y + .5) / ATLAS_SIZE
            record["uvBounds"] = [u0, v0, u1, v1]
            uv = self.objects[index].data.uv_layers.active
            for loop, pair in zip(uv.data, ((u0, v0), (u1, v0), (u1, v1), (u0, v1))):
                loop.uv = pair
        manifest_path = self.output / "LS_SIGN_ATLAS.json"
        image_path = self.output / ATLAS_NAME
        document = dict(schemaVersion=1, atlas=ATLAS_NAME, dimensions=[ATLAS_SIZE, ATLAS_SIZE],
                        source="Code-authored exact depot labels", rasterizer="Pillow ImageDraw",
                        fontPath=str(self.font_path), font="Arial Bold", signs=self.records)
        manifest_path.write_text(json.dumps(document, indent=2) + "\n", encoding="utf-8")
        subprocess.run([str(self.raster_python), str(Path(__file__).resolve()), "--rasterize",
                        str(manifest_path), "--output", str(image_path)], check=True, timeout=60)
        final = json.loads(manifest_path.read_text(encoding="utf-8"))
        self.records[:] = final["signs"]
        old_image = self.texture.image
        image = self.bpy.data.images.load(str(image_path), check_existing=False)
        image.name = ATLAS_NAME
        image.colorspace_settings.name = "sRGB"
        image.pack()
        self.texture.image = image
        self.bpy.data.images.remove(old_image)
        return final


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("--rasterize", required=True)
    parser.add_argument("--output", required=True)
    arguments = parser.parse_args()
    rasterize(arguments.rasterize, arguments.output)
