"""Source 1 convex brushes in metres; no game or Blender runtime required."""
from __future__ import annotations

import itertools
import math
import struct

import numpy as np

SCALE = 0.0254


def vertices(planes):
    planes = np.asarray(planes, dtype=float)
    triples = np.array(list(itertools.combinations(range(len(planes)), 3)))
    matrices = planes[triples, :3]
    valid = np.abs(np.linalg.det(matrices)) > 1e-8
    if not valid.any():
        return np.empty((0, 3))
    points = np.linalg.solve(matrices[valid], planes[triples[valid], 3, None])[..., 0]
    points = points[np.all(points @ planes[:, :3].T <= planes[:, 3] + 1e-5, axis=1)]
    return np.unique(np.round(points, 6), axis=0)


def read_brushes(path):
    data = path.read_bytes()
    def lump(index):
        offset, size, _, compressed = struct.unpack_from("<4i", data, 8 + 16 * index)
        if compressed:
            raise ValueError("Compressed BSP requires explicit decoding")
        return data[offset:offset + size]
    planes = np.array([p[:4] for p in struct.iter_unpack("<4fi", lump(1))])
    planes[:, 3] *= SCALE
    sides = list(struct.iter_unpack("<Hhhh", lump(19)))
    for index, (first, count, contents) in enumerate(struct.iter_unpack("<3i", lump(18))):
        if not contents & (1 | 2 | 0x10000):
            continue
        brush_sides = sides[first:first + count]
        equations = planes[[side[0] for side in brush_sides]]
        points = vertices(equations)
        if len(points) < 4:
            raise ValueError(f"Degenerate source brush {index}")
        yield index, contents, equations, points, brush_sides


def brush_faces(planes, points, sides):
    for plane, side in zip(planes, sides):
        # This CS:S BSP writes zero into the third brush-side short throughout;
        # it is not a usable displacement index. Preserve the compiled solids.
        face = points[np.abs(points @ plane[:3] - plane[3]) < 2e-4]
        if len(face) < 3:
            continue
        normal = plane[:3]
        axis = np.eye(3)[np.argmin(np.abs(normal))]
        u = np.cross(axis, normal)
        u /= np.linalg.norm(u)
        v = np.cross(normal, u)
        offsets = face - face.mean(axis=0)
        face = face[np.argsort(np.arctan2(offsets @ v, offsets @ u))]
        for i in range(1, len(face) - 1):
            yield [face[0], face[i], face[i + 1]]


def movement_boxes(planes, points, tolerance=0.20):
    """Cover a clip brush with upright boxes, with bounded plane overreach.

    Rectangular brushes remain exact. For bevels, adaptively split tight bounds;
    the audit records the conservative approximation, never bullet geometry.
    """
    angles = [0.0] + [math.atan2(p[1], p[0]) for p in planes if abs(p[2]) < 1e-5]
    def basis(angle):
        c, s = math.cos(angle), math.sin(angle)
        return np.array([[c, s, 0], [-s, c, 0], [0, 0, 1]])
    def area(angle):
        size = np.ptp(points @ basis(angle).T, axis=0)
        return size[0] * size[1]
    rotation = basis(min(angles, key=area))
    local = np.column_stack((planes[:, :3] @ rotation.T, planes[:, 3]))
    boxes = []
    def split(equations, depth=0):
        pts = vertices(equations)
        if len(pts) < 4:
            return
        low, high = pts.min(axis=0), pts.max(axis=0)
        if np.min(high - low) < 0.021:
            return
        corners = np.array(list(itertools.product(*zip(low, high))))
        overreach = np.max(corners @ local[:, :3].T - local[:, 3], axis=0)
        worst = int(np.argmax(overreach))
        if overreach[worst] <= tolerance or depth >= 16:
            if overreach[worst] > tolerance + 1e-5:
                raise ValueError("Clip approximation exceeded its tolerance")
            boxes.append({"center": ((low + high) / 2 @ rotation).tolist(),
                          "half_axes": [(rotation[i] * (high[i] - low[i]) / 2).tolist()
                                        for i in range(3)],
                          "overreach": max(0.0, float(overreach[worst]))})
            return
        axis = int(np.argmax(np.abs(local[worst, :3]) * (high - low)))
        mid = (low[axis] + high[axis]) / 2
        p = np.zeros(4); p[axis] = 1; p[3] = mid
        split(np.vstack((equations, p)), depth + 1)
        split(np.vstack((equations, -p)), depth + 1)
    split(local)
    return boxes
