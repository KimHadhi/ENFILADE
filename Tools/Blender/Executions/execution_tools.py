"""ENFILADE paired executions workspace, Blender 4.5 LTS.

START_EXECUTION_ANIMATION.cmd opens ArtSource/Executions/ENFILADE_Execution_Animation.blend with truefps_tools.py and
then this. One scene holds Kazusa's True FPS rig and the Synty zombie rig at the game's size; every pair (처형 짝) is one
draft action per rig, locked on NLA track '1 시안 (잠금)', under an empty touch-up action (Combine) that is what the user
keys into, as in the shove and run workspaces:
- 짝 고르기 shows a pair: both rigs' layers, its frames, Kazusa's place beside the zombie, the 처치 (kill) marker.
- Anything is keyed by hand on the touch-up layers (auto-key is on): the zombie in FK, Kazusa's hands and feet on her IK
  controls, her hips, spine and head in FK (the True FPS tab's IK/FK tools work too). Moving her rig object moves where
  she stands for this pair; moving the 처치 marker moves the kill.
- 손을 좀비에 다시 맞추기 makes Kazusa's draft again from the zombie as it plays now (its draft and touch-ups), so her hands
  stay on it after the zombie was changed; her own touch-ups stay on top.
- 게임에 보내기 bakes both clips (enf_exe_<pair>_p for the player, enf_exe_<pair>_z for the zombie) to
  Assets/_Project/Art/Animations/Executions and writes the pair's row of Assets/_Project/Resources/Tables/Executions.csv:
  its length, the kill moment, where the player stands and faces, where the zombie lies at the start and the hands'
  holds. Unity takes the clips with the right avatars (ModelImportRules) and the game plays them from the next Play.
Nothing here changes Blender's preferences.
"""
import csv
import importlib.util
import io
import json
import math
import sys
import time
import uuid
from pathlib import Path

import bpy
from bpy.props import FloatProperty
from mathutils import Matrix, Quaternion, Vector

bl_info = {'name': 'ENFILADE Paired Executions', 'author': 'ENFILADE', 'version': (1, 0, 0), 'blender': (4, 5, 0),
           'location': '3D View > Sidebar > 처형', 'category': 'Animation'}

HERE_NAME = 'Tools/Blender/Executions'
TRUEFPS = Path('Tools/Blender/TrueFPS/truefps_tools.py')
ZOMBIES = Path('Tools/Blender/Zombies/zombie_tools.py')
OUT_DIR = Path('Assets/_Project/Art/Animations/Executions')
TABLE = Path('Assets/_Project/Resources/Tables/Executions.csv')
BASE_TRACK = '1 시안 (잠금)'
PREFIX = 'enf_exe_'
KILL_MARKER = '처치'
CSV_HEADER = 'clip,loop,inPlace,inPlaceY,inPlaceYaw,yawOffset,cycleOffset,from,to\n'
SCENE_PAIR = 'enf_exe_pair'
SCENE_LIVE = 'enf_exe_live'


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


def label_of(key):
    pair = pairs().PAIRS.get(key)
    return pair['label'] if pair else key


# ---- Layers: a locked draft strip under the touch-up action ---------------------------------------------------------

def touch_up(name):
    action = bpy.data.actions.get(name)
    if action is None:
        action = bpy.data.actions.new(name)
    action.use_fake_user = True
    action['enf_layer'] = 'touch-up'
    return action


def set_layers(obj, draft, edit, live=True):
    """obj plays draft as its locked base and keys into edit on top (Combine); live False mutes the touch-ups."""
    data = obj.animation_data or obj.animation_data_create()
    track = data.nla_tracks.get(BASE_TRACK)
    if track is None:
        track = data.nla_tracks.new()
        track.name = BASE_TRACK
    track.lock = False
    while len(track.strips):
        track.strips.remove(track.strips[0])
    if draft is not None:
        strip = track.strips.new(draft.name, 1, draft)
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
    data.action_influence = 1.0 if live else 0.0


def current_pair(scene):
    key = scene.get(SCENE_PAIR, 'back')
    return key if bpy.data.actions.get(PREFIX + key + '_p_draft') else 'back'


def pair_frames(key):
    draft = bpy.data.actions.get(PREFIX + key + '_p_draft')
    return int(draft.get('enf_frames', 30)) if draft else 30


def pair_kill(key):
    draft = bpy.data.actions.get(PREFIX + key + '_p_draft')
    return int(draft.get('enf_kill', 15)) if draft else 15


def place_player(rig, key):
    """Kazusa's rig object where this pair has her: her root beside the zombie's, facing her way, at her game size."""
    draft = bpy.data.actions.get(PREFIX + key + '_p_draft')
    if draft is None:
        return
    root = draft.get('enf_root', (0.0, 0.0, 0.0))
    yaw = float(draft.get('enf_yaw', 0.0))
    scale = drafts().GAME_SCALE
    rig.matrix_world = (Matrix.Translation(Vector(root)) @ Matrix.Rotation(math.radians(yaw), 4, 'Z')
                        @ Matrix.Scale(scale, 4))


def remember(scene, key):
    """Keeps what the user moved for this pair: Kazusa's place and turn, the kill marker, the last frame."""
    draft = bpy.data.actions.get(PREFIX + key + '_p_draft')
    rig = player_rig(scene)
    if draft is None or rig is None:
        return
    location, rotation, _ = rig.matrix_world.decompose()
    draft['enf_root'] = (location.x, location.y, 0.0)
    forward = rotation @ Vector((0.0, -1.0, 0.0))
    draft['enf_yaw'] = math.degrees(math.atan2(forward.x, -forward.y))
    marker = scene.timeline_markers.get(KILL_MARKER)
    if marker is not None:
        draft['enf_kill'] = int(marker.frame)
    draft['enf_frames'] = int(scene.frame_end)


def select_pair(context, key, keep=True):
    """Shows one pair: both rigs' layers, its frames, Kazusa beside the zombie and the kill marker."""
    scene = context.scene
    player, zombie = player_rig(scene), zombie_rig(scene)
    p_name, z_name = clip_names(key)
    p_draft, z_draft = bpy.data.actions.get(p_name + '_draft'), bpy.data.actions.get(z_name + '_draft')
    if player is None or zombie is None or p_draft is None or z_draft is None:
        raise ValueError('이 짝의 시안이 없습니다. build_execution_workspace.py로 작업 파일을 다시 만들어야 합니다.')
    if keep and scene.get(SCENE_PAIR) and scene.get(SCENE_PAIR) != key:
        remember(scene, scene[SCENE_PAIR])
    live = scene.get(SCENE_LIVE, True)
    set_layers(player, p_draft, touch_up(p_name), live)
    set_layers(zombie, z_draft, touch_up(z_name), live)
    place_player(player, key)
    scene.frame_start, scene.frame_end = 1, pair_frames(key)
    marker = scene.timeline_markers.get(KILL_MARKER) or scene.timeline_markers.new(KILL_MARKER, frame=1)
    marker.frame = pair_kill(key)
    scene[SCENE_PAIR] = key
    scene.frame_set(1)
    scene.tool_settings.use_keyframe_insert_auto = True


# ---- Drafts ---------------------------------------------------------------------------------------------------------

def _new_action(name):
    old = bpy.data.actions.get(name)
    if old is not None:
        old.name = name + '_old'
        old.use_fake_user = False
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    action['enf_layer'] = 'draft'
    return action


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
    scene = context.scene
    zombie = zombie_rig(scene)
    pair = pairs().PAIRS[key]
    root = project_root()
    z_name = clip_names(key)[1]
    z = drafts().ZombieDraft(context, zombies(), zombie, root / pair['clip'], pair['at'])
    pairs().CHOREOGRAPHY[key][0](z, pair)
    action = _new_action(z_name + '_draft')
    action['enf_source'] = pair['clip']
    z.write(action, pair['frames'])
    return action


def make_player(context, key, follow_layers=False):
    """Kazusa's draft for a pair, its contacts worked out from the zombie: its draft alone, or (follow_layers) as it
    plays now with the touch-ups on top."""
    scene = context.scene
    player, zombie = player_rig(scene), zombie_rig(scene)
    pair = pairs().PAIRS[key]
    d = drafts()
    p_name, z_name = clip_names(key)
    old = bpy.data.actions.get(p_name + '_draft')
    count = int(old.get('enf_frames', pair['frames'])) if old is not None else pair['frames']
    keep = {k: old[k] for k in ('enf_root', 'enf_yaw', 'enf_kill', 'enf_frames') if old is not None and k in old}
    if 'enf_root' not in keep:
        right, forward = pair['root']
        keep['enf_root'] = tuple(d.zv(right, forward, 0.0))
        keep['enf_yaw'] = pair['yaw']
        keep['enf_kill'] = pair['kill']
        keep['enf_frames'] = pair['frames']
    if follow_layers:
        select_pair(context, key)
        frames = d.zombie_layers_frames(context, zombie, count)
    else:
        frames = d.zombie_bone_frames(context, zombie, bpy.data.actions[z_name + '_draft'], count)
    # Made with the rig placed where this pair has her (her rig object's place is part of every contact).
    data = player.animation_data or player.animation_data_create()
    tracks = [(t, t.mute) for t in data.nla_tracks]
    for t, _ in tracks:
        t.mute = True
    keep_action = data.action
    action = _new_action(p_name + '_draft')
    for k, v in keep.items():
        action[k] = v
    place_player(player, key)
    p = d.PlayerDraft(context, player, zombie, _seat(project_root()))
    pairs().CHOREOGRAPHY[key][1](p, frames, pair)
    p.write(action, count, frames)
    # The hands' holds, for the game's contact (Executions.csv l*/r*: it keeps them on any body's bone).
    action['enf_holds'] = json.dumps(p.holds(count))
    data.action = keep_action
    for t, mute in tracks:
        t.mute = mute
    old_action = bpy.data.actions.get(p_name + '_draft_old')
    if old_action is not None and old_action.users == 0:
        bpy.data.actions.remove(old_action)
    return action


def make_pair(context, key):
    make_zombie(context, key)
    old = bpy.data.actions.get(clip_names(key)[1] + '_draft_old')
    if old is not None and old.users == 0:
        bpy.data.actions.remove(old)
    make_player(context, key)
    touch_up(clip_names(key)[0])
    touch_up(clip_names(key)[1])


# ---- The table ------------------------------------------------------------------------------------------------------

def read_table(root):
    """Executions.csv as {id: row}, the 설명 row left out."""
    path = root / TABLE
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
    path = root / TABLE
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
    raise RuntimeError('Executions.csv를 쓰지 못했습니다(Unity가 붙잡고 있음). 잠시 뒤 다시 누르세요.')


def _number(value, digits=3):
    text = f'{value:.{digits}f}'.rstrip('0').rstrip('.')
    return '0' if text in ('-0', '') else text


# ---- Export ---------------------------------------------------------------------------------------------------------

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


def measure(context, key):
    """What the game needs besides the clips: where the zombie lies at the start, and each hand's hold of it (a bone, a
    window, and where the wrist sits from that bone in the zombie's frame at the grab), from the pair as it plays now."""
    scene = context.scene
    player, zombie = player_rig(scene), zombie_rig(scene)
    scene.frame_set(1)
    context.view_layer.update()
    zw = zombie.matrix_world
    hips = zw @ zombie.pose.bones['Hips'].head
    head = zw @ zombie.pose.bones['Head'].head
    hr, hf, _ = _game(hips)
    lie = head - hips
    head_yaw = math.degrees(math.atan2(-lie.x, -lie.y))
    out = {'zHipsRight': _number(hr), 'zHipsForward': _number(hf), 'zHeadYaw': _number(head_yaw, 1)}
    draft = bpy.data.actions.get(clip_names(key)[0] + '_draft')
    holds = json.loads(draft.get('enf_holds', '{}')) if draft is not None else {}
    for side, prefix in (('L', 'l'), ('R', 'r')):
        hold = holds.get(side)
        if not hold:
            out.update({prefix + 'Bone': '', prefix + 'From': '', prefix + 'To': '', prefix + 'Right': '',
                        prefix + 'Forward': '', prefix + 'Up': ''})
            continue
        bone, first, last = hold
        scene.frame_set(first)
        context.view_layer.update()
        wrist = player.matrix_world @ player.pose.bones['Left wrist' if side == 'L' else 'Right wrist'].head
        held = zw @ zombie.pose.bones[bone].head
        r, f, u = _game(wrist - held)
        out.update({prefix + 'Bone': bone, prefix + 'From': _number((first - 1) / scene.render.fps),
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
    if scene.get(SCENE_PAIR) == key:
        remember(scene, key)
    select_pair(context, key, keep=False)
    player, zombie = player_rig(scene), zombie_rig(scene)
    pair = pairs().PAIRS[key]
    p_name, z_name = clip_names(key)
    draft = bpy.data.actions[p_name + '_draft']
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
        try:
            if rig is player:
                # Baked at her own root and size: the game puts her root at the pair's offset (the row below).
                rig.matrix_world = Matrix.Identity(4)
                context.view_layer.update()
            tools.export_active_clip(context, rig, str(path))
        finally:
            rig.matrix_world = saved
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
    draft['enf_kill'] = kill
    draft['enf_frames'] = int(scene.frame_end)
    return written


def revert_pair(key):
    root = project_root()
    removed = []
    if root is None:
        return removed
    for name in clip_names(key):
        for path in ((root / OUT_DIR / (name + '.fbx')), (root / OUT_DIR / (name + '.clips.csv'))):
            if path.exists():
                path.unlink()
                removed.append(path.name)
    return removed


def clear_touch_ups(context, key):
    for name in clip_names(key):
        action = bpy.data.actions.get(name)
        if action is None:
            continue
        for curve in zombies().action_curves(action):
            while len(curve.keyframe_points):
                curve.keyframe_points.remove(curve.keyframe_points[0], fast=True)
            curve.update()
    select_pair(context, key)
    for rig in (player_rig(context.scene), zombie_rig(context.scene)):
        for pb in rig.pose.bones:
            pb.location = (0, 0, 0)
            pb.rotation_quaternion = (1, 0, 0, 0)
        zombies().refresh_animation(context, rig)


# ---- UI -------------------------------------------------------------------------------------------------------------

class ENFExeSettings(bpy.types.PropertyGroup):
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


class ENFEXE_OT_pick(bpy.types.Operator):
    bl_idname = 'enf_exe.pick'
    bl_label = '짝 고르기'
    bl_options = {'REGISTER', 'UNDO'}
    key: bpy.props.StringProperty()

    def execute(self, context):
        if not _ok(self, context):
            return {'CANCELLED'}
        select_pair(context, self.key)
        return {'FINISHED'}


class ENFEXE_OT_toggle(bpy.types.Operator):
    bl_idname = 'enf_exe.toggle'
    bl_label = '손질 층 켜기/끄기'
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        if not _ok(self, context):
            return {'CANCELLED'}
        scene = context.scene
        scene[SCENE_LIVE] = not scene.get(SCENE_LIVE, True)
        select_pair(context, current_pair(scene))
        self.report({'INFO'}, '손질 층 켜짐' if scene[SCENE_LIVE] else '손질 층 꺼짐 (시안만 보는 중)')
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
        self.report({'INFO'}, f'처치: {scene.frame_current} 프레임 ({(scene.frame_current - 1) / 30:.2f}초). 게임에 보내야 바뀐다')
        return {'FINISHED'}


class ENFEXE_OT_refit(bpy.types.Operator):
    bl_idname = 'enf_exe.refit'
    bl_label = '손을 좀비에 다시 맞추기'
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        if not _ok(self, context):
            return {'CANCELLED'}
        key = current_pair(context.scene)
        remember(context.scene, key)
        make_player(context, key, follow_layers=True)
        select_pair(context, key, keep=False)
        self.report({'INFO'}, '카즈사 시안을 지금 좀비에 맞춰 다시 만들었다 (내 손질 층은 그대로)')
        return {'FINISHED'}


class ENFEXE_OT_clear(bpy.types.Operator):
    bl_idname = 'enf_exe.clear'
    bl_label = '이 짝 손질 모두 지우기'
    bl_options = {'REGISTER', 'UNDO'}

    def invoke(self, context, event):
        return context.window_manager.invoke_confirm(self, event)

    def execute(self, context):
        if not _ok(self, context):
            return {'CANCELLED'}
        clear_touch_ups(context, current_pair(context.scene))
        return {'FINISHED'}


class ENFEXE_OT_export(bpy.types.Operator):
    bl_idname = 'enf_exe.export'
    bl_label = '게임에 보내기'

    def execute(self, context):
        if not _ok(self, context):
            return {'CANCELLED'}
        key = current_pair(context.scene)
        try:
            export_pair(context, key)
        except Exception as error:
            self.report({'ERROR'}, str(error))
            return {'CANCELLED'}
        self.report({'INFO'}, f'보냄: {label_of(key)} — Unity 창을 누르면 가져가고, 다음 Play부터 쓰인다')
        return {'FINISHED'}


class ENFEXE_OT_revert(bpy.types.Operator):
    bl_idname = 'enf_exe.revert'
    bl_label = '보낸 클립 지우기'

    def invoke(self, context, event):
        return context.window_manager.invoke_confirm(self, event)

    def execute(self, context):
        removed = revert_pair(current_pair(context.scene))
        self.report({'INFO'}, ('지움: ' + ', '.join(removed) + ' — 게임은 이 짝을 예전 처형으로 대신한다') if removed else '보낸 파일이 없습니다')
        return {'FINISHED'}


ZOMBIE_PARTS = (('머리', 'Head'), ('목', 'Neck'), ('가슴', 'Spine_03'), ('배', 'Spine_02'), ('허리', 'Spine_01'),
                ('왼위팔', 'Shoulder_L'), ('왼아래팔', 'Elbow_L'), ('오른위팔', 'Shoulder_R'), ('오른아래팔', 'Elbow_R'))
PLAYER_PARTS = (('왼손', 'CTRL_hand.L'), ('오른손(칼)', 'CTRL_hand.R'), ('왼발', 'CTRL_foot.L'), ('오른발', 'CTRL_foot.R'),
                ('골반', 'Hips'))


class ENFEXE_OT_zombie_part(bpy.types.Operator):
    """The zombie's part turned toward a body direction at this frame, keyed into the touch-up layer."""
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
    bl_category = '처형'

    def draw(self, context):
        layout = self.layout
        scene = context.scene
        key = current_pair(scene)
        p = pairs()
        box = layout.box()
        box.label(text='1. 짝 고르기')
        grid = box.grid_flow(columns=2, even_columns=True)
        for name in p.ORDER:
            if bpy.data.actions.get(PREFIX + name + '_p_draft'):
                grid.operator('enf_exe.pick', text=p.PAIRS[name]['label'], depress=name == key).key = name
        kill = scene.timeline_markers.get(KILL_MARKER)
        box.label(text=f'지금: {label_of(key)} · {scene.frame_end}프레임 ({(scene.frame_end - 1) / 30:.2f}초)')
        box.label(text=f'처치 {kill.frame if kill else "?"}프레임 ({((kill.frame if kill else 1) - 1) / 30:.2f}초) · '
                       + ('암살' if p.PAIRS.get(key, {}).get('kind') == 'takedown' else '처형'))

        box = layout.box()
        box.label(text='2. 손질 (자동 키 켜짐, 손질 층에 들어간다)')
        settings = scene.enf_exe
        box.label(text='좀비 (부위를 그쪽으로 돌린다)')
        for label, bone in ZOMBIE_PARTS:
            row = box.row(align=True)
            row.label(text=label)
            for move, text in (('forward', '앞'), ('back', '뒤'), ('up', '위'), ('down', '아래'), ('left', '왼'), ('right', '오른')):
                op = row.operator('enf_exe.zombie_part', text=text)
                op.bone, op.move = bone, move
        box.label(text='카즈사 (손·발·골반을 그쪽으로 옮긴다)')
        for label, bone in PLAYER_PARTS:
            row = box.row(align=True)
            row.label(text=label)
            for move, text in (('forward', '앞'), ('back', '뒤'), ('up', '위'), ('down', '아래'), ('left', '왼'), ('right', '오른')):
                op = row.operator('enf_exe.player_part', text=text)
                op.bone, op.move = bone, move
        box.prop(settings, 'step_deg')
        box.prop(settings, 'step_cm')
        box.prop(settings, 'reach')
        box.label(text='카즈사 리그 오브젝트를 옮기면 이 짝에서 서는 자리가 바뀐다')

        box = layout.box()
        box.label(text='3. 맞추기와 확인')
        box.operator('enf_exe.kill_here')
        box.operator('enf_exe.refit')
        row = box.row(align=True)
        row.operator('enf_exe.toggle')
        row.operator('enf_exe.clear')

        box = layout.box()
        box.label(text='4. 게임에 보내기')
        box.operator('enf_exe.export', icon='EXPORT')
        root = project_root()
        if root is not None:
            p_name, z_name = clip_names(key)
            sent = (root / OUT_DIR / (p_name + '.fbx')).exists()
            box.label(text=f'→ {OUT_DIR.as_posix()}/{p_name}.fbx · {z_name}.fbx')
            box.label(text='게임이 지금 이 짝을 쓰는 중' if sent else '아직 안 보냄: 게임은 예전 처형으로 대신한다')
        box.operator('enf_exe.revert')


CLASSES = (ENFExeSettings, ENFEXE_OT_pick, ENFEXE_OT_toggle, ENFEXE_OT_kill_here, ENFEXE_OT_refit, ENFEXE_OT_clear,
           ENFEXE_OT_export, ENFEXE_OT_revert, ENFEXE_OT_zombie_part, ENFEXE_OT_player_part, ENFEXE_PT_panel)


def _show_panel():
    for window in bpy.context.window_manager.windows:
        for area in window.screen.areas:
            if area.type == 'VIEW_3D':
                for space in area.spaces:
                    if space.type == 'VIEW_3D':
                        space.show_region_ui = True
    return None


def register():
    for cls in CLASSES:
        try:
            bpy.utils.register_class(cls)
        except ValueError:
            bpy.utils.unregister_class(cls)
            bpy.utils.register_class(cls)
    bpy.types.Scene.enf_exe = bpy.props.PointerProperty(type=ENFExeSettings)
    if bpy.app.background is False:
        bpy.app.timers.register(_show_panel, first_interval=.5)


def unregister():
    del bpy.types.Scene.enf_exe
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)


if __name__ == '__main__':
    register()
