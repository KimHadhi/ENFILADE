"""Renders a built scene from its layout (render_layout.py) in Blender, to look at a map's dressing without Unity.
usage: blender -b --factory-startup --disable-autoexec --python render_scene.py -- <layout.json> <views.json> <out dir>
       [mock.py]
views.json: {"yawFix": 180, "views": [{"name", "eye": [x,y,z], "look": [x,y,z], "lens": 24} or {"name", "plan": [x,z],
"size": m, "height": y}]} in Unity coordinates. Unity (x, y, z) is Blender (x, z, y). yawFix 180 turns Blender's import
of a model to Unity's (Unity mirrors X on import, Blender doesn't). mock.py, if given, runs once the scene stands, with
this script's names (bpy, scene, u2b, plain_material, yaw_fix, LOD): a builder change drawn before it is built."""
import json
import math
import os
import re
import sys

import bpy
from mathutils import Quaternion, Vector

args = sys.argv[sys.argv.index("--") + 1:]
layout_path, views_path, out = args[:3]
# Blender resolves a relative output path against the drive root (C:\hub_A, 11-14), not the working folder.
out = os.path.abspath(out)
# An optional mock-up script, run with this script's names once the scene stands (models a builder will add).
mock_path = args[3] if len(args) > 3 else None
layout = json.load(open(layout_path, encoding="utf-8"))
config = json.load(open(views_path, encoding="utf-8"))
os.makedirs(out, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
scene = bpy.context.scene
yaw_fix = config.get("yawFix", 0.0)


def u2b(p):
    return Vector((p[0], p[2], p[1]))


def q2b(q):
    x, y, z, w = q
    return Quaternion((w, -x, -z, -y))


IMAGES = {}


def image(path):
    if not path or not os.path.exists(path):
        return None
    if path not in IMAGES:
        try:
            IMAGES[path] = bpy.data.images.load(path, check_existing=True)
        except RuntimeError:
            IMAGES[path] = None
    return IMAGES[path]


MATS = {}


def surface_material(info):
    """The map's surface shader as Blender nodes: world-mapped box projection, its top set on faces looking up."""
    key = "surface:" + info["name"]
    if key in MATS:
        return MATS[key]
    m = bpy.data.materials.new(info["name"])
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = .85
    geo = nt.nodes.new("ShaderNodeNewGeometry")

    def projected(path, tile, tint):
        mapping = nt.nodes.new("ShaderNodeMapping")
        mapping.inputs["Scale"].default_value = (1 / max(.1, tile),) * 3
        nt.links.new(geo.outputs["Position"], mapping.inputs["Vector"])
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.projection = "BOX"
        tex.projection_blend = .2
        tex.image = image(path)
        nt.links.new(mapping.outputs["Vector"], tex.inputs["Vector"])
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MULTIPLY"
        mix.inputs["Factor"].default_value = 1.0
        nt.links.new(tex.outputs["Color"], mix.inputs["A"])
        mix.inputs["B"].default_value = (*tint, 1)
        return mix.outputs["Result"] if tex.image else None

    side = projected(info["tex"], info["tile"], info["tint"])
    top = projected(info["top"], info["topTile"], info["topTint"]) if info.get("top") else side
    if side is None:
        bsdf.inputs["Base Color"].default_value = (*info["tint"], 1)
    elif top is side:
        nt.links.new(side, bsdf.inputs["Base Color"])
    else:
        sep = nt.nodes.new("ShaderNodeSeparateXYZ")
        nt.links.new(geo.outputs["Normal"], sep.inputs["Vector"])
        ramp = nt.nodes.new("ShaderNodeMath")
        ramp.operation = "GREATER_THAN"
        ramp.inputs[1].default_value = .7
        nt.links.new(sep.outputs["Z"], ramp.inputs[0])
        choose = nt.nodes.new("ShaderNodeMix")
        choose.data_type = "RGBA"
        nt.links.new(ramp.outputs[0], choose.inputs["Factor"])
        nt.links.new(side, choose.inputs["A"])
        nt.links.new(top, choose.inputs["B"])
        nt.links.new(choose.outputs["Result"], bsdf.inputs["Base Color"])
    MATS[key] = m
    return m


def plain_material(info, name):
    key = "plain:" + (info["name"] if info else name)
    if key in MATS:
        return MATS[key]
    m = bpy.data.materials.new(key)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes["Principled BSDF"]
    bsdf.inputs["Roughness"].default_value = .7
    tint = info["tint"] if info else [.6, .6, .6]
    img = image(info["tex"]) if info else None
    if img:
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = img
        mix = nt.nodes.new("ShaderNodeMix")
        mix.data_type = "RGBA"
        mix.blend_type = "MULTIPLY"
        mix.inputs["Factor"].default_value = 1.0
        nt.links.new(tex.outputs["Color"], mix.inputs["A"])
        mix.inputs["B"].default_value = (*tint[:3], 1)
        nt.links.new(mix.outputs["Result"], bsdf.inputs["Base Color"])
        if info.get("transparent"):
            nt.links.new(tex.outputs["Alpha"], bsdf.inputs["Alpha"])
    else:
        bsdf.inputs["Base Color"].default_value = (*tint[:3], 1)
    MATS[key] = m
    return m


# The boxes.
for box in layout["boxes"]:
    if not box.get("enabled", True):
        continue
    bpy.ops.mesh.primitive_cube_add(size=1)
    o = bpy.context.active_object
    o.name = box["name"][:60]
    o.location = u2b(box["pos"])
    o.rotation_mode = "QUATERNION"
    o.rotation_quaternion = q2b(box["rot"])
    s = box["size"]
    o.scale = (s[0], s[2], s[1])
    info = box.get("material")
    mat = surface_material(info) if info and info.get("surface") else plain_material(info, "box")
    o.data.materials.append(mat)

# The models: each file once into a hidden collection, each copy an instance of it.
LIBRARY = {}
LOD = re.compile(r"_LOD[1-9]", re.I)


def model(path, materials):
    if path in LIBRARY:
        return LIBRARY[path]
    before = set(bpy.data.objects)
    try:
        bpy.ops.import_scene.fbx(filepath=path, use_anim=False)
    except Exception as error:  # noqa: BLE001 - a model that won't import is left out
        print("[render] could not import", path, error)
        LIBRARY[path] = None
        return None
    new = [o for o in bpy.data.objects if o not in before]
    coll = bpy.data.collections.new(os.path.basename(path))
    for o in new:
        for c in list(o.users_collection):
            c.objects.unlink(o)
        if LOD.search(o.name) or o.type not in ("MESH", "EMPTY"):
            bpy.data.objects.remove(o, do_unlink=True)
            continue
        coll.objects.link(o)
        if o.type == "MESH":
            for slot in o.material_slots:
                base = re.sub(r"\.\d{3}$", "", slot.material.name) if slot.material else ""
                info = materials.get(base)
                slot.material = plain_material(info, base) if info else plain_material(None, base or "model")
    LIBRARY[path] = coll
    return coll


for copy in layout["copies"]:
    coll = model(copy["source"], copy.get("materials", {}))
    if coll is None:
        continue
    holder = bpy.data.objects.new("copy", None)
    holder.instance_type = "COLLECTION"
    holder.instance_collection = coll
    holder.location = u2b(copy["pos"])
    holder.rotation_euler = (0, 0, math.radians(-copy["yaw"] + yaw_fix))
    s = copy["scale"]
    holder.scale = (abs(s[0]), abs(s[2]), abs(s[1]))
    scene.collection.objects.link(holder)

# Light: the map's lamps and sun, a dim sky.
for lamp in layout["lights"]:
    kind = {0: "SPOT", 1: "SUN", 2: "POINT"}.get(lamp["type"], "POINT")
    data = bpy.data.lights.new("lamp", kind)
    data.color = lamp["color"][:3]
    if kind == "SUN":
        data.energy = 3.0 * lamp["intensity"]
    else:
        data.energy = 60.0 * lamp["intensity"] * (lamp["range"] / 10.0) ** 2 / (4 if kind == "SPOT" else 1)
        data.shadow_soft_size = .3
    o = bpy.data.objects.new("lamp", data)
    o.location = u2b(lamp["pos"])
    o.rotation_mode = "QUATERNION"
    # A Unity light shines along its +Z; a Blender one along its -Z.
    o.rotation_quaternion = q2b(lamp["rot"]) @ Quaternion((0, 1, 0, 0))
    scene.collection.objects.link(o)
if not any(l["type"] == 1 for l in layout["lights"]):
    sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN"))
    sun.data.energy = 3
    sun.rotation_euler = (math.radians(40), 0, math.radians(30))
    scene.collection.objects.link(sun)
world = bpy.data.worlds.new("w")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs[0].default_value = (.55, .62, .72, 1)
world.node_tree.nodes["Background"].inputs[1].default_value = config.get("sky", .5)
scene.world = world

if mock_path:
    exec(compile(open(mock_path, encoding="utf-8").read(), mock_path, "exec"), globals())

scene.render.engine = config.get("engine", "BLENDER_EEVEE_NEXT")
try:
    scene.eevee.taa_render_samples = 32
    scene.eevee.use_shadows = True
except AttributeError:
    pass
scene.cycles.samples = 24
scene.render.resolution_x, scene.render.resolution_y = config.get("size", [1280, 720])
cam_data = bpy.data.cameras.new("cam")
cam = bpy.data.objects.new("cam", cam_data)
scene.collection.objects.link(cam)
scene.camera = cam
for view in config["views"]:
    if "plan" in view:
        cam_data.type = "ORTHO"
        cam_data.ortho_scale = view["size"]
        cam.location = Vector((view["plan"][0], view["plan"][1], view.get("height", 40)))
        cam.rotation_euler = (0, 0, 0)
        cam_data.clip_end = 500
    else:
        cam_data.type = "PERSP"
        cam_data.lens = view.get("lens", 22)
        cam_data.clip_start = .05
        cam_data.clip_end = 1000
        eye, look = u2b(view["eye"]), u2b(view["look"])
        cam.location = eye
        cam.rotation_euler = (look - eye).to_track_quat("-Z", "Y").to_euler()
    scene.render.filepath = os.path.join(out, view["name"] + ".png")
    bpy.ops.render.render(write_still=True)
    print("[render] wrote", view["name"])
print("[render] done")
