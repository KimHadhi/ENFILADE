"""First drafts of the paired executions (Blender 4.5 LTS), for build_execution_workspace.py and the 처형 tab.

Each pair is two clips on one shared root: the zombie's root (its feet, facing -Y in this file, its left +X) and Kazusa's
rig placed beside it, both at the game's size (this file is in the game's metres: the Synty zombie as it is, Kazusa's rig
object scaled by GAME_SCALE, as PlayerBody fits her eyes to the 1.6 m camera). The game puts the zombie's root where the
zombie stands and the player's root at the pair's offset from it (Tables/Executions.csv, written by the export).

Drafts are key poses, the action the user edits (so the Dope Sheet shows the draft's own keys, a column per pose):
- the zombie in FK, from the first frame of the clip it stands, sits, eats, crouches or lies in (the pack's own pose
  clips, so the pair starts where the zombie already is), turned by body-axis deltas at its key frames, eased between
  them as the choreography says, with a shudder where it struggles as noise on those bones (Graph Editor, modifiers);
- Kazusa with both arms and legs on the True FPS rig's IK controls (ik_* = 1), the hips, spine, neck and head in FK, and
  the fingers curled: the right fist round the knife, the left hand open or gripping; every part keyed at every key
  frame of hers.
Contact is a constraint, so it stays as the zombie is changed: while a hand holds a zombie bone (Hold) its control
copies a hold point (an empty on that bone, where the hand took hold), the constraint's influence keyed 1 for the hold;
the knife's tip is put on the zombie's neck or skull at the kill frame and held there the same way.
"""
import json
import math
import re
from pathlib import Path

from mathutils import Matrix, Quaternion, Vector

# Kazusa in the game is this many times her size in her own file (eyes 1.37 there, 1.6 m in game: PlayerBody's fit);
# shove_tools uses the same number.
GAME_SCALE = 1.166
FPS = 30
PACK = 'Assets/ThirdParty/Nephilite Studios/Zombie Animation Set/Animations/In Place/'

# The zombie's body axes in this file: it faces -Y, its left is +X, up is +Z.
Z_LEFT, Z_FORWARD, Z_UP = Vector((1.0, 0.0, 0.0)), Vector((0.0, -1.0, 0.0)), Vector((0.0, 0.0, 1.0))

K_CHAIN = ('Spine', 'Chest', 'Upper Chest', 'Neck', 'HeadBone')
K_SPINE_SHARE = {'Spine': .3, 'Chest': .35, 'Upper Chest': .35}
K_HEAD_SHARE = {'Neck': .4, 'HeadBone': .6}
K_FINGERS = ('Index', 'Middle', 'Ring', 'Little')
# Kazusa's channels in the Dope Sheet, the controls the user moves first, then the body top down, the fingers last.
K_ORDER = ('CTRL_hand.L', 'CTRL_hand.R', 'CTRL_foot.L', 'CTRL_foot.R', 'Hips') + K_CHAIN + (
    'CTRL_elbow.L', 'CTRL_elbow.R', 'CTRL_knee.L', 'CTRL_knee.R')

# A hand's hold of the zombie: this constraint on its IK control copies the hold point (execution_tools.HOLD_POINTS).
HOLD_CON = '잡기'
HOLD_GROUP = '잡기 (손이 좀비에 붙음)'
IK_GROUP = 'IK / FK mode'

# How the way into a key eases, as Blender keys it: (interpolation, easing) by the kind of the key a stretch ends on.
EASES = {'smooth': ('BEZIER', 'AUTO'), 'linear': ('LINEAR', 'AUTO'), 'in': ('QUAD', 'EASE_IN'),
         'out': ('QUAD', 'EASE_OUT'), 'snap': ('CUBIC', 'EASE_OUT')}


def zv(right, forward, up):
    """A point or direction in the zombie's frame (game metres: right, forward, up) in this file's space."""
    return Z_LEFT * -right + Z_FORWARD * forward + Z_UP * up


def _turn(left, forward, up, pitch, yaw, roll):
    """A rotation about body axes in degrees: pitch + bends the top forward, yaw + turns left, roll + tips the top right."""
    return (Quaternion(up, math.radians(yaw)) @ Quaternion(left, math.radians(pitch))
            @ Quaternion(forward, math.radians(roll)))


def ease(t, kind='smooth'):
    t = min(1.0, max(0.0, t))
    if kind == 'linear':
        return t
    if kind == 'in':
        return t * t
    if kind == 'out':
        return 1.0 - (1.0 - t) * (1.0 - t)
    if kind == 'snap':
        # Fast out of the start, settling into the key: a blow.
        return 1.0 - (1.0 - t) ** 3
    return t * t * (3.0 - 2.0 * t)


def _rotation(matrix):
    return matrix.to_3x3().normalized().to_quaternion()


def _around(keys, frame):
    """The keys either side of a frame and how far between them, eased by the later key's ease."""
    before = keys[0]
    for key in keys:
        if key[0] <= frame:
            before = key
    after = next((key for key in keys if key[0] >= frame), keys[-1])
    if after[0] == before[0]:
        return before, after, 0.0
    return before, after, ease((frame - before[0]) / (after[0] - before[0]), after[2])


# ---- Keys as the user sees them -------------------------------------------------------------------------------------

def curves(action):
    """An action's F-curves, legacy or layered (Blender 4.4+)."""
    if action is None:
        return []
    if getattr(action, 'is_action_legacy', True) and hasattr(action, 'fcurves'):
        try:
            return list(action.fcurves)
        except (AttributeError, RuntimeError):
            pass
    out = []
    for layer in getattr(action, 'layers', []):
        for strip in layer.strips:
            for bag in getattr(strip, 'channelbags', []):
                out.extend(bag.fcurves)
    return out


def _channels(action):
    """Where an action's channel groups live: its first channelbag (layered), or the action itself (legacy)."""
    for layer in getattr(action, 'layers', []):
        for strip in layer.strips:
            for bag in getattr(strip, 'channelbags', []):
                return bag
    return action if hasattr(action, 'groups') else None


def hold_path(side):
    return f'pose.bones["CTRL_hand.{side}"].constraints["{HOLD_CON}"].influence'


def apply_eases(action, kinds):
    """Every key eases into the next one as the choreography's key there says (kinds: {frame: kind}; a key the
    choreography doesn't name eases smoothly), with handles that never overshoot."""
    for curve in curves(action):
        points = curve.keyframe_points
        for i, point in enumerate(points):
            after = int(round(points[i + 1].co.x)) if i + 1 < len(points) else None
            interpolation, easing = EASES.get(kinds.get(after, 'smooth'), EASES['smooth'])
            point.interpolation = interpolation
            point.easing = easing
            point.handle_left_type = point.handle_right_type = 'AUTO_CLAMPED'
        curve.update()


def group_channels(action, order=()):
    """One channel group per bone (closed, so the Dope Sheet shows a row per bone), in the given order, then the holds
    and the IK/FK switches."""
    bag = _channels(action)
    if bag is None:
        return
    rank = {name: i for i, name in enumerate(order)}

    def group_of(path):
        if '.constraints[' in path:
            return HOLD_GROUP
        found = re.match(r'pose\.bones\["(.+?)"\]', path)
        if found:
            return found.group(1)
        return IK_GROUP if path.startswith('["ik_') else 'Other'

    every = curves(action)
    names = sorted({group_of(c.data_path) for c in every},
                   key=lambda n: (n in (HOLD_GROUP, IK_GROUP, 'Other'), rank.get(n, len(rank)), n))
    groups = {g.name: g for g in bag.groups}
    for name in names:
        if name not in groups:
            groups[name] = bag.groups.new(name)
    for curve in every:
        group = groups[group_of(curve.data_path)]
        if curve.group != group:
            curve.group = group
    for group in bag.groups:
        group.show_expanded = False
        group.show_expanded_graph = True


# ---- The zombie ---------------------------------------------------------------------------------------------------

class ZombieDraft:
    """The zombie's keys: a start pose from a clip, then key poses as body-axis turns of bones (each turning everything
    below it) and a move of the hips, eased between, with shudders on top."""

    def __init__(self, context, zt, rig, clip, at=0.0):
        self.context, self.zt, self.rig = context, zt, rig
        order, frames = zt.sample_clip(context, rig, clip, in_place=True)
        index = min(len(frames) - 1, max(0, int(round(at * FPS))))
        self.order = order
        self.base_rotations, self.base_hips = frames[index]
        self.keys = []
        self.shakes = []
        world = rig.matrix_world
        self.to_arm = world.inverted().to_3x3()
        self.arm_rot = _rotation(world)
        children = {n: [] for n in order}
        for name in order:
            bone = rig.data.bones[name]
            if bone.parent is not None and bone.parent.name in children:
                children[bone.parent.name].append(name)
        self.below = {}
        for name in reversed(order):
            self.below[name] = [name] + [d for c in children[name] for d in self.below[c]]

    def key(self, frame, turns=None, hips=(0.0, 0.0, 0.0), kind='smooth'):
        """A key pose: turns {bone: (pitch, yaw, roll)}, hips moved (right, forward, up) in metres."""
        self.keys.append((frame, dict(turns or {}), kind, hips))
        self.keys.sort(key=lambda k: k[0])

    def shake(self, first, last, bones, degrees, hz, seed=0):
        """A shudder from first to last: those bones (and what hangs below) jitter by up to degrees."""
        self.shakes.append((first, last, tuple(bones), degrees, hz, seed))

    def _arm_turn(self, pitch, yaw, roll):
        world = _turn(Z_LEFT, Z_FORWARD, Z_UP, pitch, yaw, roll)
        return self.arm_rot.inverted() @ world @ self.arm_rot

    def _pose(self, turns, hips):
        rotations = {n: q.copy() for n, q in self.base_rotations.items()}
        for name in self.order:
            if name in turns:
                turn = self._arm_turn(*turns[name])
                for below in self.below[name]:
                    if below in rotations:
                        rotations[below] = turn @ rotations[below]
        moved = self.base_hips + self.to_arm @ zv(*hips)
        return rotations, moved

    def pose_at(self, frame):
        """The pose at a frame between the key poses, eased as the later one says (shudders not included)."""
        before, after, t = _around(self.keys, frame)
        rot_a, hips_a = self._pose(before[1], before[3])
        rot_b, hips_b = self._pose(after[1], after[3])
        rotations = {}
        for name, qa in rot_a.items():
            qb = rot_b[name]
            if qa.dot(qb) < 0:
                qb = -qb
            rotations[name] = qa.slerp(qb, t)
        return rotations, hips_a.lerp(hips_b, t)

    def write(self, action, count):
        """The key poses as keys (every bone at each of the zombie's key frames), eased into one another as the
        choreography says, and the shudders as noise on the bones that shake."""
        zt = self.zt
        frames = sorted({k[0] for k in self.keys if 1 <= k[0] <= count} | {1, count})
        per_frame = [zt.solve_bases(self.rig, self.order, *self.pose_at(frame)) for frame in frames]
        self.rig.animation_data_create()
        zt.write_keys(self.rig, action, frames, zt.bases_to_channels(self.order, per_frame))
        apply_eases(action, {k[0]: k[2] for k in self.keys})
        self._shudders(action)
        # Every limb in FK, keyed, so picking this pair always plays the bones themselves.
        data = self.rig.animation_data or self.rig.animation_data_create()
        keep = data.action
        data.action = action
        for prop in [k for k in self.rig.keys() if k.startswith('ik_')]:
            self.rig[prop] = 0.0
            self.rig.keyframe_insert(f'["{prop}"]', frame=1, group=IK_GROUP)
        data.action = keep
        group_channels(action, self.order)

    def _shudders(self, action):
        """Each shudder as noise on its bones' turns (a turn there carries everything below it), faded in and out over
        its frames: the Graph Editor shows it as a modifier on those channels, to soften, move or delete."""
        found = {(c.data_path, c.array_index): c for c in curves(action)}
        for first, last, bones, degrees, hz, seed in self.shakes:
            span = max(2, last - first)
            for i, name in enumerate(bones):
                for axis, share in ((1, 1.0), (2, .6), (3, .5)):
                    curve = found.get((f'pose.bones["{name}"].rotation_quaternion', axis))
                    if curve is None:
                        continue
                    noise = curve.modifiers.new('NOISE')
                    # A quaternion's x, y, z hold half the angle: up to `degrees` either way.
                    noise.strength = math.radians(degrees) * share
                    noise.scale = max(1.0, FPS / max(.5, hz) * .5)
                    noise.phase = seed * 1.7 + i * 2.3 + axis * 1.1
                    noise.use_restricted_range = True
                    noise.frame_start, noise.frame_end = float(first), float(last)
                    noise.blend_in = noise.blend_out = span * .45


# ---- Kazusa -------------------------------------------------------------------------------------------------------

class Palm:
    """The left hand open toward something: its palm's middle at point, the palm facing palm_normal, the fingers along
    fingers (all in this file's space)."""

    def __init__(self, point, palm_normal, fingers, curl=.15):
        self.point, self.normal, self.fingers, self.curl = Vector(point), Vector(palm_normal), Vector(fingers), curl


class Knife:
    """The right fist round the knife: its tip at point, its blade along blade, its edge (the knife's width) along edge."""

    def __init__(self, point, blade, edge):
        self.point, self.blade, self.edge = Vector(point), Vector(blade), Vector(edge)


class Hold:
    """The hand takes hold of a zombie bone at this key's frame, placed there as `at` (a Palm or a Knife) puts it, and
    from then on moves as that bone moves (contact). A later Hold of the same bone with no `at` keeps the hold to its own
    frame; the next key that isn't a Hold lets go, easing out from where the hold has the hand."""

    def __init__(self, bone, at=None):
        self.bone, self.at = bone, at


class Seat:
    """Where the knife sits in Kazusa's right fist (Tables/Executions.csv, row Knife; the game seats it the same way on any
    character): the fist's middle along the hand (gripAlong, times the wrist-to-knuckle length) and out of the palm
    (gripPalm, metres), the blade toward the little finger tilted toward the fingers (tilt, degrees), the grip that far
    behind the model's middle (knifeGrip, metres)."""

    def __init__(self, along=1.05, palm=.022, tilt=12.0, grip=-.05, tip=.1056):
        self.along, self.palm, self.tilt, self.grip, self.tip = along, palm, tilt, grip, tip


class PlayerDraft:
    """Kazusa's keys: per key frame the hips (a point in the zombie's frame and a turn in her own), the spine's and the
    head's turns, each hand (Palm, Knife or Hold), the elbows' and knees' pole offsets, the feet and the finger curls;
    eased between, then keyed on her controls and FK bones every frame."""

    def __init__(self, context, rig, zombie, seat):
        self.context, self.rig, self.zombie, self.seat = context, rig, zombie, seat
        self.keys = []
        bones = rig.data.bones
        self.rest = {b.name: b.matrix_local.copy() for b in bones}
        self.side = {}
        for side in ('L', 'R'):
            wrist = self.rest['Left wrist' if side == 'L' else 'Right wrist'].translation
            index = self.rest['IndexFinger1_' + side].translation
            middle = self.rest['MiddleFinger1_' + side].translation
            little = self.rest['LittleFinger1_' + side].translation
            along = (middle - wrist)
            length = along.length
            along.normalize()
            out = index - little
            out = (out - along * out.dot(along)).normalized()
            # Out of the palm: the mirror of each other (in Blender's right-handed space).
            palm = along.cross(out) if side == 'L' else out.cross(along)
            self.side[side] = {'wrist': wrist.copy(), 'along': along, 'out': out, 'palm': palm.normalized(), 'length': length,
                               'rest': _rotation(self.rest['Left wrist' if side == 'L' else 'Right wrist'])}

    def key(self, frame, kind='smooth', **parts):
        self.keys.append((frame, parts, kind))
        self.keys.sort(key=lambda k: k[0])

    # The rig's own frame in this file: her root, turned to face her way, at her size.
    def _axes(self):
        rot = _rotation(self.rig.matrix_world)
        return rot @ Vector((1.0, 0.0, 0.0)), rot @ Vector((0.0, -1.0, 0.0)), Vector((0.0, 0.0, 1.0))

    def _to_arm(self, point):
        return self.rig.matrix_world.inverted() @ point

    def _dir_to_arm(self, direction):
        return (_rotation(self.rig.matrix_world).inverted() @ direction).normalized()

    def _hand_basis(self, side):
        s = self.side[side]
        return Matrix((s['along'], s['palm'], s['along'].cross(s['palm']))).transposed()

    def wrist_for(self, side, target):
        """The wrist's armature-space matrix that puts the hand where a Palm or Knife wants it."""
        s = self.side[side]
        if isinstance(target, Palm):
            along = self._dir_to_arm(target.fingers)
            palm = self._dir_to_arm(target.normal)
            palm = (palm - along * palm.dot(along)).normalized()
            rest_point = s['wrist'] + s['along'] * s['length'] * .6 + s['palm'] * .012
            point = self._to_arm(target.point)
        else:
            seat = self.seat
            blade_rest = (-s['out'] * math.cos(math.radians(seat.tilt)) + s['along'] * math.sin(math.radians(seat.tilt))).normalized()
            edge_rest = s['along'] - blade_rest * s['along'].dot(blade_rest)
            edge_rest.normalize()
            blade = self._dir_to_arm(target.blade)
            edge = self._dir_to_arm(target.edge)
            edge = (edge - blade * edge.dot(blade)).normalized()
            # The hand turned so the blade and edge go where wanted: the rotation from the rest knife frame.
            rest_frame = Matrix((blade_rest, edge_rest, blade_rest.cross(edge_rest))).transposed()
            want_frame = Matrix((blade, edge, blade.cross(edge))).transposed()
            turn = (want_frame @ rest_frame.transposed()).to_quaternion()
            grip = s['wrist'] + s['along'] * s['length'] * seat.along + s['palm'] * seat.palm / GAME_SCALE
            tip_rest = grip + blade_rest * (seat.tip - seat.grip) / GAME_SCALE
            point = self._to_arm(target.point)
            wrist = point - turn @ (tip_rest - s['wrist'])
            return Matrix.Translation(wrist) @ (turn @ s['rest']).to_matrix().to_4x4()
        want = Matrix((along, palm, along.cross(palm))).transposed()
        turn = (want @ self._hand_basis(side).transposed()).to_quaternion()
        wrist = point - turn @ (rest_point - s['wrist'])
        return Matrix.Translation(wrist) @ (turn @ s['rest']).to_matrix().to_4x4()

    def knife_matrix(self, side='R'):
        """The knife's grip frame in the rig's armature space at rest (for the preview model on the right wrist)."""
        s = self.side[side]
        seat = self.seat
        blade = (-s['out'] * math.cos(math.radians(seat.tilt)) + s['along'] * math.sin(math.radians(seat.tilt))).normalized()
        edge = (s['along'] - blade * s['along'].dot(blade)).normalized()
        grip = s['wrist'] + s['along'] * s['length'] * seat.along + s['palm'] * seat.palm / GAME_SCALE
        return grip, blade, edge

    def _value(self, part, frame, default):
        """A part's value at a frame, eased between the keys that set it (a key without it keeps the one before)."""
        keyed = [(k[0], k[1][part], k[2]) for k in self.keys if part in k[1]]
        if not keyed:
            return default, default, 0.0, None, None
        before, after, t = _around(keyed, frame)
        return before[1], after[1], t, before, after

    def _lerp(self, part, frame, default):
        a, b, t, *_ = self._value(part, frame, default)
        if isinstance(a, (int, float)):
            return a + (b - a) * t
        return Vector(a).lerp(Vector(b), t)

    def hand_track(self, side, count, zombie_frames):
        """Every frame's wrist matrix: eased between Palm/Knife keys; from a Hold key on, the hand rides its bone."""
        part = 'lhand' if side == 'L' else 'rhand'
        keyed = [(k[0], k[1][part], k[2]) for k in self.keys if part in k[1]]
        if not keyed:
            return [None] * count
        mats = {}
        out = []
        for frame in range(1, count + 1):
            before, after, t = _around(keyed, frame)
            a = self._resolve(side, before, mats, zombie_frames, frame)
            b = self._resolve(side, after, mats, zombie_frames, frame)
            if after[0] == before[0] or isinstance(after[1], Hold) and isinstance(before[1], Hold):
                out.append(a)
                continue
            if isinstance(before[1], Hold) and not isinstance(after[1], Hold):
                # Letting go: from where the hold has it now.
                a = self._held(side, before, mats, zombie_frames, frame)
            qa, qb = a.to_quaternion(), b.to_quaternion()
            if qa.dot(qb) < 0:
                qb = -qb
            out.append(Matrix.Translation(a.translation.lerp(b.translation, t)) @ qa.slerp(qb, t).to_matrix().to_4x4())
        return out

    def _resolve(self, side, key, mats, zombie_frames, frame):
        if isinstance(key[1], Hold):
            return self._held(side, key, mats, zombie_frames, frame)
        if key[0] not in mats:
            mats[key[0]] = self.wrist_for(side, key[1])
        return mats[key[0]]

    def _held(self, side, key, mats, zombie_frames, frame):
        """The hand as it held the bone at the grab, carried with the bone to this frame."""
        part = 'lhand' if side == 'L' else 'rhand'
        bone = key[1].bone
        # A run of Holds of the same bone is one hold, from the first of them: walk back while the keys still hold it.
        mine = [k for k in self.keys if part in k[1]]
        index = next(i for i, k in enumerate(mine) if k[0] == key[0])
        while index > 0 and isinstance(mine[index - 1][1][part], Hold) and mine[index - 1][1][part].bone == bone:
            index -= 1
        start = mine[index]
        grab = start[0]
        if grab not in mats:
            mats[grab] = self.wrist_for(side, start[1][part].at)
        at_grab = zombie_frames[grab - 1][bone]
        now = zombie_frames[min(frame, len(zombie_frames)) - 1][bone]
        world_grab = self.rig.matrix_world @ mats[grab]
        carried = now @ at_grab.inverted() @ world_grab
        return self.rig.matrix_world.inverted() @ carried

    def holds(self, count):
        """Each hand's hold: {side: [bone, first frame, last frame]}, the frames in which it rides the bone whole."""
        out = {}
        for side, part in (('L', 'lhand'), ('R', 'rhand')):
            keyed = [(k[0], k[1][part]) for k in self.keys if part in k[1]]
            first = next((f for f, v in keyed if isinstance(v, Hold)), None)
            if first is None:
                continue
            bone = next(v.bone for f, v in keyed if f == first)
            last = first
            for f, v in keyed:
                if f > first:
                    if not isinstance(v, Hold) or v.bone != bone:
                        break
                    last = f
            out[side] = [bone, first, min(last, count)]
        return out

    def write(self, action, count, zombie_frames):
        """Every part keyed at each of her key frames (the poses the choreography gives, eased into one another as it
        says) and each hand's hold as its 잡기 constraint's influence, 1 while the hand rides the zombie bone. Returns the
        holds: {side: (bone, the hand control's matrix from that bone's frame at the grab)}, for the hold points."""
        rig = self.rig
        data = rig.animation_data or rig.animation_data_create()
        data.action = action
        self._last = {}
        for limb in ('arm_L', 'arm_R', 'leg_L', 'leg_R'):
            rig['ik_' + limb] = 1.0
            rig.keyframe_insert(f'["ik_{limb}"]', frame=1, group=IK_GROUP)
        tracks = {side: self.hand_track(side, count, zombie_frames) for side in ('L', 'R')}
        # Each part keyed where the choreography sets it, so it eases between its own keys as the choreography says (a
        # key of another part in between would bend its way); a pole also where its limb's root moves.
        def own(*parts):
            return {k[0] for k in self.keys if 1 <= k[0] <= count and any(part in k[1] for part in parts)}
        chain = own('hips', 'hipsTurn', 'spine', 'head') | {1}
        sets = {'chain': chain, 'fingers.L': own('lcurl') | {1}, 'fingers.R': own('rcurl') | {1}}
        for side, s in (('L', 'l'), ('R', 'r')):
            sets['hand.' + side] = own(s + 'hand')
            sets['elbow.' + side] = own(s + 'elbow') | chain
            sets['foot.' + side] = own(s + 'foot')
            sets['knee.' + side] = (own(s + 'knee', s + 'foot') | chain) if sets['foot.' + side] else set()
        for frame in sorted(set().union(*sets.values())):
            self._key_pose(frame, tracks, {name for name, frames in sets.items() if frame in frames})
        holds = self._key_holds(count, tracks, zombie_frames)
        apply_eases(action, {k[0]: k[2] for k in self.keys})
        # A hand takes hold at its grab key, not before: the influence steps from its key before (where the hand is
        # still its own, swinging in) to 1 at the grab, where the hand already is on the hold point.
        for curve in curves(action):
            if curve.data_path in (hold_path('L'), hold_path('R')):
                points = curve.keyframe_points
                for i, point in enumerate(points[:-1]):
                    if point.co.y < .5 and points[i + 1].co.y >= .5:
                        point.interpolation = 'CONSTANT'
                curve.update()
        group_channels(action, K_ORDER)
        data.action = None
        return holds

    def _key_holds(self, count, tracks, zombie_frames):
        """Each hand's 잡기 influence: 0 at its key before the grab, 1 from the grab to the hold's last key, 0 at its next
        key (it lets go from where the hold has it); 0 throughout for a hand that holds nothing."""
        out = {}
        found = self.holds(count)
        for side, part in (('L', 'lhand'), ('R', 'rhand')):
            con = self.rig.pose.bones[f'CTRL_hand.{side}'].constraints.get(HOLD_CON)
            if con is None:
                continue
            hold = found.get(side)
            keys = []
            if hold is None:
                keys = [(1, 0.0)]
            else:
                bone, first, last = hold
                own = sorted(k[0] for k in self.keys if part in k[1])
                before = max((f for f in own if f < first), default=None)
                after = min((f for f in own if f > last), default=None)
                keys = ([(before, 0.0)] if before is not None else []) + [(first, 1.0), (last, 1.0)]
                keys += [(after, 0.0)] if after is not None else []
                # Where the hand took hold: its control then, from the bone's own frame then.
                world = self.rig.matrix_world @ tracks[side][first - 1]
                out[side] = (bone, zombie_frames[first - 1][bone].inverted() @ world)
            for frame, value in keys:
                con.influence = value
                con.keyframe_insert('influence', frame=frame, group=HOLD_GROUP)
            con.influence = 0.0
        return out

    def _key_quat(self, pb, quat, frame):
        """A bone's turn keyed on the same side of the quaternion sphere as its key before, so the way between keys is
        the short one."""
        last = self._last.get(pb.name)
        if last is not None and last.dot(quat) < 0:
            quat = -quat
        self._last[pb.name] = quat.copy()
        pb.rotation_mode = 'QUATERNION'
        pb.rotation_quaternion = quat
        pb.keyframe_insert('rotation_quaternion', frame=frame, group=pb.name)

    def _key_pose(self, frame, tracks, what):
        """Her parts named in `what` keyed at one frame: the chain (hips, spine, neck and head in FK), each hand, elbow
        pole, foot and knee pole on the IK controls, each hand's fingers."""
        rig = self.rig
        left_axis, forward_axis, up_axis = self._axes()
        pose = rig.pose.bones
        # Hips: a point in the zombie's frame and a turn about her own axes; the chain turns on top, parents first.
        hips_at = self._to_arm(zv(*self._lerp('hips', frame, (0.0, 0.0, 1.12))))
        hips_turn = self._lerp('hipsTurn', frame, (0.0, 0.0, 0.0))
        spine = self._lerp('spine', frame, (0.0, 0.0, 0.0))
        head = self._lerp('head', frame, (0.0, 0.0, 0.0))
        arm_rot = _rotation(rig.matrix_world).inverted()
        world_rot = _rotation(rig.matrix_world)

        def arm_turn(p, y, r):
            return arm_rot @ _turn(left_axis, forward_axis, up_axis, p, y, r) @ world_rot

        posed = {'Center': self.rest['Center'].copy()}
        turn_hips = arm_turn(*hips_turn)
        posed['Hips'] = Matrix.Translation(hips_at) @ (turn_hips @ _rotation(self.rest['Hips'])).to_matrix().to_4x4()
        cumulative = turn_hips
        for name in K_CHAIN:
            share = K_SPINE_SHARE.get(name)
            amount = spine * share if share is not None else head * K_HEAD_SHARE[name]
            cumulative = arm_turn(*amount) @ cumulative
            parent = rig.data.bones[name].parent.name
            head_at = posed[parent] @ (self.rest[parent].inverted() @ self.rest[name]).translation
            posed[name] = Matrix.Translation(head_at) @ (cumulative @ _rotation(self.rest[name])).to_matrix().to_4x4()
        if 'chain' in what:
            for name in ('Hips',) + K_CHAIN:
                bone = rig.data.bones[name]
                parent = bone.parent.name
                basis = bone.convert_local_to_pose(posed[name], bone.matrix_local, parent_matrix=posed[parent],
                                                   parent_matrix_local=self.rest[parent], invert=True)
                loc, quat, _ = basis.decompose()
                pb = pose[name]
                self._key_quat(pb, quat, frame)
                if name == 'Hips':
                    pb.location = loc
                    pb.keyframe_insert('location', frame=frame, group=name)
        for side in ('L', 'R'):
            sign = 1.0 if side == 'L' else -1.0
            wrist = tracks[side][frame - 1]
            if wrist is not None and 'hand.' + side in what:
                self._control(f'CTRL_hand.{side}', wrist, frame)
            if 'elbow.' + side in what:
                # The elbow's pole: behind, below and out from the shoulder, in her own frame.
                out, back, down = self._lerp('lelbow' if side == 'L' else 'relbow', frame, (.25, .35, .3))
                arm = 'Left arm' if side == 'L' else 'Right arm'
                shoulder = posed['Upper Chest'] @ (self.rest['Upper Chest'].inverted() @ self.rest[arm]).translation
                pole_world = (rig.matrix_world @ shoulder) + left_axis * out * sign - forward_axis * back - up_axis * down
                self._control(f'CTRL_elbow.{side}', Matrix.Translation(self._to_arm(pole_world))
                              @ self.rest[f'CTRL_elbow.{side}'].to_3x3().to_4x4(), frame, location_only=True)
            foot = self._lerp('lfoot' if side == 'L' else 'rfoot', frame, None)
            if foot is not None:
                x, y, z, yaw, toes = foot
                if 'foot.' + side in what:
                    turn = arm_turn(0.0, yaw, 0.0) @ arm_turn(toes, 0.0, 0.0)
                    at = self._to_arm(zv(x, y, z))
                    rest = self.rest[f'CTRL_foot.{side}']
                    self._control(f'CTRL_foot.{side}', Matrix.Translation(at) @ (turn @ _rotation(rest)).to_matrix().to_4x4(), frame)
                if 'knee.' + side in what:
                    fwd, sideways, up = self._lerp('lknee' if side == 'L' else 'rknee', frame, (.5, 0.0, 0.0))
                    knee = (rig.matrix_world @ posed['Hips'].translation).lerp(zv(x, y, z), .5)
                    pole_world = knee + forward_axis * fwd + left_axis * sideways * sign + up_axis * up
                    self._control(f'CTRL_knee.{side}', Matrix.Translation(self._to_arm(pole_world))
                                  @ self.rest[f'CTRL_knee.{side}'].to_3x3().to_4x4(), frame, location_only=True)
            if 'fingers.' + side in what:
                self._fingers(side, self._lerp('lcurl' if side == 'L' else 'rcurl', frame, .9 if side == 'R' else .2), frame)

    def _control(self, name, matrix, frame, location_only=False):
        rig = self.rig
        pb = rig.pose.bones[name]
        bone = pb.bone
        parent = bone.parent.name if bone.parent else None
        kwargs = {'parent_matrix': self.rest[parent], 'parent_matrix_local': self.rest[parent]} if parent else {}
        basis = bone.convert_local_to_pose(matrix, bone.matrix_local, invert=True, **kwargs)
        loc, quat, _ = basis.decompose()
        pb.rotation_mode = 'QUATERNION'
        pb.location = loc
        pb.keyframe_insert('location', frame=frame, group=name)
        if not location_only:
            self._key_quat(pb, quat, frame)

    def _fingers(self, side, curl, frame):
        """Each finger curled toward the palm by curl (0 open, 1 a fist), shared over its three joints, the thumb across."""
        s = self.side[side]
        rig = self.rig
        for finger in K_FINGERS:
            names = [f'{finger}Finger{i}_{side}' for i in (1, 2, 3)]
            for joint, name in enumerate(names):
                bone = rig.data.bones.get(name)
                if bone is None:
                    continue
                nxt = rig.data.bones.get(names[joint + 1]) if joint < 2 else None
                along = (nxt.head_local - bone.head_local) if nxt is not None else s['along']
                # Turning a finger about (its way x the palm's) carries it toward the palm: a curl, either hand.
                axis = along.normalized().cross(s['palm']).normalized()
                self._turn_bone(name, axis, curl * (75.0, 90.0, 60.0)[joint], frame)
        for joint, name in enumerate((f'Thumb0_{side}', f'Thumb1_{side}', f'Thumb2_{side}')):
            bone = rig.data.bones.get(name)
            if bone is None:
                continue
            axis = s['along']
            self._turn_bone(name, axis, curl * (25.0, 30.0, 35.0)[joint] * (1.0 if side == 'L' else -1.0), frame)

    def _turn_bone(self, name, axis, degrees, frame):
        pb = self.rig.pose.bones[name]
        rest = _rotation(pb.bone.matrix_local)
        self._key_quat(pb, rest.inverted() @ Quaternion(axis, math.radians(degrees)) @ rest, frame)


def zombie_bone_frames(context, rig, action, count, bones=('Head', 'Neck', 'Spine_03', 'Hips')):
    """The zombie's bones' world frames (rigid: no scale) at every frame of an action, for contact."""
    scene = context.scene
    data = rig.animation_data or rig.animation_data_create()
    keep_action, keep_frame = data.action, scene.frame_current
    tracks = [(t, t.mute) for t in data.nla_tracks]
    for t, _ in tracks:
        t.mute = True
    data.action = action
    out = []
    try:
        for frame in range(1, count + 1):
            scene.frame_set(frame)
            context.view_layer.update()
            row = {}
            for name in bones:
                m = rig.matrix_world @ rig.pose.bones[name].matrix
                row[name] = Matrix.Translation(m.translation) @ _rotation(m).to_matrix().to_4x4()
            out.append(row)
    finally:
        data.action = keep_action
        for t, mute in tracks:
            t.mute = mute
        scene.frame_set(keep_frame)
    return out


def zombie_layers_frames(context, rig, count, bones=('Head', 'Neck', 'Spine_03', 'Hips')):
    """The same as the zombie plays now, its draft and its touch-ups together (for re-fitting Kazusa to it)."""
    scene = context.scene
    keep = scene.frame_current
    out = []
    for frame in range(1, count + 1):
        scene.frame_set(frame)
        context.view_layer.update()
        row = {}
        for name in bones:
            m = rig.matrix_world @ rig.pose.bones[name].matrix
            row[name] = Matrix.Translation(m.translation) @ _rotation(m).to_matrix().to_4x4()
        out.append(row)
    scene.frame_set(keep)
    return out


def bone_at(frames, frame, bone, right=0.0, forward=0.0, up=0.0):
    """A point near a zombie bone at a frame: offset in the zombie's own frame (right, forward, up) from the bone's head."""
    return frames[frame - 1][bone].translation + zv(right, forward, up)
