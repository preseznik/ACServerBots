# This program is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.
#
# Adapted from jwl-7/blender-assetto-corsa-tools revision
# 920bac087de1caad32ae63725dc6fa302c9b9c18. The original writer is GPLv3.

from __future__ import annotations

import bpy

from .exporter_utils import convert_matrix
from .kn5_writer import KN5Writer


class KSAnimWriter(KN5Writer):
    """Deterministic writer for Assetto Corsa KSANIM version 2 files."""

    def __init__(self, file, context, objects, frame_start, frame_end,
                 frame_callback=None, reverse_animation=False):
        super().__init__(file)
        self.context = context
        self.objects = list(objects)
        self.frame_start = int(frame_start)
        self.frame_end = int(frame_end)
        self.frame_callback = frame_callback
        self.reverse_animation = reverse_animation
        self.tracks = []

    @staticmethod
    def _components(matrix):
        position, rotation, scale = convert_matrix(matrix).decompose()
        return (
            rotation.x, rotation.y, rotation.z, rotation.w,
            position.x, position.y, position.z,
            scale.x, scale.y, scale.z,
        )

    def _build_track_list(self):
        tracks = []
        for obj in self.objects:
            if obj.type == "ARMATURE":
                tracks.extend((bone.name, bone) for bone in obj.pose.bones)
            else:
                tracks.append((obj.name, obj))
        names = [name for name, _ in tracks]
        if len(names) != len(set(names)):
            raise ValueError("KSANIM track names must be unique")
        return tracks

    @staticmethod
    def _matrix_for_track(track):
        if isinstance(track, bpy.types.PoseBone):
            if track.parent:
                return track.parent.matrix.inverted() @ track.matrix
            return track.matrix
        return track.matrix_local

    def write(self):
        tracks = self._build_track_list()
        frame_numbers = list(range(self.frame_start, self.frame_end + 1))
        if self.reverse_animation:
            frame_numbers.reverse()
        frames = {name: [] for name, _ in tracks}
        scene = self.context.scene
        layer = self.context.view_layer
        original_frame = scene.frame_current
        try:
            for frame in frame_numbers:
                scene.frame_set(frame)
                if self.frame_callback is not None:
                    self.frame_callback(frame)
                layer.update()
                for name, track in tracks:
                    frames[name].append(self._components(self._matrix_for_track(track)))
        finally:
            scene.frame_set(original_frame)

        self.write_uint(2)
        self.write_uint(len(tracks))
        for name, _ in tracks:
            self.write_string(name)
            self.write_uint(len(frames[name]))
            for frame in frames[name]:
                for value in frame:
                    self.write_float(value)
