"""Every finger joint of a posed Blender rig as the game's hand tables carry it (FingerWays in the game), Blender 4.5.

Every Blender hand workspace's export uses this: the throwables' clips (Tools/Blender/Grenades/grenade_tools.py), the
shove workspace's mending loops (Tools/Blender/Shove/shove_tools.py), and any other table a hand is played from. So what
the user poses in Blender reaches the game whole, on any character's hand (the user, 2026-11-14, on learning that the
clips had sent one closing number per finger: "다신 이런 일이 재발하지 않도록 뼈에 새겨라. 이 문제를. 지금 당장. 고쳐라.").

For each finger segment (index, middle, ring, little, thumb; each its base, middle and tip: HandGrip's order):
- its way: where the segment points, in the hand's own frame as the game takes it (GunArms.Digits, HandGrip.WearWays):
  x the way the fingers run (wrist to the middle knuckle), y the way the palm faces (square to x), z = x × y as Unity
  crosses (for a left hand toward the thumb, for a right hand away from it);
- its twist: how far the bone is turned about its own length past the least turn that points it so from the rig's rest
  under its parent as posed (degrees, Unity's sense about the way). A finger joint's three channels all come through.
The game turns each segment of its own hand from its rest the least way to point along the way, then about it by the
twist: on this rig that gives back exactly the pose made here, on any other hand the same shape. Only the directions
of the bones are read (heads at rest, rotations posed), never their tails: some rigs point a finger bone's tail across
the finger (Kazusa's, 95 degrees off).

Columns: <prefix><finger><joint>X, Y, Z and Twist (index1X ... thumb3Twist), 60 a hand; a prefix tells two hands of one
table apart (a knife's r and l). A workspace tool loads this by its path, once (sys.modules 'enf_finger_ways').

close() closes a hand the way the game closed it from a curl and a thumb number before the joints came from here
(ArmBones.Close), for drafting a grip a workspace never keyed (the mending loops' tools), so the draft shows what the
game showed and the user takes it from there.
"""
import math

from mathutils import Matrix, Quaternion, Vector

FINGERS = ('index', 'middle', 'ring', 'little', 'thumb')
AXES = ('X', 'Y', 'Z', 'Twist')
KOREAN = {'index': '검지', 'middle': '중지', 'ring': '약지', 'little': '새끼', 'thumb': '엄지'}
# ArmBones.CurlShare: a finger's whole curl shared over its joints, knuckle first.
CURL_SHARE = (.4, .35, .25)
# What curl columns the throw workspace wrote for the thumb: its two bends times this (GrenadeArm: 90 is fully closed).
THUMB_SCALE = 1.3


# ---- The columns ------------------------------------------------------------------------------------------------------

def columns(prefix=''):
    """The 60 column names of one hand (FingerWays.Columns): X, Y, Z, Twist per segment, index1 ... thumb3."""
    return [f'{prefix}{finger}{joint}{axis}' for finger in FINGERS for joint in (1, 2, 3) for axis in AXES]


def descriptions(hand=''):
    """The description row's cells for those columns (hand: '왼손 ' or '오른손 ' where a table has both)."""
    out = []
    for finger in FINGERS:
        for joint in (1, 2, 3):
            name = f'{hand}{KOREAN[finger]} {joint}마디'
            out += [f'{name}가 가리키는 쪽: 손이 뻗은 쪽 성분', f'{name}: 손바닥 쪽 성분',
                    f'{name}: 옆 성분(뻗은 쪽 × 손바닥 쪽)', f'{name}가 제 길이를 축으로 더 돈 각도(°)']
    return out


def cells(ways):
    """measure()'s numbers as the table's cells, to the sixth decimal: what Blender has goes to the game as measured, not
    rounded (the user's rule of 2026-11-14; a fourth decimal turned a way up to 0.03 degrees)."""
    out = []
    for x, y, z, twist in ways:
        out += [f'{x:.6f}', f'{y:.6f}', f'{z:.6f}', f'{twist:.6f}']
    return out


# ---- Measuring ----------------------------------------------------------------------------------------------------------

def rest_axis(rig, chain, i):
    """Which way segment i of a finger runs, in its bone's own rest axes: toward the next joint; the last one toward its
    child bone if it has one, else on the way the joint before it ran (the rule HandGrip.Create measures the game's own
    hand by)."""
    bones = rig.data.bones
    bone = bones[chain[i]]
    if i + 1 < len(chain):
        d = bones[chain[i + 1]].head_local - bone.head_local
    else:
        child = next((c for c in bone.children if (c.head_local - bone.head_local).length > 1e-6), None)
        d = child.head_local - bone.head_local if child is not None else bone.head_local - bones[chain[i - 1]].head_local
    return (bone.matrix_local.to_3x3().inverted() @ d).normalized()


def frame(rig, wrist, chains, right=False):
    """The hand's own frame now (the armature's space): x the way the fingers run, y the way the palm faces, z the way
    Unity's Cross(x, y) points. The palm comes out of the knuckles as ArmBones.PalmNow takes it: Unity's left-handed cross
    of the run and the index-to-little line, turned round for a left hand, points as this file's right-handed cross does
    unturned for a left hand (turned for a right one); for the same reason Unity's Cross(x, y) is y × x here."""
    pose = rig.pose.bones
    w = pose[wrist].head
    index, middle, little = (pose[chains[f][0]].head for f in ('index', 'middle', 'little'))
    x = (middle - w).normalized()
    palm = x.cross(index - little)
    if right:
        palm = -palm
    y = (palm - x * palm.dot(x)).normalized()
    return x, y, y.cross(x)


def segment_way(rig, chain, i):
    """Which way segment i of a finger points now (the armature's space): its bone's rest axis as the bone is turned."""
    pb = rig.pose.bones[chain[i]]
    return (pb.matrix.to_3x3().normalized() @ rest_axis(rig, chain, i)).normalized()


def twist(rig, name, axis):
    """How far a bone is turned about its own length past the least turn from its rest under its parent as posed now, to
    point where it points (degrees, Unity's sense: this file's right-handed turn counted the other way)."""
    pb = rig.pose.bones[name]
    rest = pb.bone.matrix_local.to_3x3().normalized()
    parent = pb.parent
    if parent is not None:
        rest = parent.matrix.to_3x3().normalized() @ parent.bone.matrix_local.to_3x3().normalized().inverted() @ rest
    now = pb.matrix.to_3x3().normalized()
    rest_way, now_way = (rest @ axis).normalized(), (now @ axis).normalized()
    least = rest_way.rotation_difference(now_way).to_matrix()
    turn = (now @ (least @ rest).inverted()).to_quaternion()
    if turn.w < 0:
        turn.negate()
    angle = 2 * math.degrees(math.atan2(Vector((turn.x, turn.y, turn.z)).dot(now_way), turn.w))
    if angle > 180:
        angle -= 360
    return -angle


def measure(rig, wrist, chains, right=False):
    """Every finger segment of one hand as the game's columns take it: 15 (x, y, z, twist) in FINGERS' order, base to
    tip. wrist: the hand bone; chains: finger -> its three bones, knuckle out (the thumb from its root in the palm)."""
    x, y, z = frame(rig, wrist, chains, right)
    out = []
    for finger in FINGERS:
        chain = chains[finger]
        for i in range(3):
            axis = rest_axis(rig, chain, i)
            way = segment_way(rig, chain, i)
            out.append((way.dot(x), way.dot(y), way.dot(z), twist(rig, chain[i], axis)))
    return out


def _angle(a, b):
    return math.degrees(a.angle(b)) if a.length > 1e-9 and b.length > 1e-9 else 0.0


def closing(rig, wrist, chains, right=False):
    """How far each finger and the thumb are closed (degrees), as the curl columns a table keeps beside the ways sum it:
    each of the four, the hand's run to its first segment and then each joint; the thumb, its two bends times
    THUMB_SCALE."""
    x, _, _ = frame(rig, wrist, chains, right)
    out = {}
    for finger in FINGERS:
        s = [segment_way(rig, chains[finger], i) for i in range(3)]
        if finger == 'thumb':
            out[finger] = (_angle(s[0], s[1]) + _angle(s[1], s[2])) * THUMB_SCALE
        else:
            out[finger] = _angle(x, s[0]) + _angle(s[0], s[1]) + _angle(s[1], s[2])
    return out


# ---- Drafting a grip ----------------------------------------------------------------------------------------------------

def _turn_to(rig, chain, i, want, share=1.0):
    """Turns segment i of a finger about its joint so it points along want (the armature's space), all the way or by a
    share of the least turn (ArmBones.Turn)."""
    pb = rig.pose.bones[chain[i]]
    now = segment_way(rig, chain, i)
    if now.length < 1e-9 or want.length < 1e-9:
        return
    turn = now.rotation_difference(want.normalized())
    if share < 1.0:
        turn = Quaternion().slerp(turn, share)
    at = pb.matrix.translation.copy()
    pb.matrix = Matrix.Translation(at) @ turn.to_matrix().to_4x4() @ Matrix.Translation(-at) @ pb.matrix


def close(rig, wrist, chains, curl, thumb, right=False, update=lambda: None):
    """Closes a hand as ArmBones.Close closed it in the game from a curl and a thumb number (degrees): each of the four
    fingers turned toward the palm by its curl shared over its joints (CURL_SHARE), the thumb across the palm toward the
    little finger's side and on over the fingers, thumb / 90 of the way from where it is. update refreshes the rig's pose
    between joints (context.view_layer.update). Unity's turns and crosses are both left-handed, so a turn by an angle
    about Unity's Cross(a, b) is the same turn as one by that angle about a × b here; a cross used as a way (the thumb's
    over) points as b × a does here."""
    update()
    along, palm, _ = frame(rig, wrist, chains, right)
    # Unity turns the run toward the palm about Cross(along, palm); here that same turn is about along × palm.
    side = along.cross(palm)
    for finger in FINGERS[:4]:
        chain = chains[finger]
        angle = 0.0
        for i in range(3):
            angle += curl * CURL_SHARE[i]
            _turn_to(rig, chain, i, Matrix.Rotation(math.radians(angle), 3, side) @ along)
            update()
    share = min(max(thumb / 90.0, 0.0), 1.0)
    if share <= 0:
        return
    # Unity's over: along * .5 + palm * .6 + (right ? 1 : -1) * Cross(along, palm) * .45, where its cross points the way
    # palm × along does here.
    over = (along * .5 + palm * .6 + palm.cross(along) * (.45 if right else -.45)).normalized()
    chain = chains['thumb']
    for i in range(3):
        if i > 0:
            # Unity: over turned 15 (then 20) degrees about Cross(over, palm), the same turn as about over × palm here.
            bend = over.cross(palm)
            if bend.length > 1e-8:
                over = Matrix.Rotation(math.radians(15 if i == 1 else 20), 3, bend.normalized()) @ over
        _turn_to(rig, chain, i, over, share)
        update()


# ---- A body clip's finger track (FingerTrack in the game) ---------------------------------------------------------------

# A body clip's track is the table named after the clip with this after it (Tables/Clips/<clip>_fingers.csv).
TRACK_SUFFIX = '_fingers'
# Unity's humanoid names for the fingers (the .fbx.meta's humanName).
HUMAN = {'index': 'Index', 'middle': 'Middle', 'ring': 'Ring', 'little': 'Little', 'thumb': 'Thumb'}


def human_map(meta_path):
    """Unity's humanoid map from a model's .fbx.meta: human name (LeftHand, Left Index Proximal, ...) -> bone name."""
    import re
    with open(meta_path, encoding='utf-8') as f:
        text = f.read()
    return {human.strip(): bone.strip() for bone, human in re.findall(r'- boneName: (.+)\n\s+humanName: (.+)\n', text)}


def hand_of(human, side):
    """One hand's wrist bone and finger chains from a humanoid map (side: 'Left' or 'Right'), as measure() takes them."""
    wrist = human[f'{side}Hand']
    chains = {f: tuple(human[f'{side} {HUMAN[f]} {part}'] for part in ('Proximal', 'Intermediate', 'Distal'))
              for f in FINGERS}
    return wrist, chains


def track_rows(context, rig, left, right, start, end, fps):
    """Both hands at every frame from start to end, the rate the body's FBX bakes the clip at: [(time, left ways,
    right ways)], time in seconds from start. left and right: (wrist, chains). The scene is put back on start."""
    scene = context.scene
    rows = []
    for frame in range(int(start), int(end) + 1):
        scene.frame_set(frame)
        context.view_layer.update()
        rows.append(((frame - start) / fps, measure(rig, *left, right=False), measure(rig, *right, right=True)))
    scene.frame_set(int(start))
    return rows


def track_text(rows, note):
    """A finger track's table as the game reads it (FingerTrack): id and time, the left hand's 60 columns (prefix l), the
    right hand's (prefix r); a description row led by the note (it starts with #, so the game skips it); a row a frame,
    every number to the sixth decimal."""
    header = ['id', 'time'] + columns('l') + columns('r')
    described = [('#설명 ' + note).replace(',', ' ·'), '클립 안의 초(원본 빠르기)'] + descriptions('왼손 ') + descriptions('오른손 ')
    lines = ['﻿' + ','.join(header), ','.join(c.replace(',', ' ·') for c in described)]
    for n, (time, left, right) in enumerate(rows):
        lines.append(','.join([f'k{n:04d}', f'{time:.6f}'] + cells(left) + cells(right)))
    return '\n'.join(lines) + '\n'
