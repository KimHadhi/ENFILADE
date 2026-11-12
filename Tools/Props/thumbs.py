# Run inside Blender (-b --factory-startup): renders a labelled 192 px thumbnail per job (an FBX with its base colour,
# or a texture-only swatch) and tiles them into contact sheets of 40, so assets known only by id can be picked by eye.
# Usage: blender -b --factory-startup --disable-autoexec --python thumbs.py -- <jobs.json> <out_dir> <sheet_prefix>
import json
import math
import os
import sys

import bpy
import numpy as np
from mathutils import Vector

jobs_path, out_dir, prefix = sys.argv[sys.argv.index("--") + 1:][:3]
jobs = json.load(open(jobs_path, encoding="utf-8"))
os.makedirs(out_dir, exist_ok=True)
SIZE, COLS, PER_SHEET = 192, 8, 40

scene = bpy.context.scene
scene.render.engine = "BLENDER_WORKBENCH"
scene.render.resolution_x = scene.render.resolution_y = SIZE
scene.render.image_settings.file_format = "PNG"
scene.display.shading.light = "STUDIO"
scene.display.shading.color_type = "TEXTURE"
if scene.world is None:
    scene.world = bpy.data.worlds.new("World")
scene.world.color = (0.18, 0.18, 0.2)

camera_data = bpy.data.cameras.new("Cam")
camera_data.lens = 50
camera = bpy.data.objects.new("Cam", camera_data)
scene.collection.objects.link(camera)
scene.camera = camera

label_material = bpy.data.materials.new("Label")
label_material.diffuse_color = (1.0, 0.85, 0.1, 1.0)


def clear():
    for obj in list(bpy.data.objects):
        if obj is not camera:
            bpy.data.objects.remove(obj, do_unlink=True)
    for block in (bpy.data.meshes, bpy.data.curves, bpy.data.images):
        for item in list(block):
            if item.users == 0:
                block.remove(item)


def textured(path):
    material = bpy.data.materials.new("Thumb")
    material.use_nodes = True
    nodes = material.node_tree.nodes
    bsdf = nodes.get("Principled BSDF")
    if path and os.path.exists(path):
        tex = nodes.new("ShaderNodeTexImage")
        tex.image = bpy.data.images.load(path)
        material.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
        nodes.active = tex
    return material


def frame(objects):
    lo = Vector((1e9, 1e9, 1e9))
    hi = Vector((-1e9, -1e9, -1e9))
    for obj in objects:
        for corner in obj.bound_box:
            p = obj.matrix_world @ Vector(corner)
            lo = Vector(map(min, lo, p))
            hi = Vector(map(max, hi, p))
    centre = (lo + hi) / 2
    radius = max((hi - lo).length / 2, 0.01)
    direction = Vector((1.0, -1.3, 0.9)).normalized()
    distance = radius / math.tan(camera_data.angle / 2) * 1.15
    camera.location = centre + direction * distance
    camera.rotation_euler = (-direction).to_track_quat("-Z", "Y").to_euler()
    camera_data.clip_start = distance * 0.01
    camera_data.clip_end = distance * 10
    return hi - lo


def label(text):
    curve = bpy.data.curves.new("Label", "FONT")
    curve.body = text
    obj = bpy.data.objects.new("Label", curve)
    scene.collection.objects.link(obj)
    obj.data.materials.append(label_material)
    obj.parent = camera
    depth = camera_data.clip_start * 5
    half = depth * math.tan(camera_data.angle / 2)
    obj.location = (-half * 0.95, -half * 0.95, -depth)
    curve.size = half * 0.16
    return obj


tiles = []
for index, job in enumerate(jobs):
    clear()
    material = textured(job.get("basecolor"))
    objects = []
    if job.get("fbx"):
        try:
            bpy.ops.import_scene.fbx(filepath=job["fbx"])
            objects = [o for o in bpy.context.scene.objects if o.type == "MESH"]
        except Exception as error:
            print("IMPORT FAIL", job["id"], error)
    if not objects:
        bpy.ops.mesh.primitive_plane_add(size=1)
        objects = [bpy.context.active_object]
        camera.location = (0, 0, 1.6)
        camera.rotation_euler = (0, 0, 0)
        camera_data.clip_start, camera_data.clip_end = 0.01, 10
    else:
        frame(objects)
    for obj in objects:
        obj.data.materials.clear()
        obj.data.materials.append(material)
    size = job.get("dims") or ""
    label("%d %s %s" % (index, job["id"], size))
    path = os.path.join(out_dir, "%s_%03d.png" % (prefix, index))
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    tiles.append(path)

for sheet in range(0, len(tiles), PER_SHEET):
    chunk = tiles[sheet:sheet + PER_SHEET]
    rows = math.ceil(len(chunk) / COLS)
    canvas = np.zeros((rows * SIZE, COLS * SIZE, 4), dtype=np.float32)
    canvas[..., 3] = 1
    for i, path in enumerate(chunk):
        image = bpy.data.images.load(path)
        pixels = np.array(image.pixels[:], dtype=np.float32).reshape(SIZE, SIZE, 4)
        r, c = i // COLS, i % COLS
        y0 = (rows - 1 - r) * SIZE
        canvas[y0:y0 + SIZE, c * SIZE:(c + 1) * SIZE] = pixels
        bpy.data.images.remove(image)
    out = bpy.data.images.new("Sheet", COLS * SIZE, rows * SIZE, alpha=True)
    out.pixels = canvas.ravel()
    out.filepath_raw = os.path.join(out_dir, "%s_sheet_%d.png" % (prefix, sheet // PER_SHEET))
    out.file_format = "PNG"
    out.save()
    print("SHEET", out.filepath_raw)
