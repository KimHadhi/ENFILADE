"""Builds ArtSource/Run/ENFILADE_Run_Animation.blend from the True FPS start file: the game's walk and run cycles
(Player.csv Body.forwardMoves, rightMoves, backMoves, leftMoves) on Kazusa's True FPS rig, each a locked draft layer under
an empty touch-up layer, for the user to polish (run_tools.py). Run from the project root:

  blender --background --factory-startup ArtSource/TrueFPS/ENFILADE_TrueFPS_Start.blend
          --python Tools/Blender/Run/build_run_workspace.py -- ArtSource/Run/ENFILADE_Run_Animation.blend

It refuses to overwrite an existing workspace unless --force is given: the user's touch-ups live in it. The source .blend
is never saved.

The drafts are retargeted as a humanoid avatar retargets them, in the world:
- each bone turns from its T-pose as its source bone turns from the source's T-pose (Mixamo.tpose.fbx for the Mixamo
  cycles, BeatrunBody.fbx's own bind pose for Beatrun), its rest line first laid along the source's (two-axis: toward its
  child joint, and the body's forward), so an A-pose source still lifts Kazusa's arms right;
- the hips ride as high over the floor as the source's, scaled by the hips' heights, and sway as far, scaled the same;
- each frame the hips are raised or lowered so Kazusa's lowest foot is as low as the source's, scaled: the feet stay on
  the floor whatever the legs' proportions;
- the four limbs in FK (the True FPS rig's ik_* off), the fingers left in the rig's own pose.
Every source skeleton here faces -Y with its left at +X, as the rig does (checked in the build).
"""
import importlib.util
import json
import math
import re
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Quaternion, Vector

HERE = Path(__file__).resolve().parent
sys.dont_write_bytecode = True
ROOT = HERE.parents[2]
spec = importlib.util.spec_from_file_location('run_tools', HERE / 'run_tools.py')
run = importlib.util.module_from_spec(spec)
sys.modules['run_tools'] = run
spec.loader.exec_module(run)

ROLES = ['Hips', 'Spine', 'Chest', 'UpperChest', 'Neck', 'Head',
         'LeftShoulder', 'LeftUpperArm', 'LeftLowerArm', 'LeftHand', 'RightShoulder', 'RightUpperArm', 'RightLowerArm', 'RightHand',
         'LeftUpperLeg', 'LeftLowerLeg', 'LeftFoot', 'LeftToes', 'RightUpperLeg', 'RightLowerLeg', 'RightFoot', 'RightToes']
# Which joint a bone points at, for laying its rest line along the source's; the end bones take their parent's.
CHILD = {'Hips': 'Spine', 'Spine': 'Chest', 'Chest': 'UpperChest', 'UpperChest': 'Neck', 'Neck': 'Head',
         'LeftShoulder': 'LeftUpperArm', 'LeftUpperArm': 'LeftLowerArm', 'LeftLowerArm': 'LeftHand',
         'RightShoulder': 'RightUpperArm', 'RightUpperArm': 'RightLowerArm', 'RightLowerArm': 'RightHand',
         'LeftUpperLeg': 'LeftLowerLeg', 'LeftLowerLeg': 'LeftFoot', 'LeftFoot': 'LeftToes',
         'RightUpperLeg': 'RightLowerLeg', 'RightLowerLeg': 'RightFoot', 'RightFoot': 'RightToes'}
SAME_AS = {'Head': 'Neck', 'LeftHand': 'LeftLowerArm', 'RightHand': 'RightLowerArm', 'LeftToes': 'LeftFoot', 'RightToes': 'RightFoot'}

KAZUSA = {'Hips': 'Hips', 'Spine': 'Spine', 'Chest': 'Chest', 'UpperChest': 'Upper Chest', 'Neck': 'Neck', 'Head': 'HeadBone',
          'LeftShoulder': 'Left shoulder', 'LeftUpperArm': 'Left arm', 'LeftLowerArm': 'Left elbow', 'LeftHand': 'Left wrist',
          'RightShoulder': 'Right shoulder', 'RightUpperArm': 'Right arm', 'RightLowerArm': 'Right elbow', 'RightHand': 'Right wrist',
          'LeftUpperLeg': 'Left leg', 'LeftLowerLeg': 'Left knee', 'LeftFoot': 'Left ankle', 'LeftToes': 'Left toe',
          'RightUpperLeg': 'Right leg', 'RightLowerLeg': 'Right knee', 'RightFoot': 'Right ankle', 'RightToes': 'Right toe'}
MIXAMO = {r: 'mixamorig:' + n for r, n in {
    'Hips': 'Hips', 'Spine': 'Spine', 'Chest': 'Spine1', 'UpperChest': 'Spine2', 'Neck': 'Neck', 'Head': 'Head',
    'LeftShoulder': 'LeftShoulder', 'LeftUpperArm': 'LeftArm', 'LeftLowerArm': 'LeftForeArm', 'LeftHand': 'LeftHand',
    'RightShoulder': 'RightShoulder', 'RightUpperArm': 'RightArm', 'RightLowerArm': 'RightForeArm', 'RightHand': 'RightHand',
    'LeftUpperLeg': 'LeftUpLeg', 'LeftLowerLeg': 'LeftLeg', 'LeftFoot': 'LeftFoot', 'LeftToes': 'LeftToeBase',
    'RightUpperLeg': 'RightUpLeg', 'RightLowerLeg': 'RightLeg', 'RightFoot': 'RightFoot', 'RightToes': 'RightToeBase'}.items()}
VALVE = {r: 'ValveBiped.Bip01_' + n for r, n in {
    'Hips': 'Pelvis', 'Spine': 'Spine', 'Chest': 'Spine2', 'UpperChest': 'Spine4', 'Neck': 'Neck1', 'Head': 'Head1',
    'LeftShoulder': 'L_Clavicle', 'LeftUpperArm': 'L_UpperArm', 'LeftLowerArm': 'L_Forearm', 'LeftHand': 'L_Hand',
    'RightShoulder': 'R_Clavicle', 'RightUpperArm': 'R_UpperArm', 'RightLowerArm': 'R_Forearm', 'RightHand': 'R_Hand',
    'LeftUpperLeg': 'L_Thigh', 'LeftLowerLeg': 'L_Calf', 'LeftFoot': 'L_Foot', 'LeftToes': 'L_Toe0',
    'RightUpperLeg': 'R_Thigh', 'RightLowerLeg': 'R_Calf', 'RightFoot': 'R_Foot', 'RightToes': 'R_Toe0'}.items()}

MIXAMO_DIR = 'Assets/_Project/Art/Animations/Mixamo/'
# The game's cycles: clip -> (source file, take pattern or None, frames of the take (from, to) or None, skeleton).
SOURCES = {
    'mx_run': (MIXAMO_DIR + 'mx_run.fbx', None, None, 'mixamo'),
    'sprintfwd': ('Assets/_Project/Art/Animations/Beatrun/BeatrunBody.fbx', r'\|sprintfwd$', (0, 15), 'valve'),
    'mx_walk': (MIXAMO_DIR + 'mx_walk.fbx', None, None, 'mixamo'),
    'mx_strafe_run_right': (MIXAMO_DIR + 'mx_strafe_run_right.fbx', None, None, 'mixamo'),
    'mx_strafe_run_left': (MIXAMO_DIR + 'mx_strafe_run_left.fbx', None, None, 'mixamo'),
    'mx_run_back': (MIXAMO_DIR + 'mx_run_back.fbx', None, None, 'mixamo'),
    'mx_strafe_walk_right': (MIXAMO_DIR + 'mx_strafe_walk_right.fbx', None, None, 'mixamo'),
    'mx_strafe_walk_left': (MIXAMO_DIR + 'mx_strafe_walk_left.fbx', None, None, 'mixamo'),
    'mx_walk_back': (MIXAMO_DIR + 'mx_walk_back.fbx', None, None, 'mixamo'),
}
SKELETONS = {'mixamo': (MIXAMO, MIXAMO_DIR + 'Mixamo.tpose.fbx'), 'valve': (VALVE, None)}
FORWARD, UP = Vector((0.0, -1.0, 0.0)), Vector((0.0, 0.0, 1.0))
# The source skeletons beside Kazusa for comparing (보기용): this far to her left, at her size.
SOURCE_OFFSET = Vector((1.0, 0.0, 0.0))


def args():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    out = next((a for a in argv if not a.startswith('--')), None)
    if out is None:
        raise SystemExit('Give the output .blend path after --')
    return Path(out).resolve(), '--force' in argv, [a[len('--only='):] for a in argv if a.startswith('--only=')]


def import_fbx(path):
    scene = bpy.context.scene
    fps, base = scene.render.fps, scene.render.fps_base
    objects, actions = set(bpy.data.objects), set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=str(ROOT / path), ignore_leaf_bones=False, automatic_bone_orientation=False)
    scene.render.fps, scene.render.fps_base = fps, base
    made = [o for o in bpy.data.objects if o not in objects]
    arm = max((o for o in made if o.type == 'ARMATURE'), key=lambda o: len(o.data.bones))
    return arm, made, [a for a in bpy.data.actions if a not in actions]


def frame(primary, forward=FORWARD, up=UP):
    """An orthonormal frame from a bone's line and the body's forward (its up, for a line that runs forward)."""
    p = primary.normalized()
    second = forward if abs(p.dot(forward)) < .8 else up
    s = (second - p * second.dot(p)).normalized()
    return Matrix((p, s, p.cross(s))).transposed()


def rest_lines(arm, names, rest_world):
    """Each role's rest line in the world: toward its child joint (an end bone takes its parent's)."""
    heads = {r: rest_world[r].translation for r in names}
    lines = {}
    for role in ROLES:
        if role in CHILD and CHILD[role] in heads and role in heads:
            lines[role] = heads[CHILD[role]] - heads[role]
    for role, other in SAME_AS.items():
        if other in lines:
            lines[role] = lines[other]
    return lines


def facing_check(name, rest_world):
    left = rest_world['LeftUpperLeg'].translation - rest_world['RightUpperLeg'].translation
    toes = rest_world['LeftToes'].translation - rest_world['LeftFoot'].translation
    print(f'  {name}: left hip at {left.x:+.3f} x, toes {toes.y:+.3f} y', flush=True)
    if left.x <= 0 or toes.y >= 0:
        raise SystemExit(f'{name} does not face -Y with its left at +X; the retarget assumes it.')


def world_of(arm, bone):
    return arm.matrix_world @ arm.data.bones[bone].matrix_local


def floor_of(points):
    return min(p.z for p in points)


def retarget(context, rig, key, source, tposes):
    path, take, span, kind = source
    names, _ = SKELETONS[kind]
    arm, made, actions = import_fbx(path)
    act = next((a for a in actions if take is None or re.search(take, a.name)), None)
    if act is None:
        raise SystemExit(f'{key}: no take in {path}')
    for a in actions:
        if a is not act:
            bpy.data.actions.remove(a)
    for obj in made:
        if obj.type != 'ARMATURE':
            bpy.data.objects.remove(obj, do_unlink=True)
    context.view_layer.update()
    arm.animation_data_create()
    arm.animation_data.action = act
    if getattr(act, 'slots', None) and hasattr(arm.animation_data, 'action_slot'):
        arm.animation_data.action_slot = act.slots[0]

    # The source's T-pose: Mixamo's own T-pose file, or the file's bind pose.
    ref = tposes.get(kind) or arm
    src_rest = {r: world_of(ref, names[r]) for r in ROLES}
    if ref is not arm:
        # The T-pose file stands where the clip's character stands (feet on the floor); its size may differ in scale only.
        pass
    facing_check(key + ' source', src_rest)
    kaz_rest = {r: world_of(rig, KAZUSA[r]) for r in ROLES}
    src_lines, kaz_lines = rest_lines(ref, names, src_rest), rest_lines(rig, KAZUSA, kaz_rest)
    align = {r: (frame(src_lines[r]) @ frame(kaz_lines[r]).inverted()).to_quaternion() for r in ROLES}
    src_rest_q = {r: src_rest[r].to_3x3().normalized().to_quaternion() for r in ROLES}
    kaz_rest_q = {r: kaz_rest[r].to_3x3().normalized().to_quaternion() for r in ROLES}
    src_floor = floor_of([src_rest[r].translation for r in ('LeftFoot', 'RightFoot', 'LeftToes', 'RightToes')])
    kaz_floor = floor_of([kaz_rest[r].translation for r in ('LeftFoot', 'RightFoot', 'LeftToes', 'RightToes')])
    src_hips_h = src_rest['Hips'].translation.z - src_floor
    kaz_hips_h = kaz_rest['Hips'].translation.z - kaz_floor
    ratio = kaz_hips_h / src_hips_h
    src_ankle = src_rest['LeftFoot'].translation.z - src_floor
    kaz_ankle = kaz_rest['LeftFoot'].translation.z - kaz_floor

    a0, a1 = (int(v) for v in act.frame_range)
    f0, f1 = (a0 + span[0], a0 + span[1]) if span else (a0, a1)
    scene = context.scene
    samples = []
    for f in range(f0, f1 + 1):
        scene.frame_set(f)
        context.view_layer.update()
        ev = arm.evaluated_get(context.evaluated_depsgraph_get())
        samples.append({r: ev.matrix_world @ ev.pose.bones[names[r]].matrix for r in ROLES})
    clip_floor = min(floor_of([s[r].translation for r in ('LeftFoot', 'RightFoot', 'LeftToes', 'RightToes')]) for s in samples)
    mean_x = sum(s['Hips'].translation.x for s in samples) / len(samples)
    mean_y = sum(s['Hips'].translation.y for s in samples) / len(samples)

    # Kazusa's rest in her armature's space, for posing her bones from their parents (no depsgraph per bone).
    bones = rig.data.bones
    inv_world = rig.matrix_world.inverted()
    rest_local = {r: bones[KAZUSA[r]].matrix_local.copy() for r in ROLES}
    parent_of = {r: bones[KAZUSA[r]].parent.name for r in ROLES}
    rel = {}
    for r in ROLES:
        parent = bones[parent_of[r]]
        rel[r] = parent.matrix_local.inverted() @ rest_local[r]
    center = rig.pose.bones[parent_of['Hips']].matrix.copy()

    draft = bpy.data.actions.new(run.PREFIX + key + '_draft')
    draft.use_fake_user = True
    draft['enf_layer'] = 'draft'
    draft['enf_source'] = path + (f' ({take} {span})' if take else '')
    draft['enf_frames'] = f1 - f0 + 1
    data = rig.animation_data or rig.animation_data_create()
    for track in list(data.nla_tracks):
        data.nla_tracks.remove(track)
    data.action = draft
    for limb in ('arm_L', 'arm_R', 'leg_L', 'leg_R'):
        rig['ik_' + limb] = 0.0
        rig.keyframe_insert(f'["ik_{limb}"]', frame=1, group='IK / FK mode')
    previous = {}
    for i, sample in enumerate(samples):
        world_q = {}
        for r in ROLES:
            delta = sample[r].to_3x3().normalized().to_quaternion() @ src_rest_q[r].inverted()
            world_q[r] = delta @ align[r] @ kaz_rest_q[r]
        hip = sample['Hips'].translation
        hips_at = Vector((kaz_rest['Hips'].translation.x + (hip.x - mean_x) * ratio,
                          kaz_rest['Hips'].translation.y + (hip.y - mean_y) * ratio,
                          kaz_floor + (hip.z - clip_floor) * ratio))
        posed = pose_from(world_q, hips_at, inv_world, rel, parent_of, center)
        # The feet as low as the source's: the lowest ankle over its rest height, scaled.
        src_low = min(sample['LeftFoot'].translation.z, sample['RightFoot'].translation.z) - clip_floor - src_ankle
        kaz_low = min((rig.matrix_world @ posed[r][0]).translation.z for r in ('LeftFoot', 'RightFoot')) - kaz_floor - kaz_ankle
        hips_at.z += src_low * ratio - kaz_low
        posed = pose_from(world_q, hips_at, inv_world, rel, parent_of, center)
        for r in ROLES:
            pb = rig.pose.bones[KAZUSA[r]]
            pb.rotation_mode = 'QUATERNION'
            basis = posed[r][1]
            q = basis.to_quaternion()
            if r in previous and q.dot(previous[r]) < 0:
                q.negate()
            previous[r] = q.copy()
            pb.rotation_quaternion = q
            pb.keyframe_insert('rotation_quaternion', frame=i + 1, group=pb.name)
            if r == 'Hips':
                pb.location = basis.translation
                pb.keyframe_insert('location', frame=i + 1, group=pb.name)
    data.action = None
    for track in list(data.nla_tracks):
        data.nla_tracks.remove(track)

    # The source beside her for comparing, at her size: its skeleton only, never exported.
    collection = bpy.data.collections.get(run.SOURCE_COLLECTION)
    if collection is None:
        collection = bpy.data.collections.new(run.SOURCE_COLLECTION)
        context.scene.collection.children.link(collection)
    for owner in list(arm.users_collection):
        owner.objects.unlink(arm)
    collection.objects.link(arm)
    arm.name = '원본 · ' + key
    arm['enf_cycle'] = key
    arm.data.display_type = 'STICK'
    arm.show_in_front = True
    scale = ratio * arm.matrix_world.to_scale().x
    arm.scale = (scale, scale, scale)
    arm.location = SOURCE_OFFSET - Vector((mean_x, mean_y, clip_floor)) * ratio + Vector((0, 0, kaz_floor))
    arm.hide_select = True
    # The clip's own frames, the take's start at frame 1 as the draft's.
    if arm.animation_data:
        for track in list(arm.animation_data.nla_tracks):
            arm.animation_data.nla_tracks.remove(track)
        arm.animation_data.action = None
        track = arm.animation_data.nla_tracks.new()
        track.name = '원본'
        strip = track.strips.new(act.name, 1, act)
        if hasattr(strip, 'action_slot') and strip.action_slot is None and len(getattr(act, 'slots', [])):
            strip.action_slot = act.slots[0]
        strip.action_frame_start, strip.action_frame_end = f0, f1
        strip.frame_start = 1
        strip.frame_end = 1 + (f1 - f0)
    print(f'  {key}: {len(samples)} frames, hips x{ratio:.3f}', flush=True)
    return draft


def pose_from(world_q, hips_at, inv_world, rel, parent_of, center):
    """Each role's armature-space matrix and its basis, parents first: the hips at hips_at, every bone turned to its
    world rotation, its head where its parent puts it."""
    posed = {}
    world_rot = inv_world.to_quaternion()
    for r in ROLES:
        parent = posed[ROLE_OF[parent_of[r]]][0] if parent_of[r] in ROLE_OF else center
        rot = (world_rot @ world_q[r]).to_matrix().to_4x4()
        head = (inv_world @ hips_at) if r == 'Hips' else (parent @ rel[r]).translation
        matrix = Matrix.Translation(head) @ rot
        basis = rel[r].inverted() @ parent.inverted() @ matrix
        posed[r] = (matrix, basis)
    return posed


ROLE_OF = {name: role for role, name in KAZUSA.items()}


def main():
    out, force, only = args()
    if out.exists() and not force:
        raise SystemExit(f'{out} exists; add --force to build it again.')
    context = bpy.context
    scene = context.scene
    run.register()
    rig = run.find_rig(scene)
    if rig is None:
        raise SystemExit('Open ArtSource/TrueFPS/ENFILADE_TrueFPS_Start.blend first.')
    # Kazusa's meshes stay out of the depsgraph while every frame is sampled.
    hidden = [o for o in scene.objects if o.type == 'MESH' and not o.hide_viewport]
    for o in hidden:
        o.hide_viewport = True
    print('Kazusa:', flush=True)
    facing_check('KazusaBandRig', {r: world_of(rig, KAZUSA[r]) for r in ROLES})
    tposes = {}
    for kind, (names, tpose) in SKELETONS.items():
        if tpose:
            ref, made, actions = import_fbx(tpose)
            for a in actions:
                bpy.data.actions.remove(a)
            for obj in made:
                if obj is not ref:
                    bpy.data.objects.remove(obj, do_unlink=True)
            ref.name = 'T-pose · ' + kind
            tposes[kind] = ref
    for key, _ in run.CYCLES:
        if only and key not in only:
            continue
        print('Retargeting', key, flush=True)
        retarget(context, rig, key, SOURCES[key], tposes)
    for ref in tposes.values():
        bpy.data.objects.remove(ref, do_unlink=True)
    context.view_layer.update()
    for o in hidden:
        o.hide_viewport = False
    # The rig's own default pose again under the layers, then the first cycle on show.
    for key, _ in run.CYCLES:
        run.touch_up(run.PREFIX + key)
    first = next((k for k, _ in run.CYCLES if bpy.data.actions.get(run.PREFIX + k + '_draft')), None)
    if first:
        run.select_cycle(context, first)
    text = bpy.data.texts.get('START HERE - Run tools.py') or bpy.data.texts.new('START HERE - Run tools.py')
    text.from_string((HERE / 'run_tools.py').read_text(encoding='utf-8'))
    out.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(out), copy=False, compress=True)
    print('RUN WORKSPACE', out, flush=True)


main()
