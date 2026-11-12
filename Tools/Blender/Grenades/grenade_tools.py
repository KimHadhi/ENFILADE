"""ENFILADE throwables workspace, Blender 4.5 LTS.

START_GRENADE_ANIMATION.cmd opens ArtSource/Grenades/ENFILADE_Grenade_Animation.blend with truefps_tools.py and then
this (the user, 2026-11-12: "수류탄 던지는 모션도 아쉬운데 드는 모션, 대기 모션, 던지는 모션 내가 블렌더로 수정할 수 있게 만들어서
적용해 봐"). The off hand's clips for G are drafted on Kazusa's free left arm (CTRL_hand.L, CTRL_elbow.L and the left
fingers) while the right hand keeps the gun.
It opens ready to work in, and saving sends the work to the game (the user, 2026-11-13: "내가 해야 하는 잡일을 최대한
줄여. 나는 딱 오직 인간만이 할 수 있는 것만 해내면 되게끔"; the shared part is Tools/Blender/OneClick/one_click.py):
- On open: the last clip worked on, the rig in Pose mode with the left hand handle selected, the Dope Sheet showing the
  clip's keys (a row per bone, the draft's key poses as columns), the 투척 tab.
- A clip drafted at key poses (the grenade's four, the knife's hold) is one action the user keys into directly (auto-key
  on), its first draft kept beside it (<name>_draft) for 이 클립을 시안으로 되돌리기. The knife's draw and throw are
  MW2019's motion keyed every frame: their draft stays locked on the NLA track '1 시안 (잠금)' under a touch-up layer
  (Combine), where one key bends the clip smoothly. A file built before (every draft under an empty touch-up) is read so.
- Saving (Ctrl+S) sends every clip that changed since it was last sent (its keys, its marks or its last frame).
- 수류탄 · 꺼내기 / 대기 (되풀이) / 던지기 / 톡 던지기: the grenade's (every throwable but the knife). The draw goes from the
  gun to the belt and up, the pin at 핀; the hold is the cooking loop; the throw winds up and lets go at 놓기; the tap is
  Call of Duty's quick throw in one motion.
- 나이프 · 꺼내기 / 대기 (되풀이) / 던지기: the Knight's throwing knife, the MW2019 viewmodel of GMod's "[MW] Quick Throwing
  Knife" mirrored onto the left hand bone for bone (build_grenade_workspace.py), its hold MW's still idle with a breath.
Sending writes Assets/_Project/Data/Resources/Tables/<table>.csv: per half frame, the wrist and the elbow from the eyes
in game metres, the hand's run and facing, each finger's curl and the thumb's, in the head camera's axes as Unity's (x
right, y up, z forward), whether the throwable is in the hand (from 쥐기 to 놓기), and the 핀 and 놓기 moments. GrenadeArm
plays them on any character's left arm (Tables/ThrowArms names which clip is which). Every button keys at the current
frame with auto-key on, holding the pose some frames either side, so a click is one local change. Nothing here changes
Blender's preferences.
"""
import importlib.util
import json
import math
import sys
from pathlib import Path

import bpy
from bpy.app.handlers import persistent
from bpy.props import BoolProperty, FloatProperty, IntProperty, StringProperty
from mathutils import Matrix, Vector

bl_info = {'name': 'ENFILADE Throwables', 'author': 'ENFILADE', 'version': (2, 0, 0), 'blender': (4, 5, 0),
           'location': '3D View > Sidebar > 투척', 'category': 'Animation'}

ONE_CLICK = Path('Tools/Blender/OneClick/one_click.py')
TAB = '투척'

# Kazusa in the game is this many times her size in this file (eyes 1.37 here, 1.6 m in game: PlayerBody's fit).
GAME_SCALE = 1.166
# Where a draft is locked under its touch-up layer (the knife's every-frame clips; every clip in files from before).
BASE_TRACK = '1 시안 (잠금)'
CAMERA = 'FPS_Head_Camera'
TABLES_DIR = Path('Assets/_Project/Data/Resources/Tables')
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


def one_click():
    """The shared one-click workspace module (items, fingerprints, sending on save, the screen on open), loaded once and
    without a __pycache__ beside it."""
    if 'enf_one_click' in sys.modules:
        return sys.modules['enf_one_click']
    root = project_root()
    if root is None:
        raise RuntimeError('ENFILADE 프로젝트 안의 ArtSource/Grenades에서 파일을 열어 주세요.')
    spec = importlib.util.spec_from_file_location('enf_one_click', root / ONE_CLICK)
    module = importlib.util.module_from_spec(spec)
    sys.modules['enf_one_click'] = module
    writes = sys.dont_write_bytecode
    sys.dont_write_bytecode = True
    try:
        spec.loader.exec_module(module)
    finally:
        sys.dont_write_bytecode = writes
    return module


def find_rig(scene):
    return next((o for o in scene.objects if o.type == 'ARMATURE' and o.get('truefps_rig')), None)


def is_throw_file(scene):
    return find_rig(scene) is not None and bpy.data.actions.get('enf_grenade_throw_draft') is not None


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


# ---- The clip on show: its action (editable, or a touch-up over its locked draft) ----------------------------------------

def touch_up(name):
    """The clip's own action, made empty if it has none (the build calls this after drafting; showing the clip then makes
    a key-pose clip's into the editable copy of its draft)."""
    return one_click().touch_up(name)


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


def end_of(scene, key):
    return int(scene.get('enf_end_' + key, MOVES[key]['end']))


def keep_marks(scene, key):
    """The clip's moments and last frame as the timeline has them now (written only when they moved)."""
    marks = json.dumps({m.name: m.frame for m in scene.timeline_markers if m.name in MARKS})
    if scene.get('enf_marks_' + key) != marks:
        scene['enf_marks_' + key] = marks
    if scene.get('enf_end_' + key) != scene.frame_end:
        scene['enf_end_' + key] = scene.frame_end


def remember(scene):
    """The clip shown keeps its moments and last frame (for when it is picked again, and for its fingerprint)."""
    key = scene.get('enf_throw_move')
    if key in MOVES:
        keep_marks(scene, key)


def item(context, key):
    """The clip's action (made ready from an older file's layers) and the draft locked under it (None: edited directly).
    A clip made ready untouched counts as sent: the build sent the drafts."""
    oc = one_click()
    action, draft, fresh = oc.item_action(context, find_rig(context.scene), MOVES[key]['action'], end_of(context.scene, key))
    if fresh:
        oc.mark_sent(action, fingerprint(key))
    return action, draft


def fingerprint(key):
    """What a clip is now: its keys and eases (and its draft's, under a touch-up), its moments and its last frame."""
    oc = one_click()
    scene = bpy.context.scene
    name = MOVES[key]['action']
    action = bpy.data.actions.get(name)
    draft = bpy.data.actions.get(name + oc.DRAFT) if action is not None and action.get(oc.LAYER) == 'touch-up' else None
    return oc.fingerprint([action, draft], extra=[key, marks_of(scene, key), end_of(scene, key)])


def layered(key):
    """True for a clip edited as a touch-up over its locked draft (motion keyed every frame)."""
    action = bpy.data.actions.get(MOVES[key]['action'])
    return action is not None and action.get(one_click().LAYER) == 'touch-up'


def select_move(context, key):
    """Shows one clip: its action, its frames, its moments on the timeline and its throwable in the hand."""
    scene = context.scene
    remember(scene)
    rig = find_rig(scene)
    reset_rig(rig)
    action, draft = item(context, key)
    one_click().play(rig, action, draft)
    for marker in [m for m in scene.timeline_markers if m.name in MARKS]:
        scene.timeline_markers.remove(marker)
    for name, frame in marks_of(scene, key).items():
        scene.timeline_markers.new(name, frame=frame)
    scene.frame_start, scene.frame_end = 1, end_of(scene, key)
    scene['enf_throw_move'] = key
    scene.frame_set(1)
    show_held(scene)
    one_click().auto_key(scene)


def prepare(context):
    """Every clip ready to work in (an older file's layers made into editable clips), the last one shown."""
    for key in MOVES:
        item(context, key)
    select_move(context, current_move(context.scene))


def arrange(context):
    """The workspace as the work needs it: the clip loaded, the left hand selected in Pose mode, the clip's keys in the
    Dope Sheet, the tab open."""
    scene = context.scene
    rig = find_rig(scene)
    if rig is None:
        return True
    # The clips first: they are ready even where the screen can't be (a window switches its workspace on the next tick).
    prepare(context)
    oc = one_click()
    if not oc.open_workspace(context):
        return False
    for window in context.window_manager.windows:
        main = oc.main_view(window)
        oc.show_tab(main, TAB)
        oc.dope_sheet(window)
        if main is not None:
            oc.select(context, window, main, [rig], rig, pose=True, bones=[ARM_HAND])
    oc.tell(f'투척 「{MOVES[current_move(scene)]["label"]}」 · Space 재생 · 고치고 Ctrl+S 하면 게임에 들어갑니다')
    return True


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
    return one_click().curves(action)


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
    """Removes the shown clip's keys at one frame (its touch-ups, for a clip under a touch-up layer); returns how many."""
    rig = find_rig(context.scene)
    action = rig.animation_data.action if rig and rig.animation_data else None
    return one_click().clear_keys(action, frame)


def revert(context, key):
    """The clip as its first draft again, its moments and last frame too (a touch-up layer emptied)."""
    oc = one_click()
    move = MOVES[key]
    scene = context.scene
    if layered(key):
        action = bpy.data.actions.get(move['action'])
        bag = oc.channelbag(action)
        for curve in list(oc.curves(action)):
            bag.fcurves.remove(curve)
    else:
        oc.revert(move['action'])
    scene['enf_marks_' + key] = json.dumps(move['marks'])
    scene['enf_end_' + key] = move['end']
    if current_move(scene) == key:
        # What select_move keeps for the clip shown is what the timeline has: the draft's, then.
        for marker in [m for m in scene.timeline_markers if m.name in MARKS]:
            scene.timeline_markers.remove(marker)
        for name, frame in move['marks'].items():
            scene.timeline_markers.new(name, frame=frame)
        scene.frame_end = move['end']
    select_move(context, key)


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


def table_file(root, name, new_in='Clips'):
    """The game's table of this name under the project root. Since 0.9.0 the tables are filed in sub-folders of
    Data/Resources/Tables by subject and found by file name alone, as the game finds them (GameTables); a table that isn't
    there yet is made in new_in."""
    base = Path(root) / TABLES_DIR
    for found in sorted(base.rglob(name + '.csv')):
        return found
    return base / new_in / (name + '.csv')


def target(key):
    """Where a clip goes in the game, from the project root (found by the table's name; a new one goes to Clips)."""
    name = MOVES[key]['table']
    root = project_root()
    if root is None:
        return TABLES_DIR / 'Clips' / (name + '.csv')
    return table_file(root, name).relative_to(root)


def in_game(key):
    root = project_root()
    return root is not None and (root / target(key)).exists()


def export_clip(context, key):
    """One clip to the game (shown first if it isn't, its touch-ups on), marked as sent; returns the path written."""
    root = project_root()
    if root is None:
        raise RuntimeError('ENFILADE 프로젝트를 찾지 못했습니다. ArtSource/Grenades의 파일을 여세요.')
    oc = one_click()
    move = MOVES[key]
    scene = context.scene
    rig = find_rig(scene)
    data = rig.animation_data
    if current_move(scene) != key or data is None or data.action is None or data.action.name != move['action']:
        select_move(context, key)
        data = rig.animation_data
    keep_marks(scene, key)
    # The touch-ups count even while 손질 층 켜기/끄기 has them off.
    influence = data.action_influence
    data.action_influence = 1.0
    try:
        text = clip_text(context, key)
    finally:
        data.action_influence = influence
    path = root / target(key)
    path.parent.mkdir(parents=True, exist_ok=True)
    oc.write_meta(path, oc.TEXT_META)
    oc.write_text(path, text)
    oc.mark_sent(bpy.data.actions.get(move['action']), fingerprint(key))
    return path


def clip_text(context, key):
    """The clip's table as the game reads it."""
    move = MOVES[key]
    loop ='되풀이 클립: 끝 줄이 첫 줄과 같아야 이어진다. ' if move.get('loop') else ''
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
    return '\n'.join(lines) + '\n'


def send_changed(context, force=False):
    """Every clip that changed since it was last sent (or whose table is missing) to the game; the clip shown stays shown,
    on its frame. Returns (sent labels, failures), or None with the panel's switch off (force: sends anyway, the
    게임에 보내기 button)."""
    scene = context.scene
    if not is_throw_file(scene) or not (force or scene.enf_throw.auto_send):
        return None
    oc = one_click()
    shown, frame = current_move(scene), scene.frame_current
    remember(scene)
    sent, failed = [], []
    for key, move in MOVES.items():
        try:
            action, _draft = item(context, key)
            if not oc.needs_sending(action, fingerprint(key), in_game(key)):
                continue
            export_clip(context, key)
            sent.append(move['label'])
        except Exception as error:
            failed.append(f'{move["label"]}: {error}')
    if current_move(scene) != shown:
        select_move(context, shown)
    if scene.frame_current != frame:
        scene.frame_set(frame)
    return sent, failed


# ---- Panel ---------------------------------------------------------------------------------------------------------

class ENFThrowSettings(bpy.types.PropertyGroup):
    auto_send: BoolProperty(name='저장하면 게임에 보내기', default=True,
                            description='Ctrl+S로 저장할 때 바뀐 클립을 모두 게임에 보낸다(Tables의 클립 표)')
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
    bl_description = '이 클립을 불러오고 왼손을 골라 둔다'
    bl_options = {'REGISTER', 'UNDO'}
    move: StringProperty()

    def execute(self, context):
        if not _ok(self, context) or self.move not in MOVES:
            return {'CANCELLED'}
        _object_mode(context)
        select_move(context, self.move)
        rig = find_rig(context.scene)
        if context.area is not None and context.area.type == 'VIEW_3D':
            one_click().select(context, context.window, context.area, [rig], rig, pose=True, bones=[ARM_HAND])
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
    bl_description = '지금 프레임에서 고른 곳을 조금 옮기거나 돌리고 키를 넣는다'
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
    bl_description = '프레임마다 키가 있는 클립(나이프 꺼내기·던지기)의 손질을 끄고 시안만 보거나 다시 켠다(지우지 않는다. 보낼 때는 늘 켜진 채로)'

    def execute(self, context):
        rig = find_rig(context.scene)
        data = rig.animation_data if rig else None
        if data is not None:
            data.action_influence = 0.0 if data.action_influence > .5 else 1.0
            self.report({'INFO'}, '손질 층 ' + ('켜짐' if data.action_influence > .5 else '꺼짐 (시안만)'))
        return {'FINISHED'}


class ENFTHROW_OT_clear(bpy.types.Operator):
    bl_idname = 'enf_throw.clear'
    bl_label = '이 프레임 키 지우기'
    bl_description = '지금 프레임에 있는 이 클립의 키를 지운다(손질 층이 있는 클립은 손질 키만). 앞뒤 키 사이로 부드럽게 이어진다'
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        removed = clear_touch_ups(context, context.scene.frame_current)
        self.report({'INFO'}, f'키 {removed}개를 지웠습니다')
        return {'FINISHED'}


class ENFTHROW_OT_revert(bpy.types.Operator):
    bl_idname = 'enf_throw.revert'
    bl_label = '이 클립을 시안으로 되돌리기'
    bl_description = '이 클립의 내 손질을 모두 지우고 처음 시안으로 돌아간다(쥐기·핀·놓기 표시와 끝 프레임도). 저장하면 게임에도 시안이 들어간다'
    bl_options = {'REGISTER', 'UNDO'}

    def invoke(self, context, event):
        return context.window_manager.invoke_confirm(self, event)

    def execute(self, context):
        if not _ok(self, context):
            return {'CANCELLED'}
        try:
            revert(context, current_move(context.scene))
        except ValueError as error:
            self.report({'ERROR'}, str(error))
            return {'CANCELLED'}
        self.report({'INFO'}, '시안으로 되돌렸다. 저장하면 게임에도 시안이 들어간다')
        return {'FINISHED'}


class ENFTHROW_OT_export(bpy.types.Operator):
    bl_idname = 'enf_throw.export'
    bl_label = '게임에 보내기'
    bl_description = ('Ctrl+S와 같다: 바뀐 클립을 모두 게임 폴더로 보내고 작업 파일을 저장한다(저장하면 보내기를 꺼 두어도 보낸다). '
                      'Unity가 알아서 가져가고, Play 중이어도 다음 G부터 그 동작이 나온다')
    every: bpy.props.BoolProperty(default=False, description='바뀌지 않은 클립까지 일곱 개 모두 다시 보낸다')

    def execute(self, context):
        if not _ok(self, context):
            return {'CANCELLED'}
        scene = context.scene
        if not self.every:
            sent, failed = send_changed(context, force=True) or ([], ['투척 작업 파일이 아닙니다'])
            one_click().send_report(scene, sent, failed)
            if bpy.data.filepath:
                bpy.ops.wm.save_mainfile()
            if failed:
                self.report({'ERROR'}, '못 보냄: ' + '; '.join(failed))
                return {'CANCELLED'}
            self.report({'INFO'}, ('보냈습니다: ' + ', '.join(sent)) if sent else '바뀐 클립이 없어 저장만 했습니다')
            return {'FINISHED'}
        shown, frame = current_move(scene), scene.frame_current
        sent = []
        try:
            for key in MOVES:
                export_clip(context, key)
                sent.append(MOVES[key]['label'])
        except (RuntimeError, ValueError, OSError) as error:
            self.report({'ERROR'}, str(error))
            return {'CANCELLED'}
        finally:
            if current_move(scene) != shown:
                select_move(context, shown)
            scene.frame_set(frame)
        one_click().send_report(scene, sent, [])
        self.report({'INFO'}, f'{len(sent)}개 클립을 모두 다시 보냈습니다')
        return {'FINISHED'}


class ENFTHROW_PT_panel(bpy.types.Panel):
    bl_label = 'ENFILADE 투척 동작'
    bl_idname = 'ENFTHROW_PT_panel'
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = TAB

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
        if layered(key):
            box.label(text='프레임마다 키가 있는 클립: 고친 것은 손질 층에 들어간다')
            row.operator('enf_throw.toggle')
        row.operator('enf_throw.clear')
        box.operator('enf_throw.revert', icon='LOOP_BACK')

        box = layout.box()
        box.label(text='3. 저장(Ctrl+S)하면 게임에 들어간다')
        box.prop(settings, 'auto_send')
        oc = one_click()
        action = bpy.data.actions.get(move['action'])
        if not in_game(key):
            box.label(text='아직 게임에 없음: 저장하면 들어간다')
        elif action is not None and action.get(oc.SENT) != fingerprint(key):
            box.label(text='고친 것이 있음: 저장하면 게임에 들어간다')
        else:
            box.label(text='게임이 지금 이 클립을 쓰는 중')
        if scene.get(oc.NOTE):
            box.label(text=str(scene[oc.NOTE])[:60])
        box.operator('enf_throw.export', icon='EXPORT').every = False
        box.operator('enf_throw.export', text='모든 클립 다시 보내기').every = True
        box.label(text=str(target(key)).replace('\\', '/'))


CLASSES = (ENFThrowSettings, ENFTHROW_OT_pick, ENFTHROW_OT_mark, ENFTHROW_OT_nudge, ENFTHROW_OT_toggle, ENFTHROW_OT_clear,
           ENFTHROW_OT_revert, ENFTHROW_OT_export, ENFTHROW_PT_panel)


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
    one_click().register_workspace(TAB, is_throw_file, arrange, send_changed)


def unregister():
    one_click().unregister_workspace(TAB)
    handlers = bpy.app.handlers.frame_change_post
    for fn in [h for h in handlers if getattr(h, '__name__', '') == '_frame_changed']:
        handlers.remove(fn)
    del bpy.types.Scene.enf_throw
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)


if __name__ == '__main__':
    register()
