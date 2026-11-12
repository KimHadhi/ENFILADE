# Blender (headless): a workshop addon's Source weapon MDL -> static FBX parts + PNG textures for Unity.
# usage: blender -b --factory-startup --python addon_weapon_to_fbx.py -- <recipe.json> <addon_folder> <project_root>
#   recipe.json: Tools/Blender/Addons/<Name>.json (what to read, where the grip is, which way it points, how to split)
#   addon_folder: the addon unpacked with Tools/Workshop/workshop.py extract (outside the repo)
#
# Why a recipe per weapon: the L4D2 world models put their origin and axes wherever the modder left them (the Yamato
# hangs upside down from its guard, the KRAIT points along -Y), so each one says where its grip bone is, which way
# the muzzle or blade tip points and which way is up, and the script turns every weapon into the same frame:
# origin at the grip, tip along FBX +Z, top along +Y, true size (SourceIO's 0.01905 m per unit is the real size for
# weapons; only characters use the project's 0.025). Art/Models imports bake the axis conversion (ModelImportRules),
# which flips Z where Unity's own conversion flips X, so in Unity the tip lies along -Z: the model comes in a half turn
# about Y from the game's frame (measured on the Desperado's prefab, 2026-11-12), and whatever places it turns it
# back (Editor/Import/DesperadoSetup.cs).
# Textures come from the addon's VMT files, not from the importer's node trees: $basetexture -> <material>.png with
# $color2 baked in (masked by the base alpha when $blendtintbybasealpha is set), $bumpmap -> <material>_normal.png with
# green flipped (Source normal maps are DirectX-style, Unity's are OpenGL-style). Both are capped at max_texture.
# ModelImportRules then assigns them to the FBX's URP Lit materials by name.
import json
import os
import re
import sys

import addon_utils
import bpy
import mathutils
import numpy as np

args = sys.argv[sys.argv.index("--") + 1:]
recipe_path, addon_root, project_root = (os.path.abspath(a) for a in args[:3])
with open(recipe_path, encoding="utf-8") as f:
    recipe = json.load(f)
name = recipe["name"]
out_dir = os.path.join(project_root, recipe["out"].replace("/", os.sep))
tex_dir = os.path.join(out_dir, "Textures")
max_tex = int(recipe.get("max_texture", 2048))
os.makedirs(tex_dir, exist_ok=True)

files = {}
for folder, _, names in os.walk(addon_root):
    for n in names:
        files[os.path.relpath(os.path.join(folder, n), addon_root).replace("\\", "/").lower()] = os.path.join(folder, n)

addon_utils.enable("SourceIO-master", default_set=False, persistent=False)
for ob in list(bpy.data.objects):
    bpy.data.objects.remove(ob, do_unlink=True)
mdl = os.path.join(addon_root, recipe["mdl"].replace("/", os.sep))
bpy.ops.sourceio.mdl(filepath=mdl, directory=os.path.dirname(mdl) + os.sep, files=[{"name": os.path.basename(mdl)}],
                     import_animations=False, write_qc=False, discover_resources=True)
for ob in list(bpy.data.objects):
    if ob.type == "EMPTY":
        bpy.data.objects.remove(ob, do_unlink=True)
# A static prop (a thrown grenade's world model) comes in without an armature: its recipe gives points, not bones.
arm = next((o for o in bpy.data.objects if o.type == "ARMATURE"), None)


def bone_head(bone_name):
    return arm.matrix_world @ arm.data.bones[bone_name].head_local


def point(spec):
    return bone_head(spec["bone"]) if "bone" in spec else mathutils.Vector(spec["point"])


# Source frame -> game frame: tip to Blender -Y and up to +Z (FBX +Z and +Y; Unity -Z and +Y once imported, see the top).
tip = mathutils.Vector(recipe["tip"]).normalized()
up = mathutils.Vector(recipe["up"])
up = (up - tip * up.dot(tip)).normalized()
src = mathutils.Matrix((tip, up, tip.cross(up))).transposed()
dst_tip, dst_up = mathutils.Vector((0, -1, 0)), mathutils.Vector((0, 0, 1))
dst = mathutils.Matrix((dst_tip, dst_up, dst_tip.cross(dst_up))).transposed()
rotation = (dst @ src.transposed()).to_4x4()
origin = point(recipe["origin"])
scale = float(recipe.get("scale", 1.0))
to_game = mathutils.Matrix.Scale(scale, 4) @ rotation @ mathutils.Matrix.Translation(-origin)
# Marker points are read now: a marker may name a bone, and the armature goes once the meshes are baked.
marker_points = {marker: point(spec) for marker, spec in recipe.get("markers", {}).items()}

layer = bpy.context.view_layer
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
# keep_box [[min x, y, z], [max x, y, z]] in the model's own frame (as imported, before any of this): vertices outside go.
# A world model may carry a stray piece far from the weapon (the butterfly knife has a quad 1.6 m off along its blade),
# which would make the weapon's bounds, and so its culling and its icon framing, far too big.
if "keep_box" in recipe:
    box_lo, box_hi = (mathutils.Vector(c) for c in recipe["keep_box"])
    for ob in meshes:
        data = ob.data
        out = [not all(box_lo[i] <= (ob.matrix_world @ v.co)[i] <= box_hi[i] for i in range(3)) for v in data.vertices]
        if not any(out):
            continue
        for v in data.vertices:
            v.select = out[v.index]
        for e in data.edges:
            e.select = out[e.vertices[0]] or out[e.vertices[1]]
        for p in data.polygons:
            p.select = any(out[i] for i in p.vertices)
        for o2 in bpy.context.selected_objects:
            o2.select_set(False)
        layer.objects.active = ob
        ob.select_set(True)
        bpy.ops.object.mode_set(mode="EDIT")
        bpy.ops.mesh.select_mode(use_extend=False, use_expand=False, type="VERT")
        bpy.ops.mesh.delete(type="VERT")
        bpy.ops.object.mode_set(mode="OBJECT")
        print(f"TRIM {ob.name}: {sum(out)} vertices outside the keep box")
renames = recipe.get("meshes", {})
for ob in meshes:
    for mod in list(ob.modifiers):
        if mod.type == "ARMATURE":
            ob.modifiers.remove(mod)
    world = ob.matrix_world.copy()
    ob.parent = None
    ob.matrix_world = to_game @ world
    for key, new in renames.items():
        if ob.name.lower().startswith(key.lower()):
            ob.name = new
if arm is not None:
    bpy.data.objects.remove(arm, do_unlink=True)
for ob in bpy.context.selected_objects:
    ob.select_set(False)
for ob in meshes:
    ob.select_set(True)
layer.objects.active = meshes[0]
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

# Parts: faces of the listed materials (or of vertices whose strongest bone is listed) become their own object.
dominant = {}


def dominant_bones(ob):
    names = {g.index: g.name for g in ob.vertex_groups}
    return [names.get(max(v.groups, key=lambda g: g.weight).group) if v.groups else None for v in ob.data.vertices]


for part, rule in recipe.get("parts", {}).items():
    source = next((o for o in bpy.data.objects if o.type == "MESH" and o.name == rule.get("from", recipe.get("body", "Body"))), None)
    if source is None:
        source = next(o for o in bpy.data.objects if o.type == "MESH")
    mats = {m.lower() for m in rule.get("materials", [])}
    bones = set(rule.get("bones", []))
    owner = dominant_bones(source) if bones else None
    hits = []
    for poly in source.data.polygons:
        slot = source.material_slots[poly.material_index] if source.material_slots else None
        hit = bool(slot and slot.material and slot.material.name.lower() in mats)
        if not hit and owner is not None:
            hit = any(owner[v] in bones for v in poly.vertices)
        hits.append(hit)
    picked = sum(hits)
    if picked == 0:
        print(f"PART {part}: nothing matched")
        continue
    before = set(bpy.data.objects)
    for ob in bpy.context.selected_objects:
        ob.select_set(False)
    layer.objects.active = source
    source.select_set(True)
    # Selection flags set in object mode, faces with their own vertices and edges, are what edit mode loads. (bmesh
    # can't be used: SourceIO's meshes are a Mesh subclass that bmesh.from_edit_mesh refuses.)
    data = source.data
    picked_verts = set()
    for poly in data.polygons:
        poly.select = hits[poly.index]
        if poly.select:
            picked_verts.update(poly.vertices)
    for vert in data.vertices:
        vert.select = vert.index in picked_verts
    for edge in data.edges:
        edge.select = edge.vertices[0] in picked_verts and edge.vertices[1] in picked_verts
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_mode(use_extend=False, use_expand=False, type="FACE")
    bpy.ops.mesh.separate(type="SELECTED")
    bpy.ops.object.mode_set(mode="OBJECT")
    new = next(o for o in bpy.data.objects if o not in before)
    new.name = part
    print(f"PART {part}: {picked} faces")

for ob in [o for o in bpy.data.objects if o.type == "MESH"]:
    ob.vertex_groups.clear()
    for o2 in bpy.context.selected_objects:
        o2.select_set(False)
    layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.material_slot_remove_unused()
    for drop in recipe.get("drop_meshes", []):
        if ob.name == drop:
            bpy.data.objects.remove(ob, do_unlink=True)
            break

# Markers: empties where the next tasks need a point (muzzle, blade tip, light), in the same game frame.
markers = []
for marker, at in marker_points.items():
    empty = bpy.data.objects.new(marker, None)
    empty.empty_display_size = 0.02
    bpy.context.scene.collection.objects.link(empty)
    empty.location = to_game @ at
    markers.append(empty)


# Textures from the VMTs.
def vmt_of(material_name):
    want = material_name.lower() + ".vmt"
    return next((p for k, p in files.items() if k.endswith("/" + want) or k == want), None)


def vmt_values(path):
    text = open(path, encoding="utf-8", errors="replace").read()
    text = re.sub(r"//[^\n]*", "", text)
    values = {}
    # A key may carry a space inside its quotes ("$color2 " in the Fatebringer's VMT): the value is still the next token.
    for key, value in re.findall(r'"?(\$[A-Za-z0-9_]+)\s*"?\s+("[^"]*"|\S+)', text):
        values[key.lower()] = value.strip('"')
    return values


def vtf_of(texture):
    key = "materials/" + texture.replace("\\", "/").lower().strip("/") + ".vtf"
    return files.get(key)


def decode(vtf):
    before = set(bpy.data.images)
    bpy.ops.sourceio.vtf(directory=os.path.dirname(vtf) + os.sep, files=[{"name": os.path.basename(vtf)}])
    image = next((i for i in bpy.data.images if i not in before), None)
    if image is None:
        return None
    w, h = image.size
    if max(w, h) > max_tex:
        k = max_tex / max(w, h)
        image.scale(max(1, int(w * k)), max(1, int(h * k)))
    return image


def save(image, pixels, target):
    image.pixels.foreach_set(pixels)
    image.filepath_raw = target
    image.file_format = "PNG"
    image.save()


def color_of(text):
    numbers = [float(x) for x in re.findall(r"[-\d.]+", text or "")]
    return numbers[:3] if len(numbers) >= 3 else None


report = []
for material in bpy.data.materials:
    if material.users == 0:
        continue
    vmt = vmt_of(material.name)
    if vmt is None:
        report.append(f"TEX {material.name}: no VMT")
        continue
    values = vmt_values(vmt)
    base = vtf_of(values.get("$basetexture", ""))
    if base:
        image = decode(base)
        if image:
            px = np.empty(image.size[0] * image.size[1] * 4, np.float32)
            image.pixels.foreach_get(px)
            tint = color_of(values.get("$color2")) or color_of(values.get("$color"))
            if tint:
                share = px[3::4] if values.get("$blendtintbybasealpha") == "1" else np.ones_like(px[3::4])
                for c in range(3):
                    px[c::4] *= 1.0 + (tint[c] - 1.0) * share
            # The alpha stays when it is cut-out or see-through, or when it is the reflection mask
            # ($basemapalphaenvmapmask): URP Lit reads smoothness from it (Smoothness Source: Albedo Alpha).
            if values.get("$alphatest") != "1" and values.get("$translucent") != "1" and values.get("$basemapalphaenvmapmask") != "1":
                px[3::4] = 1.0
            save(image, px, os.path.join(tex_dir, material.name + ".png"))
            report.append(f"TEX {material.name}: base {os.path.basename(base)} {image.size[0]}x{image.size[1]} tint={tint}")
            # $selfillummask: what glows, as the base colour where the mask is white -> <material>_emission.png (URP's
            # emission map; Source lights it the same way, base times mask).
            glow = vtf_of(values.get("$selfillummask", ""))
            mask = decode(glow) if glow else None
            if mask:
                if tuple(mask.size) != tuple(image.size):
                    mask.scale(image.size[0], image.size[1])
                mk = np.empty(image.size[0] * image.size[1] * 4, np.float32)
                mask.pixels.foreach_get(mk)
                lit = px.copy()
                for c in range(3):
                    lit[c::4] *= mk[0::4]
                lit[3::4] = 1.0
                save(mask, lit, os.path.join(tex_dir, material.name + "_emission.png"))
                report.append(f"TEX {material.name}: emission {os.path.basename(glow)} lit share {float(np.mean(mk[0::4] > .1)):.3f}")
    bump = vtf_of(values.get("$bumpmap", ""))
    # A bump map named "normal…" is skipped as a shared flat one, unless the recipe says its own is named so.
    if bump and (recipe.get("own_normal_names") or not os.path.basename(bump).lower().startswith("normal")):
        image = decode(bump)
        if image and image.size[0] >= 64:
            px = np.empty(image.size[0] * image.size[1] * 4, np.float32)
            image.pixels.foreach_get(px)
            px[1::4] = 1.0 - px[1::4]
            px[3::4] = 1.0
            save(image, px, os.path.join(tex_dir, material.name + "_normal.png"))
            report.append(f"TEX {material.name}: normal {os.path.basename(bump)} {image.size[0]}x{image.size[1]}")

for ob in bpy.context.selected_objects:
    ob.select_set(False)
exported = [o for o in bpy.data.objects if o.type in ("MESH", "EMPTY")]
for ob in exported:
    ob.select_set(True)
layer.objects.active = exported[0]
target = os.path.join(out_dir, name + ".fbx")
bpy.ops.export_scene.fbx(filepath=target, use_selection=True, object_types={"MESH", "EMPTY"},
                         apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", use_mesh_modifiers=True,
                         add_leaf_bones=False, bake_anim=False, mesh_smooth_type="OFF", use_tspace=False,
                         path_mode="STRIP", embed_textures=False)
for ob in bpy.data.objects:
    if ob.type == "MESH":
        mn = [min((v.co[i] for v in ob.data.vertices), default=0) for i in range(3)]
        mx = [max((v.co[i] for v in ob.data.vertices), default=0) for i in range(3)]
        tris = sum(len(p.vertices) - 2 for p in ob.data.polygons)
        print(f"OUT {ob.name}: tris={tris} mats={[s.material.name for s in ob.material_slots if s.material]} "
              f"min=({mn[0]:.3f},{mn[1]:.3f},{mn[2]:.3f}) max=({mx[0]:.3f},{mx[1]:.3f},{mx[2]:.3f})")
    elif ob.type == "EMPTY":
        print(f"OUT marker {ob.name} at {tuple(round(v, 3) for v in ob.location)}")
for line in report:
    print(line)
print("WROTE", target)
