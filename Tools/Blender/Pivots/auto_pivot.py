"""Fills a pivot's empty span by itself, in the pivot workspace (the user, 2026-11-25: "일단 걍 모션도 네가 알아 만들어 봐
컴퓨터 써서"). Run headless on the workspace, the pivots' keys as arguments:

  blender --background ArtSource/Pivots/ENFILADE_Pivot_Animation.blend
          --python Tools/Blender/Pivots/auto_pivot.py -- Jog_L_R Jog_R_L [--gap=5] [--send]

Each pivot starts again from its draft (build_pivot_workspace.py: BIPER's old way's cycle up to its outside foot coming
down, the span, the new way's cycle from that foot about to push off), its span made GAP frames long (the draft's 8 kept
the foot down 0.37 s; a cut's plant is 0.15-0.25 s), and the span keyed frame by frame, as a side-step cut goes:
- the hips: on from the old way's speed into the new way's (a cubic between the two speeds), so they brake over the
  planted foot, stop and push back, and dip (DIP) at the brace;
- the trunk leans off the plant toward the new way (LEAN, shared up the spine), the head kept level over it: the view
  and the chest still face ahead;
- the planted foot stays down, settling from how it came down to how it pushes off (two-bone IK on the legs' own FK
  bones: the knee in the plane it bent in at the span's ends), the other foot carried across in the air on an arc (LIFT);
- the arms and the rest eased across between the two ends.
The marks follow: 디딤 where the hips stop along the old way, 들어옴 three frames before the plant, 나감 three into the new
way's cycle. With --send the changed pivots go to the game (pivot_tools.send_changed) and the workspace is saved; without
it only the workspace is saved.
"""
import importlib.util
import math
import sys
from pathlib import Path

import bpy
from mathutils import Quaternion, Vector

HERE = Path(__file__).resolve().parent
sys.dont_write_bytecode = True
ROOT = HERE.parents[2]


def _load(name, path):
    if name in sys.modules:
        return sys.modules[name]
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


build = _load('build_pivot_workspace', HERE / 'build_pivot_workspace.py')
tools = build.tools
one_click = tools.one_click

# How deep the hips dip at the brace (m), how far the trunk leans off the plant (degrees), how high the free foot is
# carried (m), the span's default length (frames).
DIP, LEAN, LIFT, GAP = .055, 9.0, .05, 5
LEGS = {'Left': ('LeftUpperLeg', 'LeftLowerLeg', 'LeftFoot', 'LeftToes'),
        'Right': ('RightUpperLeg', 'RightLowerLeg', 'RightFoot', 'RightToes')}
TRUNK_SHARE = {'Spine': 1 / 3, 'Chest': 2 / 3, 'UpperChest': 1.0, 'Neck': .5, 'Head': 0.0}


def args():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    keys = [a for a in argv if not a.startswith('--')]
    gap = next((int(a[len('--gap='):]) for a in argv if a.startswith('--gap=')), GAP)
    return keys, gap, '--send' in argv


def rot_between(a, b):
    """The least turn taking direction a onto direction b."""
    return a.normalized().rotation_difference(b.normalized())


def hermite(p0, m0, p1, m1, s):
    s2, s3 = s * s, s * s * s
    return (2 * s3 - 3 * s2 + 1) * p0 + (s3 - 2 * s2 + s) * m0 + (-2 * s3 + 3 * s2) * p1 + (s3 - s2) * m1


def hermite_slope(p0, m0, p1, m1, s):
    s2 = s * s
    return (6 * s2 - 6 * s) * p0 + (3 * s2 - 4 * s + 1) * m0 + (-6 * s2 + 6 * s) * p1 + (3 * s2 - 2 * s) * m1


def shift_after(action, first, by):
    """Every key at or after frame `first` moved `by` frames (handles too)."""
    for curve in one_click().curves(action):
        for point in curve.keyframe_points:
            if point.co.x >= first - .01:
                point.co.x += by
                point.handle_left.x += by
                point.handle_right.x += by
        curve.update()


def read_pose(context, rig, frame):
    """At a frame: each role's world turn, the hips' and every role's head in the world."""
    scene = context.scene
    scene.frame_set(frame)
    context.view_layer.update()
    ev = rig.evaluated_get(context.evaluated_depsgraph_get())
    world = rig.matrix_world
    q, head = {}, {}
    for role, name in build.KAZUSA.items():
        m = world @ ev.pose.bones[name].matrix
        q[role] = m.to_3x3().normalized().to_quaternion()
        head[role] = m.translation.copy()
    return q, head


def solve_leg(hip, knee_hint, target, thigh, shin):
    """Where the knee goes for the ankle at target: in the plane through the hip, the target and the hint, on its side."""
    to = target - hip
    d = min(max(to.length, 1e-4), (thigh + shin) * .999)
    axis = to.normalized()
    x = (thigh * thigh - shin * shin + d * d) / (2 * d)
    y = math.sqrt(max(thigh * thigh - x * x, 0.0))
    side = (knee_hint - hip) - axis * (knee_hint - hip).dot(axis)
    side = side.normalized() if side.length > 1e-5 else Vector((0, -1, 0))
    return hip + axis * x + side * y, hip + axis * d


def make(context, rig, key, gap):
    p = tools.pivot(key)
    if p is None:
        raise SystemExit(f'No pivot {key}')
    _k, _label, _step, old_clip, new_clip, gait, a, b = p
    before, old_gap, after = build.SPANS[gait]
    name = tools.PREFIX + key
    draft = bpy.data.actions[name + '_draft']
    action = one_click().revert(name)
    for mark in tools.MARKS:
        if ('enf_mark_' + mark) in draft:
            action['enf_mark_' + mark] = draft['enf_mark_' + mark]
    shift_after(action, before + old_gap + 1, gap - old_gap)
    total = before + gap + after
    one_click().play(rig, action)
    A, B = before, before + gap + 1
    side = build.planting_side(a, b)
    free = 'Right' if side == 'Left' else 'Left'
    qA, hA = read_pose(context, rig, A)
    _q, hA0 = read_pose(context, rig, A - 1)
    qB, hB = read_pose(context, rig, B)
    _q, hB1 = read_pose(context, rig, B + 1)
    span = B - A
    # The hips' way on: from the old way's speed (per frame) to the new way's, over the span (Hermite in frames).
    pA, pB = hA['Hips'], hB['Hips']
    mA, mB = (hA['Hips'] - hA0['Hips']) * span, (hB1['Hips'] - hB['Hips']) * span
    old_way = Vector((mA.x, mA.y, 0)).normalized()
    new_way = Vector((mB.x, mB.y, 0)).normalized()
    up = Vector((0, 0, 1))
    lean_axis = up.cross(new_way)
    lean_axis = lean_axis.normalized() if lean_axis.length > 1e-4 else Vector((1, 0, 0))
    # The legs' lengths, from the rest.
    bones = rig.data.bones
    lengths = {}
    for leg, (up_role, low_role, foot_role, _t) in LEGS.items():
        hip = bones[build.KAZUSA[up_role]].head_local
        knee = bones[build.KAZUSA[low_role]].head_local
        ankle = bones[build.KAZUSA[foot_role]].head_local
        scale = rig.matrix_world.to_scale().x
        lengths[leg] = ((knee - hip).length * scale, (ankle - knee).length * scale)
    inv_world = rig.matrix_world.inverted()
    rest_local = {r: bones[build.KAZUSA[r]].matrix_local.copy() for r in build.ROLES}
    parent_of = {r: bones[build.KAZUSA[r]].parent.name for r in build.ROLES}
    rel = {r: bones[parent_of[r]].matrix_local.inverted() @ rest_local[r] for r in build.ROLES}
    center = rig.pose.bones[parent_of['Hips']].matrix.copy()
    # Each bone's key at the span's start, so the keys in between keep to its side of the sphere (as the draft's do on
    # through to the new way's cycle).
    context.scene.frame_set(A)
    previous = {r: rig.pose.bones[build.KAZUSA[r]].rotation_quaternion.copy() for r in build.ROLES}
    stop_frame, stop_speed = A, None
    for f in range(A + 1, B):
        s = (f - A) / span
        ease = s * s * (3 - 2 * s)
        world_q = {r: qA[r].slerp(qB[r], ease) for r in build.ROLES}
        hips_at = hermite(pA, mA, pB, mB, s)
        # Height eased between the ends, not carried on their rise and fall: on those slopes the cubic sank the hips 17 cm.
        hips_at.z = pA.z + (pB.z - pA.z) * ease - DIP * math.sin(math.pi * s)
        speed = hermite_slope(pA, mA, pB, mB, s).dot(old_way)
        if stop_speed is None or abs(speed) < stop_speed:
            stop_frame, stop_speed = f, abs(speed)
        # The trunk off the plant toward the new way, the head kept level.
        lean = math.radians(LEAN) * math.sin(math.pi * s)
        for role, share in TRUNK_SHARE.items():
            world_q[role] = Quaternion(lean_axis, lean * share) @ world_q[role]
        # The feet: the planted one settling from how it came down to how it pushes off (the heel down, a few cm: no jump
        # where the new way's cycle takes it), the other carried across on an arc.
        planted, toes = LEGS[side][2], LEGS[side][3]
        world_q[planted], world_q[toes] = qA[planted].slerp(qB[planted], ease), qA[toes].slerp(qB[toes], ease)
        targets = {side: hA[planted].lerp(hB[planted], ease),
                   free: hA[LEGS[free][2]].lerp(hB[LEGS[free][2]], ease) + up * (LIFT * math.sin(math.pi * s))}
        posed = build.pose_from(world_q, hips_at, inv_world, rel, parent_of, center)
        world = rig.matrix_world
        for leg, (up_role, low_role, foot_role, _toes) in LEGS.items():
            hip = (world @ posed[up_role][0]).translation
            knee = (world @ posed[low_role][0]).translation
            knee_to, ankle_to = solve_leg(hip, knee, targets[leg], *lengths[leg])
            world_q[up_role] = rot_between(knee - hip, knee_to - hip) @ world_q[up_role]
            posed = build.pose_from(world_q, hips_at, inv_world, rel, parent_of, center)
            knee = (world @ posed[low_role][0]).translation
            ankle = (world @ posed[foot_role][0]).translation
            world_q[low_role] = rot_between(ankle - knee, ankle_to - knee) @ world_q[low_role]
            posed = build.pose_from(world_q, hips_at, inv_world, rel, parent_of, center)
        for role in build.ROLES:
            pb = rig.pose.bones[build.KAZUSA[role]]
            pb.rotation_mode = 'QUATERNION'
            basis = posed[role][1]
            q = basis.to_quaternion()
            if q.dot(previous[role]) < 0:
                q.negate()
            previous[role] = q.copy()
            pb.rotation_quaternion = q
            pb.keyframe_insert('rotation_quaternion', frame=f, group=pb.name)
            if role == 'Hips':
                pb.location = basis.translation
                pb.keyframe_insert('location', frame=f, group=pb.name)
    action['enf_frames'] = total
    action['enf_mark_들어옴'] = max(1, A - 3)
    action['enf_mark_디딤'] = stop_frame
    action['enf_mark_나감'] = B + 3
    action['enf_auto'] = f'auto_pivot.py: span {gap}, dip {DIP}, lean {LEAN}, lift {LIFT}'
    print(f'  {key}: span {A + 1}-{B - 1} of {total}, {side} foot planted, stops at {stop_frame}', flush=True)
    return action


def main():
    keys, gap, send = args()
    context = bpy.context
    scene = context.scene
    _load('truefps_tools', ROOT / 'Tools/Blender/TrueFPS/truefps_tools.py')
    try:
        sys.modules['truefps_tools'].register()
    except Exception as error:  # Registered already in a GUI session.
        print('truefps_tools:', error)
    tools.register()
    rig = tools.find_rig(scene)
    if rig is None:
        raise SystemExit('Open ArtSource/Pivots/ENFILADE_Pivot_Animation.blend.')
    for key in keys:
        make(context, rig, key, gap)
    shown = keys[-1] if keys else tools.current_pivot(scene)
    scene['enf_pivot'] = ''
    tools.select_pivot(context, shown)
    if send:
        sent, failed = tools.send_changed(context, force=True) or ([], ['not a pivot file'])
        print('sent', sent, 'failed', failed, flush=True)
    bpy.ops.wm.save_mainfile()
    print('AUTO PIVOT SAVED', bpy.data.filepath, flush=True)


if __name__ == '__main__':
    main()
