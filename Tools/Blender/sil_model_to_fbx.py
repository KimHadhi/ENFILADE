"""Export the sil_ess VRChat avatar .blend to Unity without changing the source file.

blender -b --factory-startup --disable-autoexec sil_ess_1_2.blend --python
    sil_model_to_fbx.py -- output.fbx

Keeps the meshes, shape keys and the author's materials ("Shade" is the silhouette shell, "Metal" the Poiyomi
skeleton), and changes only what the game needs:
- First person: Face, the inner skeleton's skull and the eye mesh (Body) become one HeadAccessories, a name the
  PlayerBody profile hides from its own camera. Not "Head": that is this rig's head bone, and a humanoid bone's name
  has to be the only one of its kind in the model.
- The skirt had no bones; every skirt vertex hung from Hips. A ring of chains (Skirt_<chain>_<joint>) now runs from the
  waist to the hem and carries the skirt, so Magica Cloth can move it (Resources/Tables/Cloth.csv). Its weights blend
  the two chains either side of a vertex and the two joints above and below it, and fade into the old ones at the
  waist.
The .fbx.meta beside the export maps the two materials to the package's own and leaves the humanoid mapping to Unity.
"""
import math
import os
import sys

import bpy
from mathutils import Vector

CHAINS = 12
JOINTS = 4
# Below this share of the skirt's height the old weights fade into the chains, so the waist doesn't tear.
WAIST_BLEND = .2
# A skirt vertex hangs only from Hips, and sits no higher than it.
SKIRT_HIPS_WEIGHT = .9


def split_faces(obj, indices, name):
    """A copy of obj holding only those faces, with their weights; obj keeps the rest. Neither mesh has shape keys."""
    source = obj.data
    group_names = [group.name for group in obj.vertex_groups]

    def subset(polygons, label):
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
        return part

    selected = [p for p in source.polygons if p.index in indices]
    remaining = [p for p in source.polygons if p.index not in indices]
    if not selected or not remaining:
        raise RuntimeError(f"{obj.name}: nothing to split as {name}")
    part = subset(selected, name)
    kept = subset(remaining, obj.name + "_kept")
    original_name = obj.name
    bpy.data.objects.remove(obj, do_unlink=True)
    kept.name = original_name
    return part


def weights(obj):
    names = {g.index: g.name for g in obj.vertex_groups}
    return [{names[g.group]: g.weight for g in v.groups} for v in obj.data.vertices]


def join(target, part):
    with bpy.context.temp_override(active_object=target, object=target, selected_objects=[target, part],
                                   selected_editable_objects=[target, part]):
        bpy.ops.object.join()


def show_everything():
    def walk(layer):
        layer.exclude = False
        layer.hide_viewport = False
        for child in layer.children:
            walk(child)
    walk(bpy.context.view_layer.layer_collection)
    for collection in bpy.data.collections:
        collection.hide_viewport = collection.hide_render = False
    for obj in bpy.context.scene.objects:
        obj.hide_viewport = obj.hide_render = False
        obj.hide_set(False)


def add_skirt(arm, dress):
    """A ring of bone chains over the skirt's own shape, and the skirt's weights moved onto them."""
    world = arm.matrix_world
    hips = world @ arm.data.bones["Hips"].head_local
    old = weights(dress)
    points = [dress.matrix_world @ v.co for v in dress.data.vertices]
    skirt = [i for i, w in enumerate(old) if w.get("Hips", 0) >= SKIRT_HIPS_WEIGHT and points[i].z <= hips.z]
    if not skirt:
        raise RuntimeError("No skirt vertices hang from Hips")
    top = max(points[i].z for i in skirt)
    hem = min(points[i].z for i in skirt)
    span = top - hem
    step = 2 * math.pi / CHAINS

    def angle(p):
        return math.atan2(p.x - hips.x, -(p.y - hips.y)) % (2 * math.pi)

    def share(p):
        return min(1.0, max(0.0, (top - p.z) / span))

    # Each joint sits at the median distance of the skirt round it: the middle of its layers, not the outer frill.
    joints = []
    for k in range(CHAINS):
        chain = []
        for j in range(JOINTS):
            s = j / (JOINTS - 1)
            z = top - s * span
            near = [(points[i] - hips).to_2d().length for i in skirt
                    if abs(points[i].z - z) <= span / (JOINTS - 1) * .5
                    and abs((angle(points[i]) - k * step + math.pi) % (2 * math.pi) - math.pi) <= step * .5]
            if not near:
                near = [(points[i] - hips).to_2d().length for i in skirt if abs(points[i].z - z) <= span / (JOINTS - 1) * .5]
            near.sort()
            r = near[len(near) // 2]
            a = k * step
            chain.append(Vector((hips.x + math.sin(a) * r, hips.y - math.cos(a) * r, z)))
        joints.append(chain)

    local = world.inverted()
    with bpy.context.temp_override(active_object=arm, object=arm, selected_objects=[arm], selected_editable_objects=[arm]):
        bpy.ops.object.mode_set(mode="EDIT")
        bones = arm.data.edit_bones
        for k, chain in enumerate(joints):
            parent = bones["Hips"]
            for j, head in enumerate(chain):
                bone = bones.new(f"Skirt_{k:02d}_{j}")
                bone.head = local @ head
                after = chain[j + 1] if j + 1 < len(chain) else head + (head - chain[j - 1]) * .5
                bone.tail = local @ after
                bone.parent = parent
                bone.use_connect = False
                bone.use_deform = True
                parent = bone
        bpy.ops.object.mode_set(mode="OBJECT")

    groups = {f"Skirt_{k:02d}_{j}": dress.vertex_groups.new(name=f"Skirt_{k:02d}_{j}") for k in range(CHAINS) for j in range(JOINTS)}
    for i in skirt:
        p = points[i]
        s = share(p)
        a = angle(p) / step
        k0 = int(math.floor(a)) % CHAINS
        f = a - math.floor(a)
        k1 = (k0 + 1) % CHAINS
        level = s * (JOINTS - 1)
        j0 = min(int(math.floor(level)), JOINTS - 2)
        t = level - j0
        chain = {(k0, j0): (1 - f) * (1 - t), (k0, j0 + 1): (1 - f) * t, (k1, j0): f * (1 - t), (k1, j0 + 1): f * t}
        blend = min(1.0, s / WAIST_BLEND)
        blend = blend * blend * (3 - 2 * blend)
        mixed = {name: w * (1 - blend) for name, w in old[i].items()}
        for (k, j), w in chain.items():
            name = f"Skirt_{k:02d}_{j}"
            mixed[name] = mixed.get(name, 0) + w * blend
        for name in old[i]:
            dress.vertex_groups[name].remove([i])
        total = sum(mixed.values()) or 1.0
        for name, w in mixed.items():
            if w > 1e-4:
                dress.vertex_groups[name].add([i], w / total, "REPLACE")
    print(f"SKIRT {len(skirt)} vertices, waist {top:.3f}, hem {hem:.3f}, {CHAINS} chains x {JOINTS} joints")


args = sys.argv[sys.argv.index("--") + 1:]
fbx_path = os.path.abspath(args[0])
show_everything()
armatures = [o for o in bpy.context.scene.objects if o.type == "ARMATURE"]
if len(armatures) != 1:
    raise RuntimeError("Expected exactly one armature")
arm = armatures[0]
for needed in ("Body", "Bone", "Dress", "Face", "Hair"):
    if needed not in bpy.data.objects:
        raise RuntimeError(f"Missing mesh {needed}; is this sil_ess_1_2.blend?")
arm.name = "SilRig"

# The skull is the inner skeleton's part that hangs from Head; it sits round the first-person camera.
bone = bpy.data.objects["Bone"]
bone_weights = weights(bone)
skull = split_faces(bone, {p.index for p in bone.data.polygons
                           if all(bone_weights[v].get("Head", 0) > .5 for v in p.vertices)}, "Skull")
# The eyes hold the facial shape keys, so they take the others in: joined vertices enter every key at rest.
eyes = bpy.data.objects["Body"]
join(eyes, bpy.data.objects["Face"])
join(eyes, skull)
eyes.name = eyes.data.name = "HeadAccessories"

add_skirt(arm, bpy.data.objects["Dress"])

export = [arm] + [o for o in bpy.context.scene.objects if o.type == "MESH" and o.parent == arm]
for obj in bpy.context.scene.objects:
    obj.select_set(obj in export)
bpy.context.view_layer.objects.active = arm
os.makedirs(os.path.dirname(fbx_path), exist_ok=True)
bpy.ops.export_scene.fbx(filepath=fbx_path, use_selection=True, object_types={"ARMATURE", "MESH"}, apply_scale_options="FBX_SCALE_ALL",
                         axis_forward="-Z", axis_up="Y", use_mesh_modifiers=False, add_leaf_bones=False, bake_anim=False,
                         path_mode="RELATIVE", embed_textures=False, use_armature_deform_only=False, mesh_smooth_type="OFF")
print("EXPORTED", fbx_path, sorted(o.name for o in export))
