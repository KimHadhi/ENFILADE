"""ENFILADE one-click animation workspaces, Blender 4.5 LTS.

The user, 2026-11-13: "야 근데 올인원 원클릭으로 세팅되는 거 없음? 걍 열기만 하면 블렌더 내에서 알아서 애니메이션 불러 올 수 있고
도프 시트는 이미 짜여져 있고 그런 거 ㅇㅇ. 내가 해야 하는 잡일을 최대한 줄여. 나는 딱 오직 인간만이 할 수 있는 것만 해내면 되게끔 ㅇㅇ."

The paired executions workspace did it first and keeps its own copy (Tools/Blender/Executions/execution_tools.py); the
workspaces listed in README.md beside it share this module:
- Opening: each workspace's tools register an arrange step, run on a timer after the True FPS tools' own start-up and
  after every file load: the workspace's screen, the item last worked on, the rig in Pose mode with the part that item
  moves selected, every Dope Sheet in Dope Sheet mode with every animated object's keys (not only the selected ones'),
  the summary row and the markers, the workspace's sidebar tab.
- Items (a move, a clip, a cycle, a pose): item_action() readies each one's action to key straight into. A draft keyed
  at key poses becomes an editable copy named <name>, the draft kept beside it as <name>_draft (시안으로 되돌리기). A draft
  keyed (nearly) every frame (retargeted or ported motion) stays locked on the NLA track '1 시안 (잠금)' under <name>, its
  touch-up layer (Combine), where one key bends the clip smoothly instead of popping one frame. Files from before (every
  draft locked under an empty touch-up) are read so; a touch-up the user had keyed over key poses is baked into the
  editable copy, so it plays as it did. Channels are grouped per bone and closed: the Dope Sheet shows a row per bone.
- Saving sends: a save_pre handler has the workspace send every item whose fingerprint (its keys, handles, eases,
  extrapolation, modifiers, and the item's own numbers such as its marks and end frame) differs from the one it was last
  sent with, or whose game file is missing. An item nobody has changed counts as sent as it is: its draft is what the
  game has (or, for a cycle never sent, the original clip the game plays instead: withheld).
Headless saves (blender --background) never send, so the build scripts can save their workspaces; the checks set
SEND_IN_BACKGROUND. Nothing here changes Blender's preferences.
"""
import hashlib
import json
import os
import re
import time
import uuid

import bpy
from bpy.app.handlers import persistent

BASE_TRACK = '1 시안 (잠금)'
ANIMATE = 'ENFILADE - Animate'
DRAFT = '_draft'
# Item action properties: what kind of layer it is, the fingerprint it was last sent with, kept out of the game.
LAYER, SENT, WITHHELD = 'enf_layer', 'enf_sent', 'enf_withheld'
# The fingerprint of the draft an editable copy was made from: a draft made again (a build's --redraft) is followed by a
# copy nobody touched.
FROM = 'enf_from'
# The scene's last send, for the panels.
NOTE = 'enf_sent_note'
# A draft keyed on at least this share of its frames is motion keyed every frame, not key poses.
DENSE = .8
SEND_IN_BACKGROUND = False


def _set(owner, name, value):
    """Sets a property only when it changes (an unchanged file stays unchanged)."""
    if getattr(owner, name) != value:
        setattr(owner, name, value)


# ---- Actions ----------------------------------------------------------------------------------------------------------

def curves(action):
    """Every F-curve of an action, legacy or layered (4.4+)."""
    if action is None:
        return []
    out = []
    for layer in getattr(action, 'layers', []):
        for strip in layer.strips:
            for bag in getattr(strip, 'channelbags', []):
                out.extend(bag.fcurves)
    if not out and getattr(action, 'is_action_legacy', False) and hasattr(action, 'fcurves'):
        out = list(action.fcurves)
    return out


def channelbag(action):
    """Where an action's curves and channel groups live: its first channelbag (layered), or the action itself (legacy)."""
    for layer in getattr(action, 'layers', []):
        for strip in layer.strips:
            for bag in getattr(strip, 'channelbags', []):
                return bag
    return action if hasattr(action, 'groups') and hasattr(action, 'fcurves') else None


def is_dense(draft, frames):
    """Keyed (nearly) every frame: retargeted or ported motion rather than key poses."""
    most = max((len(c.keyframe_points) for c in curves(draft)), default=0)
    return frames > 2 and most >= DENSE * frames


def bone_of(path):
    found = re.match(r'pose\.bones\["(.+?)"\]', path)
    return found.group(1) if found else None


def group_channels(action):
    """Every bone's channels in one group named after the bone, every group closed, so the Dope Sheet shows a row per
    bone (other channels keep their groups)."""
    bag = channelbag(action)
    if bag is None:
        return
    groups = {g.name: g for g in bag.groups}
    for curve in curves(action):
        bone = bone_of(curve.data_path)
        if bone is None:
            continue
        group = groups.get(bone)
        if group is None:
            group = groups[bone] = bag.groups.new(bone)
        # Putting a curve in the group it is in prints an error: checked first.
        if curve.group != group:
            curve.group = group
    for group in bag.groups:
        _set(group, 'show_expanded', False)
        _set(group, 'show_expanded_graph', True)


def _copy(action, name, layer):
    """A copy of action named name (kept by a fake user), marked as that kind of layer, never counted as sent."""
    copy = action.copy()
    copy.name = name
    copy.use_fake_user = True
    copy[LAYER] = layer
    for prop in (SENT, WITHHELD, FROM):
        if prop in copy:
            del copy[prop]
    return copy


def _aside(name):
    """The action holding name, renamed out of the way (to be removed), or None."""
    old = bpy.data.actions.get(name)
    if old is not None:
        old.name = name + '_old'
        old.use_fake_user = False
    return old


def keys_print(action):
    """A fingerprint of an action's keys alone (what a draft copy is compared by)."""
    return fingerprint([action])


def touch_up(name):
    """The item's own action, made as an empty touch-up layer when it has none (the build scripts call this after making
    a draft; item_action then makes a key-pose item's touch-up into the editable copy)."""
    action = bpy.data.actions.get(name)
    if action is None:
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        action[LAYER] = 'touch-up'
    return action


def item_action(context, owner, name, frames):
    """The action the user keys into for one item, and the draft locked under it (None when the action is edited
    directly). Returns (action, draft, fresh): fresh when the action is just as the draft was made and was never sent,
    which is what the game has (the builds send their drafts)."""
    draft = bpy.data.actions.get(name + DRAFT)
    action = bpy.data.actions.get(name)
    if draft is None:
        if action is None:
            raise ValueError(f'{name}: 시안도 동작도 없습니다. 작업 파일을 다시 만들어야 합니다.')
        return action, None, False
    if action is not None and action.get(LAYER) == 'edit':
        made_from = action.get(FROM)
        if made_from and made_from != keys_print(draft) and keys_print(action) == made_from:
            # The draft was made again and nobody had touched the copy: it follows the new draft (sent on the next save).
            _aside(name)
            fresh_copy = _copy(draft, name, 'edit')
            fresh_copy[FROM] = keys_print(draft)
            _replace(action, fresh_copy)
            group_channels(fresh_copy)
            return fresh_copy, None, False
        return action, None, False
    if is_dense(draft, frames):
        action = action or touch_up(name)
        if action.get(LAYER) != 'touch-up':
            action[LAYER] = 'touch-up'
        return action, draft, not curves(action) and SENT not in action
    touched = action is not None and bool(curves(action))
    if touched:
        edit = _bake(context, owner, draft, action, frames, name)
    else:
        _aside(name)
        edit = _copy(draft, name, 'edit')
        if action is not None:
            _replace(action, edit)
    edit[FROM] = keys_print(draft)
    group_channels(edit)
    return edit, None, not touched


def _replace(old, new):
    """Whoever played old plays new; old goes."""
    for obj in bpy.data.objects:
        data = obj.animation_data
        if data is not None and data.action == old:
            data.action = new
            if hasattr(data, 'action_slot') and data.action_slot is None and len(getattr(new, 'slots', [])):
                data.action_slot = new.slots[0]
    bpy.data.actions.remove(old)


def _bake(context, owner, draft, touch, frames, name):
    """A touch-up the user keyed over a locked key-pose draft (a file from before), made into one editable action that
    plays the same: the draft's curves, and every property the touch-up moved keyed on every frame as the two played."""
    scene = context.scene
    play(owner, touch, draft)
    keep = scene.frame_current
    paths = sorted({c.data_path for c in curves(touch)})
    samples = {path: [] for path in paths}
    for frame in range(1, frames + 1):
        scene.frame_set(frame)
        for path in paths:
            try:
                value = owner.path_resolve(path)
            except ValueError:
                continue
            values = list(value) if hasattr(value, '__len__') else [float(value)]
            samples[path].append((frame, values))
    scene.frame_set(keep)
    _aside(name)
    edit = _copy(draft, name, 'edit')
    play(owner, edit)
    bag = channelbag(edit)
    for path, rows in samples.items():
        if not rows or bag is None:
            continue
        for curve in [c for c in curves(edit) if c.data_path == path]:
            bag.fcurves.remove(curve)
        if path.endswith('rotation_quaternion'):
            # One side of the sphere, as the draft keys quaternions.
            for i in range(1, len(rows)):
                if sum(a * b for a, b in zip(rows[i - 1][1], rows[i][1])) < 0:
                    rows[i] = (rows[i][0], [-v for v in rows[i][1]])
        for index in range(len(rows[0][1])):
            curve = bag.fcurves.new(path, index=index)
            points = curve.keyframe_points
            points.add(len(rows))
            points.foreach_set('co', [x for frame, values in rows for x in (float(frame), values[index])])
            for point in points:
                point.interpolation = 'BEZIER'
                point.handle_left_type = point.handle_right_type = 'AUTO_CLAMPED'
            curve.update()
    bpy.data.actions.remove(touch)
    print(f'ENFILADE: {name} 손질 층을 시안과 합쳐 고칠 수 있는 동작 하나로 만들었습니다(바뀐 채널은 프레임마다 키)')
    return edit


def play(owner, action, draft=None):
    """owner plays action: alone (Replace), or as the touch-up layer (Combine) over draft locked on the NLA track."""
    if owner is None:
        return
    data = owner.animation_data or owner.animation_data_create()
    track = data.nla_tracks.get(BASE_TRACK)
    if draft is None:
        if track is not None:
            data.nla_tracks.remove(track)
    else:
        if track is None:
            track = data.nla_tracks.new()
            track.name = BASE_TRACK
        strip = track.strips[0] if len(track.strips) == 1 else None
        if strip is None or strip.action != draft:
            track.lock = False
            while len(track.strips):
                track.strips.remove(track.strips[0])
            strip = track.strips.new(draft.name, int(draft.frame_range[0]), draft)
            if hasattr(strip, 'action_slot') and strip.action_slot is None and len(getattr(draft, 'slots', [])):
                strip.action_slot = draft.slots[0]
            strip.blend_type = 'REPLACE'
            strip.extrapolation = 'HOLD'
        _set(track, 'mute', False)
        _set(track, 'lock', True)
    if data.action != action:
        data.action = action
    if action is not None and hasattr(data, 'action_slot') and data.action_slot is None and len(getattr(action, 'slots', [])):
        data.action_slot = action.slots[0]
    _set(data, 'action_blend_type', 'COMBINE' if draft is not None else 'REPLACE')
    _set(data, 'action_extrapolation', 'HOLD')
    _set(data, 'action_influence', 1.0)


def revert(name):
    """An editable item back to its first draft: a fresh copy of <name>_draft in its place. Returns it."""
    draft = bpy.data.actions.get(name + DRAFT)
    if draft is None:
        raise ValueError('이 동작의 시안이 없습니다.')
    old = _aside(name)
    fresh = _copy(draft, name, 'edit')
    fresh[FROM] = keys_print(draft)
    if old is not None:
        _replace(old, fresh)
    group_channels(fresh)
    return fresh


def clear_keys(action, frame):
    """Removes an action's keys at one frame; returns how many went."""
    removed = 0
    for curve in curves(action):
        for point in [p for p in curve.keyframe_points if abs(p.co.x - frame) < .5]:
            curve.keyframe_points.remove(point, fast=True)
            removed += 1
        curve.update()
    return removed


# ---- Fingerprints and sending ------------------------------------------------------------------------------------------

def plain(value):
    """A value as JSON can hold it, floats rounded (+ 0.0: a rounded -0.0 is 0.0, so a value on zero never reads as
    changed)."""
    if value is None or isinstance(value, (str, bool)):
        return value
    if isinstance(value, (int, float)):
        return round(float(value), 4) + 0.0
    if isinstance(value, dict):
        return {str(k): plain(v) for k, v in sorted(value.items())}
    if isinstance(value, (set, frozenset)):
        return sorted(str(v) for v in value)
    try:
        return [plain(v) for v in value]
    except TypeError:
        return str(value)


def fingerprint(actions, extra=()):
    """What an item is now: its actions' keys, handles, eases, extrapolation and modifiers, and its own numbers."""
    digest = hashlib.sha1()
    for action in actions:
        if action is None:
            digest.update(b'-')
            continue
        for curve in sorted(curves(action), key=lambda c: (c.data_path, c.array_index)):
            points = curve.keyframe_points
            count = len(points)
            digest.update(f'{curve.data_path}[{curve.array_index}]{count}{curve.mute}{curve.extrapolation}'.encode())
            values = [0.0] * (2 * count)
            for attr in ('co', 'handle_left', 'handle_right'):
                points.foreach_get(attr, values)
                digest.update(','.join(f'{v:.5f}' for v in values).encode())
            kinds = [0] * count
            for attr in ('interpolation', 'easing'):
                points.foreach_get(attr, kinds)
                digest.update(str(kinds).encode())
            for modifier in curve.modifiers:
                digest.update(_settings(modifier).encode())
    for value in extra:
        digest.update(json.dumps(plain(value), ensure_ascii=False).encode())
    return digest.hexdigest()


def _settings(modifier):
    """Every plain setting of an F-curve modifier (a Noise's strength and scale, a Cycles' modes, a range, ...)."""
    out = []
    for prop in modifier.bl_rna.properties:
        if prop.identifier == 'rna_type' or prop.type in ('POINTER', 'COLLECTION'):
            continue
        out.append((prop.identifier, plain(getattr(modifier, prop.identifier, None))))
    return json.dumps(out, ensure_ascii=False)


def needs_sending(action, mark, in_game):
    """True unless the item was last sent (or deliberately kept out of the game) as it is now and the game still has it."""
    return action is not None and not (action.get(SENT) == mark and (in_game or action.get(WITHHELD)))


def mark_sent(action, mark):
    if action is not None:
        action[SENT] = mark
        if WITHHELD in action:
            del action[WITHHELD]


def mark_withheld(action, mark):
    """The item kept out of the game as it is now (the game plays what it stands in for) until it changes."""
    if action is not None:
        action[SENT] = mark
        action[WITHHELD] = True


def write_meta(path, body=''):
    """A Unity .meta beside a new file, written before it (a file that lands first gets a GUID of Unity's own, which a
    .meta written later would contradict). An existing .meta is never touched."""
    meta = path.with_name(path.name + '.meta')
    if not meta.exists():
        # Unity's own line ends.
        meta.write_bytes((f'fileFormatVersion: 2\nguid: {uuid.uuid4().hex}\n' + body).encode('utf-8'))


TEXT_META = 'TextScriptImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n'


def write_text(path, text):
    """A text file written in place and retried: a file Unity holds open can't be truncated by a plain write on this
    PC (Executions.csv, 2026-11-13). Its line ends stay what the file has (a new one gets this PC's, as a plain
    Path.write_text gave them before), so an unchanged item writes the same bytes. Raises after five seconds."""
    newline = os.linesep
    if path.exists():
        newline = '\r\n' if b'\r\n' in path.read_bytes()[:4096] else '\n'
    data = text.replace('\r\n', '\n').replace('\n', newline).encode('utf-8')
    path.parent.mkdir(parents=True, exist_ok=True)
    for _attempt in range(20):
        try:
            if path.exists():
                with open(path, 'r+b') as handle:
                    handle.seek(0)
                    handle.write(data)
                    handle.truncate()
            else:
                path.write_bytes(data)
            return path
        except OSError:
            time.sleep(.25)
    raise RuntimeError(f'{path.name}을 쓰지 못했습니다(Unity가 붙잡고 있음). 다시 저장하면 다시 보냅니다.')


# ---- Workspaces: opening ready, saving sends -----------------------------------------------------------------------------

_WORKSPACES = {}


def register_workspace(name, matches, arrange, send):
    """A workspace's tools hook in. matches(scene): this scene is that workspace's file. arrange(context): readies the
    screen (False: try again next tick). send(context): sends what changed, returning (sent labels, failures), or None
    when its switch is off."""
    _WORKSPACES[name] = dict(matches=matches, arrange=arrange, send=send)
    for handlers, fn in ((bpy.app.handlers.save_pre, _save_pre), (bpy.app.handlers.load_post, _load_post)):
        for handler in list(handlers):
            if getattr(handler, '__name__', '') == fn.__name__ and getattr(handler, '__module__', '') == __name__:
                handlers.remove(handler)
        handlers.append(fn)
    schedule()


def unregister_workspace(name):
    _WORKSPACES.pop(name, None)
    if _WORKSPACES:
        return
    for handlers in (bpy.app.handlers.save_pre, bpy.app.handlers.load_post):
        for handler in list(handlers):
            if getattr(handler, '__module__', '') == __name__:
                handlers.remove(handler)
    if bpy.app.timers.is_registered(_arrange):
        bpy.app.timers.unregister(_arrange)


def _matching(scene):
    out = []
    for name, ws in list(_WORKSPACES.items()):
        try:
            if scene is not None and ws['matches'](scene):
                out.append((name, ws))
        except (ReferenceError, AttributeError, KeyError):
            pass
    return out


def send_report(scene, sent, failed):
    """The status line after a send (also kept on the scene for the panels)."""
    if not sent and not failed:
        return
    stamp = time.strftime('%H:%M:%S')
    text = (f'{stamp} 게임에 보냄: ' + ', '.join(sent)) if sent else f'{stamp} 보내지 못함'
    if failed:
        text += ' · 못 보냄: ' + '; '.join(failed)
    scene[NOTE] = text
    tell(text + (' — Unity 창을 누르면 가져갑니다' if sent else ''))


@persistent
def _save_pre(*_unused):
    """Saving sends what changed to the game (each workspace's switch turns it off)."""
    if bpy.app.background and not SEND_IN_BACKGROUND:
        return
    context = bpy.context
    scene = context.scene
    for name, ws in _matching(scene):
        try:
            result = ws['send'](context)
        except Exception as error:  # Never stop a save for it: the next save tries again.
            print(f'ENFILADE {name}: 저장하며 게임에 보내지 못함', error)
            continue
        if result:
            send_report(scene, *result)


@persistent
def _load_post(*_unused):
    schedule()


_TRIES = [0]


def schedule(first=.8):
    """The arrange step after the window is up: after the True FPS tools' own start-up (0.2 s), so a workspace's tab and
    views are the last word."""
    if bpy.app.background or bpy.app.timers.is_registered(_arrange):
        return
    _TRIES[0] = 0
    bpy.app.timers.register(_arrange, first_interval=first)


def _arrange():
    _TRIES[0] += 1
    again = False
    for name, ws in _matching(bpy.context.scene):
        try:
            if ws['arrange'](bpy.context) is False:
                again = True
        except Exception as error:  # A start-up nicety: never stop the tools for it.
            print(f'ENFILADE {name}: 작업 화면을 맞추지 못함', error)
    if again and _TRIES[0] < 6:
        return .3
    _TRIES[0] = 0
    return None


def open_workspace(context, name=ANIMATE):
    """Every window on that workspace. False when one had to switch: its screen is the window's from the next tick."""
    workspace = bpy.data.workspaces.get(name)
    if workspace is None:
        return True
    switched = False
    for window in context.window_manager.windows:
        if window.workspace != workspace:
            window.workspace = workspace
            switched = True
    return not switched


def views(window):
    return [a for a in window.screen.areas if a.type == 'VIEW_3D']


def main_view(window):
    """The largest 3D view not looking through a camera (the first-person preview does)."""
    free = [a for a in views(window) if a.spaces.active.region_3d.view_perspective != 'CAMERA'] or views(window)
    return max(free, key=lambda a: a.width * a.height) if free else None


def show_tab(area, tab):
    """The view's sidebar open on that tab."""
    if area is None:
        return
    _set(area.spaces.active, 'show_region_ui', True)
    region = next((r for r in area.regions if r.type == 'UI'), None)
    if region is not None:
        try:
            region.active_panel_category = tab
        except (TypeError, ValueError, RuntimeError, AttributeError):
            pass


def dope_sheet(window):
    """Every Dope Sheet but the timeline in Dope Sheet mode: every animated object's keys, the summary row, the markers."""
    for area in window.screen.areas:
        if area.type != 'DOPESHEET_EDITOR' or area.ui_type == 'TIMELINE':
            continue
        space = area.spaces.active
        _set(space, 'ui_mode', 'DOPESHEET')
        _set(space.dopesheet, 'show_only_selected', False)
        _set(space.dopesheet, 'show_summary', True)
        _set(space, 'show_markers', True)


def select(context, window, area, objects, active, pose=True, bones=()):
    """objects selected with active the active one, in Pose mode (the given bones of active selected, the first active)
    or Object mode."""
    region = next((r for r in area.regions if r.type == 'WINDOW'), None) if area is not None else None
    try:
        with context.temp_override(window=window, area=area, region=region):
            if context.object is not None and context.object.mode != 'OBJECT':
                bpy.ops.object.mode_set(mode='OBJECT')
            for obj in context.view_layer.objects:
                if obj.select_get():
                    obj.select_set(False)
            for obj in objects:
                if obj is not None and obj.name in context.view_layer.objects:
                    obj.select_set(True)
            if active is None or active.name not in context.view_layer.objects:
                return
            context.view_layer.objects.active = active
            if pose and active.type == 'ARMATURE':
                bpy.ops.object.mode_set(mode='POSE')
                if bones:
                    for bone in active.data.bones:
                        if bone.select:
                            bone.select = bone.select_head = bone.select_tail = False
                    for name in bones:
                        bone = active.data.bones.get(name)
                        if bone is not None:
                            bone.select = True
                    first = active.data.bones.get(bones[0])
                    if first is not None:
                        active.data.bones.active = first
    except RuntimeError as error:
        print('ENFILADE: 모드를 바꾸지 못함', error)


def frame_view(area, middle, toward, distance, ortho=False):
    """A 3D view on middle from the side toward points at (a unit vector from middle to the eye)."""
    view = area.spaces.active.region_3d
    view.view_perspective = 'ORTHO' if ortho else 'PERSP'
    view.view_location = middle
    view.view_rotation = (-toward).to_track_quat('-Z', 'Y')
    view.view_distance = distance


def auto_key(scene, on=True):
    _set(scene.tool_settings, 'use_keyframe_insert_auto', on)


def tell(text):
    """A line in the status bar for ten seconds (and the console)."""
    print('ENFILADE:', text)
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
