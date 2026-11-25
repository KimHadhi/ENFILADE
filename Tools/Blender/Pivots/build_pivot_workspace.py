"""Builds ArtSource/Pivots/ENFILADE_Pivot_Animation.blend from the True FPS start file: a draft of every pivot
(pivot_tools.PIVOTS) on Kazusa's True FPS rig, for the user to animate the plant and the push (pivot_tools.py), and
BIPER's own clips beside her to look at. Run from the project root:

  blender --background --factory-startup ArtSource/TrueFPS/ENFILADE_TrueFPS_Start.blend
          --python Tools/Blender/Pivots/build_pivot_workspace.py -- ArtSource/Pivots/ENFILADE_Pivot_Animation.blend

It refuses to overwrite an existing workspace unless --force is given: the user's work lives in it. The source .blend is
never saved.

BIPER's cycles (Assets/_Project/Art/Animations/BiperRunner, the ones the game's legs mix) are retargeted as
build_run_workspace.py retargets the run cycles, in the world: each bone turns from its rest as its BIPER bone turns from
BIPER's, its rest line first laid along BIPER's; the hips ride as high over the floor as BIPER's, scaled by the hips'
heights, and each frame the lowest foot is set as low as BIPER's, scaled; the fingers by the hand (run_tools.key_fingers).
BIPER's files come in lying on their back (the importer's +90 degrees about X under their own Y-up); they are stood up
first (another 180 about X), facing -Y with their left at +X as Kazusa does (checked).

A pivot's draft, at 30 frames a second:
- the old way's cycle up to the frame its planting foot comes down furthest out along the way it goes (디딤): the foot
  leading the old way (the left going left), or, out of ahead or back, the foot away from the new way;
- an empty span (GAP frames) for the user: the plant, the brace and the push;
- the new way's cycle from the frame that same foot is furthest behind along the new way, still down (about to push off);
- both placed so that foot stands at the origin at either side of the span, so it stays where it came down.
Marks: 들어옴 a few frames before the plant, 디딤 at it, 나감 a few frames into the new way's cycle.
"""
import importlib.util
import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector

HERE = Path(__file__).resolve().parent
sys.dont_write_bytecode = True
ROOT = HERE.parents[2]


def _load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


run = _load('run_tools', HERE.parent / 'Run' / 'run_tools.py')
tools = _load('pivot_tools', HERE / 'pivot_tools.py')

ROLES = ['Hips', 'Spine', 'Chest', 'UpperChest', 'Neck', 'Head',
         'LeftShoulder', 'LeftUpperArm', 'LeftLowerArm', 'LeftHand', 'RightShoulder', 'RightUpperArm', 'RightLowerArm', 'RightHand',
         'LeftUpperLeg', 'LeftLowerLeg', 'LeftFoot', 'LeftToes', 'RightUpperLeg', 'RightLowerLeg', 'RightFoot', 'RightToes']
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
ROLE_OF = {name: role for role, name in KAZUSA.items()}
BIPER = {'Hips': 'pelvis', 'Spine': 'spine_01', 'Chest': 'spine_02', 'UpperChest': 'spine_03', 'Neck': 'neck_01', 'Head': 'head',
         'LeftShoulder': 'clavicle_l', 'LeftUpperArm': 'upperarm_l', 'LeftLowerArm': 'lowerarm_l', 'LeftHand': 'hand_l',
         'RightShoulder': 'clavicle_r', 'RightUpperArm': 'upperarm_r', 'RightLowerArm': 'lowerarm_r', 'RightHand': 'hand_r',
         'LeftUpperLeg': 'thigh_l', 'LeftLowerLeg': 'calf_l', 'LeftFoot': 'foot_l', 'LeftToes': 'ball_l',
         'RightUpperLeg': 'thigh_r', 'RightLowerLeg': 'calf_r', 'RightFoot': 'foot_r', 'RightToes': 'ball_r'}
BIPER_DIR = 'Assets/_Project/Art/Animations/BiperRunner/'
FORWARD, UP = Vector((0.0, -1.0, 0.0)), Vector((0.0, 0.0, 1.0))
# Frames of the old way before the plant, of the new way after the push, and the span between, by gait.
SPANS = {'Jog': (12, 8, 12), 'Walk': (16, 10, 16)}
# A foot is down within this much (m, BIPER's own size) of its lowest in the clip.
DOWN_WITHIN = .03
# Where BIPER's two cycles of the pivot shown stand (the old way's, the new way's), and the other clips' row, behind her.
SLOTS = {'from': Vector((-1.1, 1.6, 0.0)), 'to': Vector((1.1, 1.6, 0.0))}
EXTRA_ROW_Y, EXTRA_STEP = 3.2, 1.3


def _biper_hand(side):
    """BIPER's hand (UE mannequin names): each finger's three bones knuckle out, the thumb from its root."""
    s = side[0].lower()
    return f'hand_{s}', {'index': (f'index_01_{s}', f'index_02_{s}', f'index_03_{s}'),
                         'middle': (f'middle_01_{s}', f'middle_02_{s}', f'middle_03_{s}'),
                         'ring': (f'ring_01_{s}', f'ring_02_{s}', f'ring_03_{s}'),
                         'little': (f'pinky_01_{s}', f'pinky_02_{s}', f'pinky_03_{s}'),
                         'thumb': (f'thumb_01_{s}', f'thumb_02_{s}', f'thumb_03_{s}')}


run.HANDS['biper'] = {side: _biper_hand(side) for side in run.SIDES}


def args():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    out = next((a for a in argv if not a.startswith('--')), None)
    if out is None:
        raise SystemExit('Give the output .blend path after --')
    return Path(out).resolve(), '--force' in argv


def frame(primary, forward=FORWARD, up=UP):
    p = primary.normalized()
    second = forward if abs(p.dot(forward)) < .8 else up
    s = (second - p * second.dot(p)).normalized()
    return Matrix((p, s, p.cross(s))).transposed()


def rest_lines(names, rest_world):
    heads = {r: rest_world[r].translation for r in names}
    lines = {}
    for role in ROLES:
        if role in CHILD and CHILD[role] in heads and role in heads:
            lines[role] = heads[CHILD[role]] - heads[role]
    for role, other in SAME_AS.items():
        if other in lines:
            lines[role] = lines[other]
    return lines


def world_of(arm, bone):
    return arm.matrix_world @ arm.data.bones[bone].matrix_local


def facing_check(name, rest_world):
    """Upright (the head over the hips) with its left at +X: a body that faces -Y. Not by the toes: a bind pose's foot
    can point nearly straight down (BIPER's StrafeWalk_R135 file, toes 4 mm ahead of the ankle)."""
    left = rest_world['LeftUpperLeg'].translation - rest_world['RightUpperLeg'].translation
    up = rest_world['Head'].translation - rest_world['Hips'].translation
    if left.x <= 0 or up.z <= 0:
        raise SystemExit(f'{name} does not stand facing -Y with its left at +X (left {left.x:+.3f}, up {up.z:+.3f}).')


def import_biper(clip):
    """A BIPER clip's skeleton standing up and playing it (its other objects and takes gone)."""
    scene = bpy.context.scene
    fps, base = scene.render.fps, scene.render.fps_base
    objects, actions = set(bpy.data.objects), set(bpy.data.actions)
    bpy.ops.import_scene.fbx(filepath=str(ROOT / (BIPER_DIR + clip + '.fbx')), ignore_leaf_bones=False,
                             automatic_bone_orientation=False)
    scene.render.fps, scene.render.fps_base = fps, base
    made = [o for o in bpy.data.objects if o not in objects]
    arm = max((o for o in made if o.type == 'ARMATURE'), key=lambda o: len(o.data.bones))
    new = [a for a in bpy.data.actions if a not in actions]
    # The take the importer gave the skeleton (named after the object, which a second import calls Armature.001).
    act = arm.animation_data.action if arm.animation_data and arm.animation_data.action in new else None
    act = act or next((a for a in new if a.name.startswith(arm.name + '|')), None)
    if act is None:
        raise SystemExit(f'{clip}: no take on its skeleton')
    for a in new:
        if a is not act:
            bpy.data.actions.remove(a)
    for obj in made:
        if obj is not arm:
            bpy.data.objects.remove(obj, do_unlink=True)
    act.name = 'BIPER · ' + clip
    act.use_fake_user = False
    arm.animation_data_create()
    arm.animation_data.action = act
    if getattr(act, 'slots', None) and hasattr(arm.animation_data, 'action_slot'):
        arm.animation_data.action_slot = act.slots[0]
    arm.rotation_mode = 'XYZ'
    arm.rotation_euler.x += math.pi
    bpy.context.view_layer.update()
    return arm, act


def sample(context, arm, act):
    """Every frame of the clip: each role's world matrix, both hands' finger ways and where its root bone is. The loop's
    last frame (its first again) is left out of a cycle by the caller."""
    scene = context.scene
    f0, f1 = (int(v) for v in act.frame_range)
    out = []
    for f in range(f0, f1 + 1):
        scene.frame_set(f)
        context.view_layer.update()
        ev = arm.evaluated_get(context.evaluated_depsgraph_get())
        root = ev.matrix_world @ ev.pose.bones['root'].head
        out.append(({r: ev.matrix_world @ ev.pose.bones[BIPER[r]].matrix for r in ROLES}, run.hand_ways(ev, 'biper'),
                    Vector((root.x, root.y, 0.0))))
    return out


class Source:
    """One BIPER clip retargeted: per frame, Kazusa's world turn of each role, BIPER's hips and feet (m), fingers.

    The body's travel is the feet's, not the file's root: BIPER's files carry their root ahead and back the wrong way
    round (Jog_Lfoot's root goes back 0.1 m a frame while the foot on the ground goes back 0.2 m: the jog runs backwards on
    its root; the side ways are right), which the game never reads (BiperRunner.clips.csv drops the root, inPlace 1, and
    GaitClips measures the feet). Here the root is taken off and the clip carried the way and as fast as its feet on the
    ground push back, as GaitClips measures it."""

    def __init__(self, context, rig, clip, cycle=True):
        self.clip = clip
        self.arm, self.act = import_biper(clip)
        # BIPER's own scale as imported (its centimetres), before a reference copy is sized to Kazusa.
        self.base_scale = self.arm.matrix_world.to_scale().x
        src_rest = {r: world_of(self.arm, BIPER[r]) for r in ROLES}
        facing_check('BIPER ' + clip, src_rest)
        kaz_rest = {r: world_of(rig, KAZUSA[r]) for r in ROLES}
        src_lines, kaz_lines = rest_lines(BIPER, src_rest), rest_lines(KAZUSA, kaz_rest)
        align = {r: (frame(src_lines[r]) @ frame(kaz_lines[r]).inverted()).to_quaternion() for r in ROLES}
        src_rest_q = {r: src_rest[r].to_3x3().normalized().to_quaternion() for r in ROLES}
        kaz_rest_q = {r: kaz_rest[r].to_3x3().normalized().to_quaternion() for r in ROLES}
        feet = ('LeftFoot', 'RightFoot', 'LeftToes', 'RightToes')
        src_floor = min(src_rest[r].translation.z for r in feet)
        kaz_floor = min(kaz_rest[r].translation.z for r in feet)
        self.ratio = (kaz_rest['Hips'].translation.z - kaz_floor) / (src_rest['Hips'].translation.z - src_floor)
        self.kaz_floor = kaz_floor
        self.src_ankle = src_rest['LeftFoot'].translation.z - src_floor
        self.kaz_ankle = kaz_rest['LeftFoot'].translation.z - kaz_floor
        rows = sample(context, self.arm, self.act)
        if cycle and len(rows) > 2:
            rows = rows[:-1]
        # Off the root: the hips and the feet where the clip has them over its root.
        self.rows = [(s, hands) for s, hands, _root in rows]
        self.hips_at = [s['Hips'].translation - root for s, _h, root in rows]
        self.feet_at = {side: [s[side + 'Foot'].translation - root for s, _h, root in rows] for side in ('Left', 'Right')}
        self.clip_floor = min(min(s[r].translation.z for r in feet) for s, _h, _r in rows)
        self.low = {side: min(p.z for p in self.feet_at[side]) for side in ('Left', 'Right')}
        # How far a frame carries the body: back along what the feet on the ground do under the hips (median).
        steps = []
        for side in ('Left', 'Right'):
            at = self.feet_at[side]
            for i in range(len(at) - 1):
                if at[i].z - self.low[side] <= DOWN_WITHIN and at[i + 1].z - self.low[side] <= DOWN_WITHIN:
                    step = (at[i + 1] - self.hips_at[i + 1]) - (at[i] - self.hips_at[i])
                    steps.append(Vector((step.x, step.y, 0.0)))
        if cycle and steps:
            xs, ys = sorted(v.x for v in steps), sorted(v.y for v in steps)
            self.step = -Vector((xs[len(xs) // 2], ys[len(ys) // 2], 0.0))
        else:
            self.step = Vector()
        self.way = self.step.normalized() if self.step.length > 1e-5 else FORWARD.copy()
        self.world_q = []
        for s, _hands in self.rows:
            q = {}
            for r in ROLES:
                delta = s[r].to_3x3().normalized().to_quaternion() @ src_rest_q[r].inverted()
                q[r] = delta @ align[r] @ kaz_rest_q[r]
            self.world_q.append(q)
    def at(self, i):
        """Frame i of the cycle, any whole number (before its first or past its last it goes round): the row, its hands,
        Kazusa's turns."""
        n = len(self.rows)
        s, hands = self.rows[i % n]
        return s, hands, self.world_q[i % n]

    def foot(self, i, side):
        return self.feet_at[side][i % len(self.rows)] + self.step * i

    def hips(self, i):
        return self.hips_at[i % len(self.rows)] + self.step * i

    def down(self, i, side):
        return self.feet_at[side][i % len(self.rows)].z - self.low[side] <= DOWN_WITHIN

    def reach(self, i, side, way):
        off = self.foot(i, side) - self.hips(i)
        return off.x * way.x + off.y * way.y


def planting_side(a, b):
    """The foot that plants: the one leading the old way out of a way to the side, else the one away from the new way."""
    old, new = tools.WAY_ANGLE[a], tools.WAY_ANGLE[b]
    if old not in (0, 180):
        return 'Left' if old < 0 else 'Right'
    return 'Right' if new < 0 else 'Left'


def plant_frame(source, side):
    """The cycle's frame that foot comes down furthest out along the way it goes."""
    frames = [i for i in range(len(source.rows)) if source.down(i, side)]
    return max(frames, key=lambda i: source.reach(i, side, source.way))


def push_frame(source, side):
    """The cycle's frame that foot is furthest behind along the way it goes, still down (about to push off)."""
    frames = [i for i in range(len(source.rows)) if source.down(i, side)]
    return min(frames, key=lambda i: source.reach(i, side, source.way))


def pose_from(world_q, hips_at, inv_world, rel, parent_of, center):
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


def build_draft(context, rig, key, gait, a, b, sources):
    """One pivot's draft: the old way's cycle to the plant, an empty span, the new way's cycle from the push."""
    before, gap, after = SPANS[gait]
    old, new = sources[tools.JOG[a] if gait == 'Jog' else tools.WALK[a]], sources[tools.JOG[b] if gait == 'Jog' else tools.WALK[b]]
    side = planting_side(a, b)
    plant, push = plant_frame(old, side), push_frame(new, side)
    # (draft frame, source, its frame, where that source's planting foot stood at the span's edge)
    frames = [(1 + k, old, plant - before + 1 + k, old.foot(plant, side)) for k in range(before)]
    frames += [(before + gap + 1 + k, new, push + k, new.foot(push, side)) for k in range(after)]

    bones = rig.data.bones
    inv_world = rig.matrix_world.inverted()
    rest_local = {r: bones[KAZUSA[r]].matrix_local.copy() for r in ROLES}
    parent_of = {r: bones[KAZUSA[r]].parent.name for r in ROLES}
    rel = {r: bones[parent_of[r]].matrix_local.inverted() @ rest_local[r] for r in ROLES}
    center = rig.pose.bones[parent_of['Hips']].matrix.copy()

    name = tools.PREFIX + key
    draft = bpy.data.actions.new(name + '_draft')
    draft.use_fake_user = True
    draft['enf_layer'] = 'draft'
    draft['enf_source'] = f'{old.clip} -> {new.clip} ({side} foot plants)'
    total = before + gap + after
    draft['enf_frames'] = total
    draft['enf_mark_들어옴'] = max(1, before - 3)
    draft['enf_mark_디딤'] = before
    draft['enf_mark_나감'] = before + gap + 4
    data = rig.animation_data or rig.animation_data_create()
    for track in list(data.nla_tracks):
        data.nla_tracks.remove(track)
    data.action = draft
    for limb in ('arm_L', 'arm_R', 'leg_L', 'leg_R'):
        rig['ik_' + limb] = 0.0
        rig.keyframe_insert(f'["ik_{limb}"]', frame=1, group='IK / FK mode')
    previous, hands = {}, []
    for at, source, i, anchor in frames:
        s, ways, world_q = source.at(i)
        hip = source.hips(i)
        # The planting foot at the origin at the span's edge: the body placed off it, scaled to Kazusa.
        off = (hip - anchor) * source.ratio
        hips_at = Vector((off.x, off.y, source.kaz_floor + (hip.z - source.clip_floor) * source.ratio))
        posed = pose_from(world_q, hips_at, inv_world, rel, parent_of, center)
        src_low = min(s['LeftFoot'].translation.z, s['RightFoot'].translation.z) - source.clip_floor - source.src_ankle
        kaz_low = min((rig.matrix_world @ posed[r][0]).translation.z for r in ('LeftFoot', 'RightFoot')) - source.kaz_floor - source.kaz_ankle
        hips_at.z += src_low * source.ratio - kaz_low
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
            pb.keyframe_insert('rotation_quaternion', frame=at, group=pb.name)
            if r == 'Hips':
                pb.location = basis.translation
                pb.keyframe_insert('location', frame=at, group=pb.name)
        hands.append((at, ways))
    run.key_fingers(rig, draft, hands)
    data.action = None
    print(f'  {key}: {old.clip} frame {plant} -> {new.clip} frame {push}, {side} foot, {total} frames', flush=True)
    return draft


def reference(context, source, role, slot, place, label):
    """BIPER's skeleton playing its clip over and over on its spot at Kazusa's size, for looking at only (never exported).
    On its spot: its root's travel is the wrong way round ahead and back (Source), and a loop that walked off would jump
    back each time round."""
    collection = bpy.data.collections.get(tools.REFERENCE_COLLECTION)
    if collection is None:
        collection = bpy.data.collections.new(tools.REFERENCE_COLLECTION)
        context.scene.collection.children.link(collection)
    arm = source.arm if slot is None or slot == 'from' else source.arm.copy()
    for owner in list(arm.users_collection):
        owner.objects.unlink(arm)
    collection.objects.link(arm)
    arm.name = f'BIPER · {source.clip}' + (f' ({slot})' if slot else '')
    arm['enf_reference'] = source.clip
    arm['enf_role'] = role
    if slot:
        arm['enf_slot'] = slot
    arm.data.display_type = 'STICK'
    arm.show_in_front = True
    scale = source.ratio * source.base_scale
    arm.scale = (scale, scale, scale)
    arm.location = Vector((0, 0, source.kaz_floor - source.clip_floor * source.ratio))
    arm.hide_select = True
    act = bpy.data.actions.get(source.act.name + ' (제자리)')
    if act is None:
        act = source.act.copy()
        act.name = source.act.name + ' (제자리)'
        bag = run.one_click().channelbag(act)
        if bag is not None:
            for curve in [c for c in bag.fcurves if c.data_path == 'pose.bones["root"].location']:
                bag.fcurves.remove(curve)
    data = arm.animation_data or arm.animation_data_create()
    data.action = None
    for track in list(data.nla_tracks):
        data.nla_tracks.remove(track)
    track = data.nla_tracks.new()
    track.name = '레퍼런스'
    strip = track.strips.new(act.name, 1, act)
    if hasattr(strip, 'action_slot') and strip.action_slot is None and len(getattr(act, 'slots', [])):
        strip.action_slot = act.slots[0]
    strip.repeat = 40.0
    # Its hips over the spot on its first frame.
    context.scene.frame_set(1)
    context.view_layer.update()
    ev = arm.evaluated_get(context.evaluated_depsgraph_get())
    hips = ev.matrix_world @ ev.pose.bones['pelvis'].head
    arm.location += Vector((place.x - hips.x, place.y - hips.y, 0.0))
    text = bpy.data.curves.new(arm.name + ' 글자', 'FONT')
    text.body = label
    text.size = .12
    text.align_x = 'CENTER'
    sign = bpy.data.objects.new(arm.name + ' 글자', text)
    collection.objects.link(sign)
    sign.location = place + Vector((0, 0, source.kaz_floor + 1.95))
    sign.rotation_euler = (math.pi / 2, 0, 0)
    sign['enf_reference'] = source.clip
    sign['enf_role'] = role
    if slot:
        sign['enf_slot'] = slot
    sign.hide_select = True
    return arm


def main():
    out, force = args()
    if out.exists() and not force:
        raise SystemExit(f'{out} exists; add --force to build it again.')
    context = bpy.context
    scene = context.scene
    scene.render.fps, scene.render.fps_base = 30, 1.0
    rig = run.find_rig(scene)
    if rig is None:
        raise SystemExit('Open ArtSource/TrueFPS/ENFILADE_TrueFPS_Start.blend first.')
    hidden = [o for o in scene.objects if o.type == 'MESH' and not o.hide_viewport]
    for o in hidden:
        o.hide_viewport = True
    facing_check('KazusaBandRig', {r: world_of(rig, KAZUSA[r]) for r in ROLES})
    clips = sorted({p[3] for p in tools.PIVOTS} | {p[4] for p in tools.PIVOTS})
    sources = {}
    for clip in clips:
        print('Retargeting', clip, flush=True)
        sources[clip] = Source(context, rig, clip)
    for key, _label, _step, _a_clip, _b_clip, gait, a, b in tools.PIVOTS:
        build_draft(context, rig, key, gait, a, b, sources)
    # BIPER's own: each cycle twice (where the pivot shown comes from, where it goes), and the other clips in a row.
    for clip, source in sources.items():
        reference(context, source, 'cycle', 'from', SLOTS['from'], f'{clip}  (from)')
        reference(context, source, 'cycle', 'to', SLOTS['to'], f'{clip}  (to)')
    count = len(tools.EXTRA_REFERENCES)
    for n, (clip, _label) in enumerate(tools.EXTRA_REFERENCES):
        print('Reference', clip, flush=True)
        source = Source(context, rig, clip, cycle=False)
        x = (n - (count - 1) / 2) * EXTRA_STEP
        reference(context, source, 'extra', None, Vector((x, EXTRA_ROW_Y, 0.0)), clip)
    for o in hidden:
        o.hide_viewport = False
    scene['enf_pivot'] = tools.PIVOTS[0][0]
    scene.frame_start, scene.frame_end = 1, int(bpy.data.actions[tools.PREFIX + tools.PIVOTS[0][0] + '_draft']['enf_frames'])
    text = bpy.data.texts.get('START HERE - Pivot tools.py') or bpy.data.texts.new('START HERE - Pivot tools.py')
    text.from_string((HERE / 'pivot_tools.py').read_text(encoding='utf-8'))
    out.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(out), copy=False, compress=True)
    print('PIVOT WORKSPACE', out, flush=True)


main()
