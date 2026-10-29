# Blender (headless): Source MDL animations -> one skeleton-only FBX (one take per animation) + clip list CSV.
# usage: blender -b --factory-startup --python source_anims_to_fbx.py -- <anims.mdl> <out.fbx> <rig name>
#        <comma names|*> [measure names|-] [yaw offset] [onto model.mdl] [clip name suffix]
# A name "alias=sequence@side:forward" bakes one cell of a blended sequence under its own name (crouchleft=crouchfwd@-1:0).
# The suffix ("_tp") renames the clips so a second set with the same clip names can sit beside the first one; the
# player body finds clips by name and takes the first match. Blended sequences give one clip (their forward or
# neutral cell), Bip01_Spine4's turn is folded into Spine2 because Unity's humanoid has no slot for it, and finger
# turns are carried into the model's own finger axes.
# SourceIO parses the MDL; actions are written here directly (SourceIO 5.5.2's own animation import stops on
# some clips). A bone's pose basis is rest_rel⁻¹ · local, where local is the Source parent-space transform.
#
# With "onto", the clips are baked onto that model's own skeleton the way the Source engine plays them: each bone
# keeps the model's length and takes the clip's rotation, the root's travel is scaled by pelvis height, and the
# clips are turned from Source's +X forward to the model's rest facing. Unity's humanoid retarget then maps a
# skeleton onto an identical one, so nothing twists.
import csv
import math
import os
import sys

import addon_utils
import bpy
from mathutils import Matrix, Quaternion, Vector

args = sys.argv[sys.argv.index("--") + 1:]
mdl_path, out_fbx, rig = os.path.abspath(args[0]), os.path.abspath(args[1]), args[2]
wanted = None if args[3] == "*" else [n.strip() for n in args[3].split(",") if n.strip()]
measure = [n.strip() for n in args[4].split(",")] if len(args) > 4 and args[4] != "-" else []
onto_path = os.path.abspath(args[6]) if len(args) > 6 and args[6] != "-" else None
suffix = args[7] if len(args) > 7 else ""
# Unity's Original root orientation keeps the clip basis. Source clips face +X, which needs a +90 degree import
# offset to face Unity +Z; baked onto a model they face its toes (Blender -Y, Unity -Z), which needs 180.
yaw_offset = float(args[5]) if len(args) > 5 else (180.0 if onto_path else 90.0)

addon_utils.enable("SourceIO-master", default_set=False, persistent=False)
from SourceIO.blender_bindings.models.mdl44.import_mdl import create_armature  # noqa: E402
from SourceIO.library.models.mdl.v49.mdl_file import MdlV49  # noqa: E402
from SourceIO.library.utils import FileBuffer  # noqa: E402
from SourceIO.library.utils.math_utilities import SOURCE1_HAMMER_UNIT_TO_METERS as SCALE  # noqa: E402

for ob in list(bpy.data.objects):
    bpy.data.objects.remove(ob, do_unlink=True)
scene = bpy.context.scene
scene.render.fps, scene.render.fps_base = 30, 1.0


def load(path):
    with FileBuffer(path) as f:
        return MdlV49.from_buffer(f)


def quat(r):
    return Quaternion((r[3], r[0], r[1], r[2]))


mdl = load(mdl_path)
skeleton = load(onto_path) if onto_path else mdl
arm = create_armature(skeleton, SCALE)
if arm.name not in scene.collection.objects:
    scene.collection.objects.link(arm)
arm.name = arm.data.name = rig


def straighten_arms(armature):
    """Rest pose -> T-pose: rotate each arm chain about the shoulder so upper arm to hand points straight out.
    Bases are computed from this rest pose afterwards, so the animated poses do not change; only Unity's
    humanoid reference pose matches the T-posed characters."""
    bpy.context.view_layer.objects.active = armature
    bpy.ops.object.mode_set(mode="EDIT")
    bones = armature.data.edit_bones
    for side, sign in (("L", 1.0), ("R", -1.0)):
        upper, hand = bones.get(f"ValveBiped.Bip01_{side}_UpperArm"), bones.get(f"ValveBiped.Bip01_{side}_Hand")
        if upper is None or hand is None:
            continue
        pivot = upper.head.copy()
        turn = (hand.head - pivot).normalized().rotation_difference(Vector((sign, 0.0, 0.0))).to_matrix().to_4x4()
        about = Matrix.Translation(pivot) @ turn @ Matrix.Translation(-pivot)
        chain = [upper] + list(upper.children_recursive)
        saved = {b.name: b.matrix.copy() for b in chain}
        for b in chain:
            b.matrix = about @ saved[b.name]
        print("TPOSE", side, "hand", tuple(round(v, 3) for v in bones[hand.name].head))
    bpy.ops.object.mode_set(mode="OBJECT")


if not onto_path:
    straighten_arms(arm)

names = [b.name[:63] for b in skeleton.bones]
rest = [arm.data.bones[n].matrix_local.copy() for n in names]
rest_rel_inv = [(rest[i] if b.parent_id == -1 else rest[b.parent_id].inverted() @ rest[i]).inverted()
                for i, b in enumerate(skeleton.bones)]
source_index = {b.name[:63]: i for i, b in enumerate(mdl.bones)}

# Onto a model: Source clips face +X; turn them to the model's rest facing (its toes) and scale root travel.
facing = Matrix.Identity(4)
root_scale = 1.0
if onto_path:
    by_name = {b.name: b for b in arm.data.bones}
    foot, toe = by_name.get("ValveBiped.Bip01_L_Foot"), by_name.get("ValveBiped.Bip01_L_Toe0")
    front = (toe.head_local - foot.head_local) if foot and toe else Vector((0.0, -1.0, 0.0))
    front.z = 0.0
    # Toes splay a little; the model faces the nearest axis.
    turn = round(math.atan2(front.y, front.x) / (math.pi / 2)) * (math.pi / 2)
    facing = Matrix.Rotation(turn, 4, "Z")
    pelvis_target = next(b for b in skeleton.bones if b.parent_id == -1)
    pelvis_source = mdl.bones[source_index.get(pelvis_target.name[:63], 0)]
    root_scale = pelvis_target.position[2] / pelvis_source.position[2] if pelvis_source.position[2] else 1.0
    print("ONTO", os.path.basename(onto_path), "facing_deg", round(math.degrees(turn), 1),
          "root_scale", round(root_scale, 3))

# A model's finger bones can point their local axes differently from the clip skeleton's (Hiro's are up to 167° off),
# so a finger rotation copied as-is bends sideways, and even the clip's rest pose comes out twisted. Fingers instead
# take the clip's turn away from its own rest pose, carried into the model finger's axes and applied to the model's
# rest pose: local = model rest · K⁻¹ · clip rest⁻¹ · clip local · K, with K = clip rest⁻¹ · model rest in model space
# (both reference poses face the same way). A finger at its rest pose in the clip lands exactly on the model's rest.
# Every other bone shares the Source axis convention and is copied as-is.
correction = {}
if onto_path:
    def rest_world(model):
        out = []
        for bone in model.bones:
            local = quat(bone.quat)
            out.append(local if bone.parent_id == -1 else out[bone.parent_id] @ local)
        return out

    source_rest, model_rest = rest_world(mdl), rest_world(skeleton)
    for bi, name in enumerate(names):
        si = source_index.get(name)
        if si is not None and "Finger" in name:
            k = source_rest[si].inverted() @ model_rest[bi]
            correction[bi] = (quat(skeleton.bones[bi].quat) @ k.inverted() @ quat(mdl.bones[si].quat).inverted(), k)

for pb in arm.pose.bones:
    pb.rotation_mode = "QUATERNION"
if not arm.animation_data:
    arm.animation_data_create()

order = wanted if wanted else [d.name.lstrip("@") for d in mdl.anim_descs]
index = {}
for n, desc in enumerate(mdl.anim_descs):
    index.setdefault(desc.name.lstrip("@"), n)

# A blended sequence lists one animation per grid cell under its own name, so "first animation of that name" is an
# arbitrary corner. Beatrun's third-person set blends movement direction (pose parameters 7 = sideways, 6 = forward,
# keys -1/0/1, idle in the centre) and turns the whole body toward it: take the cell for the clip's own direction.
# Other grids (hang yaw/pitch, swing) take the cell where every parameter is 0.
MOVE_PARAMS = (7, 6)
DIRECTIONS = {"runbwd": (0, -1), "walkback": (0, -1), "crouchbwd": (0, -1),
              "dodgejumpleft": (-1, 0), "dodgejumpright": (1, 0)}
# A name "crouchleft=crouchfwd@-1:0" bakes that cell of a blended sequence as its own clip (sideways, forward keys).
aliases = {}
for n in list(order):
    if "=" in n:
        alias, spec = n.split("=", 1)
        source, cell = spec.split("@", 1)
        aliases[alias] = (source, tuple(float(v) for v in cell.split(":")))
        order[order.index(n)] = alias


def grid_dims(count, keys, start, end):
    for n0 in range(1, len(keys)):
        n1 = len(keys) - n0
        if n0 * n1 == count and all(start[0] - 1e-3 <= k <= end[0] + 1e-3 for k in keys[:n0]) \
                and all(start[1] - 1e-3 <= k <= end[1] + 1e-3 for k in keys[n0:]):
            return n0, n1
    return None


def nearest(values, target):
    return min(range(len(values)), key=lambda i: abs(values[i] - target))


sequences = {}
for seq in getattr(mdl, "sequences", []):
    cells = list(seq.anim_desc_indices)
    keys = [float(k) for k in seq.pose_keys]
    dims = grid_dims(len(cells), keys, tuple(seq.param_start), tuple(seq.param_end)) if len(cells) > 1 else (1, 1)
    if dims is None:
        continue
    n0, n1 = dims
    k0, k1 = (keys[:n0], keys[n0:]) if len(cells) > 1 else ([0.0], [0.0])
    want = DIRECTIONS.get(seq.name, (0, 1)) if tuple(seq.param_offset) == MOVE_PARAMS and n0 == n1 == 3 else (0, 0)
    sequences[seq.name] = (cells[nearest(k0, want[0]) * n1 + nearest(k1, want[1])], bool(int(seq.flags) & 1))
    for alias, (source, cell) in aliases.items():
        if source == seq.name:
            sequences[alias] = (cells[nearest(k0, cell[0]) * n1 + nearest(k1, cell[1])], bool(int(seq.flags) & 1))

# Unity's humanoid has three spine slots (Spine, Chest, UpperChest = Bip01_Spine, Spine1, Spine2). Bip01_Spine4 sits
# between UpperChest and the neck, so its rotation would be dropped on import. Fold it into Spine2 instead: Spine2
# takes Spine4's turn and Spine4 stays at rest, so the neck, head and arms keep their orientation.
fold_into = next((i for i, n in enumerate(names) if n.endswith("Bip01_Spine2")), None)
fold_from = next((i for i, n in enumerate(names) if n.endswith("Bip01_Spine4")), None)
if fold_into is None or fold_from is None:
    fold_into = fold_from = None
else:
    fold_rest = rest_rel_inv[fold_from].inverted().to_quaternion().inverted()


def local_transform(bi, pos_all, rot_all, fr):
    """Source parent-space transform of skeleton bone bi at frame fr, or None to keep its rest pose."""
    bone = skeleton.bones[bi]
    si = source_index.get(names[bi]) if onto_path else bi
    if si is None:
        return None
    p, r = pos_all[fr, si], rot_all[fr, si]
    if not onto_path:
        return Matrix.Translation(Vector(p) * SCALE) @ quat(r).to_matrix().to_4x4()
    if bone.parent_id == -1:
        return facing @ Matrix.Translation(Vector(p) * SCALE * root_scale) @ quat(r).to_matrix().to_4x4()
    rot = quat(r)
    if bi in correction:
        before, after = correction[bi]
        rot = before @ rot @ after
    return Matrix.Translation(Vector(bone.position) * SCALE) @ rot.to_matrix().to_4x4()


def folded(local, pos_all, rot_all, fr):
    """Spine2's transform carrying Spine4's turn as well (see fold_into above)."""
    upper = local_transform(fold_from, pos_all, rot_all, fr)
    if upper is None:
        return local
    turn = local.to_quaternion() @ upper.to_quaternion() @ fold_rest
    return Matrix.Translation(local.to_translation()) @ turn.to_matrix().to_4x4()


rows = []
for clip in order:
    n, seq_loop = sequences.get(clip, (index.get(clip), None))
    data = mdl.animations[n] if n is not None and n < len(mdl.animations) else None
    if data is None:
        print("SKIP", clip, "missing")
        continue
    desc = mdl.anim_descs[n]
    frames = int(data.shape[0])
    if frames < 2:
        print("SKIP", clip, "single frame")  # Unity imports a one-frame take as an empty clip
        continue
    step = 30.0 / (desc.fps or 30.0)
    act = bpy.data.actions.new(clip + suffix)
    act.use_fake_user = True
    pos_all, rot_all = data["pos"], data["rot"]
    for bi, bone in enumerate(names):
        if bi == fold_from:
            continue
        locs, quats, prev = [], [], None
        for fr in range(frames):
            local = local_transform(bi, pos_all, rot_all, fr)
            if local is None:
                break
            if bi == fold_into:
                local = folded(local, pos_all, rot_all, fr)
            loc, q, _ = (rest_rel_inv[bi] @ local).decompose()
            if prev is not None and prev.dot(q) < 0:
                q.negate()
            prev = q
            locs.append(loc)
            quats.append(q)
        if not locs:
            continue
        base = f'pose.bones["{bone}"]'
        for prop, values, size in (("location", locs, 3), ("rotation_quaternion", quats, 4)):
            for i in range(size):
                fc = act.fcurves.new(f"{base}.{prop}", index=i, action_group=bone)
                fc.keyframe_points.add(frames)
                co = []
                for fr in range(frames):
                    co += (fr * step, values[fr][i])
                fc.keyframe_points.foreach_set("co", co)
                for kp in fc.keyframe_points:
                    kp.interpolation = "LINEAR"
                fc.update()
    loop = seq_loop if seq_loop is not None else bool(int(desc.flags) & 1)
    rows.append((clip + suffix, int(loop), 0, yaw_offset, frames, desc.fps))
    print("ACTION", clip, frames, desc.fps, "loop" if loop else "once")

# Unity builds the avatar of a skeleton-only FBX from the first take's first frame (there is no bind pose), so the
# first take is the rest pose. Actions export in name order; "_rest" sorts before every clip.
rest_act = bpy.data.actions.new("_rest")
rest_act.use_fake_user = True
for bone in names:
    base = f'pose.bones["{bone}"]'
    for prop, values in (("location", (0.0, 0.0, 0.0)), ("rotation_quaternion", (1.0, 0.0, 0.0, 0.0))):
        for i, v in enumerate(values):
            fc = rest_act.fcurves.new(f"{base}.{prop}", index=i, action_group=bone)
            fc.keyframe_points.add(2)
            fc.keyframe_points.foreach_set("co", (0.0, v, 1.0, v))
            fc.update()

head = next((pb for pb in arm.pose.bones if pb.name.endswith("Bip01_Head1")), None)
for clip in measure:
    act = bpy.data.actions.get(clip + suffix)
    if act is None or head is None:
        continue
    arm.animation_data.action = act
    lo, hi = act.frame_range
    for label, fr in (("start", lo), ("mid", (lo + hi) / 2), ("end", hi)):
        scene.frame_set(int(round(fr)))
        bpy.context.view_layer.update()
        h = arm.matrix_world @ head.head
        feet = [arm.matrix_world @ pb.head for pb in arm.pose.bones if pb.name.endswith(("_Foot", "_Toe0"))]
        pelvis = arm.matrix_world @ next(pb for pb in arm.pose.bones if pb.name.endswith("Pelvis")).head
        print(f"MEASURE {clip} {label} head=({h.x:.3f},{h.y:.3f},{h.z:.3f}) pelvis=({pelvis.x:.3f},{pelvis.y:.3f},{pelvis.z:.3f})"
              f" feet=" + " ".join(f"({v.x:.2f},{v.y:.2f},{v.z:.2f})" for v in feet))
arm.animation_data.action = None
# A skeleton-only FBX has no bind pose, so Unity builds the avatar from the pose left on the bones: put back rest.
for pb in arm.pose.bones:
    pb.location, pb.rotation_quaternion, pb.scale = (0.0, 0.0, 0.0), (1.0, 0.0, 0.0, 0.0), (1.0, 1.0, 1.0)
scene.frame_set(0)

for o in bpy.context.selected_objects:
    o.select_set(False)
arm.select_set(True)
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.fbx(filepath=out_fbx, use_selection=True, object_types={"ARMATURE"},
                         apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y", add_leaf_bones=False,
                         use_armature_deform_only=False, bake_anim=True,
                         # Every bone keyed on every frame. Skipping "unanimated" bones drops channels that hold a
                         # constant non-rest pose, and thinning keys bends limbs mid-clip; Unity thins on import.
                         bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False, bake_anim_use_all_actions=True,
                         bake_anim_force_startend_keying=True, bake_anim_step=1.0, bake_anim_simplify_factor=0.0)
with open(os.path.splitext(out_fbx)[0] + ".clips.csv", "w", newline="", encoding="utf-8") as fh:
    w = csv.writer(fh)
    w.writerow(["clip", "loop", "inPlace", "yawOffset", "#frames", "#fps"])
    for row in rows:
        w.writerow(row)
print("EXPORTED", out_fbx, len(rows), "clips")
