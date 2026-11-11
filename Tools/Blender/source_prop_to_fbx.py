# Blender (headless): Source MDL weapon or prop -> FBX + PNG textures for Unity.
# usage: blender -b --factory-startup --python source_prop_to_fbx.py -- <model.mdl> <out_dir> <name> [max texture px]
# source_model_to_fbx.py is for characters: it needs "body" and "head" meshes and splits hair off. This one keeps every
# mesh SourceIO builds, so it takes w_/v_ weapon models and props. Textures wider or taller than the given size are
# scaled down before they are written (0 or nothing: kept as they are); Unity's import rule caps Art/Models textures
# at 2048 anyway, so anything larger only grows the repository. Animations are not imported.
# Tools/Workshop/port_assets.py runs this for the fbx rows of port_manifest.csv.
import os
import re
import sys

import addon_utils
import bpy

args = sys.argv[sys.argv.index("--") + 1:]
mdl, out_dir, name = os.path.abspath(args[0]), os.path.abspath(args[1]), args[2]
max_px = int(args[3]) if len(args) > 3 else 0

addon_utils.enable("SourceIO-master", default_set=False, persistent=False)
for ob in list(bpy.data.objects):
    bpy.data.objects.remove(ob, do_unlink=True)
bpy.ops.sourceio.mdl(filepath=mdl, directory=os.path.dirname(mdl) + os.sep, files=[{"name": os.path.basename(mdl)}],
                     import_animations=False, write_qc=False, discover_resources=True)

for ob in list(bpy.data.objects):
    if ob.type == "EMPTY":  # Source attachments (muzzle, hand points): not needed in Unity
        bpy.data.objects.remove(ob, do_unlink=True)
meshes = [o for o in bpy.data.objects if o.type == "MESH"]
if not meshes:
    raise SystemExit("no mesh in " + mdl)
arm = next((o for o in bpy.data.objects if o.type == "ARMATURE"), None)
if arm is not None:
    arm.name = arm.data.name = name

tex_dir = os.path.join(out_dir, "Textures")
os.makedirs(tex_dir, exist_ok=True)
for img in list(bpy.data.images):
    if img.size[0] < 64 or img.type != "IMAGE":
        continue
    width, height = img.size
    if max_px and max(width, height) > max_px:
        scale = max_px / max(width, height)
        img.scale(max(1, round(width * scale)), max(1, round(height * scale)))
    # "katana.vtf" -> "katana.png", so ModelImportRules can match it to the material by name.
    file_name = re.sub(r'[\\/:*?"<>|]', "_", re.sub(r"\.(vtf|tga|png|dds)$", "", img.name, flags=re.I)) + ".png"
    path = os.path.join(tex_dir, file_name)
    img.file_format = "PNG"
    img.save(filepath=path)
    loaded = bpy.data.images.load(path)
    img.user_remap(loaded)
    print(f"TEX {file_name} {width}x{height} -> {loaded.size[0]}x{loaded.size[1]}")

layer = bpy.context.view_layer
for o in bpy.context.selected_objects:
    o.select_set(False)
chosen = meshes + ([arm] if arm is not None else [])
for o in chosen:
    o.hide_set(False)
    o.select_set(True)
layer.objects.active = arm if arm is not None else meshes[0]
bpy.ops.export_scene.fbx(filepath=os.path.join(out_dir, name + ".fbx"), use_selection=True,
                         object_types={"ARMATURE", "MESH"}, apply_scale_options="FBX_SCALE_ALL",
                         axis_forward="-Z", axis_up="Y", use_mesh_modifiers=False, add_leaf_bones=False,
                         bake_anim=False, path_mode="RELATIVE", embed_textures=False,
                         use_armature_deform_only=False, mesh_smooth_type="OFF")
for o in meshes:
    print("MESH", o.name, len(o.data.vertices), [s.material.name for s in o.material_slots if s.material])
if arm is not None:
    print("BONES", len(arm.data.bones))
print("EXPORTED", os.path.join(out_dir, name + ".fbx"))
