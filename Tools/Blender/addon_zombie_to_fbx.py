# Blender (headless): L4D2 common infected MDLs (ValveBiped skeleton) -> humanoid-ready FBX + PNG textures for ZombieLooks.
# usage: blender -b --factory-startup --python addon_zombie_to_fbx.py -- <out_dir> <prefix> <model.mdl> [more.mdl ...]
#
# What ZombieBody.Dress needs from a model, and how each is met here:
# - standing, feet on the ground, facing Unity +Z: the import lies wherever the MDL's reference pose put it (the Low Poly
#   Common Infected lie along +Y), so the body is turned by its own bones: up from the feet to the head, forward from the
#   heels to the toes, then dropped onto z = 0 with the pelvis over the origin. Forward goes to Blender -Y, which the FBX
#   export (forward -Z, up Y) turns into Unity +Z.
# - metres at the project's Source scale: SourceIO reads 1 unit as 0.01905 m (Source's map scale); characters here use
#   0.025 m (Docs/AddonPortNotes.md, unit), so a 72-unit infected stands 1.8 m tall.
# - a humanoid avatar: the ValveBiped bones are kept as they are; Unity maps them itself (the .meta asks for a humanoid
#   made from the model, as Hiro's does).
# - its name says its sex ("_Male_", "_Female_") for ZombieVoice.
# Textures come from each material's VMT ($basetexture) as <material>.png; the pack's are 128 px.
import os
import re
import sys

import addon_utils
import bpy
import mathutils
import numpy as np

SCALE = 0.025 / 0.01905
args = sys.argv[sys.argv.index("--") + 1:]
out_dir, prefix, models = os.path.abspath(args[0]), args[1], [os.path.abspath(a) for a in args[2:]]
tex_dir = os.path.join(out_dir, "Textures")
os.makedirs(tex_dir, exist_ok=True)
addon_utils.enable("SourceIO-master", default_set=False, persistent=False)


def addon_root(mdl):
    p = os.path.dirname(mdl)
    while p and os.path.basename(p).lower() != "models":
        p = os.path.dirname(p)
    return os.path.dirname(p)


def files_of(root):
    found = {}
    for folder, _, names in os.walk(root):
        for n in names:
            found[os.path.relpath(os.path.join(folder, n), root).replace("\\", "/").lower()] = os.path.join(folder, n)
    return found


def vmt_values(path):
    text = re.sub(r"//[^\n]*", "", open(path, encoding="utf-8", errors="replace").read())
    return {k.lower(): v.strip('"') for k, v in re.findall(r'"?(\$[A-Za-z0-9_]+)"?\s+("[^"]*"|\S+)', text)}


# Tokens that make a body female: the word itself, or the Zombie Panic! Source women in the GMod Zombie SNPCs pack.
FEMALE = {"female", "woman", "jessica", "lea", "vanessa", "gal"}
REQUIRED = ("Pelvis", "Head1", "L_Foot", "R_Foot", "L_Toe0", "R_Toe0")
MAX_TEXTURE = 1024


def name_for(stem):
    tokens = {s for s in re.split(r"[_\d]+", stem.lower()) if s}
    sex = "Female" if tokens & FEMALE else "Male"
    rest = re.sub(r"^common_", "", stem, flags=re.I)
    rest = re.sub(r"(?i)female|male", "", rest)
    rest = re.sub(r"_+", "_", rest).strip("_") or "base"
    return f"{prefix}_{sex}_{rest}"


def clear():
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.armatures, bpy.data.materials, bpy.data.images, bpy.data.actions):
        for block in list(coll):
            if block.users == 0:
                coll.remove(block)


saved_textures = set()
for mdl in models:
    clear()
    root = addon_root(mdl)
    files = files_of(root)
    name = name_for(os.path.splitext(os.path.basename(mdl))[0])
    if os.path.exists(os.path.join(out_dir, name + ".fbx")):
        print("SKIP exists", name)
        continue
    try:
        bpy.ops.sourceio.mdl(filepath=mdl, directory=os.path.dirname(mdl) + os.sep, files=[{"name": os.path.basename(mdl)}],
                             import_animations=False, write_qc=False, discover_resources=True)
    except RuntimeError as error:
        # A few MDLs in a pack only borrow the game's own mesh data (no VTX/VVD of their own): nothing to convert.
        print("FAIL", os.path.basename(mdl), str(error).splitlines()[0][:120])
        continue
    for ob in list(bpy.data.objects):
        if ob.type == "EMPTY":
            bpy.data.objects.remove(ob, do_unlink=True)
    arm = next((o for o in bpy.data.objects if o.type == "ARMATURE"), None)
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    missing = [b for b in REQUIRED if arm is None or "ValveBiped.Bip01_" + b not in arm.data.bones]
    if missing or not meshes:
        # Not a ValveBiped body (a hulk or a crawler rigged its own way): Unity couldn't make it a humanoid either.
        print("FAIL", os.path.basename(mdl), "missing bones", missing)
        continue

    def head(bone):
        return arm.matrix_world @ arm.data.bones["ValveBiped.Bip01_" + bone].head_local

    feet = (head("L_Foot") + head("R_Foot")) / 2
    toes = (head("L_Toe0") + head("R_Toe0")) / 2
    up = (head("Head1") - feet).normalized()
    forward = toes - feet
    forward = (forward - up * forward.dot(up)).normalized()
    src = mathutils.Matrix((forward, up, forward.cross(up))).transposed()
    dst_f, dst_u = mathutils.Vector((0, -1, 0)), mathutils.Vector((0, 0, 1))
    dst = mathutils.Matrix((dst_f, dst_u, dst_f.cross(dst_u))).transposed()
    turn = (dst @ src.transposed()).to_4x4()
    arm.matrix_world = mathutils.Matrix.Scale(SCALE, 4) @ turn @ arm.matrix_world
    bpy.context.view_layer.update()

    layer = bpy.context.view_layer
    for ob in bpy.context.selected_objects:
        ob.select_set(False)
    for ob in [arm] + meshes:
        ob.select_set(True)
    layer.objects.active = arm
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    bpy.context.view_layer.update()

    # Feet on the ground, pelvis over the origin.
    lowest = min((m.matrix_world @ v.co).z for m in meshes for v in m.data.vertices)
    pelvis = head("Pelvis")
    arm.location = (-pelvis.x, -pelvis.y, -lowest)
    bpy.context.view_layer.update()
    bpy.ops.object.transform_apply(location=True, rotation=False, scale=False)
    bpy.context.view_layer.update()

    arm.name = arm.data.name = name
    for i, m in enumerate(meshes):
        m.name = "Body" if i == 0 else f"Body{i}"

    for material in {s.material for m in meshes for s in m.material_slots if s.material}:
        key = material.name.lower()
        if key in saved_textures:
            continue
        vmt = next((p for k, p in files.items() if k.endswith("/" + key + ".vmt")), None)
        if vmt is None:
            print("TEX no VMT", material.name)
            continue
        base = vmt_values(vmt).get("$basetexture", "")
        vtf = files.get("materials/" + base.replace("\\", "/").lower().strip("/") + ".vtf")
        if vtf is None:
            print("TEX no VTF", material.name, base)
            continue
        before = set(bpy.data.images)
        bpy.ops.sourceio.vtf(directory=os.path.dirname(vtf) + os.sep, files=[{"name": os.path.basename(vtf)}])
        image = next((i for i in bpy.data.images if i not in before), None)
        if image is None:
            continue
        w, h = image.size
        if max(w, h) > MAX_TEXTURE:
            image.scale(max(1, w * MAX_TEXTURE // max(w, h)), max(1, h * MAX_TEXTURE // max(w, h)))
        px = np.empty(image.size[0] * image.size[1] * 4, np.float32)
        image.pixels.foreach_get(px)
        px[3::4] = 1.0
        image.pixels.foreach_set(px)
        image.filepath_raw = os.path.join(tex_dir, material.name + ".png")
        image.file_format = "PNG"
        image.save()
        saved_textures.add(key)
        print("TEX", material.name, image.size[:])

    heights = [(m.matrix_world @ v.co) for m in meshes for v in m.data.vertices]
    top = max(v.z for v in heights)
    toe_y = head("L_Toe0").y
    for ob in bpy.context.selected_objects:
        ob.select_set(False)
    for ob in [arm] + meshes:
        ob.select_set(True)
    layer.objects.active = arm
    target = os.path.join(out_dir, name + ".fbx")
    bpy.ops.export_scene.fbx(filepath=target, use_selection=True, object_types={"ARMATURE", "MESH"},
                             apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", use_mesh_modifiers=False,
                             add_leaf_bones=False, bake_anim=False, path_mode="STRIP", embed_textures=False)
    print(f"OUT {name} height={top:.2f} toe_y={toe_y:.2f} tris={sum(len(p.vertices) - 2 for m in meshes for p in m.data.polygons)} "
          f"bones={len(arm.data.bones)} mats={sorted({s.material.name for m in meshes for s in m.material_slots if s.material})}")
