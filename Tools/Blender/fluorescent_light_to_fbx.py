# Writes one ceiling fluorescent light as an FBX for Unity, from Poly Haven's "Mounted Fluorescent Lights" (CC0).
# The download holds seven variants side by side (a-g); Docs/Guides/LightingGuide.md uses variant a only.
# Usage: blender -b --factory-startup --disable-autoexec mounted_fluorescent_lights_2k.blend --python fluorescent_light_to_fbx.py -- <out.fbx> [variant]
# The object's origin is its mounting face and the light hangs 3.5 cm below it, so it is placed at the ceiling's underside.
import sys
import bpy

args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
if not args:
    raise SystemExit("usage: ... --python fluorescent_light_to_fbx.py -- <out.fbx> [variant]")
fbx_path = args[0]
variant = args[1] if len(args) > 1 else "a"
keep = bpy.data.objects["mounted_fluorescent_lights_" + variant]

for obj in list(bpy.data.objects):
    if obj is not keep:
        bpy.data.objects.remove(obj, do_unlink=True)
keep.location = (0.0, 0.0, 0.0)
bpy.ops.object.select_all(action="DESELECT")
keep.select_set(True)
bpy.context.view_layer.objects.active = keep

bpy.ops.export_scene.fbx(filepath=fbx_path, use_selection=True, object_types={"MESH"}, apply_scale_options="FBX_SCALE_ALL",
                         axis_forward="-Z", axis_up="Y", use_mesh_modifiers=True, add_leaf_bones=False, bake_anim=False,
                         path_mode="STRIP", embed_textures=False, mesh_smooth_type="OFF")
print("EXPORTED", fbx_path, keep.name, tuple(round(d, 4) for d in keep.dimensions))
