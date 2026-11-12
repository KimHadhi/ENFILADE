"""ENFILADE shove moves workspace, Blender 4.5 LTS.

START_SHOVE_ANIMATION.cmd opens ArtSource/Shove/ENFILADE_Shove_Animation.blend with truefps_tools.py and then this, and
the file is ready to work in: the user only poses and times (the user, 2026-11-13: "내가 해야 하는 잡일을 최대한 줄여. 나는
딱 오직 인간만이 할 수 있는 것만 해내면 되게끔"; the shared part is Tools/Blender/OneClick/one_click.py).
- It opens on the last move worked on: a gun move in Object mode with the gun (GUN) selected, the kick and the mending
  moves in Pose mode with the foot or the left hand selected; the Dope Sheet showing the move's keys (a row per bone,
  the draft's key poses as columns), the 밀치기 tab.
- Each move is one action the user keys into directly (auto-key on); its first draft is kept beside it (<name>_draft) for
  이 동작을 시안으로 되돌리기. A file built before (each draft locked on the NLA track '1 시안 (잠금)' under an empty
  touch-up action) is read as its drafts.
- Saving (Ctrl+S) sends every move that changed since it was last sent to the game (the panel's 지금 게임에 보내기 sends
  the move shown at once):
  - 총 휘두르기 · 긴 총 (enf_bash_long) and 총 휘두르기 · 권총 (enf_bash_pistol): the empty GUN moves off GUN_hold, which
    sits where Kazusa's hands hold that gun under the head camera (GunBakes gunFromEyes). The hands follow it here as they
    do in the game. Sent as Assets/_Project/Resources/Combat/GunMoves/<name>.csv: the gun's move and turn off its hold in
    the gun's own frame, in game metres, which FPSBody.Bash plays on any character's gun (GunMoves).
  - 발차기 (enf_kick_front): Kazusa's right leg on the True FPS rig, baked from the original skeleton to
    Assets/_Project/Art/Animations/Shove/enf_kick_front.fbx (+ .clips.csv); Unity takes it with her avatar
    (ModelImportRules) into the body's clips, and the kick plays it on the legs (Player.csv Body.kick).
  - 수리 · 망치 (enf_repair_hammer) and 수리 · 토치 (enf_repair_torch): the Support's mending loops on Kazusa's free left
    arm (CTRL_hand.L, CTRL_elbow.L) while the right hand keeps the gun; the left hand's hold on the gun is let go while one
    is shown. Sent as Assets/_Project/Resources/Tables/RepairHammer.csv or RepairTorch.csv: the wrist, the elbow and the
    hand's way from the eyes in the view's axes, which RepairArm plays on any character's left arm (GrenadeArmPath's
    columns). repair_moves.py adds them to an older workspace and keys their drafts.
Every button keys at the current frame with auto-key on, holding the pose some frames either side, so a click is one
local change. Nothing here changes Blender's preferences.
"""
import importlib.util
import json
import math
import sys
from pathlib import Path

import bpy
from bpy.props import BoolProperty, EnumProperty, FloatProperty, IntProperty
from mathutils import Matrix, Quaternion, Vector

bl_info = {'name': 'ENFILADE Shove Moves', 'author': 'ENFILADE', 'version': (2, 0, 0), 'blender': (4, 5, 0),
           'location': '3D View > Sidebar > 밀치기', 'category': 'Animation'}

TRUEFPS = Path('Tools/Blender/TrueFPS/truefps_tools.py')
ONE_CLICK = Path('Tools/Blender/OneClick/one_click.py')
TAB = '밀치기'
# Kazusa in the game is this many times her size in this file (eyes 1.37 here, 1.6 m in game: PlayerBody's fit).
GAME_SCALE = 1.166
# Where older files (and repair_moves.py's drafting) kept a draft locked under the touch-up layer.
BASE_TRACK = '1 시안 (잠금)'
GUN, GUN_HOLD = 'GUN', 'GUN_hold'
GUN_MOVES_DIR = Path('Assets/_Project/Resources/Combat/GunMoves')
KICK_DIR = Path('Assets/_Project/Art/Animations/Shove')
PLAYER_TABLE = Path('Assets/_Project/Resources/Tables/Player.csv')
# Seconds per sample in a gun move file: two per frame at 30 fps, so the game's curve follows the polish closely.
SAMPLES_PER_FRAME = 2
STRIKE = '타격'

# The moves: what moves (the gun or the body), the action that is exported, the gun shown, the frame the strike lands
# on and the last frame, and the part selected when the move is opened. Where the hands hold each gun, from the eyes in
# this file's units: GunBakes gunFromEyes / GAME_SCALE, the eyes' frame turned into the camera's (x right, y up, z back).
# hit: when the game itself hits, which the strike should meet; the animation never moves it. A shove with a gun hits
# from the press until Player.csv Ability.shoveActiveTime ('by'); the kick's foot lands at Shove.kickHitTime ('at').
MOVES = {
    'bash_long': dict(label='총 휘두르기 · 긴 총', kind='gun', action='enf_bash_long', gun='GUN_AK105', impact=3, end=14,
                      hold=(0.0620, -0.0748, -0.2599), hit=('Ability.shoveActiveTime', .2, 'by')),
    'bash_pistol': dict(label='총 휘두르기 · 권총', kind='gun', action='enf_bash_pistol', gun='GUN_R08', impact=3, end=13,
                        hold=(0.0668, -0.0659, -0.2415), hit=('Ability.shoveActiveTime', .2, 'by')),
    'kick': dict(label='발차기', kind='body', action='enf_kick_front', impact=10, end=20, hit=('Shove.kickHitTime', .3, 'at'),
                 part='CTRL_foot.R'),
    # The Support's mending in the free left hand while the gun stays in the right (the user, 2026-11-12: "남는 손으로
    # 망치 손으로 두들기는 거"): a loop each, played while F is held on a barricade (RepairArm), the left arm and hand only.
    # 타격 is where the hammer meets the planks: the knock sounds there, on every PC. The torch strikes nothing (0).
    # curl/thumb: how far the fingers and the thumb close round the tool (degrees), written into every key.
    'repair_hammer': dict(label='수리 · 망치', kind='arm', action='enf_repair_hammer', table='RepairHammer', tool='TOOL_Hammer',
                          impact=6, end=15, curl=88.0, thumb=62.0, part='CTRL_hand.L'),
    # The torch is the heist pack's gas torch: its canister (7.4 cm across) holds the fingers further open than the hammer's
    # handle does.
    'repair_torch': dict(label='수리 · 토치', kind='arm', action='enf_repair_torch', table='RepairTorch', tool='TOOL_Torch',
                         impact=0, end=31, curl=70.0, thumb=45.0, part='CTRL_hand.L'),
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


# ---- Paths and the shared tools ------------------------------------------------------------------------------------

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


def _module(name, rel):
    """A project tool module, loaded once and without a __pycache__ beside it (one there is tracked by git)."""
    if name in sys.modules:
        return sys.modules[name]
    root = project_root()
    if root is None:
        raise RuntimeError('ENFILADE 프로젝트 안의 ArtSource/Shove에서 파일을 열어 주세요.')
    spec = importlib.util.spec_from_file_location(name, root / rel)
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    writes = sys.dont_write_bytecode
    sys.dont_write_bytecode = True
    try:
        spec.loader.exec_module(module)
    finally:
        sys.dont_write_bytecode = writes
    return module


def truefps():
    """truefps_tools as a module (for its baking FBX export)."""
    return _module('truefps_tools', TRUEFPS)


def one_click():
    """The shared one-click workspace module: items, fingerprints, sending on save, the screen on open."""
    return _module('enf_one_click', ONE_CLICK)


def find_rig(scene):
    return next((o for o in scene.objects if o.type == 'ARMATURE' and o.get('truefps_rig')), None)


def is_shove_file(scene):
    return find_rig(scene) is not None and GUN in bpy.data.objects and bpy.data.actions.get('enf_bash_long_draft') is not None


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


# ---- The move on show -------------------------------------------------------------------------------------------------

def touch_up(name):
    """The move's own action, made empty if it has none (the build scripts call this after drafting; showing the move
    then makes it the editable copy of the draft)."""
    return one_click().touch_up(name)


def current_move(scene):
    key = scene.get('enf_shove_move', 'bash_long')
    return key if key in MOVES else 'bash_long'


def numbers(scene, key):
    """A move's strike frame and last frame, as last left on the timeline."""
    move = MOVES[key]
    return int(scene.get('enf_impact_' + key, move['impact'])), int(scene.get('enf_end_' + key, move['end']))


def remember(scene):
    """The move shown keeps where its strike marker and last frame are, for when it is picked again and for its
    fingerprint."""
    key = scene.get('enf_shove_move')
    if key not in MOVES:
        return
    marker = scene.timeline_markers.get(STRIKE)
    if marker is not None and scene.get('enf_impact_' + key) != marker.frame:
        scene['enf_impact_' + key] = marker.frame
    if scene.get('enf_end_' + key) != scene.frame_end:
        scene['enf_end_' + key] = scene.frame_end


def owner_of(scene, key):
    return bpy.data.objects.get(GUN) if MOVES[key]['kind'] == 'gun' else find_rig(scene)


def item(context, key):
    """The move's action (made ready from an older file's layers) and the draft locked under it (None: edited directly).
    A move made ready untouched counts as sent: the builds sent the drafts."""
    oc = one_click()
    move = MOVES[key]
    scene = context.scene
    action, draft, fresh = oc.item_action(context, owner_of(scene, key), move['action'], numbers(scene, key)[1])
    if fresh:
        oc.mark_sent(action, fingerprint(key))
    return action, draft


def fingerprint(key):
    """What a move is now: its keys and eases, its strike and its last frame."""
    oc = one_click()
    move = MOVES[key]
    action = bpy.data.actions.get(move['action'])
    draft = bpy.data.actions.get(move['action'] + oc.DRAFT) if action is not None and action.get(oc.LAYER) == 'touch-up' else None
    return oc.fingerprint([action, draft], extra=[key, *numbers(bpy.context.scene, key)])


def select_move(context, key):
    """Shows one move: its action on the gun or the rig (the other one still), its gun, its frames and its strike."""
    oc = one_click()
    move = MOVES[key]
    scene = context.scene
    remember(scene)
    rig = find_rig(scene)
    gun = bpy.data.objects.get(GUN)
    hold = bpy.data.objects.get(GUN_HOLD)
    # A handle the last move animated keeps its last value once nothing animates it: back to the start pose first, so
    # the left hand comes back onto the gun after a mending move and the legs down after the kick.
    reset_rig(rig)
    action, draft = item(context, key)
    if move['kind'] == 'gun':
        oc.play(gun, action, draft)
        oc.play(rig, None)
        if hold is not None:
            hold.location = move['hold']
    else:
        oc.play(rig, action, draft)
        if gun is not None:
            oc.play(gun, None)
            gun.location, gun.rotation_quaternion = (0, 0, 0), (1, 0, 0, 0)
        if hold is not None:
            hold.location = MOVES['bash_long']['hold']
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
    impact, end = numbers(scene, key)
    scene.frame_start, scene.frame_end = 1, end
    marker = scene.timeline_markers.get(STRIKE) or scene.timeline_markers.new(STRIKE, frame=impact)
    marker.frame = impact
    scene['enf_shove_move'] = key
    scene.frame_set(1)
    oc.auto_key(scene)


def focus(context, window, area, key):
    """What the move moves selected: the gun in Object mode, else the rig in Pose mode with the move's part."""
    move = MOVES[key]
    if move['kind'] == 'gun':
        gun = bpy.data.objects.get(GUN)
        one_click().select(context, window, area, [gun], gun, pose=False)
    else:
        rig = find_rig(context.scene)
        one_click().select(context, window, area, [rig], rig, pose=True, bones=[move['part']])


def prepare(context):
    """Every move ready to work in (an older file's layers made into editable moves), the last one shown."""
    for key in MOVES:
        item(context, key)
    select_move(context, current_move(context.scene))


def arrange(context):
    """The workspace as the work needs it: the move loaded, its part selected, its keys in the Dope Sheet, the tab open."""
    scene = context.scene
    if find_rig(scene) is None or GUN not in bpy.data.objects:
        return True
    # The moves first: they are ready even where the screen can't be (a window switches its workspace on the next tick).
    prepare(context)
    oc = one_click()
    if not oc.open_workspace(context):
        return False
    key = current_move(scene)
    for window in context.window_manager.windows:
        main = oc.main_view(window)
        oc.show_tab(main, TAB)
        oc.dope_sheet(window)
        if main is not None:
            focus(context, window, main, key)
    oc.tell(f'밀치기 「{MOVES[key]["label"]}」 · Space 재생 · 고치고 Ctrl+S 하면 게임에 들어갑니다')
    return True


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
    return one_click().curves(action)


def _key(obj, bone, frame):
    """Keys what is shown into the move's action."""
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


def clear_frame(context, frame):
    """Removes the shown move's keys at one frame (it then eases from the key before to the key after); returns how many."""
    scene = context.scene
    owner = owner_of(scene, current_move(scene))
    action = owner.animation_data.action if owner is not None and owner.animation_data else None
    return one_click().clear_keys(action, frame)


# ---- To the game ---------------------------------------------------------------------------------------------------

# Blender (x right, y forward, z up, right-handed) to Unity (x right, y up, z forward, left-handed): y and z swap.
_SWAP = Matrix(((1, 0, 0), (0, 0, 1), (0, 1, 0)))


def target(key):
    """Where a move goes in the game, from the project root."""
    move = MOVES[key]
    if move['kind'] == 'gun':
        return GUN_MOVES_DIR / (move['action'] + '.csv')
    if move['kind'] == 'arm':
        return TABLES_DIR / (move['table'] + '.csv')
    return KICK_DIR / (move['action'] + '.fbx')


def in_game(key):
    root = project_root()
    return root is not None and (root / target(key)).exists()


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
    oc = one_click()
    path = root / target(key)
    fps = scene.render.fps / scene.render.fps_base
    marker = scene.timeline_markers.get(STRIKE)
    impact = ((marker.frame if marker else MOVES[key]['impact']) - scene.frame_start) / fps
    lines = ['time,x,y,z,qx,qy,qz,qw', f'#impact={impact:.4f}',
             f'#from=ArtSource/Shove/ENFILADE_Shove_Animation.blend {MOVES[key]["action"]}']
    for time, move, turn in gun_samples(context, key):
        lines.append(f'{time:.4f},{move.x:.5f},{move.y:.5f},{move.z:.5f},{turn.x:.5f},{turn.y:.5f},{turn.z:.5f},{turn.w:.5f}')
    path.parent.mkdir(parents=True, exist_ok=True)
    oc.write_meta(path, oc.TEXT_META)
    return oc.write_text(path, '\n'.join(lines) + '\n')


def export_kick(context, root):
    scene = context.scene
    oc = one_click()
    rig = find_rig(scene)
    name = MOVES['kick']['action']
    path = root / KICK_DIR / (name + '.fbx')
    path.parent.mkdir(parents=True, exist_ok=True)
    clips = path.with_name(name + '.clips.csv')
    oc.write_meta(clips, oc.TEXT_META)
    oc.write_meta(path)
    truefps().export_active_clip(context, rig, str(path))
    oc.write_text(clips, 'clip,loop,inPlace,inPlaceY,inPlaceYaw,yawOffset,cycleOffset,from,to\n' f'{name},0,0,0,0,0,0,,\n')
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
    marker = scene.timeline_markers.get(STRIKE)
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
    oc = one_click()
    path = root / target(key)
    note = ('#설명 (블렌더 밀치기 작업실 ArtSource/Shove의 ' + move['label'] + ' 동작을 게임에 보내기로 쓴 왼팔의 길. '
            'GrenadeArm.csv와 같은 칸: 어깨에서 손목과 팔꿈치까지 팔 길이의 배수와 손의 방향을 시선 축(x 오른쪽 y 위 z 앞)으로. '
            'strike 1인 줄이 망치가 판자에 닿는 순간(소리). 손으로 고치지 말고 블렌더에서 고쳐 다시 보낸다. RepairArm이 어느 캐릭터 팔에든 맞춰 되풀이한다)')
    lines = ['﻿id,cycle,wristX,wristY,wristZ,elbowX,elbowY,elbowZ,alongX,alongY,alongZ,palmX,palmY,palmZ,curl,thumb,held,strike', note]
    for n, (cycle, wrist, elbow, along, palm, strike) in enumerate(arm_rows(context, key)):
        cells = [f'k{n:02d}', f'{cycle:.4f}'] + [f'{v:.4f}' for v in (*wrist, *elbow, *along, *palm)]
        cells += [f'{move["curl"]:.1f}', f'{move["thumb"]:.1f}', '1', '1' if strike else '0']
        lines.append(','.join(cells))
    path.parent.mkdir(parents=True, exist_ok=True)
    oc.write_meta(path, oc.TEXT_META)
    return oc.write_text(path, '\n'.join(lines) + '\n')


def export_move(context, key):
    """One move to the game (shown first if it isn't), marked as sent; returns the path written."""
    root = project_root()
    if root is None:
        raise RuntimeError('ENFILADE 프로젝트를 찾지 못했습니다. ArtSource/Shove의 파일을 여세요.')
    if current_move(context.scene) != key or MOVES[key]['action'] != getattr(_shown_action(context, key), 'name', None):
        select_move(context, key)
    remember(context.scene)
    kind = MOVES[key]['kind']
    path = export_gun(context, key, root) if kind == 'gun' else export_arm(context, key, root) if kind == 'arm' \
        else export_kick(context, root)
    one_click().mark_sent(bpy.data.actions.get(MOVES[key]['action']), fingerprint(key))
    return path


def _shown_action(context, key):
    owner = owner_of(context.scene, key)
    return owner.animation_data.action if owner is not None and owner.animation_data else None


def send_changed(context, force=False):
    """Every move that changed since it was last sent (or whose game file is missing) to the game; the move shown stays
    shown, on its frame. Returns (sent labels, failures), or None with the panel's switch off (force: sends anyway, the
    게임에 보내기 button)."""
    scene = context.scene
    if not is_shove_file(scene) or not (force or scene.enf_shove.auto_send):
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
            export_move(context, key)
            sent.append(move['label'])
        except Exception as error:
            failed.append(f'{move["label"]}: {error}')
    if current_move(scene) != shown:
        select_move(context, shown)
    if scene.frame_current != frame:
        scene.frame_set(frame)
    return sent, failed


def revert(context, key):
    """The move as its first draft again (its strike and last frame too)."""
    move = MOVES[key]
    one_click().revert(move['action'])
    scene = context.scene
    scene['enf_impact_' + key], scene['enf_end_' + key] = move['impact'], move['end']
    if current_move(scene) == key:
        # What select_move keeps for the move shown is what the timeline has: the draft's, then.
        marker = scene.timeline_markers.get(STRIKE)
        if marker is not None:
            marker.frame = move['impact']
        scene.frame_end = move['end']
    select_move(context, key)


# ---- Panel ---------------------------------------------------------------------------------------------------------

class ENFShoveSettings(bpy.types.PropertyGroup):
    auto_send: BoolProperty(name='저장하면 게임에 보내기', default=True,
                            description='Ctrl+S로 저장할 때 바뀐 동작을 모두 게임에 보낸다')
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
    bl_description = '이 동작을 불러오고 움직일 곳(총, 발, 왼손)을 골라 둔다'
    bl_options = {'REGISTER', 'UNDO'}
    move: EnumProperty(items=[(k, v['label'], '') for k, v in MOVES.items()])

    def execute(self, context):
        if not _ok(self, context):
            return {'CANCELLED'}
        _object_mode(context)
        select_move(context, self.move)
        if context.area is not None and context.area.type == 'VIEW_3D':
            focus(context, context.window, context.area, self.move)
        return {'FINISHED'}


class ENFSHOVE_OT_impact(bpy.types.Operator):
    bl_idname = 'enf_shove.impact'
    bl_label = '타격 프레임으로'
    bl_description = '맞는 순간(타격 표시)으로 간다. 게임은 이 순간에 맞힌다'

    def execute(self, context):
        marker = context.scene.timeline_markers.get(STRIKE)
        if marker:
            context.scene.frame_set(marker.frame)
        return {'FINISHED'}


class ENFSHOVE_OT_nudge(bpy.types.Operator):
    bl_idname = 'enf_shove.nudge'
    bl_label = '움직이기'
    bl_description = '지금 프레임에서 고른 곳을 조금 옮기거나 돌리고 키를 넣는다'
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


class ENFSHOVE_OT_clear(bpy.types.Operator):
    bl_idname = 'enf_shove.clear'
    bl_label = '이 프레임 키 지우기'
    bl_description = '지금 프레임에 있는 이 동작의 키를 지운다. 앞뒤 키 사이로 부드럽게 이어진다'
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        removed = clear_frame(context, context.scene.frame_current)
        self.report({'INFO'}, f'키 {removed}개를 지웠습니다')
        return {'FINISHED'}


class ENFSHOVE_OT_revert(bpy.types.Operator):
    bl_idname = 'enf_shove.revert'
    bl_label = '이 동작을 시안으로 되돌리기'
    bl_description = '이 동작의 내 손질을 모두 지우고 처음 시안으로 돌아간다(타격·끝 프레임도). 저장하면 게임에도 시안이 들어간다'
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


class ENFSHOVE_OT_export(bpy.types.Operator):
    bl_idname = 'enf_shove.export'
    bl_label = '게임에 보내기'
    bl_description = 'Ctrl+S와 같다: 바뀐 동작을 모두 게임 폴더로 보내고 작업 파일을 저장한다(저장하면 보내기를 꺼 두어도 보낸다)'

    def execute(self, context):
        if not _ok(self, context):
            return {'CANCELLED'}
        sent, failed = send_changed(context, force=True) or ([], ['밀치기 작업 파일이 아닙니다'])
        one_click().send_report(context.scene, sent, failed)
        if bpy.data.filepath:
            bpy.ops.wm.save_mainfile()
        if failed:
            self.report({'ERROR'}, '못 보냄: ' + '; '.join(failed))
            return {'CANCELLED'}
        self.report({'INFO'}, ('보냈습니다: ' + ', '.join(sent)) if sent else '바뀐 동작이 없어 저장만 했습니다')
        return {'FINISHED'}


class ENFSHOVE_PT_panel(bpy.types.Panel):
    bl_label = 'ENFILADE 밀치기 동작'
    bl_idname = 'ENFSHOVE_PT_panel'
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = TAB

    def draw(self, context):
        scene = context.scene
        layout = self.layout
        key = current_move(scene)
        move = MOVES[key]
        box = layout.box()
        box.label(text='1. 동작 고르기')
        for k, v in MOVES.items():
            box.operator('enf_shove.pick', text=v['label'], depress=k == key).move = k
        marker = scene.timeline_markers.get(STRIKE)
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
        row.operator('enf_shove.clear')
        row.operator('enf_shove.revert', icon='LOOP_BACK')

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
            box.label(text='게임이 지금 이 동작을 쓰는 중')
        if scene.get(oc.NOTE):
            box.label(text=str(scene[oc.NOTE])[:60])
        box.operator('enf_shove.export', icon='EXPORT')
        box.label(text=str(target(key)).replace('\\', '/'))


CLASSES = (ENFShoveSettings, ENFSHOVE_OT_pick, ENFSHOVE_OT_impact, ENFSHOVE_OT_nudge, ENFSHOVE_OT_clear,
           ENFSHOVE_OT_revert, ENFSHOVE_OT_export, ENFSHOVE_PT_panel)


def register():
    for cls in CLASSES:
        if hasattr(bpy.types, cls.__name__):
            try:
                bpy.utils.unregister_class(getattr(bpy.types, cls.__name__))
            except RuntimeError:
                pass
        bpy.utils.register_class(cls)
    bpy.types.Scene.enf_shove = bpy.props.PointerProperty(type=ENFShoveSettings)
    one_click().register_workspace(TAB, is_shove_file, arrange, send_changed)


def unregister():
    one_click().unregister_workspace(TAB)
    del bpy.types.Scene.enf_shove
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)


if __name__ == '__main__':
    register()
