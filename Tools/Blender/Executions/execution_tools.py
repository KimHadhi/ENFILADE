"""ENFILADE paired executions workspace, Blender 4.5 LTS.

START_EXECUTION_ANIMATION.cmd opens ArtSource/Executions/ENFILADE_Execution_Animation.blend with truefps_tools.py and
then this, and the file is ready to work in: the user only poses and times (the user, 2026-11-13: "내가 해야 하는 잡일을
최대한 줄여. 나는 딱 오직 인간만이 할 수 있는 것만 해내면 되게끔").
- It opens on the last pair worked on: both rigs in Pose mode, the Dope Sheet showing both rigs' keys (a row per bone,
  the draft's key poses as columns), the 처형 tab open, a three-quarter view and a side view framed on the pair.
- Every pair (처형 짝) is one action per rig that the user keys into directly (auto-key on); the first draft is kept
  beside it (<name>_draft) for 시안으로 되돌리기. A hand that holds the zombie copies a hold point on the zombie's bone
  (잡는 자리, an empty), so it stays on the zombie however the zombie is changed; the hold's span is its 잡기 constraint's
  influence, keyed in the pair's action. Moving the hold point moves where that hand holds, for that pair.
- Saving (Ctrl+S) sends every pair that changed since it was last sent to the game: both clips (enf_exe_<pair>_p for the
  player, _z for the zombie) to Assets/_Project/Art/Animations/Executions and the pair's row of
  Assets/_Project/Data/Data/Resources/Tables/Melee/Executions.csv (its length, the kill moment, where the player stands and faces, where
  the zombie lies at the start, the hands' holds). Unity takes the clips with the right avatars (ModelImportRules).
Nothing here changes Blender's preferences.
"""
import csv
import hashlib
import importlib.util
import io
import json
import math
import sys
import time
import uuid
from pathlib import Path

import bpy
from bpy.app.handlers import persistent
from bpy.props import BoolProperty, FloatProperty
from mathutils import Matrix, Vector

bl_info = {'name': 'ENFILADE Paired Executions', 'author': 'ENFILADE', 'version': (2, 0, 0), 'blender': (4, 5, 0),
           'location': '3D View > Sidebar > 처형', 'category': 'Animation'}

HERE_NAME = 'Tools/Blender/Executions'
TRUEFPS = Path('Tools/Blender/TrueFPS/truefps_tools.py')
ZOMBIES = Path('Tools/Blender/Zombies/zombie_tools.py')
OUT_DIR = Path('Assets/_Project/Art/Animations/Executions')
TABLES_DIR = Path('Assets/_Project/Data/Resources/Tables')
TABLE_NAME = 'Executions'
# Files built by 0.8.294-0.8.297 laid each draft locked on this NLA track under an empty touch-up action.
OLD_TRACK = '1 시안 (잠금)'
PREFIX = 'enf_exe_'
KILL_MARKER = '처치'
CSV_HEADER = 'clip,loop,inPlace,inPlaceY,inPlaceYaw,yawOffset,cycleOffset,from,to\n'
SCENE_PAIR = 'enf_exe_pair'
SCENE_SENT = 'enf_exe_sent'
WORKSPACE = 'ENFILADE - Animate'
TAB = '처형'
HOLD_COLLECTION = '08 잡는 자리 (손이 붙는 곳, 내보내지 않음)'
HOLD_POINTS = {'L': '잡는 자리 · 왼손', 'R': '잡는 자리 · 오른손(칼)'}
SIDE_WORDS = {'L': '왼손', 'R': '오른손(칼)'}
PROPS = ('enf_root', 'enf_yaw', 'enf_kill', 'enf_frames', 'enf_hold_L', 'enf_hold_R')


def project_root():
    """The ENFILADE checkout: from this file, else from the open .blend (ArtSource/Executions/...)."""
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
        raise RuntimeError('ENFILADE 프로젝트 안의 ArtSource/Executions에서 파일을 열어 주세요.')
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
    return _module('truefps_tools', TRUEFPS)


def zombies():
    return _module('zombie_tools', ZOMBIES)


def drafts():
    return _module('execution_drafts', Path(HERE_NAME) / 'execution_drafts.py')


def pairs():
    drafts()
    return _module('execution_pairs', Path(HERE_NAME) / 'execution_pairs.py')


def player_rig(scene):
    return next((o for o in scene.objects if o.type == 'ARMATURE' and o.get('truefps_rig') and o.get('truefps_kind') != 'zombie'), None)


def zombie_rig(scene):
    return next((o for o in scene.objects if o.type == 'ARMATURE' and o.get('truefps_rig') and o.get('truefps_kind') == 'zombie'), None)


def clip_names(key):
    return PREFIX + key + '_p', PREFIX + key + '_z'


def pair_action(key, side='p'):
    return bpy.data.actions.get(PREFIX + key + '_' + side)


def label_of(key):
    pair = pairs().PAIRS.get(key)
    return pair['label'] if pair else key


def _set(owner, name, value):
    """Sets a property only when it changes (an unchanged file stays unchanged)."""
    if getattr(owner, name) != value:
        setattr(owner, name, value)


def _plain(value):
    if value is None:
        return None
    if isinstance(value, str):
        return value
    if isinstance(value, (int, float)):
        # + 0.0: a rounded -0.0 is 0.0, so a value on zero never reads as changed.
        return round(float(value), 4) + 0.0
    try:
        return [_plain(v) for v in value]
    except TypeError:
        return str(value)


def _yaw(degrees):
    """A turn in degrees, rounded, in [-180, 180): a half turn is one number however the float lands either side."""
    return ((round(degrees, 2) + 180.0) % 360.0) - 180.0


def _store(action, prop, value):
    """Keeps a pair's number on its action, written only when it moved past rounding (so saving an unchanged pair
    leaves it unchanged and unsent)."""
    old = _plain(action.get(prop))
    new = _plain(value)
    if old is not None and json.dumps(old) == json.dumps(new):
        return
    action[prop] = value


# ---- The pair on the rigs -------------------------------------------------------------------------------------------

def set_action(obj, action):
    """obj plays this action, the one the user keys into."""
    data = obj.animation_data or obj.animation_data_create()
    old = data.nla_tracks.get(OLD_TRACK)
    if old is not None:
        data.nla_tracks.remove(old)
    if data.action != action:
        data.action = action
    if action is not None and hasattr(data, 'action_slot') and data.action_slot is None and len(getattr(action, 'slots', [])):
        data.action_slot = action.slots[0]
    _set(data, 'action_blend_type', 'REPLACE')
    _set(data, 'action_extrapolation', 'HOLD')
    _set(data, 'action_influence', 1.0)


def current_pair(scene):
    key = scene.get(SCENE_PAIR, 'back')
    return key if pair_action(key) else 'back'


def pair_frames(key):
    action = pair_action(key)
    return int(action.get('enf_frames', 30)) if action else 30


def pair_kill(key):
    action = pair_action(key)
    return int(action.get('enf_kill', 15)) if action else 15


def place_player(rig, key):
    """Kazusa's rig object where this pair has her: her root beside the zombie's, facing her way, at her game size."""
    action = pair_action(key)
    if action is None:
        return
    root = Vector(tuple(action.get('enf_root', (0.0, 0.0, 0.0))))
    yaw = float(action.get('enf_yaw', 0.0))
    want = Matrix.Translation(root) @ Matrix.Rotation(math.radians(yaw), 4, 'Z') @ Matrix.Scale(drafts().GAME_SCALE, 4)
    if any(abs(a - b) > 1e-6 for r, s in zip(rig.matrix_world, want) for a, b in zip(r, s)):
        rig.matrix_world = want


def _bone_frame(rig, bone):
    """A bone's frame in the scene as the pose has it now: its head, its turn, no scale (what holds are kept from)."""
    m = rig.matrix_world @ rig.pose.bones[bone].matrix
    return Matrix.Translation(m.translation) @ m.to_3x3().normalized().to_4x4()


def _hold(action, side):
    try:
        return json.loads(action.get('enf_hold_' + side, '') or 'null')
    except (TypeError, ValueError):
        return None


def ensure_holds(scene):
    """The hold points (an empty per hand, on the zombie's bone where that hand holds it) and each hand control's 잡기
    constraints copying its point's place and turn (never its size); the influence comes from each pair's action."""
    player, zombie = player_rig(scene), zombie_rig(scene)
    if player is None or zombie is None:
        return
    collection = bpy.data.collections.get(HOLD_COLLECTION)
    if collection is None:
        collection = bpy.data.collections.new(HOLD_COLLECTION)
        scene.collection.children.link(collection)
    d = drafts()
    for side, name in HOLD_POINTS.items():
        point = bpy.data.objects.get(name)
        if point is None:
            point = bpy.data.objects.new(name, None)
            collection.objects.link(point)
            point.parent = zombie
        point.empty_display_type = 'SPHERE'
        point.empty_display_size = .035
        point.show_in_front = True
        point.hide_render = True
        pb = player.pose.bones[f'CTRL_hand.{side}']
        for con_name, kind in d.HOLD_CONS:
            con = pb.constraints.get(con_name)
            if con is None:
                con = pb.constraints.new(kind)
                con.name = con_name
                con.influence = 0.0
            con.target = point
            con.target_space = con.owner_space = 'WORLD'
        _from_one_hold(pb, side)


def _from_one_hold(pb, side):
    """A file of 0.8.300 held each hand with one Copy Transforms (잡기), which also copied the hold point's size into the
    wrist: its influence keys go to the place and turn constraints in every action, and it goes."""
    d = drafts()
    old = pb.constraints.get(d.OLD_HOLD_CON)
    if old is None or old.type != 'COPY_TRANSFORMS':
        return
    old_path = d.hold_path(side, d.OLD_HOLD_CON)
    for action in bpy.data.actions:
        bag = d._channels(action)
        source = next((c for c in d.curves(action) if c.data_path == old_path), None)
        if bag is None or source is None:
            continue
        points = [(p.co.copy(), p.interpolation, p.easing) for p in source.keyframe_points]
        group = source.group
        for con_name, _ in d.HOLD_CONS:
            path = d.hold_path(side, con_name)
            curve = next((c for c in d.curves(action) if c.data_path == path), None) or bag.fcurves.new(path, index=0)
            curve.keyframe_points.clear()
            curve.keyframe_points.add(len(points))
            for point, (co, interpolation, easing) in zip(curve.keyframe_points, points):
                point.co = co
                point.interpolation = interpolation
                point.easing = easing
                point.handle_left_type = point.handle_right_type = 'AUTO_CLAMPED'
            curve.update()
            if group is not None and curve.group != group:
                curve.group = group
        bag.fcurves.remove(source)
    pb.constraints.remove(old)


def apply_holds(context, key):
    """Each hold point on this pair's bone, where its hand took hold (kept from the bone's frame, so it rides the bone
    whatever the zombie does); hidden for a hand that holds nothing in this pair."""
    scene = context.scene
    zombie, action = zombie_rig(scene), pair_action(key)
    if zombie is None or action is None:
        return
    context.view_layer.update()
    for side, name in HOLD_POINTS.items():
        point = bpy.data.objects.get(name)
        if point is None:
            continue
        hold = _hold(action, side)
        bone = hold['bone'] if hold and hold.get('bone') in zombie.pose.bones else 'Hips'
        if point.parent != zombie:
            point.parent = zombie
        _set(point, 'parent_type', 'BONE')
        _set(point, 'parent_bone', bone)
        point.matrix_parent_inverse = Matrix.Identity(4)
        pb = zombie.pose.bones[bone]
        # A bone parent holds its child from the bone's tail.
        parent = zombie.matrix_world @ pb.matrix @ Matrix.Translation((0.0, pb.bone.length, 0.0))
        offset = Matrix(hold['offset']) if hold else Matrix.Identity(4)
        point.matrix_basis = parent.inverted() @ (_bone_frame(zombie, bone) @ offset)
        try:
            point.hide_set(hold is None)
        except RuntimeError:
            pass


def remember(scene, key):
    """Keeps what the user moved for this pair on its action: Kazusa's place and turn, the kill marker, the last frame,
    where each hand holds the zombie."""
    action, rig, zombie = pair_action(key), player_rig(scene), zombie_rig(scene)
    if action is None or rig is None:
        return
    location, rotation, _ = rig.matrix_world.decompose()
    _store(action, 'enf_root', [round(location.x, 4), round(location.y, 4), 0.0])
    forward = rotation @ Vector((0.0, -1.0, 0.0))
    _store(action, 'enf_yaw', _yaw(math.degrees(math.atan2(forward.x, -forward.y))))
    marker = scene.timeline_markers.get(KILL_MARKER)
    if marker is not None:
        _store(action, 'enf_kill', int(marker.frame))
    _store(action, 'enf_frames', int(scene.frame_end))
    if zombie is None:
        return
    bpy.context.view_layer.update()
    for side, name in HOLD_POINTS.items():
        hold, point = _hold(action, side), bpy.data.objects.get(name)
        if not hold or point is None or point.parent_bone != hold['bone'] or hold['bone'] not in zombie.pose.bones:
            continue
        offset = _bone_frame(zombie, hold['bone']).inverted() @ point.matrix_world
        rows = [[round(v, 5) for v in row] for row in offset]
        if max(abs(a - b) for r, s in zip(rows, hold['offset']) for a, b in zip(r, s)) > 2e-4:
            hold['offset'] = rows
            action['enf_hold_' + side] = json.dumps(hold)


def select_pair(context, key, keep=True):
    """Shows one pair: both rigs' actions, its frames, Kazusa beside the zombie, the hold points and the kill marker."""
    scene = context.scene
    player, zombie = player_rig(scene), zombie_rig(scene)
    p_action, z_action = pair_action(key), pair_action(key, 'z')
    if player is None or zombie is None or p_action is None or z_action is None:
        raise ValueError('이 짝의 동작이 없습니다. build_execution_workspace.py로 작업 파일을 다시 만들어야 합니다.')
    if keep and scene.get(SCENE_PAIR) and scene.get(SCENE_PAIR) != key and pair_action(scene[SCENE_PAIR]):
        remember(scene, scene[SCENE_PAIR])
    for action in (p_action, z_action):
        _from_old_file(action)
    ensure_holds(scene)
    set_action(player, p_action)
    set_action(zombie, z_action)
    place_player(player, key)
    _set(scene, 'frame_start', 1)
    _set(scene, 'frame_end', pair_frames(key))
    marker = scene.timeline_markers.get(KILL_MARKER) or scene.timeline_markers.new(KILL_MARKER, frame=1)
    _set(marker, 'frame', pair_kill(key))
    if scene.get(SCENE_PAIR) != key:
        scene[SCENE_PAIR] = key
    if scene.frame_current != 1:
        scene.frame_set(1)
    apply_holds(context, key)
    _set(scene.tool_settings, 'use_keyframe_insert_auto', True)


def _from_old_file(action):
    """A file built by 0.8.294-0.8.297 kept the draft locked under an empty touch-up action: an untouched one gets the draft's
    keys, so it plays as before."""
    draft = bpy.data.actions.get(action.name + '_draft')
    if draft is None or drafts().curves(action) or not drafts().curves(draft):
        return
    fresh = draft.copy()
    fresh.use_fake_user = True
    if 'enf_layer' in fresh:
        del fresh['enf_layer']
    name = action.name
    action.name = name + '_old'
    fresh.name = name
    for owner in [o for o in bpy.data.objects if o.animation_data and o.animation_data.action == action]:
        set_action(owner, fresh)
    bpy.data.actions.remove(action)


# ---- Drafts ---------------------------------------------------------------------------------------------------------

def _fresh(name):
    old = bpy.data.actions.get(name)
    if old is not None:
        old.name = name + '_old'
        old.use_fake_user = False
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    return action


def _drop_old(name):
    old = bpy.data.actions.get(name + '_old')
    if old is not None and old.users == 0:
        bpy.data.actions.remove(old)


def _keep_draft(action):
    """The draft as made, kept beside the action the user edits (시안으로 되돌리기 copies it back)."""
    name = action.name + '_draft'
    old = bpy.data.actions.get(name)
    if old is not None:
        bpy.data.actions.remove(old)
    copy = action.copy()
    copy.name = name
    copy.use_fake_user = True
    copy['enf_layer'] = 'draft'
    return copy


def _seat(root):
    """Where the knife sits in the fist: Executions.csv row Knife, the numbers the game seats it by."""
    d = drafts()
    row = read_table(root).get('Knife', {}) if root else {}

    def number(name, fallback):
        try:
            return float(row.get(name, '') or fallback)
        except ValueError:
            return fallback
    return d.Seat(number('gripAlong', 1.05), number('gripPalm', .022), number('tilt', 12.0), number('knifeGrip', -.05),
                  number('knifeTip', .1056))


def make_zombie(context, key):
    """The zombie's draft for a pair from its start clip and the pair's key poses."""
    zombie = zombie_rig(context.scene)
    pair = pairs().PAIRS[key]
    z_name = clip_names(key)[1]
    z = drafts().ZombieDraft(context, zombies(), zombie, project_root() / pair['clip'], pair['at'])
    pairs().CHOREOGRAPHY[key][0](z, pair)
    action = _fresh(z_name)
    action['enf_source'] = pair['clip']
    z.write(action, pair['frames'])
    _keep_draft(action)
    _drop_old(z_name)
    return action


def make_player(context, key):
    """Kazusa's draft for a pair, its contacts worked out from the zombie's draft, and where each hand holds it."""
    scene = context.scene
    player, zombie = player_rig(scene), zombie_rig(scene)
    pair = pairs().PAIRS[key]
    d = drafts()
    p_name, z_name = clip_names(key)
    count = pair['frames']
    frames = d.zombie_bone_frames(context, zombie, bpy.data.actions[z_name], count)
    data = player.animation_data or player.animation_data_create()
    keep_action = data.action
    action = _fresh(p_name)
    right, forward = pair['root']
    action['enf_root'] = [round(v, 4) for v in d.zv(right, forward, 0.0)]
    action['enf_yaw'] = _yaw(float(pair['yaw']))
    action['enf_kill'] = int(pair['kill'])
    action['enf_frames'] = int(count)
    # Made with the rig placed where this pair has her (her rig object's place is part of every contact).
    place_player(player, key)
    p = d.PlayerDraft(context, player, zombie, _seat(project_root()))
    pairs().CHOREOGRAPHY[key][1](p, frames, pair)
    holds = p.write(action, count, frames)
    for side in ('L', 'R'):
        if side in holds:
            bone, offset = holds[side]
            action['enf_hold_' + side] = json.dumps({'bone': bone, 'offset': [[round(v, 5) for v in row] for row in offset]})
    data.action = keep_action
    _keep_draft(action)
    _drop_old(p_name)
    return action


def make_pair(context, key):
    ensure_holds(context.scene)
    make_zombie(context, key)
    make_player(context, key)


def restore_draft(context, key):
    """The pair as its first draft: both actions copied back from the drafts kept beside them."""
    for name in clip_names(key):
        draft = bpy.data.actions.get(name + '_draft')
        if draft is None:
            raise ValueError('이 짝의 시안이 없습니다.')
        fresh = draft.copy()
        fresh.use_fake_user = True
        if 'enf_layer' in fresh:
            del fresh['enf_layer']
        old = bpy.data.actions.get(name)
        if old is not None:
            old.name = name + '_old'
            old.use_fake_user = False
        fresh.name = name
    select_pair(context, key, keep=False)
    for name in clip_names(key):
        _drop_old(name)


# ---- The table ------------------------------------------------------------------------------------------------------

def table_file(root, name, new_in='Melee'):
    """The game's table of this name under the project root. Since 0.9.0 the tables are filed in sub-folders of
    Data/Resources/Tables by subject and found by file name alone, as the game finds them (GameTables); a table that isn't
    there yet is made in new_in."""
    base = Path(root) / TABLES_DIR
    for found in sorted(base.rglob(name + '.csv')):
        return found
    return base / new_in / (name + '.csv')


def read_table(root):
    """Executions.csv as {id: row}, the 설명 row left out."""
    path = table_file(root, TABLE_NAME)
    if not path.exists():
        return {}
    text = path.read_bytes().decode('utf-8-sig')
    rows = list(csv.reader(io.StringIO(text)))
    if not rows:
        return {}
    header = rows[0]
    out = {}
    for row in rows[1:]:
        if not row or not row[0] or row[0].startswith('#'):
            continue
        out[row[0]] = dict(zip(header, row))
    return out


def write_row(root, key, values):
    """Writes these columns of the pair's row (made if missing), keeping every other row, the 설명 row and the file's
    line ends. Written in place, retried: a file Unity has open can't be truncated by a plain write on this PC."""
    path = table_file(root, TABLE_NAME)
    raw = path.read_bytes()
    text = raw.decode('utf-8-sig')
    newline = '\r\n' if '\r\n' in text else '\n'
    rows = list(csv.reader(io.StringIO(text)))
    header = rows[0]
    for column in values:
        if column not in header:
            raise ValueError(f'Executions.csv에 {column} 열이 없습니다.')
    target = next((r for r in rows[1:] if r and r[0] == key), None)
    if target is None:
        target = [key] + [''] * (len(header) - 1)
        rows.append(target)
    while len(target) < len(header):
        target.append('')
    for column, value in values.items():
        target[header.index(column)] = value
    out = io.StringIO()
    writer = csv.writer(out, lineterminator=newline)
    writer.writerows(rows)
    data = ('﻿' + out.getvalue()).encode('utf-8')
    for attempt in range(20):
        try:
            with open(path, 'r+b') as handle:
                handle.seek(0)
                handle.write(data)
                handle.truncate()
            return
        except OSError:
            time.sleep(.25)
    raise RuntimeError('Executions.csv를 쓰지 못했습니다(Unity가 붙잡고 있음). 다시 저장하면 다시 보냅니다.')


def _number(value, digits=3):
    text = f'{value:.{digits}f}'.rstrip('0').rstrip('.')
    return '0' if text in ('-0', '') else text


# ---- Sending to the game --------------------------------------------------------------------------------------------

def _meta(path, body):
    """A Unity .meta beside a new file, written before it (a file that lands first gets a GUID of Unity's own, which the
    .meta would then contradict). An existing .meta is never touched."""
    meta = path.with_name(path.name + '.meta')
    if meta.exists():
        return
    meta.write_text(f'fileFormatVersion: 2\nguid: {uuid.uuid4().hex}\n' + body, encoding='utf-8')


TEXT_META = 'TextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'


def _game(v):
    """A scene point or direction (this file is in the game's metres) in the zombie's frame: right, forward, up."""
    return -v.x, -v.y, v.z


def held_frames(action, side, count):
    """The frames a hand rides its hold point whole (its 잡기 influence at 1): the first run of them, or None."""
    path = drafts().hold_path(side)
    curve = next((c for c in drafts().curves(action) if c.data_path == path), None)
    if curve is None:
        return None
    first = last = None
    for frame in range(1, count + 1):
        if curve.evaluate(frame) >= .999:
            if first is None:
                first = last = frame
            elif frame == last + 1:
                last = frame
            else:
                break
        elif first is not None:
            break
    return (first, last) if first is not None else None


def measure(context, key):
    """What the game needs besides the clips: where the zombie lies at the start, and each hand's hold of it (a bone, a
    window, and where the wrist sits from that bone in the zombie's frame at the grab), from the pair as it plays now."""
    scene = context.scene
    player, zombie = player_rig(scene), zombie_rig(scene)
    action = pair_action(key)
    scene.frame_set(1)
    context.view_layer.update()
    zw = zombie.matrix_world
    hips = zw @ zombie.pose.bones['Hips'].head
    head = zw @ zombie.pose.bones['Head'].head
    hr, hf, _ = _game(hips)
    lie = head - hips
    head_yaw = math.degrees(math.atan2(-lie.x, -lie.y))
    out = {'zHipsRight': _number(hr), 'zHipsForward': _number(hf), 'zHeadYaw': _number(head_yaw, 1)}
    for side, prefix in (('L', 'l'), ('R', 'r')):
        hold = _hold(action, side)
        span = held_frames(action, side, scene.frame_end) if hold else None
        if not span:
            out.update({prefix + 'Bone': '', prefix + 'From': '', prefix + 'To': '', prefix + 'Right': '',
                        prefix + 'Forward': '', prefix + 'Up': ''})
            continue
        first, last = span
        scene.frame_set(first)
        context.view_layer.update()
        wrist = player.matrix_world @ player.pose.bones['Left wrist' if side == 'L' else 'Right wrist'].head
        held = zw @ zombie.pose.bones[hold['bone']].head
        r, f, u = _game(wrist - held)
        out.update({prefix + 'Bone': hold['bone'], prefix + 'From': _number((first - 1) / scene.render.fps),
                    prefix + 'To': _number((last - 1) / scene.render.fps), prefix + 'Right': _number(r),
                    prefix + 'Forward': _number(f), prefix + 'Up': _number(u)})
    scene.frame_set(1)
    return out


def export_pair(context, key):
    """Both clips of the pair to the game and its row of Executions.csv; returns the paths written."""
    scene = context.scene
    root = project_root()
    if root is None:
        raise RuntimeError('ENFILADE 프로젝트를 찾지 못했습니다.')
    current = current_pair(scene)
    if key == current:
        remember(scene, key)
    else:
        select_pair(context, key)
    try:
        player, zombie = player_rig(scene), zombie_rig(scene)
        pair = pairs().PAIRS[key]
        p_name, z_name = clip_names(key)
        folder = root / OUT_DIR
        folder.mkdir(parents=True, exist_ok=True)
        tools = truefps()
        written = []
        values = measure(context, key)
        location, rotation, _ = player.matrix_world.decompose()
        for name, rig in ((p_name, player), (z_name, zombie)):
            path = folder / (name + '.fbx')
            sidecar = folder / (name + '.clips.csv')
            _meta(sidecar, TEXT_META)
            # One clip, not looped, its root kept as authored (inPlace 0): it plays where the pair puts it.
            sidecar.write_text(CSV_HEADER + f'{name},0,0,0,0,0,0,,\n', encoding='utf-8')
            _meta(path, '')
            saved = rig.matrix_world.copy()
            zombie_at = zombie.matrix_world.copy()
            try:
                if rig is player:
                    # Baked at her own root and size: the game puts her root at the pair's offset (the row below). The
                    # zombie goes with her into her frame, and with it the hold points her hands copy, so a held hand is
                    # baked where it is on her (0.8.300 left the zombie in place: held hands came out far off her body).
                    zombie.matrix_world = saved.inverted() @ zombie_at
                    rig.matrix_world = Matrix.Identity(4)
                    context.view_layer.update()
                tools.export_active_clip(context, rig, str(path))
            finally:
                rig.matrix_world = saved
                zombie.matrix_world = zombie_at
                context.view_layer.update()
            written += [path, sidecar]
        forward = rotation @ Vector((0.0, -1.0, 0.0))
        right, ahead, _ = _game(location)
        # The game's yaw turns to the right (Unity), Blender's to the left.
        yaw = -math.degrees(math.atan2(forward.x, -forward.y))
        fps = scene.render.fps / scene.render.fps_base
        marker = scene.timeline_markers.get(KILL_MARKER)
        kill = int(marker.frame) if marker is not None else pair_kill(key)
        values.update({
            'kind': pair['kind'], 'playerClip': p_name, 'zombieClip': z_name,
            'length': _number((scene.frame_end - 1) / fps), 'kill': _number((kill - 1) / fps),
            'right': _number(right), 'forward': _number(ahead), 'yaw': _number(yaw, 1),
        })
        write_row(root, key, values)
        return written
    finally:
        if key != current:
            select_pair(context, current, keep=False)


def fingerprint(key):
    """What a pair is now (both actions' keys, eases and modifiers, its place, kill, length and holds), to tell whether
    it changed since it was last sent."""
    digest = hashlib.sha1()
    for name in clip_names(key):
        action = bpy.data.actions.get(name)
        if action is None:
            digest.update(b'-')
            continue
        for curve in sorted(drafts().curves(action), key=lambda c: (c.data_path, c.array_index)):
            points = curve.keyframe_points
            count = len(points)
            digest.update(f'{curve.data_path}[{curve.array_index}]{count}'.encode())
            values = [0.0] * (2 * count)
            for attr in ('co', 'handle_left', 'handle_right'):
                points.foreach_get(attr, values)
                digest.update(','.join(f'{v:.5f}' for v in values).encode())
            kinds = [0] * count
            points.foreach_get('interpolation', kinds)
            digest.update(str(kinds).encode())
            for m in curve.modifiers:
                digest.update(str((m.type, m.mute, round(getattr(m, 'strength', 0.0), 5), round(getattr(m, 'scale', 0.0), 5),
                                   round(getattr(m, 'phase', 0.0), 5), m.use_restricted_range, m.frame_start, m.frame_end,
                                   m.blend_in, m.blend_out)).encode())
        for prop in PROPS:
            digest.update(f'{prop}={json.dumps(_plain(action.get(prop)))}'.encode())
    return digest.hexdigest()


_PRINTS = {}


def fingerprint_soon(key):
    """fingerprint, at most every two seconds per pair (the panel asks on every redraw, playback included)."""
    now = time.monotonic()
    cached = _PRINTS.get(key)
    if cached is None or now - cached[0] > 2.0:
        cached = (now, fingerprint(key))
        _PRINTS[key] = cached
    return cached[1]


def mark_sent(key):
    action = pair_action(key)
    if action is not None:
        action['enf_sent'] = fingerprint(key)
        if 'enf_withheld' in action:
            del action['enf_withheld']


def is_sent(key):
    root = project_root()
    return root is not None and (root / OUT_DIR / (clip_names(key)[0] + '.fbx')).exists()


def send_changed(context):
    """Every pair that changed since it was last sent (or never was) goes to the game; the pair shown stays shown.
    Returns (sent labels, failures)."""
    scene = context.scene
    key = current_pair(scene)
    remember(scene, key)
    sent, failed = [], []
    for k in pairs().ORDER:
        action = pair_action(k)
        if action is None or pair_action(k, 'z') is None:
            continue
        mark = fingerprint(k)
        if action.get('enf_sent') == mark and (is_sent(k) or action.get('enf_withheld')):
            continue
        try:
            export_pair(context, k)
            mark_sent(k)
            sent.append(label_of(k))
        except Exception as error:
            failed.append(f'{label_of(k)}: {error}')
    if current_pair(scene) != key:
        select_pair(context, key, keep=False)
    return sent, failed


def withhold(key):
    """The pair's clips out of the game (it then plays its old execution there), kept out until the pair changes."""
    root = project_root()
    removed = []
    if root is not None:
        for name in clip_names(key):
            for path in ((root / OUT_DIR / (name + '.fbx')), (root / OUT_DIR / (name + '.clips.csv'))):
                if path.exists():
                    path.unlink()
                    removed.append(path.name)
    action = pair_action(key)
    if action is not None:
        action['enf_sent'] = fingerprint(key)
        action['enf_withheld'] = True
    return removed


# ---- Opening ready to work -------------------------------------------------------------------------------------------

def _pair_axes(scene):
    """The pair's middle (a little below the chest) and its ways: from the zombie toward Kazusa, and across."""
    player = player_rig(scene)
    at = player.matrix_world.translation if player else Vector((0.0, .5, 0.0))
    toward = Vector((at.x, at.y, 0.0))
    toward = toward.normalized() if toward.length > .05 else Vector((0.0, 1.0, 0.0))
    across = Vector((-toward.y, toward.x, 0.0))
    return Vector((at.x * .5, at.y * .5, .8)), toward, across


def frame_views(context):
    """The largest 3D view at three quarters from one side of the pair, any other 3D view square from the other side
    (orthographic), both on the pair's middle."""
    scene = context.scene
    middle, toward, across = _pair_axes(scene)
    for window in context.window_manager.windows:
        areas = [a for a in window.screen.areas if a.type == 'VIEW_3D']
        if not areas:
            continue
        main = max(areas, key=lambda a: a.width * a.height)
        for area in areas:
            space = area.spaces.active
            view = space.region_3d
            if area == main:
                eye = middle + across * 2.3 - toward * .8 + Vector((0.0, 0.0, .55))
                view.view_perspective = 'PERSP'
                view.view_location = middle
                view.view_rotation = (middle - eye).to_track_quat('-Z', 'Y')
                view.view_distance = (middle - eye).length
            else:
                view.view_perspective = 'ORTHO'
                view.view_location = middle
                view.view_rotation = across.to_track_quat('-Z', 'Y')
                view.view_distance = 2.6


def arrange_ui(context):
    """The workspace as the work needs it: the 처형 tab open, the pair loaded, both rigs in Pose mode, both rigs' keys in
    the Dope Sheet, the views framed on the pair."""
    scene = context.scene
    player, zombie = player_rig(scene), zombie_rig(scene)
    if player is None or zombie is None:
        return True
    workspace = bpy.data.workspaces.get(WORKSPACE)
    # The workspace first: its screen is the window's only from the next tick on (then this runs again).
    switched = False
    for window in context.window_manager.windows:
        if workspace is not None and window.workspace != workspace:
            window.workspace = workspace
            switched = True
    if switched:
        return False
    select_pair(context, current_pair(scene), keep=False)
    shown = [zombie, player] + [o for o in scene.objects if o.parent == zombie or o.parent == player
                                 or o.name in HOLD_POINTS.values()]
    for window in context.window_manager.windows:
        screen = window.screen
        views = [a for a in screen.areas if a.type == 'VIEW_3D']
        main = max(views, key=lambda a: a.width * a.height) if views else None
        for area in views:
            space = area.spaces.active
            if area == main:
                space.show_region_ui = True
                region = next((r for r in area.regions if r.type == 'UI'), None)
                if region is not None:
                    try:
                        region.active_panel_category = TAB
                    except (TypeError, ValueError, RuntimeError, AttributeError):
                        pass
                continue
            # The True FPS start file's small first-person preview: the pair from the side instead, the zombie in it.
            space.camera = None
            space.use_local_camera = False
            if space.local_view is not None:
                for obj in shown:
                    try:
                        obj.local_view_set(space, True)
                    except RuntimeError:
                        pass
            space.overlay.show_overlays = True
        for area in screen.areas:
            if area.type == 'DOPESHEET_EDITOR' and area.ui_type != 'TIMELINE':
                space = area.spaces.active
                space.ui_mode = 'DOPESHEET'
                space.dopesheet.show_only_selected = False
                space.dopesheet.show_summary = True
                space.show_markers = True
        if main is not None:
            region = next((r for r in main.regions if r.type == 'WINDOW'), None)
            try:
                with context.temp_override(window=window, area=main, region=region):
                    if context.object is not None and context.object.mode != 'OBJECT':
                        bpy.ops.object.mode_set(mode='OBJECT')
                    for obj in context.view_layer.objects:
                        obj.select_set(False)
                    zombie.select_set(True)
                    player.select_set(True)
                    context.view_layer.objects.active = player
                    bpy.ops.object.mode_set(mode='POSE')
            except RuntimeError as error:
                print('ENFILADE 처형: 포즈 모드로 못 들어감', error)
    frame_views(context)
    _tell(f'처형 짝 「{label_of(current_pair(scene))}」 · Space 재생 · 고치고 Ctrl+S 하면 게임에 들어갑니다')
    return True


def _tell(text):
    print('ENFILADE 처형:', text)
    for window in bpy.context.window_manager.windows:
        try:
            window.workspace.status_text_set(text)
        except (AttributeError, RuntimeError):
            pass
    if not bpy.app.background:
        if bpy.app.timers.is_registered(_quiet):
            bpy.app.timers.unregister(_quiet)
        bpy.app.timers.register(_quiet, first_interval=10.0)


def _quiet():
    for window in bpy.context.window_manager.windows:
        try:
            window.workspace.status_text_set(None)
        except (AttributeError, RuntimeError):
            pass
    return None


_ARRANGE_TRIES = [0]


def _arrange():
    _ARRANGE_TRIES[0] += 1
    try:
        if not arrange_ui(bpy.context) and _ARRANGE_TRIES[0] < 6:
            return .3
    except Exception as error:
        print('ENFILADE 처형: 작업 화면을 맞추지 못함', error)
    _ARRANGE_TRIES[0] = 0
    return None


@persistent
def _load_post(*_unused):
    if not bpy.app.background and not bpy.app.timers.is_registered(_arrange):
        _ARRANGE_TRIES[0] = 0
        bpy.app.timers.register(_arrange, first_interval=.8)


@persistent
def _save_pre(*_unused):
    """Saving sends what changed to the game (the panel's switch turns it off)."""
    try:
        context = bpy.context
        scene = context.scene
        if scene is None or player_rig(scene) is None or zombie_rig(scene) is None or not scene.enf_exe.auto_send:
            return
        sent, failed = send_changed(context)
        stamp = time.strftime('%H:%M:%S')
        if sent or failed:
            text = (f'{stamp} 게임에 보냄: ' + ', '.join(sent)) if sent else f'{stamp} 보내지 못함'
            if failed:
                text += ' · 못 보냄: ' + '; '.join(failed)
            scene[SCENE_SENT] = text
            _tell(text + (' — Unity 창을 누르면 가져갑니다' if sent else ''))
    except Exception as error:
        print('ENFILADE 처형: 저장하며 게임에 보내지 못함', error)


# ---- UI -------------------------------------------------------------------------------------------------------------

class ENFExeSettings(bpy.types.PropertyGroup):
    auto_send: BoolProperty(name='저장하면 게임에 보내기', default=True,
                            description='Ctrl+S로 저장할 때 바뀐 짝을 모두 게임에 보낸다(클립 두 개와 Executions.csv 줄)')
    step_deg: FloatProperty(name='돌리는 각도', default=6.0, min=1.0, max=30.0, description='좀비 부위 버튼 한 번에 돌리는 각도(도)')
    step_cm: FloatProperty(name='옮기는 거리', default=3.0, min=.5, max=20.0, description='카즈사 손·발·골반 버튼 한 번에 옮기는 거리(cm)')
    reach: FloatProperty(name='앞뒤로 퍼지는 프레임', default=4.0, min=0.0, max=20.0,
                         description='버튼으로 바꾼 자세가 이 프레임만큼 앞뒤로 부드럽게 퍼진다. 0이면 그 프레임만 바뀐다')


def _ok(op, context):
    scene = context.scene
    if player_rig(scene) is None or zombie_rig(scene) is None:
        op.report({'ERROR'}, '카즈사 리그와 좀비 리그를 찾지 못했습니다.')
        return False
    if context.object and context.object.mode == 'EDIT':
        bpy.ops.object.mode_set(mode='OBJECT')
    return True


def holding_now(context):
    """Which hands ride their hold point at the current frame: [(side, bone)]."""
    scene = context.scene
    action = pair_action(current_pair(scene))
    out = []
    if action is None:
        return out
    for side in ('L', 'R'):
        hold = _hold(action, side)
        curve = next((c for c in drafts().curves(action) if c.data_path == drafts().hold_path(side)), None)
        if hold and curve is not None and curve.evaluate(scene.frame_current) > .5:
            out.append((side, hold['bone']))
    return out


class ENFEXE_OT_pick(bpy.types.Operator):
    bl_idname = 'enf_exe.pick'
    bl_label = '짝 고르기'
    bl_options = {'REGISTER', 'UNDO'}
    key: bpy.props.StringProperty()

    def execute(self, context):
        if not _ok(self, context):
            return {'CANCELLED'}
        select_pair(context, self.key)
        frame_views(context)
        return {'FINISHED'}


class ENFEXE_OT_kill_here(bpy.types.Operator):
    bl_idname = 'enf_exe.kill_here'
    bl_label = '처치 순간을 지금 프레임으로'
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        scene = context.scene
        marker = scene.timeline_markers.get(KILL_MARKER) or scene.timeline_markers.new(KILL_MARKER, frame=1)
        marker.frame = scene.frame_current
        remember(scene, current_pair(scene))
        self.report({'INFO'}, f'처치: {scene.frame_current} 프레임 ({(scene.frame_current - 1) / 30:.2f}초)')
        return {'FINISHED'}


class ENFEXE_OT_revert(bpy.types.Operator):
    bl_idname = 'enf_exe.revert'
    bl_label = '이 짝을 시안으로 되돌리기'
    bl_description = '이 짝의 내 손질을 모두 지우고 처음 시안으로 돌아간다(자리·처치·잡는 자리도). 되돌릴 수 없다'
    bl_options = {'REGISTER', 'UNDO'}

    def invoke(self, context, event):
        return context.window_manager.invoke_confirm(self, event)

    def execute(self, context):
        if not _ok(self, context):
            return {'CANCELLED'}
        try:
            restore_draft(context, current_pair(context.scene))
        except ValueError as error:
            self.report({'ERROR'}, str(error))
            return {'CANCELLED'}
        self.report({'INFO'}, '시안으로 되돌렸다. 저장하면 게임에도 시안이 들어간다')
        return {'FINISHED'}


class ENFEXE_OT_send(bpy.types.Operator):
    bl_idname = 'enf_exe.send'
    bl_label = '지금 게임에 보내기'
    bl_description = '저장하지 않고 이 짝만 지금 게임에 보낸다'

    def execute(self, context):
        if not _ok(self, context):
            return {'CANCELLED'}
        key = current_pair(context.scene)
        try:
            export_pair(context, key)
            mark_sent(key)
        except Exception as error:
            self.report({'ERROR'}, str(error))
            return {'CANCELLED'}
        self.report({'INFO'}, f'보냄: {label_of(key)} — Unity 창을 누르면 가져가고, 다음 Play부터 쓰인다')
        return {'FINISHED'}


class ENFEXE_OT_withhold(bpy.types.Operator):
    bl_idname = 'enf_exe.withhold'
    bl_label = '게임에서 빼기'
    bl_description = '이 짝의 클립을 게임에서 지운다. 게임은 이 상태에서 예전 처형(제자리 칼질)으로 대신한다. 이 짝을 다시 고치면 저장할 때 다시 들어간다'

    def invoke(self, context, event):
        return context.window_manager.invoke_confirm(self, event)

    def execute(self, context):
        removed = withhold(current_pair(context.scene))
        self.report({'INFO'}, ('지움: ' + ', '.join(removed)) if removed else '게임에 들어간 클립이 없습니다')
        return {'FINISHED'}


ZOMBIE_PARTS = (('머리', 'Head'), ('목', 'Neck'), ('가슴', 'Spine_03'), ('배', 'Spine_02'), ('허리', 'Spine_01'),
                ('왼위팔', 'Shoulder_L'), ('왼아래팔', 'Elbow_L'), ('오른위팔', 'Shoulder_R'), ('오른아래팔', 'Elbow_R'))
PLAYER_PARTS = (('왼손', 'CTRL_hand.L'), ('오른손(칼)', 'CTRL_hand.R'), ('왼발', 'CTRL_foot.L'), ('오른발', 'CTRL_foot.R'),
                ('골반', 'Hips'))


class ENFEXE_OT_zombie_part(bpy.types.Operator):
    """The zombie's part turned toward a body direction at this frame, keyed."""
    bl_idname = 'enf_exe.zombie_part'
    bl_label = '좀비 부위 돌리기'
    bl_options = {'REGISTER', 'UNDO'}
    bone: bpy.props.StringProperty()
    move: bpy.props.StringProperty()

    def execute(self, context):
        if not _ok(self, context):
            return {'CANCELLED'}
        settings = context.scene.enf_exe
        try:
            zombies().nudge(context, zombie_rig(context.scene), self.bone, self.move, settings.step_deg, int(settings.reach))
        except ValueError as error:
            self.report({'WARNING'}, str(error))
            return {'CANCELLED'}
        return {'FINISHED'}


class ENFEXE_OT_player_part(bpy.types.Operator):
    """Kazusa's hand, foot or hips moved a few centimetres along her own directions at this frame, keyed."""
    bl_idname = 'enf_exe.player_part'
    bl_label = '카즈사 부위 옮기기'
    bl_options = {'REGISTER', 'UNDO'}
    bone: bpy.props.StringProperty()
    move: bpy.props.StringProperty()

    def execute(self, context):
        if not _ok(self, context):
            return {'CANCELLED'}
        scene = context.scene
        rig = player_rig(scene)
        settings = scene.enf_exe
        frame, reach = scene.frame_current, int(settings.reach)
        inverse = rig.matrix_world.to_3x3().inverted()
        right = (inverse @ Vector((-1, 0, 0)))
        directions = {'forward': inverse @ Vector((0, -1, 0)), 'back': inverse @ Vector((0, 1, 0)), 'up': inverse @ Vector((0, 0, 1)),
                      'down': inverse @ Vector((0, 0, -1)), 'left': -right, 'right': right}
        pb = rig.pose.bones[self.bone]
        for at in (frame - reach, frame + reach) if reach > 0 else ():
            if scene.frame_start <= at <= scene.frame_end:
                scene.frame_set(at)
                pb.keyframe_insert('location', frame=at, group=self.bone)
                pb.keyframe_insert('rotation_quaternion', frame=at, group=self.bone)
        scene.frame_set(frame)
        context.view_layer.update()
        matrix = pb.matrix.copy()
        # In her own directions, the step in this file's metres (her armature is at her own size, so it is scaled).
        matrix.translation = matrix.translation + directions[self.move].normalized() * (settings.step_cm / 100.0) / drafts().GAME_SCALE
        pb.matrix = matrix
        context.view_layer.update()
        pb.keyframe_insert('location', frame=frame, group=self.bone)
        pb.keyframe_insert('rotation_quaternion', frame=frame, group=self.bone)
        return {'FINISHED'}


class ENFEXE_PT_panel(bpy.types.Panel):
    bl_label = '처형 다듬기'
    bl_idname = 'ENFEXE_PT_panel'
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = TAB

    def draw(self, context):
        layout = self.layout
        scene = context.scene
        key = current_pair(scene)
        p = pairs()
        kill = scene.timeline_markers.get(KILL_MARKER)
        box = layout.box()
        box.label(text=f'지금: {label_of(key)}', icon='ARMATURE_DATA')
        box.label(text=f'{scene.frame_end}프레임 ({(scene.frame_end - 1) / 30:.2f}초) · 처치 {kill.frame if kill else "?"}'
                       f' ({((kill.frame if kill else 1) - 1) / 30:.2f}초) · '
                       + ('암살' if p.PAIRS.get(key, {}).get('kind') == 'takedown' else '처형'))
        for side, bone in holding_now(context):
            box.label(text=f'{SIDE_WORDS[side]}이 좀비 {bone}에 붙어 있음', icon='CONSTRAINT')
            box.label(text=f'  잡는 곳은 「{HOLD_POINTS[side]}」 공을 옮긴다')

        box = layout.box()
        box.label(text='짝 고르기')
        grid = box.grid_flow(columns=2, even_columns=True)
        for name in p.ORDER:
            if pair_action(name):
                grid.operator('enf_exe.pick', text=p.PAIRS[name]['label'], depress=name == key).key = name

        box = layout.box()
        box.label(text='맞추기')
        box.operator('enf_exe.kill_here', icon='MARKER_HLT')
        box.operator('enf_exe.revert', icon='LOOP_BACK')

        box = layout.box()
        box.label(text='게임')
        settings = scene.enf_exe
        box.prop(settings, 'auto_send')
        box.operator('enf_exe.send', icon='EXPORT')
        action = pair_action(key)
        if action is not None and action.get('enf_withheld'):
            box.label(text='게임에서 뺀 짝: 예전 처형으로 대신하는 중')
        elif not is_sent(key):
            box.label(text='아직 게임에 없음: 저장하면 들어간다')
        elif action is not None and action.get('enf_sent') != fingerprint_soon(key):
            box.label(text='고친 것이 있음: 저장하면 게임에 들어간다')
        else:
            box.label(text='게임이 지금 이 짝을 쓰는 중')
        if scene.get(SCENE_SENT):
            box.label(text=str(scene[SCENE_SENT])[:60])
        box.operator('enf_exe.withhold', icon='X')


class ENFEXE_PT_nudge(bpy.types.Panel):
    bl_label = '버튼으로 손질'
    bl_idname = 'ENFEXE_PT_nudge'
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = TAB
    bl_parent_id = 'ENFEXE_PT_panel'
    bl_options = {'DEFAULT_CLOSED'}

    def draw(self, context):
        layout = self.layout
        settings = context.scene.enf_exe
        layout.label(text='좀비 (부위를 그쪽으로 돌린다)')
        for label, bone in ZOMBIE_PARTS:
            row = layout.row(align=True)
            row.label(text=label)
            for move, text in (('forward', '앞'), ('back', '뒤'), ('up', '위'), ('down', '아래'), ('left', '왼'), ('right', '오른')):
                op = row.operator('enf_exe.zombie_part', text=text)
                op.bone, op.move = bone, move
        layout.label(text='카즈사 (손·발·골반을 그쪽으로 옮긴다)')
        for label, bone in PLAYER_PARTS:
            row = layout.row(align=True)
            row.label(text=label)
            for move, text in (('forward', '앞'), ('back', '뒤'), ('up', '위'), ('down', '아래'), ('left', '왼'), ('right', '오른')):
                op = row.operator('enf_exe.player_part', text=text)
                op.bone, op.move = bone, move
        layout.prop(settings, 'step_deg')
        layout.prop(settings, 'step_cm')
        layout.prop(settings, 'reach')


CLASSES = (ENFExeSettings, ENFEXE_OT_pick, ENFEXE_OT_kill_here, ENFEXE_OT_revert, ENFEXE_OT_send, ENFEXE_OT_withhold,
           ENFEXE_OT_zombie_part, ENFEXE_OT_player_part, ENFEXE_PT_panel, ENFEXE_PT_nudge)


def _drop_handlers():
    for handlers in (bpy.app.handlers.load_post, bpy.app.handlers.save_pre):
        for handler in list(handlers):
            if getattr(handler, '__name__', '') in ('_load_post', '_save_pre') and \
                    getattr(handler, '__module__', '') in {__name__, 'execution_tools'}:
                handlers.remove(handler)


def register():
    for cls in CLASSES:
        existing = getattr(bpy.types, cls.__name__, None)
        if existing is not None:
            bpy.utils.unregister_class(existing)
        bpy.utils.register_class(cls)
    bpy.types.Scene.enf_exe = bpy.props.PointerProperty(type=ENFExeSettings)
    _drop_handlers()
    bpy.app.handlers.load_post.append(_load_post)
    bpy.app.handlers.save_pre.append(_save_pre)
    if not bpy.app.background and not bpy.app.timers.is_registered(_arrange):
        # After the True FPS tools have opened their workspace (0.2 s), so this file's tab and views are the last word.
        bpy.app.timers.register(_arrange, first_interval=.8)


def unregister():
    _drop_handlers()
    if bpy.app.timers.is_registered(_arrange):
        bpy.app.timers.unregister(_arrange)
    del bpy.types.Scene.enf_exe
    for cls in reversed(CLASSES):
        existing = getattr(bpy.types, cls.__name__, None)
        if existing is not None:
            bpy.utils.unregister_class(existing)


if __name__ == '__main__':
    register()
