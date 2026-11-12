"""Ports the class knives' own first-person clips from their workshop addons into the knife workspace (the user,
2026-11-13: "야 클래스별 칼 왜 다 기본 모션이야. 애드온 따오면서 그 모션도 같이 있었을 거 아니야. 빠르게 적용해 줘;;").

  blender --background --factory-startup --python Tools/Blender/Knives/knife_addon_port.py -- <addons folder> [--report]

<addons folder> holds each addon extracted into a folder named by its source and workshop id (l4d2_<id>, gmod_<id>), as
`python -I Tools/Workshop/workshop.py extract <id>.vpk <folder>` leaves them (GMod's .gma unpacked the same way). Each
knife's viewmodel is read with SourceIO's library (no import: its Blender animation import fails on these models), its
right arm per frame taken into the view's axes (Unity's: x right, y up, z forward) from the eyes, and moved onto the
workspace's body where the player saw it: each point drawn out from the eyes along its own line of sight by the one amount
that bends her arm at the guard as the viewmodel's is bent (a viewmodel's shoulder is posed for the screen, anywhere), a
wrist beyond her reach brought nearer along that line; the ways of the hand stay as they are. The light attack's blade meets
by HIT_BY (the fists' sweep is live for 0.18 s from the throw; the wind-up is quickened, the follow-through kept), a
heavy attack and a backhand start at their wind-up's furthest (a charge holds the knife there; a backhand
follows a forehand), an attack ends once the arm is home
again, and every clip eases into its guard's first pose. Writes Tools/Blender/Knives/addon_clips.json: per knife, the
clips at 30 fps with their marks; build_knife_workspace.py keys them as the workspace's drafts (dense: the touch-up
layer of Tools/Blender/OneClick takes the user's polish) and --export sends them to the game's tables
(KnifeMelee<Knife><Clip>.csv; ClassKnives.csv names them per class). It also measures how each addon's fist holds its
knife at the guard (seat_of: the game's model fitted onto the viewmodel's own knife mesh) into each knife's "seat";
ClassKnives.csv's meleeHeld*, meleeBlade* and meleeEdge* carry those numbers.

L4D2 viewmodels: the model's origin is the eye (Source axes: x forward, y left, z up); a draw is a layer added to the idle
(the sequence's DELTA and POST flags: each bone's turn after the idle's, its shift added), which SourceIO's reader takes as
a full pose, so delta animations are decoded here as Source does (no rest pose under them). The Delta Force rig (Heishang)
moves its camera with the arms: the view is its tag_camera's.
"""
import json
import math
import os
import sys
from pathlib import Path

import addon_utils
import bpy  # noqa: F401  (run inside Blender: SourceIO and mathutils come with it)
import numpy as np
from mathutils import Matrix, Quaternion, Vector

ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / 'Tools' / 'Blender' / 'Knives' / 'addon_clips.json'

VM_UNIT = 0.01905
FPS = 30
# Kazusa, the workspace's body: her right shoulder from her eyes (game metres, the view's axes) and her arm's length
# (shoulder to elbow to wrist), as build_knife_workspace.py measures her.
SHOULDER = Vector((.11, -.17, -.09))
ARM = .49
# The amount points are drawn out from the eyes stays within this (1: the viewmodel's own metres).
SCALES = (.9, 1.1)
# The wrist is kept this much of her arm's length from her shoulder at most (a straight arm's IK flips its elbow).
MOST_REACH = .97
# Where a blade can meet what it cuts: the wrist at least this far ahead of the eyes and at most this far below them (m).
IN_FRONT = (.12, -.35)
# A light attack's blade meets by this long after the throw (s; the drafts' 벰 is 0.067 s, the fists' sweep lives 0.18 s).
HIT_BY = 0.1
# An attack is home again once its wrist is this near the guard's (m) and its hand within this turn (degrees) ...
HOME_NEAR, HOME_TURN = .035, 20.0
# ... at least this long after its blade met (s); else it is cut this long after (s).
HOME_AFTER, MOST_AFTER = .25, .85
# Every clip eases into its guard's first pose over its last frames.
EASE_FRAMES = 4
# A guard loops this long at most (s), its end eased back into its start.
GUARD_MOST, GUARD_EASE = 2.4, .5
# The elbow handle sits this far beyond the elbow, away from the shoulder-wrist line (m).
POLE_OUT = .12
# The workspace's fist on a handle (the drafts' GRIP: index, middle, ring, little, degrees) and the thumb's closing kept
# within this: the knife sits fixed in the fist, so a hand the addon closes harder than that closes only that far (a
# viewmodel's fingers wrap its own handle; an open hand reaching for the knife stays open). A draw's 쥐기 is its first
# frame closed this much of the way.
GRIP, THUMB = (150, 160, 165, 170), (20.0, 90.0)
GRAB_SHARE = .9

L4D2 = dict(eye=None,
            shoulder='ValveBiped.Bip01_R_UpperArm', elbow='ValveBiped.Bip01_R_Forearm', wrist='ValveBiped.Bip01_R_Hand',
            fingers={'index': ('ValveBiped.Bip01_R_Finger1', 'ValveBiped.Bip01_R_Finger11', 'ValveBiped.Bip01_R_Finger12'),
                     'middle': ('ValveBiped.Bip01_R_Finger2', 'ValveBiped.Bip01_R_Finger21', 'ValveBiped.Bip01_R_Finger22'),
                     'ring': ('ValveBiped.Bip01_R_Finger3', 'ValveBiped.Bip01_R_Finger31', 'ValveBiped.Bip01_R_Finger32'),
                     'little': ('ValveBiped.Bip01_R_Finger4', 'ValveBiped.Bip01_R_Finger41', 'ValveBiped.Bip01_R_Finger42'),
                     'thumb': ('ValveBiped.Bip01_R_Finger0', 'ValveBiped.Bip01_R_Finger01', 'ValveBiped.Bip01_R_Finger02')},
            weapon='ValveBiped.weapon_bone')
DELTA_FORCE = dict(L4D2, eye='tag_camera', shoulder='j_shoulder_ri', elbow='j_elbow_ri', wrist='j_wrist_ri',
                   fingers={'index': ('j_index_ri_1', 'j_index_ri_2', 'j_index_ri_3'), 'middle': ('j_mid_ri_1', 'j_mid_ri_2', 'j_mid_ri_3'),
                            'ring': ('j_ring_ri_1', 'j_ring_ri_2', 'j_ring_ri_3'), 'little': ('j_pinky_ri_1', 'j_pinky_ri_2', 'j_pinky_ri_3'),
                            'thumb': ('j_thumb_ri_1', 'j_thumb_ri_2', 'j_thumb_ri_3')},
                   weapon='tag_knife_offset')

# Each knife's viewmodel and which of its animations plays which of the melee slot's clips (an animation's name without
# its @). L4D2 has one light swing and a shove (its secondary attack, here the heavy one); a knife with one swing slashes
# with it both ways (ClassKnives.csv names its slash for its backslash too). The forehand (slash) goes right to left, the
# backhand left to right, as the drafts do. A clip it has none for keeps the workspace's own draft.
KNIVES = {
    'Karambit': dict(folder='l4d2_2560050233', mdl='models/v_models/v_knife_t.mdl', rig=L4D2, idle='a_idle_1',
                     clips=dict(guard='a_idle_1', draw='deploy_layer', slash='midslash1', stab='melee')),
    'Butterfly': dict(folder='l4d2_3621862806', mdl='models/v_models/v_knife_t.mdl', rig=L4D2, idle='idle',
                      clips=dict(guard='idle', draw='d1', slash='mq', backslash='mp', stab='_b')),
    'ScissorsShiv': dict(folder='l4d2_3555641124', mdl='models/v_models/a_knife_t.mdl', rig=L4D2, idle='a_idle_1',
                         clips=dict(guard='a_idle_1', draw='deploy_layer', slash='midslash1', stab='melee')),
    'PrimalKnife': dict(folder='l4d2_2980835519', mdl='models/v_models/v_knife_t.mdl', rig=L4D2, idle='a_idle_1',
                        clips=dict(guard='a_idle_1', draw='deploy_layer', slash='midslash1', backslash='midslash2', stab='melee')),
    'Heishang': dict(folder='gmod_3834087095', mdl='models/deltaforce/heishang_dev/full/v_heishang_full.mdl', rig=DELTA_FORCE,
                     idle='idle_open', clips=dict(guard='idle_open', draw='draw_open', slash='melee', backslash='fire_02', stab='fire_03')),
}
FOUR = ('index', 'middle', 'ring', 'little')
THUMB_SCALE = 1.3


def args():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    if not argv:
        raise SystemExit('Give the folder holding the extracted addons after --')
    return Path(argv[0]), '--report' in argv


# ---- Reading a viewmodel ----------------------------------------------------------------------------------------------

def load_sourceio():
    addon_utils.enable('SourceIO-master', default_set=False, persistent=False)
    from SourceIO.library.models.mdl.structs import local_animation as la
    from SourceIO.library.models.mdl.v49.mdl_file import MdlV49
    from SourceIO.library.utils import FileBuffer

    def read_mdl_animations(self, buffer, bones, frames):
        # SourceIO's own, but a delta animation is read as Source reads it: no rest pose under its bones, and an animated
        # delta bone's values without the bone's rest turn and place added.
        delta = bool(self.flags & la.AnimDescFlags.DELTA)
        out = np.zeros((frames, len(bones)), la.ANIM_DTYPE)
        for bone in bones:
            out[:, bone.bone_id]['rot'] = (0.0, 0.0, 0.0, 1.0) if delta else bone.quat
            out[:, bone.bone_id]['pos'] = (0.0, 0.0, 0.0) if delta else bone.position
        zero = np.zeros(3, dtype=np.float32)
        for _ in bones:
            entry = buffer.tell()
            index = buffer.read_uint8()
            if index == 255:
                buffer.skip(3)
                break
            used = bones[index]
            flags = la.AnimBoneFlags(buffer.read_uint8())
            next_offset = buffer.read_int16()
            bare = bool(flags & la.AnimBoneFlags.ANIM_DELTA)
            out[:, index]['rot'] = self._read_anim_rot_value(buffer, flags, frames, used.quat, zero if bare else used.rotation,
                                                             used.rotation_scale)
            out[:, index]['pos'] = self._read_anim_pos_value(buffer, flags, frames, zero if bare else used.position, used.position_scale)
            if next_offset > 0:
                buffer.seek(entry + next_offset)
                continue
            break
        return out

    la.StudioAnimDesc._read_mdl_animations = read_mdl_animations
    return MdlV49, FileBuffer


class Viewmodel:
    """One viewmodel's bones and animations (Source's units and axes)."""

    def __init__(self, path, rig, idle, mdl_type, file_buffer):
        with file_buffer(str(path)) as f:
            self.mdl = mdl_type.from_buffer(f)
        self.rig = rig
        self.bones = self.mdl.bones
        self.index = {b.name: i for i, b in enumerate(self.bones)}
        self.descs = {d.name.lstrip('@'): i for i, d in enumerate(self.mdl.anim_descs)}
        self.seqs = {s.name: s for s in self.mdl.sequences}
        self.idle = idle

    def anim(self, name):
        """(frames, fps, delta, post) of an animation by its name or its sequence's."""
        seq = self.seqs.get(name)
        n = seq.anim_desc_indices[0] if seq is not None and name not in self.descs else self.descs[name]
        desc = self.mdl.anim_descs[n]
        seq_flags = seq.flags if seq is not None else 0
        delta = bool(desc.flags & 0x4) or bool(seq_flags & 0x4)
        post = bool(seq_flags & 0x10) or bool(desc.flags & 0x10)
        return self.mdl.animations[n], float(desc.fps), delta, post

    def locals_at(self, data, frame):
        """Each bone's own place and turn at a frame of an animation."""
        pos, rot = data['pos'][frame], data['rot'][frame]
        return [(Vector(pos[i]), Quaternion((rot[i][3], rot[i][0], rot[i][1], rot[i][2]))) for i in range(len(self.bones))]

    def world(self, local):
        out = [None] * len(self.bones)
        for i, b in enumerate(self.bones):
            p, q = local[i]
            m = Matrix.Translation(p) @ q.to_matrix().to_4x4()
            out[i] = m if b.parent_id == -1 else out[b.parent_id] @ m
        return out

    def pose(self, name, t):
        """Every bone in the model at t seconds into an animation (a delta one over the idle at the same moment)."""
        data, fps, delta, post = self.anim(name)
        local = self.sample(data, fps, t)
        if delta:
            idle, idle_fps, _, _ = self.anim(self.idle)
            base = self.sample(idle, idle_fps, t, loop=True)
            local = [(bp + dp, (bq @ dq) if post else (dq @ bq)) for (bp, bq), (dp, dq) in zip(base, local)]
        return self.world(local)

    def sample(self, data, fps, t, loop=False):
        count = int(data.shape[0])
        f = t * fps
        if loop and count > 1:
            f = f % (count - 1)
        f = min(max(f, 0.0), count - 1)
        a = int(math.floor(f))
        b = min(a + 1, count - 1)
        s = f - a
        la, lb = self.locals_at(data, a), self.locals_at(data, b)
        return [(pa.lerp(pb, s), qa.slerp(qb, s) if qa.dot(qb) >= 0 else qa.slerp(-qb, s)) for (pa, qa), (pb, qb) in zip(la, lb)]

    def length(self, name):
        data, fps, _, _ = self.anim(name)
        return (int(data.shape[0]) - 1) / fps


def view_of(v):
    """Source's axes (x forward, y left, z up) to the view's (x right, y up, z forward)."""
    return Vector((-v.y, v.z, v.x))


def unity_cross(a, b):
    return Vector((a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x))


def angle(a, b):
    return math.degrees(a.angle(b)) if a.length > 1e-9 and b.length > 1e-9 else 0.0


class Arm:
    """The right arm of one viewmodel at a moment, in the view's axes (raw: the viewmodel's metres from its eye)."""

    def __init__(self, vm, world):
        rig = vm.rig
        view = world[vm.index[rig['eye']]].inverted() if rig['eye'] else Matrix.Identity(4)

        def at(name):
            return view_of((view @ world[vm.index[name]]).to_translation()) * VM_UNIT

        def turn(name):
            return (view @ world[vm.index[name]]).to_3x3()

        self.shoulder, self.elbow, self.wrist = at(rig['shoulder']), at(rig['elbow']), at(rig['wrist'])
        f = rig['fingers']
        index, middle, little = at(f['index'][0]), at(f['middle'][0]), at(f['little'][0])
        self.hand = (middle - self.wrist).length
        self.along = (middle - self.wrist).normalized()
        palm = unity_cross(self.along, index - little)
        self.palm = (palm - self.along * palm.dot(self.along)).normalized()
        across = little - index
        across = across - self.along * across.dot(self.along) - self.palm * across.dot(self.palm)
        self.little_way = across.normalized()
        self.curls = {}
        for finger, names in f.items():
            j = [at(n) for n in names]
            first, second = (j[1] - j[0]).normalized(), (j[2] - j[1]).normalized()
            # The last joint has no joint beyond it: its own length axis, the one its parent's way to it lies along.
            parent_local = turn(names[1]).inverted() @ unview(j[2] - j[1])
            k = max(range(3), key=lambda i: abs(parent_local[i]))
            axis = Vector((0.0, 0.0, 0.0))
            axis[k] = 1.0 if parent_local[k] > 0 else -1.0
            last = view_of(turn(names[2]) @ axis).normalized()
            if finger == 'thumb':
                self.curls[finger] = (angle(first, second) + angle(second, last)) * THUMB_SCALE
            else:
                self.curls[finger] = angle(self.along, first) + angle(first, second) + angle(second, last)
        weapon = view @ world[vm.index[rig['weapon']]]
        self.weapon = view_of(weapon.to_translation()) * VM_UNIT
        self.weapon_turn = weapon.to_3x3()


def unview(v):
    return Vector((v.z, -v.x, v.y))


# ---- Onto Kazusa ------------------------------------------------------------------------------------------------------

class Fit:
    """The viewmodel's arm laid on Kazusa's so the hand is where the player saw it: every point drawn out from the eyes
    along its own line of sight (a viewmodel's shoulder sits anywhere, posed for the screen), by the one amount that bends
    her arm at the guard as much as the viewmodel's arm is bent there. A wrist out of her reach comes nearer along that
    line (still where it was on the screen) as far as she reaches."""

    def __init__(self, guard):
        length = (guard.elbow - guard.shoulder).length + (guard.wrist - guard.elbow).length
        want = min((guard.wrist - guard.shoulder).length / length, MOST_REACH) * ARM
        # |scale * wrist - SHOULDER| = want: the larger root (the hand ahead of the shoulder).
        w = guard.wrist
        a, b, c = w.dot(w), -2.0 * w.dot(SHOULDER), SHOULDER.dot(SHOULDER) - want * want
        disc = b * b - 4 * a * c
        scale = (-b + math.sqrt(disc)) / (2 * a) if disc >= 0 and a > 1e-9 else ARM / length
        # Within SCALES: a viewmodel posed with its shoulder ahead of the eyes would otherwise bring every point up to the
        # face, the knife filling the view.
        self.scale = min(max(scale, SCALES[0]), SCALES[1])

    def point(self, p):
        return p * self.scale

    @staticmethod
    def reach(wrist):
        """The wrist where her arm reaches it: along its line of sight from the eyes, no further than she reaches."""
        most = ARM * MOST_REACH
        if (wrist - SHOULDER).length <= most:
            return wrist
        ray = wrist.normalized()
        # |t * ray - SHOULDER| = most: the far crossing, no further out than the wrist itself.
        b = -2.0 * ray.dot(SHOULDER)
        c = SHOULDER.dot(SHOULDER) - most * most
        disc = b * b - 4 * c
        if disc >= 0:
            t = (-b + math.sqrt(disc)) / 2
            if t > 0:
                return ray * min(t, wrist.length)
        return SHOULDER + (wrist - SHOULDER).normalized() * most

    def key(self, arm):
        wrist, elbow = self.reach(self.point(arm.wrist)), self.point(arm.elbow)
        middle = (SHOULDER + wrist) * .5
        out = elbow - middle
        out = out - (wrist - SHOULDER).normalized() * out.dot((wrist - SHOULDER).normalized())
        if out.length < 1e-4:
            out = Vector((.6, -.8, 0.0))
        pole = elbow + out.normalized() * POLE_OUT
        return dict(wrist=wrist, along=arm.along.copy(), palm=arm.palm.copy(), pole=pole,
                    curl=[min(arm.curls[f], g) for f, g in zip(FOUR, GRIP)], thumb=min(max(arm.curls['thumb'], THUMB[0]), THUMB[1]))


def blend(a, b, s):
    s = min(max(s, 0.0), 1.0)
    along = a['along'].lerp(b['along'], s).normalized()
    palm = a['palm'].lerp(b['palm'], s)
    palm = (palm - along * palm.dot(along)).normalized()
    return dict(wrist=a['wrist'].lerp(b['wrist'], s), along=along, palm=palm, pole=a['pole'].lerp(b['pole'], s),
                curl=[x + (y - x) * s for x, y in zip(a['curl'], b['curl'])], thumb=a['thumb'] + (b['thumb'] - a['thumb']) * s)


def smooth(s):
    return s * s * (3 - 2 * s)


# ---- The clips --------------------------------------------------------------------------------------------------------

def arms_of(vm, name, step=1.0 / 120):
    """The arm through a whole animation, every step seconds (its own frames are finer or coarser)."""
    length = vm.length(name)
    count = max(2, int(round(length / step)) + 1)
    return [(i * length / (count - 1), Arm(vm, vm.pose(name, i * length / (count - 1)))) for i in range(count)]


def at_time(samples, t):
    """The sample nearest before and after t, blended (samples: (time, key))."""
    if t <= samples[0][0]:
        return samples[0][1]
    if t >= samples[-1][0]:
        return samples[-1][1]
    for (ta, a), (tb, b) in zip(samples, samples[1:]):
        if ta <= t <= tb:
            return blend(a, b, (t - ta) / max(tb - ta, 1e-9))
    return samples[-1][1]


def hit_of(samples, share=.7):
    """The blade's moment: where the knuckles move fastest out in front of the eyes (not dropped away below the view or
    swung back past the face), in the first share of the clip (s). The speed is taken over a 30th of a second, from a 30th
    in: an animation's first frames can jump from the pose before it."""
    first, last = samples[0][0], samples[0][0] + (samples[-1][0] - samples[0][0]) * share
    half = .5 / FPS
    best, when = -1.0, first
    t = first + 1.0 / FPS
    while t <= last:
        a, b = at_time(samples, t - half), at_time(samples, t + half)
        if min(a['wrist'].z, b['wrist'].z) >= IN_FRONT[0] and min(a['wrist'].y, b['wrist'].y) >= IN_FRONT[1]:
            speed = ((b['wrist'] + b['along'] * .1) - (a['wrist'] + a['along'] * .1)).length * FPS
            if speed > best:
                best, when = speed, t
        t += 1.0 / 240
    return when


def frames_of(samples, start, hit, end, guard_first, kind):
    """The clip at 30 fps from start to end (s, the source's), its wind-up quickened so the blade meets by HIT_BY, its last
    frames eased into the guard's first pose. Returns the keys and the 벰 frame (1-based; None without one)."""
    keys = []
    if hit is not None:
        windup = hit - start
        want = min(windup, HIT_BY)
        hit_frame = 1 + int(round(want * FPS))
        want = (hit_frame - 1) / FPS
        after = end - hit
        count = hit_frame + int(round(after * FPS))
        for k in range(count):
            t = k / FPS
            source = start + (t * windup / want if want > 1e-6 and t <= want else (windup + (t - want) if want > 1e-6 else t))
            keys.append(at_time(samples, min(source, end)))
    else:
        hit_frame = None
        count = 1 + int(round((end - start) * FPS))
        keys = [at_time(samples, start + k / FPS) for k in range(count)]
    if guard_first is not None and kind != 'guard':
        n = min(EASE_FRAMES, len(keys) - 1)
        for i in range(n):
            k = len(keys) - n + i
            keys[k] = blend(keys[k], guard_first, smooth((i + 1) / n))
    return keys, hit_frame


def home_of(samples, hit, guard_first):
    """When an attack's arm is home again: near the guard's first pose a while after its blade met; else cut."""
    end = samples[-1][0]
    for t, key in samples:
        if t < hit + HOME_AFTER:
            continue
        if (key['wrist'] - guard_first['wrist']).length < HOME_NEAR and angle(key['along'], guard_first['along']) < HOME_TURN:
            return t
        if t >= hit + MOST_AFTER:
            return t
    return end


def cocked_of(samples, hit):
    """A wind-up at its furthest: before the blade meets, the pose furthest from where it meets, of those with the hand out
    in front (a charge holds the knife there: never one drawn up into the face)."""
    at_hit = at_time(samples, hit)
    best, when = 0.0, samples[0][0]
    for t, key in samples:
        if t >= hit:
            break
        if key['wrist'].z < IN_FRONT[0] or key['wrist'].y < IN_FRONT[1]:
            continue
        d = (key['wrist'] - at_hit['wrist']).length
        if d > best:
            best, when = d, t
    return when


def loop_guard(keys):
    """The guard's end eased back into its start, so it loops."""
    n = min(int(round(GUARD_EASE * FPS)), len(keys) - 1)
    first = keys[0]
    for i in range(n):
        k = len(keys) - n + i
        keys[k] = blend(keys[k], first, smooth((i + 1) / n))
    keys[-1] = dict(first)
    return keys


def port(knife, spec, addons, mdl_type, file_buffer, report):
    path = addons / spec['folder'] / spec['mdl'].replace('/', '\\' if sys.platform == 'win32' else '/')
    vm = Viewmodel(path, spec['rig'], spec['idle'], mdl_type, file_buffer)
    guard_arm = Arm(vm, vm.pose(spec['clips']['guard'], 0.0))
    fit = Fit(guard_arm)
    out = {}
    guard_first = fit.key(guard_arm)
    for clip, name in spec['clips'].items():
        raw = arms_of(vm, name)
        samples = [(t, fit.key(a)) for t, a in raw]
        length = samples[-1][0]
        start, hit, end = 0.0, None, length
        if clip == 'guard':
            end = min(length, GUARD_MOST)
        elif clip in ('slash', 'backslash', 'stab'):
            hit = hit_of(samples)
            if clip in ('stab', 'backslash'):
                # A heavy attack and a backhand start wound up: a charge holds the knife there, a backhand follows the
                # forehand from where it left the arm.
                start = cocked_of(samples, hit)
            end = home_of(samples, hit, guard_first)
        keys, hit_frame = frames_of(samples, start, hit, end, guard_first, clip)
        if clip == 'guard':
            keys = loop_guard(keys)
        marks = {}
        if hit_frame is not None:
            marks['벰'] = hit_frame
        if clip == 'draw':
            closed = next((i for i, k in enumerate(keys) if sum(k['curl']) >= GRAB_SHARE * sum(GRIP)), 0)
            marks['쥐기'] = closed + 1
        out[clip] = dict(source=name, frames=[pack(k) for k in keys], marks=marks, end=len(keys),
                         span=[round(start, 4), round(hit, 4) if hit is not None else None, round(end, 4)])
        if report:
            def v(x):
                return '(%+.2f %+.2f %+.2f)' % tuple(x)
            first, mid = keys[0], keys[min(len(keys) - 1, (hit_frame or 1) - 1)]
            print('%-12s %-9s %-16s len %.2fs used %.2f-%.2f hit %s -> %d frames | first wrist %s along %s | at hit wrist %s along %s curls %s'
                  % (knife, clip, name, length, start, end, ('%.3f' % hit) if hit is not None else '-', len(keys),
                     v(first['wrist']), v(first['along']), v(mid['wrist']), v(mid['along']), [int(c) for c in mid['curl']] + [int(mid['thumb'])]))
            if hit is not None:
                # The knuckles' speed (m/s) every 30th of a second through the whole source animation, the blade's moment marked.
                speeds = []
                for k in range(int(length * FPS)):
                    a, b = at_time(samples, k / FPS), at_time(samples, (k + 1) / FPS)
                    d = ((b['wrist'] + b['along'] * .1) - (a['wrist'] + a['along'] * .1)).length * FPS
                    speeds.append(('*' if abs(k / FPS - hit) < .5 / FPS else '') + '%.1f' % d)
                print('    speed', ' '.join(speeds))
                print('    wrist', ' '.join('(%+.2f,%+.2f,%+.2f)' % tuple(at_time(samples, k / FPS)['wrist']) for k in range(0, int(length * FPS), 2)))
    if report:
        g = guard_arm
        print('%-12s viewmodel arm: shoulder %s elbow %s wrist %s (raw m) hand %.3f scale %.3f'
              % (knife, tuple(round(c, 3) for c in g.shoulder), tuple(round(c, 3) for c in g.elbow), tuple(round(c, 3) for c in g.wrist),
                 g.hand, fit.scale))
    return out


# ---- How the addon's fist holds its knife ---------------------------------------------------------------------------

# The knife's own bones: not the arms' (ValveBiped's, Delta Force's j_*_le/_ri) nor an IK target.
def knife_bone(name):
    return not (name.startswith('ValveBiped.Bip01_') or 'IK_TARGET' in name
                or (name.startswith('j_') and ('_le' in name or '_ri' in name)))


# A viewmodel whose knife mesh is in more than one piece: the ones that are the game's model (the Heishang's sheath is
# HS_Bone002, not in the model).
SEAT_BONES = {'Heishang': ('HS_Bone001',)}
KNIFE_MODEL = 'Assets/_Project/Art/Models/Addons/Knives/{0}/{0}.fbx'
# A viewmodel whose own knife mesh is in another model than its animations (the scissors' a_ model holds the clips).
MESH_MODEL = {'ScissorsShiv': 'models/v_models/v_knife_t.mdl'}


def seat_of(knife, spec, addons, vm):
    """Where the addon's fist holds its knife at the guard, in ClassKnives' hand axes (x away from the thumb, y out of the
    palm, z the hand's run): the game's model laid on the viewmodel's own knife mesh (the same knife in another frame,
    found from all their points, its size too), and its handle's middle (its origin), its blade (pommel to origin) and
    its edge taken into the hand. Returns the seat with the fit's miss (m), or None."""
    path = addons / spec['folder'] / MESH_MODEL.get(knife, spec['mdl'])
    before = set(bpy.data.objects)
    try:
        bpy.ops.sourceio.mdl(filepath=str(path), directory=str(path.parent) + os.sep, files=[{'name': path.name}],
                             import_animations=False, write_qc=False, discover_resources=False)
    except Exception as error:  # noqa: BLE001 (SourceIO raises anything)
        print('SEAT', knife, 'import failed:', error)
        return None
    made = [o for o in bpy.data.objects if o not in before]
    world = vm.pose(spec['clips']['guard'], 0.0)
    eye = world[vm.index[spec['rig']['eye']]].inverted() if spec['rig']['eye'] else Matrix.Identity(4)
    held = []
    for obj in (o for o in made if o.type == 'MESH'):
        names = {g.index: g.name for g in obj.vertex_groups}
        for v in obj.data.vertices:
            if not v.groups:
                continue
            bone = names[max(v.groups, key=lambda g: g.weight).group]
            if not knife_bone(bone) or bone not in vm.index or (knife in SEAT_BONES and bone not in SEAT_BONES[knife]):
                continue
            i = vm.index[bone]
            bind = np.array(vm.bones[i].pose_to_bone, dtype=float)
            if bind.shape == (4, 3):
                bind = bind.T
            to_bone = Matrix([[float(x) for x in bind[r]] for r in range(3)] + [[0.0, 0.0, 0.0, 1.0]])
            held.append((eye @ world[i] @ to_bone) @ ((obj.matrix_world @ v.co) / VM_UNIT) * VM_UNIT)
    for obj in made:
        bpy.data.objects.remove(obj, do_unlink=True)
    if len(held) < 50:
        print('SEAT', knife, 'no knife mesh found')
        return None
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(ROOT / KNIFE_MODEL.format(knife)))
    made = [o for o in bpy.data.objects if o not in before]
    model = [o.matrix_world @ v.co for o in made if o.type == 'MESH' for v in o.data.vertices]
    marks = {o.name.split('.')[0]: o.matrix_world.translation.copy() for o in made if o.type == 'EMPTY'}
    for obj in made:
        bpy.data.objects.remove(obj, do_unlink=True)
    if 'Pommel' not in marks or not model:
        print('SEAT', knife, 'the model has no Pommel marker')
        return None
    # The two at one size (a viewmodel's knife may be drawn larger), then laid one on the other.
    P, Q = np.array([list(v) for v in held]), np.array([list(v) for v in model])
    size = math.sqrt(max(np.linalg.eigvalsh(np.cov(Q.T)))) / math.sqrt(max(np.linalg.eigvalsh(np.cov(P.T))))
    R, t, miss, mirrored = fit_shape(P * size, Q)
    back = np.linalg.inv(R)

    def in_view(q):
        """A point of the model (Blender's import of its FBX) into the view's axes at the guard (metres)."""
        return view_of(Vector(back @ (np.array(list(q)) - t)) / size)

    arm = Arm(vm, world)
    origin, pommel = in_view(Vector((0.0, 0.0, 0.0))), in_view(marks['Pommel'])
    edge_point = in_view(marks['Edge']) if 'Edge' in marks else None
    # The hand's axes: x toward the little finger (away from the thumb), y out of the palm, z its run.
    axes = (arm.little_way, arm.palm, arm.along)

    def hand(v):
        return [round(float(v.dot(a)), 3) for a in axes]

    blade = (origin - pommel).normalized()
    edge = None
    if edge_point is not None:
        e = edge_point - origin
        e = e - blade * e.dot(blade)
        edge = e.normalized() if e.length > 1e-6 else None
    return dict(held=[round(c / arm.hand, 3) for c in hand(origin - arm.wrist)], blade=hand(blade),
                edge=hand(edge) if edge is not None else [0.0, 0.0, 1.0], miss=round(miss, 4), size=round(size, 3),
                mirrored=bool(mirrored))


def fit_shape(P, Q):
    """The turn and shift that lay point cloud P on Q (one knife's mesh in two frames, its points in another order):
    their principal axes lined up every way round, then nearest points matched and refit (ICP) from each start; the
    closest fit wins. Returns R, t (Q ~ R P + t), the rms miss and whether it had to be mirrored (as the throwables
    workspace fits MW's knife: Tools/Blender/Grenades/build_grenade_workspace.py)."""
    from mathutils.kdtree import KDTree
    tree = KDTree(len(Q))
    for i, q in enumerate(Q):
        tree.insert(Vector(q), i)
    tree.balance()
    P = P[::max(1, len(P) // 1500)]

    def axes(X):
        c = X.mean(axis=0)
        w, v = np.linalg.eigh(np.cov((X - c).T))
        return c, v[:, np.argsort(w)[::-1]]

    def kabsch(A, B, mirror):
        ca, cb = A.mean(axis=0), B.mean(axis=0)
        U, _, Vt = np.linalg.svd((A - ca).T @ (B - cb))
        flip = (-1.0 if mirror else 1.0) * (1.0 if np.linalg.det(Vt.T @ U.T) > 0 else -1.0)
        R = Vt.T @ np.diag([1.0, 1.0, flip]) @ U.T
        return R, cb - R @ ca

    cp, ap = axes(P)
    cq, aq = axes(Q)
    best = None
    for s1 in (1, -1):
        for s2 in (1, -1):
            for s3 in (1, -1):
                R = aq @ np.diag([s1, s2, s3]) @ ap.T
                mirror = np.linalg.det(R) < 0
                t = cq - R @ cp
                for _ in range(30):
                    moved = P @ R.T + t
                    pairs = np.array([tree.find(Vector(m))[1] for m in moved])
                    R, t = kabsch(P, Q[pairs], mirror)
                moved = P @ R.T + t
                miss = float(np.sqrt(np.mean([tree.find(Vector(m))[2] ** 2 for m in moved])))
                if best is None or miss < best[2]:
                    best = (R, t, miss, mirror)
    return best


def pack(key):
    def r(v, n=4):
        return [round(float(c), n) for c in v]
    return [r(key['wrist']), r(key['along']), r(key['palm']), r(key['pole']), [round(float(c), 1) for c in key['curl']],
            round(float(key['thumb']), 1)]


def main():
    addons, report = args()
    mdl_type, file_buffer = load_sourceio()
    result = {}
    for knife, spec in KNIVES.items():
        result[knife] = dict(source=f"{spec['folder']}/{spec['mdl']}", clips=port(knife, spec, addons, mdl_type, file_buffer, report))
        seat = seat_of(knife, spec, addons, Viewmodel(addons / spec['folder'] / spec['mdl'], spec['rig'], spec['idle'], mdl_type,
                                                      file_buffer))
        if seat is not None:
            result[knife]['seat'] = seat
        print('SEAT', knife, seat)
    OUT.write_text(json.dumps(result, ensure_ascii=False, separators=(',', ':')) + '\n', encoding='utf-8')
    print('wrote', OUT)


if __name__ == '__main__':
    main()
