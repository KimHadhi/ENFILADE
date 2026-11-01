"""Export an imported KKBP FK .blend to Unity without changing the source file.

blender -b --factory-startup --disable-autoexec character.blend --python
    kkbp_model_to_fbx.py -- output_directory CharacterName

Keeps facial shape keys and weighted accessory bones, removes unweighted branches,
and separates the face and head accessories for the first-person head setting.
An optional CharacterName.coverage.json beside this script removes body faces
that are permanently covered by that outfit, preserving UVs and all shape keys.
The JSON manifest supplies the Unity humanoid mapping and material textures.
After assigning that mapping in Unity, use Configure > Pose > Enforce T-Pose
and Apply. The raw FK bind pose faces -Z; using it directly as the humanoid
reference pose reverses facing and makes lowered-arm animations lift the arms.
Keep the resulting .fbx.meta with the exported asset.
"""
import json
import os
import re
import shutil
import sys

import bpy
import numpy as np


def split_faces(obj, indices, name):
    if not indices:
        return None
    source = obj.data
    group_names = [group.name for group in obj.vertex_groups]

    def subset(polygons, label):
        # Explicitly copy shape-key coordinates: edit-mode separation can lose
        # key layers on KKBP meshes created by a different Blender version.
        vertex_ids = sorted({v for p in polygons for v in p.vertices})
        remap = {v: i for i, v in enumerate(vertex_ids)}
        material_ids = sorted({p.material_index for p in polygons})
        materials = {m: i for i, m in enumerate(material_ids)}
        mesh = bpy.data.meshes.new(label)
        mesh.from_pydata([source.vertices[v].co for v in vertex_ids], [], [[remap[v] for v in p.vertices] for p in polygons])
        for m in material_ids:
            mesh.materials.append(source.materials[m])
        for new, old in zip(mesh.polygons, polygons):
            new.material_index = materials[old.material_index]
            new.use_smooth = old.use_smooth
        loops = [i for p in polygons for i in p.loop_indices]
        for layer in source.uv_layers:
            target = mesh.uv_layers.new(name=layer.name)
            for new, old in zip(target.data, loops):
                new.uv = layer.data[old].uv
        mesh.normals_split_custom_set([source.corner_normals[i].vector for i in loops])
        part = obj.copy()
        part.data = mesh
        part.name = label
        for group in group_names:
            part.vertex_groups.new(name=group)
        for collection in obj.users_collection:
            collection.objects.link(part)
        for i, v in enumerate(vertex_ids):
            for group in source.vertices[v].groups:
                part.vertex_groups[group.group].add([i], group.weight, "REPLACE")
        if source.shape_keys:
            keys = source.shape_keys.key_blocks
            positions = np.empty(len(source.vertices) * 3, dtype=np.float32)
            for key in keys:
                new = part.shape_key_add(name=key.name, from_mix=False)
                key.data.foreach_get("co", positions)
                new.data.foreach_set("co", positions.reshape(-1, 3)[vertex_ids].ravel())
                new.slider_min, new.slider_max = key.slider_min, key.slider_max
                new.value = key.value
            for key in keys:
                part.data.shape_keys.key_blocks[key.name].relative_key = part.data.shape_keys.key_blocks[key.relative_key.name]
        return part

    selected = [p for p in source.polygons if p.index in indices]
    remaining = [p for p in source.polygons if p.index not in indices]
    if not selected or not remaining:
        return None
    part = subset(selected, name)
    kept = subset(remaining, obj.name + "_kept")
    original_name = obj.name
    bpy.data.objects.remove(obj, do_unlink=True)
    kept.name = original_name
    return part


def split_materials(obj, indices, name):
    return split_faces(obj, {p.index for p in obj.data.polygons if p.material_index in indices}, name)


def identifier(name):
    return re.sub(r"[^A-Za-z0-9_]+", "_", name.removeprefix("KK ")).strip("_")


args = sys.argv[sys.argv.index("--") + 1:]
out_dir, name = os.path.abspath(args[0]), args[1]
os.makedirs(os.path.join(out_dir, "Textures"), exist_ok=True)
armatures = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
if len(armatures) != 1:
    raise RuntimeError("Expected exactly one imported FK armature")
arm = armatures[0]
if "Hips" not in arm.data.bones or "Left arm" not in arm.data.bones:
    raise RuntimeError("Import the model using KKBP's Use FK Armature option first")
if any(b.constraints for b in arm.pose.bones) or (arm.animation_data and arm.animation_data.drivers):
    raise RuntimeError("This exporter expects an FK rig without pose constraints or armature drivers")
arm.name = arm.data.name = name + "Rig"
body = next(o for o in bpy.context.scene.objects if o.type == "MESH" and o.name.startswith("Body "))
body.name = "Body"
split_materials(body, {i for i, m in enumerate(body.data.materials) if m and m.get("id") != "cf_m_body"}, "Head")

for o in list(bpy.context.scene.objects):
    if o.type != "MESH":
        continue
    if o.name.startswith("Hair Outfit"):
        o.name = "Hair"
    elif o.name.startswith("Gag Eyes"):
        o.name = "GagEyes"
    elif o.name.startswith("Tears "):
        o.name = "Tears"
    elif o.name.startswith("Tongue (rigged)"):
        o.name = "RiggedTongue"
    elif o.name.startswith("Outfit "):
        o.name = "Outfit"
        # Head-worn pieces can be mixed into the outfit mesh by KKBP.
        head_bones = {"Head"} | {b.name for b in arm.data.bones if any(p.name == "Head" for p in b.parent_recursive)}
        head_materials = set()
        for i in range(len(o.data.materials)):
            vertices = {v for p in o.data.polygons if p.material_index == i for v in p.vertices}
            used = {o.vertex_groups[g.group].name for v in vertices for g in o.data.vertices[v].groups if g.weight > 1e-6 and o.vertex_groups[g.group].name in arm.data.bones}
            if used and used <= head_bones:
                head_materials.add(i)
        split_materials(o, head_materials, "HeadAccessories")

# Outfit-specific coverage belongs to the export, so future re-exports cannot restore skin beneath opaque clothes.
# Bounds are in the source armature's coordinates. Never modify the purchased .blend or exposed body parts.
coverage = []
recipe_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), name + ".coverage.json")
if os.path.isfile(recipe_path):
    with open(recipe_path, encoding="utf-8") as file:
        recipe = json.load(file)
    for mask in recipe.get("bodyMasks", []):
        obj = bpy.data.objects[mask["mesh"]]
        to_rig = arm.matrix_world.inverted() @ obj.matrix_world
        points = [to_rig @ v.co for v in obj.data.vertices]
        minimum, maximum = mask["min"], mask["max"]
        indices = {p.index for p in obj.data.polygons
                   if all(all(minimum[axis] <= points[v][axis] <= maximum[axis] for axis in range(3)) for v in p.vertices)}
        if not indices:
            raise RuntimeError("Coverage mask matched no faces: " + mask["name"])
        removed = split_faces(obj, indices, "CoveredBody")
        if removed is None:
            raise RuntimeError("Coverage mask would remove an entire body mesh")
        bpy.data.objects.remove(removed, do_unlink=True)
        coverage.append({"name": mask["name"], "removedFaces": len(indices), "min": minimum, "max": maximum})

human = {"Hips": "Hips", "Spine": "Spine", "Chest": "Chest", "UpperChest": "Upper Chest", "Neck": "Neck", "Head": "Head"}
parents = {}
for side, suffix in (("Left", "L"), ("Right", "R")):
    for unity, source in (("Shoulder", "shoulder"), ("UpperArm", "arm"), ("LowerArm", "elbow"), ("Hand", "wrist"), ("UpperLeg", "leg"), ("LowerLeg", "knee"), ("Foot", "ankle"), ("Toes", "toe"), ("Eye", "Eye")):
        human[side + unity] = side + " " + source
    parents[side + " shoulder"] = "Upper Chest"
    parents[side + " leg"] = "Hips"
    parents[side + " ankle"] = side + " knee"
    # Map the actual eye pivot, so its weighted iris children follow Unity's eye bone.
    eye_pivot = "cf_J_Eye_rz_" + suffix
    if eye_pivot in arm.data.bones:
        human[side + "Eye"] = eye_pivot
        parents[eye_pivot] = "Head"
    for finger, prefix, start in (("Thumb", "Thumb", 0), ("Index", "IndexFinger", 1), ("Middle", "MiddleFinger", 1), ("Ring", "RingFinger", 1), ("Little", "LittleFinger", 1)):
        for i, segment in enumerate(("Proximal", "Intermediate", "Distal")):
            source = prefix + str(start + i) + "_" + suffix
            # Unity's finger names contain spaces (HumanTrait.BoneName), unlike
            # the HumanBodyBones enum names used by runtime code.
            human[side + " " + finger + " " + segment] = source
            if i == 0:
                parents[source] = side + " wrist"
missing = [b for b in human.values() if b not in arm.data.bones]
if missing:
    raise RuntimeError("Missing expected humanoid bones: " + ", ".join(missing))

bpy.ops.object.select_all(action="DESELECT")
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
for child, parent in parents.items():
    bone = arm.data.edit_bones[child]
    bone.use_connect = False
    bone.parent = arm.data.edit_bones[parent]
bpy.ops.object.mode_set(mode="OBJECT")

meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
export_collection = bpy.data.collections.new("UnityExport")
bpy.context.scene.collection.children.link(export_collection)
for obj in [arm] + meshes:
    if obj.name not in bpy.context.view_layer.objects:
        export_collection.objects.link(obj)
bpy.context.view_layer.update()
needed = set(human.values())
for obj in meshes:
    for vertex in obj.data.vertices:
        for group in vertex.groups:
            bone = obj.vertex_groups[group.group].name
            if group.weight > 1e-6 and bone in arm.data.bones:
                needed.add(bone)
for bone in list(needed):
    needed.update(p.name for p in arm.data.bones[bone].parent_recursive)
before_bones = len(arm.data.bones)
bpy.context.view_layer.objects.active = arm
bpy.ops.object.mode_set(mode="EDIT")
for bone in list(arm.data.edit_bones):
    if bone.name not in needed:
        arm.data.edit_bones.remove(bone)
bpy.ops.object.mode_set(mode="OBJECT")
for obj in meshes:
    for group in list(obj.vertex_groups):
        if group.name not in arm.data.bones:
            obj.vertex_groups.remove(group)

# Unity's humanoid mapper searches all transforms, including mesh objects.
# Keep the renderer named Head, and give its skeletal joint a distinct name.
arm.data.bones["Head"].name = "HeadBone"
human["Head"] = "HeadBone"

manifest = {"name": name, "source": os.path.basename(bpy.data.filepath), "humanoid": [{"human": h, "bone": b} for h,b in human.items()], "originalBones": before_bones, "exportedBones": len(arm.data.bones), "materials": [], "disabledMeshes": ["GagEyes", "Tears", "RiggedTongue"], "firstPersonShadowOnly": ["Head", "Hair", "HeadAccessories"], "meshes": []}
if coverage:
    manifest["bodyCoverage"] = coverage
used_materials = {m for obj in meshes for m in obj.data.materials if m}
for mat in sorted(used_materials, key=lambda m:m.name):
    old_name = mat.name
    new_name = identifier(old_name)
    textures = {}
    for key, suffix in (("_light.png", "_BaseColor"), ("_dark.png", "_Shadow")):
        node = mat.node_tree.nodes.get(key) if mat.use_nodes else None
        if node is None or node.image is None:
            continue
        img = node.image
        dest = os.path.join(out_dir, "Textures", new_name + suffix + ".png")
        source = bpy.path.abspath(img.filepath)
        if os.path.isfile(source):
            shutil.copy2(source, dest)
        else:
            img.file_format = "PNG"
            img.save(filepath=dest)
        textures[key] = dest
    if not textures:
        # Optional gag/tear shaders depend on Blender drivers; keep their geometry
        # but hide those effects in the Unity prefab until an expression system drives them.
        manifest["materials"].append({"name":new_name,"baseColor":"","shadow":"","cutout":False})
        mat.name = new_name
        continue
    img = mat.node_tree.nodes["_light.png"].image
    pixels = np.empty(img.size[0]*img.size[1]*4, dtype=np.float32)
    img.pixels.foreach_get(pixels)
    cutout = bool(np.any(pixels[3::4] < .99))
    del pixels
    manifest["materials"].append({"name":new_name,"baseColor":"Textures/" + os.path.basename(textures["_light.png"]),"shadow":"Textures/" + os.path.basename(textures["_dark.png"]) if "_dark.png" in textures else "","cutout":cutout})
    # FBX understands a Principled shader with a direct image connection.
    mat.name = new_name
    mat.use_nodes = True
    mat.node_tree.nodes.clear()
    output = mat.node_tree.nodes.new("ShaderNodeOutputMaterial")
    shader = mat.node_tree.nodes.new("ShaderNodeBsdfPrincipled")
    texture = mat.node_tree.nodes.new("ShaderNodeTexImage")
    texture.image = bpy.data.images.load(textures["_light.png"], check_existing=True)
    mat.node_tree.links.new(texture.outputs["Color"], shader.inputs["Base Color"])
    mat.node_tree.links.new(texture.outputs["Alpha"], shader.inputs["Alpha"])
    mat.node_tree.links.new(shader.outputs["BSDF"], output.inputs["Surface"])
    shader.inputs["Roughness"].default_value = 1

bpy.ops.object.select_all(action="DESELECT")
for obj in [arm] + meshes:
    obj.hide_set(False)
    obj.select_set(True)
    if obj.type == "MESH":
        manifest["meshes"].append({"name":obj.name,"vertices":len(obj.data.vertices),"shapeKeys":len(obj.data.shape_keys.key_blocks)-1 if obj.data.shape_keys else 0})
bpy.context.view_layer.objects.active = arm
fbx_path = os.path.join(out_dir, name + ".fbx")
bpy.ops.export_scene.fbx(filepath=fbx_path, use_selection=True, object_types={"ARMATURE","MESH"}, apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", use_mesh_modifiers=False, add_leaf_bones=False, bake_anim=False, path_mode="RELATIVE", embed_textures=False, use_armature_deform_only=False, mesh_smooth_type="OFF")
with open(os.path.join(out_dir, name + ".kkbp.json"), "w", encoding="utf-8") as file:
    json.dump(manifest, file, ensure_ascii=False, indent=2)
print(f"EXPORTED {fbx_path}: {before_bones} -> {len(arm.data.bones)} bones, {len(meshes)} meshes", flush=True)
