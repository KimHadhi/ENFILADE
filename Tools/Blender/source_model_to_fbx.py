# Blender (headless): Source MDL character -> FBX + PNG textures for Unity.
# usage: blender -b --factory-startup --python source_model_to_fbx.py -- <model.mdl> <out_dir> <name>
# Splits hair-like materials into a "Hair" mesh so the game can hide head parts in first person.
import os
import sys

import addon_utils
import bpy
import numpy as np

args = sys.argv[sys.argv.index("--") + 1:]
mdl, out_dir, name = os.path.abspath(args[0]), os.path.abspath(args[1]), args[2]
HAIR_MATERIALS = {"hair", "flower"}

addon_utils.enable("SourceIO-master", default_set=False, persistent=False)
for ob in list(bpy.data.objects):
    bpy.data.objects.remove(ob, do_unlink=True)
bpy.ops.sourceio.mdl(filepath=mdl, directory=os.path.dirname(mdl) + os.sep, files=[{"name": os.path.basename(mdl)}],
                     import_animations=False, write_qc=False, discover_resources=True)

for ob in list(bpy.data.objects):
    if ob.type == "EMPTY":  # Source attachments (eyes, weapon points): not needed in Unity
        bpy.data.objects.remove(ob, do_unlink=True)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
arm.name = arm.data.name = name
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
body = next(o for o in meshes if o.name.startswith("body"))
head = next(o for o in meshes if o.name.startswith("head"))
body.name, head.name = "Body", "Head"

layer = bpy.context.view_layer
for o in bpy.context.selected_objects:
    o.select_set(False)
layer.objects.active = body
body.select_set(True)
bpy.ops.object.mode_set(mode="EDIT")
bpy.ops.mesh.select_all(action="DESELECT")
for i, slot in enumerate(body.material_slots):
    if slot.material and slot.material.name in HAIR_MATERIALS:
        body.active_material_index = i
        bpy.ops.object.material_slot_select()
bpy.ops.mesh.separate(type="SELECTED")
bpy.ops.object.mode_set(mode="OBJECT")
hair = next(o for o in bpy.data.objects if o.type == "MESH" and o.name.startswith("Body."))
hair.name = "Hair"
for o in (body, hair):
    layer.objects.active = o
    bpy.ops.object.material_slot_remove_unused()

# The 16x16 flat normal map adds nothing; keep only colour textures.
for mat in bpy.data.materials:
    if not mat.use_nodes:
        continue
    for node in list(mat.node_tree.nodes):
        if node.type == "NORMAL_MAP" or (node.type == "TEX_IMAGE" and node.image and node.image.name == "normal"):
            mat.node_tree.nodes.remove(node)

tex_dir = os.path.join(out_dir, "Textures")
os.makedirs(tex_dir, exist_ok=True)
for img in list(bpy.data.images):
    if img.size[0] < 64 or img.type != "IMAGE":
        continue
    path = os.path.join(tex_dir, img.name + ".png")
    px = np.empty(img.size[0] * img.size[1] * 4, np.float32)
    img.pixels.foreach_get(px)
    alpha = px[3::4]
    print(f"TEX {img.name} alpha_min={alpha.min():.3f} transparent={(alpha < .5).mean():.3%}")
    img.file_format = "PNG"
    img.save(filepath=path)
    loaded = bpy.data.images.load(path)
    img.user_remap(loaded)

for o in bpy.context.selected_objects:
    o.select_set(False)
for o in (arm, body, head, hair):
    o.select_set(True)
layer.objects.active = arm
bpy.ops.export_scene.fbx(filepath=os.path.join(out_dir, name + ".fbx"), use_selection=True,
                         object_types={"ARMATURE", "MESH"}, apply_scale_options="FBX_SCALE_ALL",
                         axis_forward="-Z", axis_up="Y", use_mesh_modifiers=False, add_leaf_bones=False,
                         bake_anim=False, path_mode="RELATIVE", embed_textures=False,
                         use_armature_deform_only=False, mesh_smooth_type="OFF")
for o in (body, head, hair):
    print("MESH", o.name, len(o.data.vertices), [s.material.name for s in o.material_slots])
print("EXPORTED", os.path.join(out_dir, name + ".fbx"))
