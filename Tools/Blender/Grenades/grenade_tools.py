"""ENFILADE throwables workspace, Blender 4.5 LTS.

START_GRENADE_ANIMATION.cmd opens ArtSource/Grenades/ENFILADE_Grenade_Animation.blend with truefps_tools.py and then
this (the user, 2026-11-12: "수류탄 던지는 모션도 아쉬운데 드는 모션, 대기 모션, 던지는 모션 내가 블렌더로 수정할 수 있게 만들어서
적용해 봐"). The off hand's clips for G are drafted on Kazusa's free left arm (CTRL_hand.L, CTRL_elbow.L and the left
fingers) while the right hand keeps the gun: each is a locked draft layer under an empty touch-up layer (Combine), as in
the shove workspace (ArtSource/Shove, whose tools this follows but does not share, so neither breaks the other):
- 수류탄 · 꺼내기 / 대기 (되풀이) / 던지기 / 톡 던지기: the grenade's (every throwable but the knife). The draw goes from the
  gun to the belt and up, the pin at 핀; the hold is the cooking loop; the throw winds up and lets go at 놓기; the tap is
  Call of Duty's quick throw in one motion.
- 나이프 · 꺼내기 / 대기 (되풀이) / 던지기: the Knight's throwing knife, the MW2019 viewmodel of GMod's "[MW] Quick Throwing
  Knife" mirrored onto the left hand bone for bone (build_grenade_workspace.py), its hold MW's still idle with a breath.
"게임에 보내기" writes Assets/_Project/Resources/Tables/<table>.csv: per half frame, the wrist and the elbow from the eyes
in game metres, the hand's run and facing, each finger's curl and the thumb's, in the head camera's axes as Unity's (x
right, y up, z forward), whether the throwable is in the hand (from 쥐기 to 놓기), and the 핀 and 놓기 moments. GrenadeArm
plays them on any character's left arm (Tables/ThrowArms names which clip is which). Every button keys at the current
frame with auto-key on, holding the pose some frames either side, so a click is one local change. Nothing here changes
Blender's preferences.
"""
import json
import math
from pathlib import Path

import bpy
from bpy.app.handlers import persistent
from bpy.props import FloatProperty, IntProperty, StringProperty
from mathutils import Matrix, Vector

bl_info = {'name': 'ENFILADE Throwables', 'author': 'ENFILADE', 'version': (1, 0, 0), 'blender': (4, 5, 0),
           'location': '3D View > Sidebar > 투척', 'category': 'Animation'}

# Kazusa in the game is this many times her size in this file (eyes 1.37 here, 1.6 m in game: PlayerBody's fit).
GAME_SCALE = 1.166
BASE_TRACK = '1 시안 (잠금)'
CAMERA = 'FPS_Head_Camera'
TABLES_DIR = Path('Assets/_Project/Resources/Tables')
# Two samples a frame at 30 fps, so the game's curve follows the polish closely.
SAMPLES_PER_FRAME = 2
ARM_HAND, ARM_ELBOW = 'CTRL_hand.L', 'CTRL_elbow.L'
ARM_BONES = dict(shoulder='Left arm', elbow='Left elbow', wrist='Left wrist')
# The left fingers, knuckle out; the thumb from its root in the palm.
FINGERS = {
    'index': ('IndexFinger1_L', 'IndexFinger2_L', 'IndexFinger3_L'),
    'middle': ('MiddleFinger1_L', 'MiddleFinger2_L', 'MiddleFinger3_L'),
    'ring': ('RingFinger1_L', 'RingFinger2_L', 'RingFinger3_L'),
    'little': ('LittleFinger1_L', 'LittleFinger2_L', 'LittleFinger3_L'),
    'thumb': ('Thumb0_L', 'Thumb1_L', 'Thumb2_L'),
}
FOUR = ('index', 'middle', 'ring', 'little')
# A finger's whole curl shared over its joints, knuckle first (GrenadeArm.CurlShare: the game closes them so).
CURL_SHARE = (.4, .35, .25)
# The thumb's two bends (root to middle, middle to tip) are this much of what the game calls the thumb's closing
# (GrenadeArm: 90 is fully over the throwable).
THUMB_SCALE = 1.3

# The timeline marks: when the throwable comes into the hand, when the pin comes out (its sound; a cooking one's fuse
# starts) and when it leaves the hand (the throw happens there, on every PC).
GRAB, PIN, RELEASE = '쥐기', '핀', '놓기'
MARKS = (GRAB, PIN, RELEASE)

# The clips. look: which preview rides in the hand. marks: the frames of the moments (the user moves them on the
# timeline; each clip keeps its own). loop: the hold, played round and round while G is held.
MOVES = {
    'grenade_draw': dict(label='수류탄 · 꺼내기', group='수류탄', action='enf_grenade_draw', table='GrenadeDraw', end=14,
                         marks={GRAB: 6, PIN: 12}, look='grenade'),
    'grenade_hold': dict(label='수류탄 · 대기 (되풀이)', group='수류탄', action='enf_grenade_hold', table='GrenadeHold', end=37,
                         marks={}, look='grenade', loop=True),
    'grenade_throw': dict(label='수류탄 · 던지기', group='수류탄', action='enf_grenade_throw', table='GrenadeThrow', end=19,
                          marks={RELEASE: 8}, look='grenade'),
    'grenade_tap': dict(label='수류탄 · 톡 던지기', group='수류탄', action='enf_grenade_tap', table='GrenadeTap', end=19,
                        marks={GRAB: 4, PIN: 6, RELEASE: 10}, look='grenade'),
    'knife_draw': dict(label='나이프 · 꺼내기', group='나이프', action='enf_knife_draw', table='KnifeDraw', end=19,
                       marks={GRAB: 1}, look='knife'),
    'knife_hold': dict(label='나이프 · 대기 (되풀이)', group='나이프', action='enf_knife_hold', table='KnifeHold', end=49,
                       marks={}, look='knife', loop=True),
    'knife_throw': dict(label='나이프 · 던지기', group='나이프', action='enf_knife_throw', table='KnifeThrow', end=26,
                        marks={RELEASE: 11}, look='knife'),
}
# The previews in the hand (never exported): the game's own models, seated as the game seats them (Tables/Grenades held*).
LOOKS = {'grenade': 'PREVIEW_Grenade', 'knife': 'PREVIEW_Knife'}

ARM_PARTS = (('왼손', ARM_HAND), ('왼팔꿈치 방향', ARM_ELBOW))
MOVE_DIRS = (('forward', '앞으로'), ('back', '뒤로'), ('up', '위로'), ('down', '아래로'), ('left', '왼쪽'), ('right', '오른쪽'))
HAND_TURNS = (('wrist_up', '손목 위로 젖히기'), ('wrist_down', '손목 아래로 꺾기'), ('wrist_left', '손목 왼쪽'), ('wrist_right', '손목 오른쪽'),
              ('twist_left', '손목 왼쪽으로 비틀기'), ('twist_right', '손목 오른쪽으로 비틀기'))
FINGER_PARTS = (('네 손가락', FOUR), ('검지', ('index',)), ('엄지', ('thumb',)))


# ---- The file -------------------------------------------------------------------------------------------------------

def project_root():
    """The ENFILADE checkout: from this file, else from the open .blend (ArtSource/Grenades/...)."""
    starts = []
    try:
        starts.append(Path(__file__).resolve())
    except (NameError, OSError):
        pass
    if bpy.data.filepath:
        starts.append(Path(bpy.data.filepath).resolve())
    for start in starts:
        for parent in start.parents:
            if (parent / 'Assets' / '_Project').is_dir() and (parent / 'ProjectSettings').is_dir():
                return parent
    return None


def find_rig(scene):
    return next((o for o in scene.objects if o.type == 'ARMATURE' and o.get('truefps_rig')), None)


def view_basis():
    """The head camera (the first-person view): where it is and its right, up and forward, in the file's world."""
    m = bpy.data.objects[CAMERA].matrix_world
    return m.translation.copy(), m.col[0].xyz.normalized(), m.col[1].xyz.normalized(), -m.col[2].xyz.normalized()


def to_world(v, basis, point=False):
    """A vector in the view's axes (x right, y up, z forward; game metres for a point) into the file's world."""
    origin, right, up, forward = basis
    w = right * v[0] + up * v[1] + forward * v[2]
    return origin + w / GAME_SCALE if point else w


def to_view(w, basis, point=False):
    """The file's world into the view's axes (game metres for a point)."""
    origin, right, up, forward = basis
    d = (w - origin) * GAME_SCALE if point else w
    return Vector((d.dot(right), d.dot(up), d.dot(forward)))


def head(rig, name):
    return rig.matrix_world @ rig.pose.bones[name].head


def tail(rig, name):
    return rig.matrix_world @ rig.pose.bones[name].tail


def hand_frame(rig):
    """The left hand as the game reads it (world): the wrist, its run (wrist to middle knuckle), its palm's facing
    (Blender's right-handed cross; GrenadeArm.PalmNow's in Unity's axes) and its length."""
    wrist = head(rig, ARM_BONES['wrist'])
    index, middle, little = head(rig, FINGERS['index'][0]), head(rig, FINGERS['middle'][0]), head(rig, FINGERS['little'][0])
    along = (middle - wrist).normalized()
    palm = along.cross(index - little)
    palm = (palm - along * palm.dot(along)).normalized()
    return wrist, along, palm, (middle - wrist).length


_rest_ways = {}


def rest_way(rig, name):
    """The way a finger bone runs along its finger, in the bone's own axes: toward the next joint, the last one going on
    the way the one before it ran, as the rig stands at rest. Its tail is no guide: this rig's finger bones point their
    tails across the finger (95 degrees off, as the avatar's import made them)."""
    key = (rig.name, name)
    if key not in _rest_ways:
        bones = rig.data.bones
        names = next(chain for chain in FINGERS.values() if name in chain)
        i = names.index(name)
        bone = bones[name]
        if i + 1 < len(names):
            d = bones[names[i + 1]].head_local - bone.head_local
        else:
            d = bone.head_local - bones[names[i - 1]].head_local
        _rest_ways[key] = (bone.matrix_local.to_3x3().inverted() @ d).normalized()
    return _rest_ways[key]


def bone_way(rig, name):
    """Which way a finger bone runs now (world)."""
    return (rig.matrix_world.to_3x3() @ rig.pose.bones[name].matrix.to_3x3() @ rest_way(rig, name)).normalized()


def segments(rig, finger):
    """A finger's three segments' ways (world), knuckle out."""
    return [bone_way(rig, n) for n in FINGERS[finger]]


def _angle(a, b):
    return math.degrees(a.angle(b)) if a.length > 1e-9 and b.length > 1e-9 else 0.0


def curls(rig):
    """Each finger's curl (degrees: the hand's run to the first segment, then each joint) and the thumb's closing, as the
    game's columns have them."""
    _, along, _, _ = hand_frame(rig)
    out = {}
    for finger in FOUR:
        s = segments(rig, finger)
        out[finger] = _angle(along, s[0]) + _angle(s[0], s[1]) + _angle(s[1], s[2])
    t = segments(rig, 'thumb')
    out['thumb'] = (_angle(t[0], t[1]) + _angle(t[1], t[2])) * THUMB_SCALE
    return out


# ---- Layers: a locked draft strip under the touch-up action --------------------------------------------------------

def touch_up(name):
    action = bpy.data.actions.get(name)
    if action is None:
        action = bpy.data.actions.new(name)
    action.use_fake_user = True
    action['enf_layer'] = 'touch-up'
    return action


def set_layers(obj, draft, edit):
    """obj plays draft as its locked base and keys into edit on top (Combine)."""
    data = obj.animation_data or obj.animation_data_create()
    track = data.nla_tracks.get(BASE_TRACK)
    if track is None:
        track = data.nla_tracks.new()
        track.name = BASE_TRACK
    track.lock = False
    while len(track.strips):
        track.strips.remove(track.strips[0])
    if draft is not None:
        strip = track.strips.new(draft.name, int(draft.frame_range[0]), draft)
        if hasattr(strip, 'action_slot') and strip.action_slot is None and len(getattr(draft, 'slots', [])):
            strip.action_slot = draft.slots[0]
        strip.blend_type = 'REPLACE'
        strip.extrapolation = 'HOLD'
    track.lock = True
    track.mute = False
    data.action = edit
    if edit is not None and hasattr(data, 'action_slot') and data.action_slot is None and len(getattr(edit, 'slots', [])):
        data.action_slot = edit.slots[0]
    data.action_blend_type = 'COMBINE'
    data.action_extrapolation = 'HOLD'
    data.action_influence = 1.0


def reset_rig(rig):
    """The rig's handles and fingers at the start pose (True FPS's truefps_default_pose)."""
    if rig is None:
        return
    for name, pose in json.loads(rig.get('truefps_default_pose', '{}')).items():
        pb = rig.pose.bones.get(name)
        if pb is None:
            continue
        if 'location' in pose:
            pb.location = pose['location']
        if 'rotation_quaternion' in pose and pb.rotation_mode == 'QUATERNION':
            pb.rotation_quaternion = pose['rotation_quaternion']
        if 'scale' in pose:
            pb.scale = pose['scale']


def current_move(scene):
    key = scene.get('enf_throw_move', 'grenade_throw')
    return key if key in MOVES else 'grenade_throw'


def marks_of(scene, key):
    """A clip's moments (frames): as last left on the timeline, else the draft's."""
    try:
        kept = json.loads(scene.get('enf_marks_' + key, ''))
        if isinstance(kept, dict):
            return {k: int(v) for k, v in kept.items() if k in MARKS}
    except (ValueError, TypeError):
        pass
    return dict(MOVES[key]['marks'])


def keep_marks(scene, key):
    scene['enf_marks_' + key] = json.dumps({m.name: m.frame for m in scene.timeline_markers if m.name in MARKS})
    scene['enf_end_' + key] = scene.frame_end


def select_move(context, key):
    """Shows one clip: its layers, its frames, its moments on the timeline and its throwable in the hand."""
    scene = context.scene
    previous = scene.get('enf_throw_move')
    if previous in MOVES:
        keep_marks(scene, previous)
    move = MOVES[key]
    rig = find_rig(scene)
    reset_rig(rig)
    set_layers(rig, bpy.data.actions.get(move['action'] + '_draft'), touch_up(move['action']))
    for marker in [m for m in scene.timeline_markers if m.name in MARKS]:
        scene.timeline_markers.remove(marker)
    for name, frame in marks_of(scene, key).items():
        scene.timeline_markers.new(name, frame=frame)
    scene.frame_start, scene.frame_end = 1, int(scene.get('enf_end_' + key, move['end']))
    scene['enf_throw_move'] = key
    scene.frame_set(1)
    show_held(scene)
    scene.tool_settings.use_keyframe_insert_auto = True


def held_window(scene, key):
    """The frames the throwable is in the hand: from 쥐기 (else the start) to before 놓기 (else the end)."""
    marks = {m.name: m.frame for m in scene.timeline_markers if m.name in MARKS}
    return marks.get(GRAB, scene.frame_start), marks.get(RELEASE, scene.frame_end + 1)


def show_held(scene):
    """Only the clip's own throwable shows, and only while it is in the hand."""
    key = current_move(scene)
    look = MOVES[key]['look']
    first, last = held_window(scene, key)
    held = first <= scene.frame_current < last
    for name, holder in LOOKS.items():
        obj = bpy.data.objects.get(holder)
        if obj is None:
            continue
        show = name == look and held
        for part in [obj] + list(obj.children_recursive):
            if part.hide_viewport == show:
                part.hide_viewport = not show
            part.hide_render = not show


@persistent
def _frame_changed(scene, _depsgraph=None):
    try:
        if find_rig(scene) is not None and 'enf_throw_move' in scene:
            show_held(scene)
    except (ReferenceError, KeyError, AttributeError):
        pass


# ---- Moving a part and keying it -----------------------------------------------------------------------------------

def _curves(action):
    if action is None:
        return []
    if getattr(action, 'is_action_legacy', True) and hasattr(action, 'fcurves'):
        try:
            return list(action.fcurves)
        except (AttributeError, RuntimeError):
            pass
    found = []
    for layer in getattr(action, 'layers', []):
        for strip in layer.strips:
            for bag in getattr(strip, 'channelbags', []):
                found.extend(bag.fcurves)
    return found


def _keyed_at(rig, path, frame):
    data = rig.animation_data
    action = data.action if data else None
    for curve in _curves(action):
        if curve.data_path == path and any(abs(p.co.x - frame) < .5 for p in curve.keyframe_points):
            return True
    return False


def _key(rig, bone):
    pb = rig.pose.bones[bone]
    frame = bpy.context.scene.frame_current
    if bone != ARM_ELBOW:
        pb.keyframe_insert('rotation_quaternion', frame=frame, group=bone)
    if bone in (ARM_HAND, ARM_ELBOW):
        pb.keyframe_insert('location', frame=frame, group=bone)


def change(context, rig, bones, reach, apply):
    """At the current frame: holds the bones' pose reach frames either side first, then apply() changes it, and keys
    them into the touch-up layer (Blender works out the touch-up that gives what is shown over the draft)."""
    scene = context.scene
    frame = scene.frame_current
    for at in (frame - reach, frame + reach) if reach > 0 else ():
        if scene.frame_start <= at <= scene.frame_end:
            missing = [b for b in bones if not _keyed_at(rig, f'pose.bones["{b}"].rotation_quaternion', at)
                       and not _keyed_at(rig, f'pose.bones["{b}"].location', at)]
            if missing:
                scene.frame_set(at)
                for bone in missing:
                    _key(rig, bone)
    scene.frame_set(frame)
    context.view_layer.update()
    apply()
    context.view_layer.update()
    for bone in bones:
        _key(rig, bone)


def turn_bone(rig, name, matrix_world_turn):
    """Turns a pose bone about its own head by a world-space turn (3x3)."""
    pb = rig.pose.bones[name]
    inverse = rig.matrix_world.to_3x3().inverted()
    turn = (inverse @ matrix_world_turn @ rig.matrix_world.to_3x3()).to_4x4()
    at = pb.matrix.translation.copy()
    pb.matrix = Matrix.Translation(at) @ turn @ Matrix.Translation(-at) @ pb.matrix


def nudge_arm(context, bone, how, step_cm, step_deg, reach):
    """Moves the left hand or the elbow handle along the view's axes, or turns the hand about them round its own place."""
    rig = find_rig(context.scene)
    _, right, up, forward = view_basis()
    moves = {'forward': forward, 'back': -forward, 'up': up, 'down': -up, 'left': -right, 'right': right}
    turns = {'wrist_up': (right, 1), 'wrist_down': (right, -1), 'wrist_left': (up, 1), 'wrist_right': (up, -1),
             'twist_left': (forward, -1), 'twist_right': (forward, 1)}

    def apply():
        pb = rig.pose.bones[bone]
        if how in moves:
            way = rig.matrix_world.to_3x3().inverted() @ moves[how]
            matrix = pb.matrix.copy()
            matrix.translation = matrix.translation + way.normalized() * (step_cm / 100.0)
            pb.matrix = matrix
        else:
            axis, sign = turns[how]
            turn_bone(rig, bone, Matrix.Rotation(math.radians(step_deg) * sign, 3, axis))
    change(context, rig, [bone], reach, apply)


def bend_finger(rig, finger, degrees):
    """Closes (or opens, below 0) a finger by that much in all, shared over its joints as the game shares a curl; the
    thumb bends its two outer joints."""
    _, along, palm, _ = hand_frame(rig)
    names = FINGERS[finger]
    if finger == 'thumb':
        shares = (0.0, .5, .5)
    else:
        shares = CURL_SHARE
    for name, share in zip(names, shares):
        if share <= 0:
            continue
        bpy.context.view_layer.update()
        # Closing turns a segment from the hand's run toward the palm: about the hand's side (run x palm), the same
        # way however far it is curled already (as GrenadeArm.Fingers closes them); the thumb about the axis square
        # to its own way and the palm.
        axis = along.cross(palm) if finger != 'thumb' else bone_way(rig, name).cross(palm)
        if axis.length < 1e-6:
            continue
        turn_bone(rig, name, Matrix.Rotation(math.radians(degrees * share), 3, axis.normalized()))


def nudge_fingers(context, fingers, how, step_deg, reach):
    rig = find_rig(context.scene)
    bones = [b for f in fingers for b in FINGERS[f]]

    def apply():
        for finger in fingers:
            bend_finger(rig, finger, step_deg * (1 if how == 'close' else -1))
    change(context, rig, bones, reach, apply)


def clear_touch_ups(context, frame):
    """Removes the touch-up keys at one frame of the clip shown; returns how many went."""
    rig = find_rig(context.scene)
    action = rig.animation_data.action if rig and rig.animation_data else None
    removed = 0
    for curve in _curves(action):
        for point in [p for p in curve.keyframe_points if abs(p.co.x - frame) < .5]:
            curve.keyframe_points.remove(point, fast=True)
            removed += 1
        curve.update()
    return removed


# ---- To the game ---------------------------------------------------------------------------------------------------

HEADER = ['id', 'cycle', 'wristX', 'wristY', 'wristZ', 'elbowX', 'elbowY', 'elbowZ', 'alongX', 'alongY', 'alongZ',
          'palmX', 'palmY', 'palmZ', 'curl', 'curlIndex', 'curlMiddle', 'curlRing', 'curlLittle', 'thumb', 'held', 'pin', 'release']


def clip_rows(context, key):
    """The left arm over the clip's frames as the game's clips have it: per half frame, the wrist and the elbow from
    the eyes in game metres, the hand's run and its palm's facing, in the head camera's axes as Unity's; each finger's
    curl and the thumb's; whether the throwable is in the hand; whether this sample is the pin or the release."""
    scene = context.scene
    rig = find_rig(scene)
    fps = scene.render.fps / scene.render.fps_base
    start, end = scene.frame_start, scene.frame_end
    marks = {m.name: m.frame for m in scene.timeline_markers if m.name in MARKS}
    first, last = held_window(scene, key)
    rows = []
    for i in range((end - start) * SAMPLES_PER_FRAME + 1):
        at = start + i / SAMPLES_PER_FRAME
        frame = int(math.floor(at))
        scene.frame_set(frame, subframe=at - frame)
        context.view_layer.update()
        basis = view_basis()
        wrist, along, palm, _ = hand_frame(rig)
        elbow = head(rig, ARM_BONES['elbow'])
        c = curls(rig)
        rows.append(dict(
            cycle=(at - start) / fps, wrist=to_view(wrist, basis, True), elbow=to_view(elbow, basis, True),
            along=to_view(along, basis), palm=to_view(palm, basis), curls=c,
            held=first <= at < last,
            pin=PIN in marks and abs(at - marks[PIN]) < 1e-6,
            release=RELEASE in marks and abs(at - marks[RELEASE]) < 1e-6))
    scene.frame_set(start)
    return rows


def export_clip(context, key):
    root = project_root()
    if root is None:
        raise RuntimeError('ENFILADE 프로젝트를 찾지 못했습니다. ArtSource/Grenades의 파일을 여세요.')
    move = MOVES[key]
    keep_marks(context.scene, key)
    path = root / TABLES_DIR / (move['table'] + '.csv')
    path.parent.mkdir(parents=True, exist_ok=True)
    loop = '되풀이 클립: 끝 줄이 첫 줄과 같아야 이어진다. ' if move.get('loop') else ''
    note = ('"#설명 (블렌더 투척 작업실 ArtSource/Grenades의 ' + move['label'] + ' 클립을 게임에 보내기로 쓴 왼팔. ' + loop +
            '손으로 고치지 말고 블렌더에서 고쳐 다시 보낸다. 어느 클립을 언제 쓰는지는 ThrowArms.csv. GrenadeArm이 어느 캐릭터 팔에든 맞춘다)"')
    cells = ['클립의 초(원본 빠르기. ThrowArms의 빠르기 배수가 곱해진다)',
             '눈에서 손목까지 오른쪽(m)', '눈에서 손목까지 위(m)', '눈에서 손목까지 앞(m)',
             '눈에서 팔꿈치까지 오른쪽(m). 팔꿈치가 이쪽으로 굽는다', '눈에서 팔꿈치까지 위(m)', '눈에서 팔꿈치까지 앞(m)',
             '손이 뻗은 쪽(손목→가운데 손가락 뿌리) 오른쪽', '손이 뻗은 쪽 위', '손이 뻗은 쪽 앞',
             '손바닥이 향한 쪽 오른쪽', '손바닥이 향한 쪽 위', '손바닥이 향한 쪽 앞',
             '네 손가락이 굽은 정도의 평균(°)', '검지가 굽은 정도(° · 마디마다 더함)', '중지(°)', '약지(°)', '새끼(°)',
             '엄지가 접힌 정도(90이면 투척물 위로 다 덮음)', '1이면 투척물이 손에 있다(쥐기~놓기 전)',
             '1이면 이 순간에 핀을 뽑는다(소리 · 쿠킹 시작)', '1이면 이 순간에 손을 떠난다(던짐)']
    # A cell never holds a comma (the tables are plain comma lists).
    cells = [c.replace(',', ' ·') for c in cells]
    lines = ['﻿' + ','.join(HEADER), note + ',' + ','.join(cells)]
    for n, row in enumerate(clip_rows(context, key)):
        c = row['curls']
        mean = sum(c[f] for f in FOUR) / 4.0
        values = [f'k{n:03d}', f'{row["cycle"]:.4f}'] + [f'{v:.4f}' for v in (*row['wrist'], *row['elbow'], *row['along'], *row['palm'])]
        values += [f'{mean:.1f}'] + [f'{c[f]:.1f}' for f in FOUR] + [f'{c["thumb"]:.1f}']
        values += ['1' if row['held'] else '0', '1' if row['pin'] else '0', '1' if row['release'] else '0']
        lines.append(','.join(values))
    path.write_text('\n'.join(lines) + '\n', encoding='utf-8')
    return path


# ---- Panel ---------------------------------------------------------------------------------------------------------

class ENFThrowSettings(bpy.types.PropertyGroup):
    step_cm: FloatProperty(name='옮기는 거리 (cm)', default=2.0, min=.1, max=20.0)
    step_deg: FloatProperty(name='돌리는 각도 (도)', default=5.0, min=.5, max=45.0)
    reach: IntProperty(name='섞는 길이 (프레임)', default=2, min=0, max=10,
                       description='누른 프레임에서만 바뀌고 이만큼 앞뒤 프레임은 그대로 둔다')


def _ok(op, context):
    if find_rig(context.scene) is None or CAMERA not in bpy.data.objects:
        op.report({'ERROR'}, 'ENFILADE_Grenade_Animation.blend를 열어 주세요.')
        return False
    return True


def _object_mode(context):
    if context.object and context.object.mode not in ('OBJECT', 'POSE'):
        bpy.ops.object.mode_set(mode='OBJECT')


class ENFTHROW_OT_pick(bpy.types.Operator):
    bl_idname = 'enf_throw.pick'
    bl_label = '클립 고르기'
    bl_description = '이 클립의 시안과 손질 층을 보여 준다'
    bl_options = {'REGISTER', 'UNDO'}
    move: StringProperty()

    def execute(self, context):
        if not _ok(self, context) or self.move not in MOVES:
            return {'CANCELLED'}
        _object_mode(context)
        select_move(context, self.move)
        return {'FINISHED'}


class ENFTHROW_OT_mark(bpy.types.Operator):
    bl_idname = 'enf_throw.mark'
    bl_label = '순간으로 가기'
    bl_description = '이 순간의 프레임으로 간다'
    mark: StringProperty()

    def execute(self, context):
        marker = context.scene.timeline_markers.get(self.mark)
        if marker:
            context.scene.frame_set(marker.frame)
        return {'FINISHED'}


class ENFTHROW_OT_nudge(bpy.types.Operator):
    bl_idname = 'enf_throw.nudge'
    bl_label = '움직이기'
    bl_description = '지금 프레임에서 고른 곳을 조금 옮기거나 돌리고 손질 층에 키를 넣는다'
    bl_options = {'REGISTER', 'UNDO'}
    part: StringProperty()
    how: StringProperty()

    def execute(self, context):
        if not _ok(self, context):
            return {'CANCELLED'}
        settings = context.scene.enf_throw
        try:
            if self.part.startswith('fingers:'):
                nudge_fingers(context, self.part.split(':', 1)[1].split(','), self.how, settings.step_deg, settings.reach)
            else:
                nudge_arm(context, self.part, self.how, settings.step_cm, settings.step_deg, settings.reach)
        except (KeyError, ValueError) as error:
            self.report({'ERROR'}, str(error))
            return {'CANCELLED'}
        return {'FINISHED'}


class ENFTHROW_OT_toggle(bpy.types.Operator):
    bl_idname = 'enf_throw.toggle'
    bl_label = '손질 층 켜기/끄기'
    bl_description = '손질을 끄고 시안만 보거나 다시 켠다(지우지 않는다)'

    def execute(self, context):
        rig = find_rig(context.scene)
        data = rig.animation_data if rig else None
        if data is not None:
            data.action_influence = 0.0 if data.action_influence > .5 else 1.0
            self.report({'INFO'}, '손질 층 ' + ('켜짐' if data.action_influence > .5 else '꺼짐 (시안만)'))
        return {'FINISHED'}


class ENFTHROW_OT_clear(bpy.types.Operator):
    bl_idname = 'enf_throw.clear'
    bl_label = '이 프레임 손질 지우기'
    bl_description = '지금 프레임에 넣은 손질 키를 지운다(시안은 그대로)'
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        removed = clear_touch_ups(context, context.scene.frame_current)
        self.report({'INFO'}, f'손질 키 {removed}개를 지웠습니다')
        return {'FINISHED'}


class ENFTHROW_OT_export(bpy.types.Operator):
    bl_idname = 'enf_throw.export'
    bl_label = '게임에 보내기'
    bl_description = '보고 있는 클립을 게임 폴더로 내보낸다. Unity가 알아서 가져가고, Play 중이어도 다음 G부터 그 동작이 나온다'
    every: bpy.props.BoolProperty(default=False)

    def execute(self, context):
        if not _ok(self, context):
            return {'CANCELLED'}
        scene = context.scene
        keys = list(MOVES) if self.every else [current_move(scene)]
        shown = current_move(scene)
        sent = []
        try:
            for key in keys:
                if key != current_move(scene):
                    select_move(context, key)
                sent.append(export_clip(context, key))
        except (RuntimeError, ValueError, OSError) as error:
            self.report({'ERROR'}, str(error))
            return {'CANCELLED'}
        finally:
            if current_move(scene) != shown:
                select_move(context, shown)
        self.report({'INFO'}, f'보냈습니다: {sent[0]}' if len(sent) == 1 else f'{len(sent)}개 클립을 보냈습니다')
        return {'FINISHED'}


class ENFTHROW_PT_panel(bpy.types.Panel):
    bl_label = 'ENFILADE 투척 동작'
    bl_idname = 'ENFTHROW_PT_panel'
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = '투척'

    def draw(self, context):
        scene = context.scene
        layout = self.layout
        key = current_move(scene)
        move = MOVES[key]
        fps = scene.render.fps / scene.render.fps_base
        box = layout.box()
        box.label(text='1. 클립 고르기')
        for group in ('수류탄', '나이프'):
            col = box.column(align=True)
            for k, v in MOVES.items():
                if v['group'] == group:
                    col.operator('enf_throw.pick', text=v['label'], depress=k == key).move = k
        box.label(text=f'끝: {scene.frame_end}프레임 ({(scene.frame_end - scene.frame_start) / fps:.3f}초, 원본 빠르기)')
        for marker in sorted((m for m in scene.timeline_markers if m.name in MARKS), key=lambda m: m.frame):
            row = box.row(align=True)
            row.label(text=f'{marker.name}: {marker.frame}프레임 ({(marker.frame - scene.frame_start) / fps:.3f}초)')
            row.operator('enf_throw.mark', text='', icon='TIME').mark = marker.name
        if move.get('loop'):
            box.label(text='되풀이: 끝 프레임이 처음과 같아야 이어진다')
        else:
            box.label(text='표시(쥐기·핀·놓기)는 타임라인에서 클릭한 뒤 G로 옮긴다')

        box = layout.box()
        box.label(text='2. 손보기 (지금 프레임에 키가 들어감)')
        settings = scene.enf_throw
        box.prop(settings, 'step_cm')
        box.prop(settings, 'step_deg')
        box.prop(settings, 'reach')
        for label, part in ARM_PARTS:
            col = box.column(align=True)
            col.label(text=label)
            grid = col.grid_flow(columns=2, align=True)
            for how, text in MOVE_DIRS:
                op = grid.operator('enf_throw.nudge', text=text)
                op.part, op.how = part, how
            if part == ARM_HAND:
                grid = col.grid_flow(columns=2, align=True)
                for how, text in HAND_TURNS:
                    op = grid.operator('enf_throw.nudge', text=text)
                    op.part, op.how = part, how
        col = box.column(align=True)
        col.label(text='손가락 (돌리는 각도만큼)')
        for label, fingers in FINGER_PARTS:
            row = col.row(align=True)
            for how, text in (('close', label + ' 쥐기'), ('open', label + ' 펴기')):
                op = row.operator('enf_throw.nudge', text=text)
                op.part, op.how = 'fingers:' + ','.join(fingers), how
        row = box.row(align=True)
        row.operator('enf_throw.toggle')
        row.operator('enf_throw.clear')

        box = layout.box()
        box.label(text='3. 게임에 보내기')
        box.operator('enf_throw.export', icon='EXPORT').every = False
        box.operator('enf_throw.export', text='모든 클립 보내기').every = True
        box.label(text=str(TABLES_DIR / (move['table'] + '.csv')).replace('\\', '/'))


CLASSES = (ENFThrowSettings, ENFTHROW_OT_pick, ENFTHROW_OT_mark, ENFTHROW_OT_nudge, ENFTHROW_OT_toggle, ENFTHROW_OT_clear,
           ENFTHROW_OT_export, ENFTHROW_PT_panel)


def _show_panel():
    for window in bpy.context.window_manager.windows:
        for area in window.screen.areas:
            if area.type == 'VIEW_3D' and area.spaces.active.region_3d.view_perspective != 'CAMERA':
                area.spaces.active.show_region_ui = True
                region = next((r for r in area.regions if r.type == 'UI'), None)
                if region is not None:
                    try:
                        region.active_panel_category = '투척'
                    except (TypeError, AttributeError):
                        pass
    scene = bpy.context.scene
    if scene and find_rig(scene) is not None and 'enf_throw_move' in scene:
        scene.tool_settings.use_keyframe_insert_auto = True
        show_held(scene)
    return None


def register():
    for cls in CLASSES:
        if hasattr(bpy.types, cls.__name__):
            try:
                bpy.utils.unregister_class(getattr(bpy.types, cls.__name__))
            except RuntimeError:
                pass
        bpy.utils.register_class(cls)
    bpy.types.Scene.enf_throw = bpy.props.PointerProperty(type=ENFThrowSettings)
    handlers = bpy.app.handlers.frame_change_post
    for fn in [h for h in handlers if getattr(h, '__name__', '') == '_frame_changed']:
        handlers.remove(fn)
    handlers.append(_frame_changed)
    if not bpy.app.background:
        # After True FPS's own start-up (0.2 s), which opens its own tab.
        bpy.app.timers.register(_show_panel, first_interval=.6)


def unregister():
    handlers = bpy.app.handlers.frame_change_post
    for fn in [h for h in handlers if getattr(h, '__name__', '') == '_frame_changed']:
        handlers.remove(fn)
    del bpy.types.Scene.enf_throw
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)


if __name__ == '__main__':
    register()
