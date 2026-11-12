"""ENFILADE shove moves workspace, Blender 4.5 LTS.

START_SHOVE_ANIMATION.cmd opens ArtSource/Shove/ENFILADE_Shove_Animation.blend with truefps_tools.py and then this.
The shove's three moves are drafted and ready to polish. Each is a locked draft layer under an empty touch-up layer
(Combine), as in the zombie workspace:
- 총 휘두르기 · 긴 총 (enf_bash_long) and 총 휘두르기 · 권총 (enf_bash_pistol): the empty GUN moves off GUN_hold, which
  sits where Kazusa's hands hold that gun under the head camera (GunBakes gunFromEyes). The hands follow it here as they
  do in the game. "게임에 보내기" writes Assets/_Project/Resources/Combat/GunMoves/<name>.csv: the gun's move and turn off
  its hold in the gun's own frame, in game metres, which FPSBody.Bash plays on any character's gun (GunMoves).
- 발차기 (enf_kick_front): Kazusa's right leg on the True FPS rig. "게임에 보내기" bakes the original skeleton to
  Assets/_Project/Art/Animations/Shove/enf_kick_front.fbx (+ .clips.csv); Unity takes it with her avatar
  (ModelImportRules) into the body's clips, and the kick plays it on the legs (Player.csv Body.kick).
- 수리 · 망치 (enf_repair_hammer) and 수리 · 토치 (enf_repair_torch): the Support's mending loops on Kazusa's free left
  arm (CTRL_hand.L, CTRL_elbow.L) while the right hand keeps the gun; the left hand's hold on the gun is let go while one
  is shown. "게임에 보내기" writes Assets/_Project/Resources/Tables/RepairHammer.csv or RepairTorch.csv: the wrist, the
  elbow and the hand's way from the eyes in the view's axes, which RepairArm plays on any character's left arm
  (GrenadeArmPath's columns). repair_moves.py adds them to an older workspace and keys their drafts.
Every button keys at the current frame with auto-key on, holding the pose some frames either side, so a click is one
local change. Nothing here changes Blender's preferences.
"""
import importlib.util
import json
import math
import sys
from pathlib import Path

import bpy
from bpy.props import EnumProperty, FloatProperty, IntProperty
from mathutils import Matrix, Quaternion, Vector

bl_info = {'name': 'ENFILADE Shove Moves', 'author': 'ENFILADE', 'version': (1, 0, 0), 'blender': (4, 5, 0),
           'location': '3D View > Sidebar > 밀치기', 'category': 'Animation'}

TRUEFPS = Path('Tools/Blender/TrueFPS/truefps_tools.py')
# Kazusa in the game is this many times her size in this file (eyes 1.37 here, 1.6 m in game: PlayerBody's fit).
GAME_SCALE = 1.166
BASE_TRACK = '1 시안 (잠금)'
GUN, GUN_HOLD = 'GUN', 'GUN_hold'
GUN_MOVES_DIR = Path('Assets/_Project/Resources/Combat/GunMoves')
KICK_DIR = Path('Assets/_Project/Art/Animations/Shove')
PLAYER_TABLE = Path('Assets/_Project/Resources/Tables/Player.csv')
# Seconds per sample in a gun move file: two per frame at 30 fps, so the game's curve follows the polish closely.
SAMPLES_PER_FRAME = 2

# The moves: what moves (the gun or the body), the touch-up action that is exported, the gun shown, the frame the
# strike lands on and the last frame. Where the hands hold each gun, from the eyes in this file's units: GunBakes
# gunFromEyes / GAME_SCALE, the eyes' frame turned into the camera's (x right, y up, z back).
# hit: when the game itself hits, which the strike should meet; the animation never moves it. A shove with a gun hits
# from the press until Player.csv Ability.shoveActiveTime ('by'); the kick's foot lands at Shove.kickHitTime ('at').
MOVES = {
    'bash_long': dict(label='총 휘두르기 · 긴 총', kind='gun', action='enf_bash_long', gun='GUN_AK105', impact=3, end=14,
                      hold=(0.0620, -0.0748, -0.2599), hit=('Ability.shoveActiveTime', .2, 'by')),
    'bash_pistol': dict(label='총 휘두르기 · 권총', kind='gun', action='enf_bash_pistol', gun='GUN_R08', impact=3, end=13,
                        hold=(0.0668, -0.0659, -0.2415), hit=('Ability.shoveActiveTime', .2, 'by')),
    'kick': dict(label='발차기', kind='body', action='enf_kick_front', impact=10, end=20, hit=('Shove.kickHitTime', .3, 'at')),
    # The Support's mending in the free left hand while the gun stays in the right (the user, 2026-11-12: "남는 손으로
    # 망치 손으로 두들기는 거"): a loop each, played while F is held on a barricade (RepairArm), the left arm and hand only.
    # 타격 is where the hammer meets the planks: the knock sounds there, on every PC. The torch strikes nothing (0).
    # curl/thumb: how far the fingers and the thumb close round the tool (degrees), written into every key.
    'repair_hammer': dict(label='수리 · 망치', kind='arm', action='enf_repair_hammer', table='RepairHammer', tool='TOOL_Hammer',
                          impact=6, end=15, curl=88.0, thumb=62.0),
    # The torch is the heist pack's gas torch: its canister (7.4 cm across) holds the fingers further open than the hammer's
    # handle does.
    'repair_torch': dict(label='수리 · 토치', kind='arm', action='enf_repair_torch', table='RepairTorch', tool='TOOL_Torch',
                         impact=0, end=31, curl=70.0, thumb=45.0),
}

# The parts a beginner moves, by their Korean names: the gun, or the kick's IK handles on the rig, or the left arm's.
GUN_PARTS = (('총', GUN),)
BODY_PARTS = (('오른발', 'CTRL_foot.R'), ('오른무릎 방향', 'CTRL_knee.R'), ('왼발', 'CTRL_foot.L'), ('골반', 'Hips'))
ARM_HAND, ARM_ELBOW = 'CTRL_hand.L', 'CTRL_elbow.L'
ARM_PARTS = (('왼손', ARM_HAND), ('왼팔꿈치 방향', ARM_ELBOW))
MOVE_DIRS = (('forward', '앞으로'), ('back', '뒤로'), ('up', '위로'), ('down', '아래로'), ('left', '왼쪽'), ('right', '오른쪽'))
TURNS = (('pitch_up', '총구 위로'), ('pitch_down', '총구 아래로'), ('yaw_left', '총구 왼쪽'), ('yaw_right', '총구 오른쪽'),
         ('roll_left', '왼쪽으로 기울이기'), ('roll_right', '오른쪽으로 기울이기'))
FOOT_TURNS = (('toes_up', '발끝 위로'), ('toes_down', '발끝 아래로'))
HAND_TURNS = (('wrist_up', '손목 위로 젖히기'), ('wrist_down', '손목 아래로 꺾기'), ('wrist_left', '손목 왼쪽'), ('wrist_right', '손목 오른쪽'),
              ('twist_left', '손목 왼쪽으로 비틀기'), ('twist_right', '손목 오른쪽으로 비틀기'))
# The left hand handle follows the gun through this constraint (the shove workspace's hold); a mending move frees it.
HOLD_CONSTRAINT = 'Hold the gun (shove workspace)'
TABLES_DIR = Path('Assets/_Project/Resources/Tables')
# The left arm's bones on Kazusa's rig, for the path a mending move exports (RepairArm's terms).
ARM_BONES = dict(shoulder='Left arm', elbow='Left elbow', wrist='Left wrist', index='IndexFinger1_L', middle='MiddleFinger1_L',
                 little='LittleFinger1_L')


# ---- Paths and the True FPS tools ------------------------------------------------------------------------------------

def project_root():
    """The ENFILADE checkout: from this file, else from the open .blend (ArtSource/Shove/...)."""
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


def truefps():
    """truefps_tools as a module (for its baking FBX export), loaded once without writing a __pycache__."""
    if 'truefps_tools' in sys.modules:
        return sys.modules['truefps_tools']
    root = project_root()
    if root is None:
        raise RuntimeError('ENFILADE 프로젝트 안의 ArtSource/Shove에서 파일을 열어 주세요.')
    spec = importlib.util.spec_from_file_location('truefps_tools', root / TRUEFPS)
    module = importlib.util.module_from_spec(spec)
    sys.modules['truefps_tools'] = module
    writes = sys.dont_write_bytecode
    sys.dont_write_bytecode = True
    try:
        spec.loader.exec_module(module)
    finally:
        sys.dont_write_bytecode = writes
    return module


def find_rig(scene):
    return next((o for o in scene.objects if o.type == 'ARMATURE' and o.get('truefps_rig')), None)


_game_numbers = {}


def game_number(row, fallback):
    """A number from the game's Player.csv (read once a session), so the panel checks the strike against the game's own
    hit timing rather than a copy of it."""
    if row not in _game_numbers:
        value = fallback
        root = project_root()
        try:
            for line in (root / PLAYER_TABLE).read_text(encoding='utf-8-sig').splitlines():
                cells = line.split(',')
                if cells[0] == row:
                    value = float(cells[1])
                    break
        except (OSError, TypeError, ValueError, IndexError):
            pass
        _game_numbers[row] = value
    return _game_numbers[row]


# ---- Layers: a locked draft strip under the touch-up action --------------------------------------------------------

def touch_up(name):
    action = bpy.data.actions.get(name)
    if action is None:
        action = bpy.data.actions.new(name)
    action.use_fake_user = True
    action['enf_layer'] = 'touch-up'
    return action


def set_layers(obj, draft, edit, live=True):
    """obj plays draft as its locked base and keys into edit on top (Combine); live False mutes both."""
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
    track.mute = not live
    data.action = edit
    if edit is not None and hasattr(data, 'action_slot') and data.action_slot is None and len(getattr(edit, 'slots', [])):
        data.action_slot = edit.slots[0]
    data.action_blend_type = 'COMBINE'
    data.action_extrapolation = 'HOLD'
    data.action_influence = 1.0 if live else 0.0


def select_move(context, key):
    """Shows one move: its layers live (the other object's muted), its gun, its frames and its strike marker."""
    move = MOVES[key]
    scene = context.scene
    # The move left keeps where its strike marker and last frame were put, for when it is picked again.
    previous = scene.get('enf_shove_move')
    marker = scene.timeline_markers.get('타격')
    if previous in MOVES and marker is not None:
        scene['enf_impact_' + previous] = marker.frame
        scene['enf_end_' + previous] = scene.frame_end
    rig = find_rig(scene)
    gun = bpy.data.objects.get(GUN)
    hold = bpy.data.objects.get(GUN_HOLD)
    # A handle the last move animated keeps its last value once nothing animates it: back to the start pose first, so
    # the left hand comes back onto the gun after a mending move and the legs down after the kick.
    reset_rig(rig)
    if move['kind'] == 'gun':
        set_layers(gun, bpy.data.actions.get(move['action'] + '_draft'), touch_up(move['action']))
        if hold is not None:
            hold.location = move['hold']
        if rig is not None:
            set_layers(rig, bpy.data.actions.get(MOVES['kick']['action'] + '_draft'), touch_up(MOVES['kick']['action']), live=False)
    else:
        if gun is not None:
            set_layers(gun, bpy.data.actions.get(MOVES['bash_long']['action'] + '_draft'), touch_up(MOVES['bash_long']['action']), live=False)
            gun.location, gun.rotation_quaternion = (0, 0, 0), (1, 0, 0, 0)
        if hold is not None:
            hold.location = MOVES['bash_long']['hold']
        set_layers(rig, bpy.data.actions.get(move['action'] + '_draft'), touch_up(move['action']))
    # A mending move has the left hand off the gun, doing it; every other move keeps it on the gun.
    free_left_hand(rig, move['kind'] == 'arm')
    for other in MOVES.values():
        if other.get('gun') and other['gun'] in bpy.data.objects:
            show = other['gun'] == move.get('gun') or (move['kind'] in ('body', 'arm') and other['gun'] == MOVES['bash_long']['gun'])
            for obj in [bpy.data.objects[other['gun']]] + list(bpy.data.objects[other['gun']].children_recursive):
                obj.hide_set(not show)
                obj.hide_render = not show
        if other.get('tool') and other['tool'] in bpy.data.objects:
            for obj in [bpy.data.objects[other['tool']]] + list(bpy.data.objects[other['tool']].children_recursive):
                obj.hide_set(other['tool'] != move.get('tool'))
                obj.hide_render = other['tool'] != move.get('tool')
    scene.frame_start, scene.frame_end = 1, int(scene.get('enf_end_' + key, move['end']))
    marker = marker or scene.timeline_markers.new('타격', frame=move['impact'])
    marker.frame = int(scene.get('enf_impact_' + key, move['impact']))
    scene['enf_shove_move'] = key
    scene.frame_set(1)
    scene.tool_settings.use_keyframe_insert_auto = True


def current_move(scene):
    key = scene.get('enf_shove_move', 'bash_long')
    return key if key in MOVES else 'bash_long'


def reset_rig(rig):
    """The rig's handles at the start pose (True FPS's truefps_default_pose, the hands on the gun's grips)."""
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


def free_left_hand(rig, free):
    """The left hand handle off the gun (a mending move keys it itself) or on it, as the gun moves have it."""
    if rig is None or ARM_HAND not in rig.pose.bones:
        return
    for con in rig.pose.bones[ARM_HAND].constraints:
        if con.name == HOLD_CONSTRAINT:
            con.influence = 0.0 if free else 1.0


# ---- Moving a part and keying it -----------------------------------------------------------------------------------

def _keyed_at(obj, path, frame):
    data = obj.animation_data
    action = data.action if data else None
    if action is None:
        return False
    for curve in _curves(action):
        if curve.data_path == path and any(abs(p.co.x - frame) < .5 for p in curve.keyframe_points):
            return True
    return False


def _curves(action):
    if action is None:
        return []
    if getattr(action, 'is_action_legacy', True) and hasattr(action, 'fcurves'):
        try:
            return list(action.fcurves)
        except (AttributeError, RuntimeError):
            pass
    curves = []
    for layer in getattr(action, 'layers', []):
        for strip in layer.strips:
            for bag in getattr(strip, 'channelbags', []):
                curves.extend(bag.fcurves)
    return curves


def _key(obj, bone, frame):
    """Keys what is shown into the touch-up layer: Blender works out the touch-up that gives it over the draft."""
    if bone is None:
        obj.keyframe_insert('location', frame=frame, group=GUN)
        obj.keyframe_insert('rotation_quaternion', frame=frame, group=GUN)
    else:
        pb = obj.pose.bones[bone]
        pb.keyframe_insert('location', frame=frame, group=bone)
        pb.keyframe_insert('rotation_quaternion', frame=frame, group=bone)


def change(context, obj, bone, reach, apply):
    """At the current frame: holds the pose reach frames either side first, then apply() changes it, and keys it."""
    scene = context.scene
    frame = scene.frame_current
    path = 'location' if bone is None else f'pose.bones["{bone}"].location'
    for at in (frame - reach, frame + reach) if reach > 0 else ():
        if scene.frame_start <= at <= scene.frame_end and not _keyed_at(obj, path, at):
            scene.frame_set(at)
            _key(obj, bone, at)
    scene.frame_set(frame)
    context.view_layer.update()
    apply()
    context.view_layer.update()
    _key(obj, bone, frame)


def body_axes(rig):
    """The character's right, forward and up in the armature's space (she faces -Y in this file, Z up)."""
    inverse = rig.matrix_world.to_3x3().inverted()
    return (inverse @ Vector((-1, 0, 0))).normalized(), (inverse @ Vector((0, -1, 0))).normalized(), (inverse @ Vector((0, 0, 1))).normalized()


def nudge_gun(context, how, step_cm, step_deg, reach):
    gun = bpy.data.objects[GUN]
    gun.rotation_mode = 'QUATERNION'
    # GUN_hold's axes are the gun's: x right, y along the barrel, z up.
    moves = {'forward': Vector((0, 1, 0)), 'back': Vector((0, -1, 0)), 'up': Vector((0, 0, 1)), 'down': Vector((0, 0, -1)),
             'left': Vector((-1, 0, 0)), 'right': Vector((1, 0, 0))}
    turns = {'pitch_up': ((1, 0, 0), 1), 'pitch_down': ((1, 0, 0), -1), 'yaw_left': ((0, 0, 1), 1), 'yaw_right': ((0, 0, 1), -1),
             'roll_left': ((0, 1, 0), -1), 'roll_right': ((0, 1, 0), 1)}

    def apply():
        if how in moves:
            gun.location = gun.location + moves[how] * (step_cm / 100.0)
        else:
            axis, sign = turns[how]
            # About the gun's own axes as it is turned now, round its middle (the game's weapon bone).
            gun.rotation_quaternion = gun.rotation_quaternion @ Quaternion(Vector(axis), math.radians(step_deg) * sign)
    change(context, gun, None, reach, apply)


def nudge_body(context, bone, how, step_cm, step_deg, reach):
    rig = find_rig(context.scene)
    right, forward, up = body_axes(rig)
    pb = rig.pose.bones[bone]
    directions = {'forward': forward, 'back': -forward, 'up': up, 'down': -up, 'left': -right, 'right': right}

    def apply():
        matrix = pb.matrix.copy()
        if how in directions:
            matrix.translation = matrix.translation + directions[how] * (step_cm / 100.0)
        else:
            angle = math.radians(step_deg) * (1 if how == 'toes_up' else -1)
            turn = Matrix.Rotation(angle, 4, right)
            head = matrix.translation.copy()
            matrix = Matrix.Translation(head) @ turn @ Matrix.Translation(-head) @ matrix
        pb.matrix = matrix
    change(context, rig, bone, reach, apply)


def view_axes(rig):
    """The head camera's right, up and forward in the armature's space (the first-person view), else the body's."""
    cam = bpy.data.objects.get('FPS_Head_Camera')
    if cam is None:
        right, forward, up = body_axes(rig)
        return right, up, forward
    inverse = rig.matrix_world.to_3x3().inverted()
    m = cam.matrix_world.to_3x3()
    return (inverse @ m.col[0]).normalized(), (inverse @ m.col[1]).normalized(), (inverse @ -m.col[2]).normalized()


def nudge_hand(context, how, step_deg, reach):
    """Turns the left hand handle about the view's axes round its own place: the wrist cocked up or down, swung left
    or right, twisted."""
    rig = find_rig(context.scene)
    pb = rig.pose.bones[ARM_HAND]
    right, up, forward = view_axes(rig)
    axes = {'wrist_up': (right, 1), 'wrist_down': (right, -1), 'wrist_left': (up, 1), 'wrist_right': (up, -1),
            'twist_left': (forward, -1), 'twist_right': (forward, 1)}

    def apply():
        axis, sign = axes[how]
        matrix = pb.matrix.copy()
        head = matrix.translation.copy()
        matrix = Matrix.Translation(head) @ Matrix.Rotation(math.radians(step_deg) * sign, 4, axis) @ Matrix.Translation(-head) @ matrix
        pb.matrix = matrix
    change(context, rig, ARM_HAND, reach, apply)


def clear_touch_ups(context, frame):
    """Removes the touch-up keys at one frame of the move shown; returns how many went."""
    move = MOVES[current_move(context.scene)]
    obj = bpy.data.objects[GUN] if move['kind'] == 'gun' else find_rig(context.scene)
    action = obj.animation_data.action if obj and obj.animation_data else None
    removed = 0
    for curve in _curves(action):
        for point in [p for p in curve.keyframe_points if abs(p.co.x - frame) < .5]:
            curve.keyframe_points.remove(point, fast=True)
            removed += 1
        curve.update()
    return removed


# ---- To the game ---------------------------------------------------------------------------------------------------

# Blender (x right, y forward, z up, right-handed) to Unity (x right, y up, z forward, left-handed): y and z swap.
_SWAP = Matrix(((1, 0, 0), (0, 0, 1), (0, 1, 0)))


def gun_samples(context, key):
    """The gun's move and turn off its hold over the move's frames, in Unity's gun frame and game metres."""
    scene = context.scene
    gun = bpy.data.objects[GUN]
    fps = scene.render.fps / scene.render.fps_base
    rows, previous = [], None
    start, end = scene.frame_start, scene.frame_end
    steps = (end - start) * SAMPLES_PER_FRAME
    for i in range(steps + 1):
        at = start + i / SAMPLES_PER_FRAME
        frame = int(math.floor(at))
        scene.frame_set(frame, subframe=at - frame)
        context.view_layer.update()
        local = gun.matrix_basis
        move = local.translation * GAME_SCALE
        turn = (_SWAP @ local.to_3x3().normalized() @ _SWAP).to_quaternion()
        if previous is not None and previous.dot(turn) < 0:
            turn.negate()
        previous = turn
        rows.append(((at - start) / fps, Vector((move.x, move.z, move.y)), turn))
    scene.frame_set(start)
    return rows


def export_gun(context, key, root):
    scene = context.scene
    path = root / GUN_MOVES_DIR / (MOVES[key]['action'] + '.csv')
    path.parent.mkdir(parents=True, exist_ok=True)
    fps = scene.render.fps / scene.render.fps_base
    marker = scene.timeline_markers.get('타격')
    impact = ((marker.frame if marker else MOVES[key]['impact']) - scene.frame_start) / fps
    lines = ['time,x,y,z,qx,qy,qz,qw', f'#impact={impact:.4f}',
             f'#from=ArtSource/Shove/ENFILADE_Shove_Animation.blend {MOVES[key]["action"]}']
    for time, move, turn in gun_samples(context, key):
        lines.append(f'{time:.4f},{move.x:.5f},{move.y:.5f},{move.z:.5f},{turn.x:.5f},{turn.y:.5f},{turn.z:.5f},{turn.w:.5f}')
    path.write_text('\n'.join(lines) + '\n', encoding='utf-8')
    return path


def export_kick(context, root):
    scene = context.scene
    rig = find_rig(scene)
    name = MOVES['kick']['action']
    path = root / KICK_DIR / (name + '.fbx')
    path.parent.mkdir(parents=True, exist_ok=True)
    truefps().export_active_clip(context, rig, str(path))
    clips = path.with_name(name + '.clips.csv')
    clips.write_text('clip,loop,inPlace,inPlaceY,inPlaceYaw,yawOffset,cycleOffset,from,to\n'
                     f'{name},0,0,0,0,0,0,,\n', encoding='utf-8')
    return path


def arm_rows(context, key):
    """The left arm over the move's frames as the game's arm paths have it (GrenadeArmPath, played by RepairArm): the
    wrist and the elbow from the shoulder in arm lengths, the hand's run (wrist to middle knuckle) and its palm's facing,
    in the head camera's axes as Unity's (x right, y up, z forward), and whether this sample is the strike (타격)."""
    scene = context.scene
    rig = find_rig(scene)
    cam = bpy.data.objects['FPS_Head_Camera']
    move = MOVES[key]
    fps = scene.render.fps / scene.render.fps_base
    start, end = scene.frame_start, scene.frame_end
    marker = scene.timeline_markers.get('타격')
    strike = marker.frame if marker is not None and move.get('impact', 0) > 0 and start <= marker.frame <= end else None
    rows = []
    for i in range((end - start) * SAMPLES_PER_FRAME + 1):
        at = start + i / SAMPLES_PER_FRAME
        frame = int(math.floor(at))
        scene.frame_set(frame, subframe=at - frame)
        context.view_layer.update()
        cm = cam.matrix_world
        origin = cm.translation
        right, up, forward = cm.col[0].xyz.normalized(), cm.col[1].xyz.normalized(), -cm.col[2].xyz.normalized()

        def view(name):
            d = rig.matrix_world @ rig.pose.bones[ARM_BONES[name]].head - origin
            return Vector((d.dot(right), d.dot(up), d.dot(forward)))
        s, e, w = view('shoulder'), view('elbow'), view('wrist')
        index, middle, little = view('index'), view('middle'), view('little')
        length = (e - s).length + (w - e).length
        along = (middle - w).normalized()
        # The palm out of it, as GrenadeArm.PalmNow takes it: these axes are Unity's (left-handed), and its cross is the
        # same sum as mathutils' here.
        palm = -along.cross(index - little)
        palm = (palm - along * palm.dot(along)).normalized()
        rows.append(((at - start) / fps, (w - s) / length, (e - s) / length, along, palm,
                     strike is not None and abs(at - strike) < 1e-6))
    scene.frame_set(start)
    return rows


def export_arm(context, key, root):
    move = MOVES[key]
    path = root / TABLES_DIR / (move['table'] + '.csv')
    path.parent.mkdir(parents=True, exist_ok=True)
    note = ('#설명 (블렌더 밀치기 작업실 ArtSource/Shove의 ' + move['label'] + ' 동작을 게임에 보내기로 쓴 왼팔의 길. '
            'GrenadeArm.csv와 같은 칸: 어깨에서 손목과 팔꿈치까지 팔 길이의 배수와 손의 방향을 시선 축(x 오른쪽 y 위 z 앞)으로. '
            'strike 1인 줄이 망치가 판자에 닿는 순간(소리). 손으로 고치지 말고 블렌더에서 고쳐 다시 보낸다. RepairArm이 어느 캐릭터 팔에든 맞춰 되풀이한다)')
    lines = ['﻿id,cycle,wristX,wristY,wristZ,elbowX,elbowY,elbowZ,alongX,alongY,alongZ,palmX,palmY,palmZ,curl,thumb,held,strike', note]
    for n, (cycle, wrist, elbow, along, palm, strike) in enumerate(arm_rows(context, key)):
        cells = [f'k{n:02d}', f'{cycle:.4f}'] + [f'{v:.4f}' for v in (*wrist, *elbow, *along, *palm)]
        cells += [f'{move["curl"]:.1f}', f'{move["thumb"]:.1f}', '1', '1' if strike else '0']
        lines.append(','.join(cells))
    path.write_text('\n'.join(lines) + '\n', encoding='utf-8')
    return path


def export_move(context, key):
    root = project_root()
    if root is None:
        raise RuntimeError('ENFILADE 프로젝트를 찾지 못했습니다. ArtSource/Shove의 파일을 여세요.')
    if MOVES[key]['kind'] == 'gun':
        return export_gun(context, key, root)
    if MOVES[key]['kind'] == 'arm':
        return export_arm(context, key, root)
    return export_kick(context, root)


# ---- Panel ---------------------------------------------------------------------------------------------------------

class ENFShoveSettings(bpy.types.PropertyGroup):
    step_cm: FloatProperty(name='옮기는 거리 (cm)', default=2.0, min=.1, max=20.0)
    step_deg: FloatProperty(name='돌리는 각도 (도)', default=5.0, min=.5, max=45.0)
    reach: IntProperty(name='섞는 길이 (프레임)', default=2, min=0, max=10,
                       description='누른 프레임에서만 바뀌고 이만큼 앞뒤 프레임은 그대로 둔다')


def _ok(op, context):
    if find_rig(context.scene) is None or GUN not in bpy.data.objects:
        op.report({'ERROR'}, 'ENFILADE_Shove_Animation.blend를 열어 주세요.')
        return False
    return True


def _object_mode(context):
    if context.object and context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')


class ENFSHOVE_OT_pick(bpy.types.Operator):
    bl_idname = 'enf_shove.pick'
    bl_label = '동작 고르기'
    bl_description = '이 동작의 시안과 손질 층을 보여 준다'
    bl_options = {'REGISTER', 'UNDO'}
    move: EnumProperty(items=[(k, v['label'], '') for k, v in MOVES.items()])

    def execute(self, context):
        if not _ok(self, context):
            return {'CANCELLED'}
        _object_mode(context)
        select_move(context, self.move)
        return {'FINISHED'}


class ENFSHOVE_OT_impact(bpy.types.Operator):
    bl_idname = 'enf_shove.impact'
    bl_label = '타격 프레임으로'
    bl_description = '맞는 순간(타격 표시)으로 간다. 게임은 이 순간에 맞힌다'

    def execute(self, context):
        marker = context.scene.timeline_markers.get('타격')
        if marker:
            context.scene.frame_set(marker.frame)
        return {'FINISHED'}


class ENFSHOVE_OT_nudge(bpy.types.Operator):
    bl_idname = 'enf_shove.nudge'
    bl_label = '움직이기'
    bl_description = '지금 프레임에서 고른 곳을 조금 옮기거나 돌리고 손질 층에 키를 넣는다'
    bl_options = {'REGISTER', 'UNDO'}
    part: bpy.props.StringProperty()
    how: bpy.props.StringProperty()

    def execute(self, context):
        if not _ok(self, context):
            return {'CANCELLED'}
        settings = context.scene.enf_shove
        try:
            if self.part == GUN:
                _object_mode(context)
                nudge_gun(context, self.how, settings.step_cm, settings.step_deg, settings.reach)
            elif self.how in dict(HAND_TURNS):
                nudge_hand(context, self.how, settings.step_deg, settings.reach)
            else:
                nudge_body(context, self.part, self.how, settings.step_cm, settings.step_deg, settings.reach)
        except (KeyError, ValueError) as error:
            self.report({'ERROR'}, str(error))
            return {'CANCELLED'}
        return {'FINISHED'}


class ENFSHOVE_OT_toggle(bpy.types.Operator):
    bl_idname = 'enf_shove.toggle'
    bl_label = '손질 층 켜기/끄기'
    bl_description = '손질을 끄고 시안만 보거나 다시 켠다(지우지 않는다)'

    def execute(self, context):
        move = MOVES[current_move(context.scene)]
        obj = bpy.data.objects[GUN] if move['kind'] == 'gun' else find_rig(context.scene)
        data = obj.animation_data
        if data is not None:
            data.action_influence = 0.0 if data.action_influence > .5 else 1.0
            self.report({'INFO'}, '손질 층 ' + ('켜짐' if data.action_influence > .5 else '꺼짐 (시안만)'))
        return {'FINISHED'}


class ENFSHOVE_OT_clear(bpy.types.Operator):
    bl_idname = 'enf_shove.clear'
    bl_label = '이 프레임 손질 지우기'
    bl_description = '지금 프레임에 넣은 손질 키를 지운다(시안은 그대로)'
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        removed = clear_touch_ups(context, context.scene.frame_current)
        self.report({'INFO'}, f'손질 키 {removed}개를 지웠습니다')
        return {'FINISHED'}


class ENFSHOVE_OT_export(bpy.types.Operator):
    bl_idname = 'enf_shove.export'
    bl_label = '게임에 보내기'
    bl_description = '보고 있는 동작을 게임 폴더로 내보낸다. Unity가 알아서 가져가고, 다음 Play부터 그 동작이 나온다'

    def execute(self, context):
        if not _ok(self, context):
            return {'CANCELLED'}
        key = current_move(context.scene)
        try:
            path = export_move(context, key)
        except (RuntimeError, ValueError) as error:
            self.report({'ERROR'}, str(error))
            return {'CANCELLED'}
        self.report({'INFO'}, f'보냈습니다: {path}')
        return {'FINISHED'}


class ENFSHOVE_PT_panel(bpy.types.Panel):
    bl_label = 'ENFILADE 밀치기 동작'
    bl_idname = 'ENFSHOVE_PT_panel'
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = '밀치기'

    def draw(self, context):
        scene = context.scene
        layout = self.layout
        key = current_move(scene)
        move = MOVES[key]
        box = layout.box()
        box.label(text='1. 동작 고르기')
        for k, v in MOVES.items():
            box.operator('enf_shove.pick', text=v['label'], depress=k == key).move = k
        marker = scene.timeline_markers.get('타격')
        fps = scene.render.fps / scene.render.fps_base
        if marker:
            at = (marker.frame - scene.frame_start) / fps
            box.label(text=f'타격: {marker.frame}프레임 ({at:.3f}초) · 끝 {scene.frame_end}프레임')
            if move.get('hit'):
                row, fallback, how = move['hit']
                game = game_number(row, fallback)
                if how == 'by' and at > game + 1e-3:
                    box.label(text=f'게임은 누른 뒤 {game:.2f}초 안에 맞힌다: 타격을 그 앞으로', icon='ERROR')
                elif how == 'at' and abs(at - game) > .5 / fps:
                    box.label(text=f'게임의 발은 {game:.2f}초({round(game * fps) + scene.frame_start}프레임)에 닿는다', icon='ERROR')
            elif move['kind'] == 'arm':
                box.label(text='되풀이: 끝 프레임이 처음과 같아야 이어진다' if move.get('impact', 0) > 0
                          else '되풀이: 끝 프레임이 처음과 같아야 이어진다 · 토치는 타격 없음')
        box.operator('enf_shove.impact')

        box = layout.box()
        box.label(text='2. 손보기 (지금 프레임에 키가 들어감)')
        settings = scene.enf_shove
        box.prop(settings, 'step_cm')
        box.prop(settings, 'step_deg')
        box.prop(settings, 'reach')
        parts = GUN_PARTS if move['kind'] == 'gun' else ARM_PARTS if move['kind'] == 'arm' else BODY_PARTS
        for label, part in parts:
            col = box.column(align=True)
            col.label(text=label)
            grid = col.grid_flow(columns=2, align=True)
            for how, text in MOVE_DIRS:
                op = grid.operator('enf_shove.nudge', text=text)
                op.part, op.how = part, how
            turns = TURNS if part == GUN else FOOT_TURNS if part.startswith('CTRL_foot') else HAND_TURNS if part == ARM_HAND else ()
            if turns:
                grid = col.grid_flow(columns=2, align=True)
                for how, text in turns:
                    op = grid.operator('enf_shove.nudge', text=text)
                    op.part, op.how = part, how
        row = box.row(align=True)
        row.operator('enf_shove.toggle')
        row.operator('enf_shove.clear')

        box = layout.box()
        box.label(text='3. 게임에 보내기')
        box.operator('enf_shove.export', icon='EXPORT')
        target = (GUN_MOVES_DIR / (move['action'] + '.csv')) if move['kind'] == 'gun' else (TABLES_DIR / (move['table'] + '.csv')) \
            if move['kind'] == 'arm' else (KICK_DIR / (move['action'] + '.fbx'))
        box.label(text=str(target).replace('\\', '/'))


CLASSES = (ENFShoveSettings, ENFSHOVE_OT_pick, ENFSHOVE_OT_impact, ENFSHOVE_OT_nudge, ENFSHOVE_OT_toggle,
           ENFSHOVE_OT_clear, ENFSHOVE_OT_export, ENFSHOVE_PT_panel)


def _show_panel():
    for window in bpy.context.window_manager.windows:
        for area in window.screen.areas:
            if area.type == 'VIEW_3D' and area.spaces.active.region_3d.view_perspective != 'CAMERA':
                area.spaces.active.show_region_ui = True
                region = next((r for r in area.regions if r.type == 'UI'), None)
                if region is not None:
                    try:
                        region.active_panel_category = '밀치기'
                    except (TypeError, AttributeError):
                        pass
    if bpy.context.scene and GUN in bpy.data.objects:
        bpy.context.scene.tool_settings.use_keyframe_insert_auto = True
    return None


def register():
    for cls in CLASSES:
        if hasattr(bpy.types, cls.__name__):
            try:
                bpy.utils.unregister_class(getattr(bpy.types, cls.__name__))
            except RuntimeError:
                pass
        bpy.utils.register_class(cls)
    bpy.types.Scene.enf_shove = bpy.props.PointerProperty(type=ENFShoveSettings)
    if not bpy.app.background:
        # After True FPS's own start-up (0.2 s), which opens its own tab.
        bpy.app.timers.register(_show_panel, first_interval=.6)


def unregister():
    del bpy.types.Scene.enf_shove
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)


if __name__ == '__main__':
    register()
