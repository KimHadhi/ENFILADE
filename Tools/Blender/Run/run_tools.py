"""ENFILADE run cycles workspace, Blender 4.5 LTS.

START_RUN_ANIMATION.cmd opens ArtSource/Run/ENFILADE_Run_Animation.blend with truefps_tools.py and then this.
The game's walk and run cycles (Player.csv Body.forwardMoves, rightMoves, backMoves, leftMoves) are on Kazusa's True FPS
rig, each a locked draft layer (retargeted by build_run_workspace.py the way a humanoid avatar retargets, keyed every
frame) under a touch-up layer (Combine), where one key bends the cycle smoothly instead of popping one frame.
It opens ready to work in, and saving sends the work to the game (the user, 2026-11-13: "내가 해야 하는 잡일을 최대한
줄여. 나는 딱 오직 인간만이 할 수 있는 것만 해내면 되게끔"; the shared part is Tools/Blender/OneClick/one_click.py):
- On open: the last cycle, the rig in Pose mode, the Dope Sheet showing the touch-up keys with the summary row, the
  달리기 tab.
- 걸음 고르기 picks a cycle: its layers, its frames (the loop's last frame is its first again) and its source skeleton
  beside Kazusa (보기용, never exported).
- 자세 다듬기 changes the whole cycle at once on the touch-up layer, every frame, so the loop stays whole: the trunk's lean,
  how far the arms swing, how high the knees come, how much the hips bob, how much the head nods.
- Anything else is keyed by hand on the touch-up layer (auto-key is on); the IK/FK tools of the True FPS tab work too.
- Saving (Ctrl+S) sends every cycle changed since it was last sent: the original skeleton baked to
  Assets/_Project/Art/Animations/Run/enf_<cycle>.fbx (+ .clips.csv). Unity takes it with Kazusa's avatar
  (ModelImportRules), and the body plays enf_<cycle> instead of <cycle> wherever the tables list <cycle>
  (PlayerBody.Locomotion), measured on each character like any cycle, so its stride and pace fit by themselves. A cycle
  nobody changed stays out of the game (the original plays). 원래 클립으로 되돌리기 deletes the file and keeps the cycle
  out until it changes again.
Nothing here changes Blender's preferences.
"""
import importlib.util
import json
import math
import sys
from pathlib import Path

import bpy
from bpy.props import BoolProperty, EnumProperty, FloatProperty
from mathutils import Quaternion, Vector

bl_info = {'name': 'ENFILADE Run Cycles', 'author': 'ENFILADE', 'version': (2, 0, 0), 'blender': (4, 5, 0),
           'location': '3D View > Sidebar > 달리기', 'category': 'Animation'}

TRUEFPS = Path('Tools/Blender/TrueFPS/truefps_tools.py')
ONE_CLICK = Path('Tools/Blender/OneClick/one_click.py')
TAB = '달리기'
RUN_DIR = Path('Assets/_Project/Art/Animations/Run')
CSV_HEADER = 'clip,loop,inPlace,inPlaceY,inPlaceYaw,yawOffset,cycleOffset,from,to\n'
BASE_TRACK = '1 시안 (잠금)'
PREFIX = 'enf_'
SOURCE_COLLECTION = '원본 걸음 (보기용, 내보내지 않음)'
# The game's cycles, in the panel's order: clip name -> label.
CYCLES = (('mx_run', '달리기 · 앞'), ('sprintfwd', '전력질주 · 앞'), ('mx_walk', '걷기 · 앞'),
          ('mx_strafe_run_right', '달리기 · 오른쪽'), ('mx_strafe_run_left', '달리기 · 왼쪽'), ('mx_run_back', '달리기 · 뒤'),
          ('mx_strafe_walk_right', '걷기 · 오른쪽'), ('mx_strafe_walk_left', '걷기 · 왼쪽'), ('mx_walk_back', '걷기 · 뒤'))
# Kazusa's humanoid bones (KazusaBand.fbx.meta) by the parts the posture buttons move.
TRUNK = ('Spine', 'Chest', 'Upper Chest')
ARMS = ('Left arm', 'Left elbow', 'Right arm', 'Right elbow')
LEGS = ('Left leg', 'Left knee', 'Right leg', 'Right knee')
HEAD = ('Neck', 'HeadBone')
HIPS = 'Hips'


def project_root():
    """The ENFILADE checkout: from this file, else from the open .blend (ArtSource/Run/...)."""
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
        raise RuntimeError('ENFILADE 프로젝트 안의 ArtSource/Run에서 파일을 열어 주세요.')
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
    """truefps_tools as a module (for its baking FBX export and loop check)."""
    return _module('truefps_tools', TRUEFPS)


def one_click():
    """The shared one-click workspace module: items, fingerprints, sending on save, the screen on open."""
    return _module('enf_one_click', ONE_CLICK)


def find_rig(scene):
    return next((o for o in scene.objects if o.type == 'ARMATURE' and o.get('truefps_rig')), None)


def is_run_file(scene):
    return find_rig(scene) is not None and any(bpy.data.actions.get(PREFIX + key + '_draft') for key, _ in CYCLES)


def label_of(key):
    return next((label for name, label in CYCLES if name == key), key)


# ---- Layers: a locked draft strip under the touch-up action --------------------------------------------------------

def touch_up(name):
    """The cycle's touch-up layer, made empty if it has none."""
    return one_click().touch_up(name)


def current_cycle(scene):
    key = scene.get('enf_run_cycle', CYCLES[0][0])
    return key if bpy.data.actions.get(PREFIX + key + '_draft') else CYCLES[0][0]


def frames_of(key):
    draft = bpy.data.actions.get(PREFIX + key + '_draft')
    return int(draft.get('enf_frames', draft.frame_range[1])) if draft else 2


def target(key):
    """Where a cycle goes in the game, from the project root."""
    return RUN_DIR / (PREFIX + key + '.fbx')


def in_game(key):
    root = project_root()
    return root is not None and (root / target(key)).exists()


def item(context, key):
    """The cycle's touch-up layer and its locked draft. A cycle made ready untouched counts as what the game has: its
    file if one was sent, else the original clip (withheld)."""
    oc = one_click()
    action, draft, fresh = oc.item_action(context, find_rig(context.scene), PREFIX + key, frames_of(key))
    if fresh:
        (oc.mark_sent if in_game(key) else oc.mark_withheld)(action, fingerprint(key))
    return action, draft


def fingerprint(key):
    """What a cycle is now: its touch-ups and its draft (keys, eases), its length."""
    name = PREFIX + key
    return one_click().fingerprint([bpy.data.actions.get(name), bpy.data.actions.get(name + '_draft')],
                                   extra=[key, frames_of(key)])


def select_cycle(context, key):
    """Shows one cycle: its layers, its frames and its source skeleton beside Kazusa."""
    scene = context.scene
    rig = find_rig(scene)
    draft = bpy.data.actions.get(PREFIX + key + '_draft')
    if rig is None or draft is None:
        raise ValueError('이 걸음의 시안이 없습니다. build_run_workspace.py로 작업 파일을 다시 만들어야 합니다.')
    action, draft = item(context, key)
    one_click().play(rig, action, draft)
    if not scene.get('enf_run_live', True):
        rig.animation_data.action_influence = 0.0
    collection = bpy.data.collections.get(SOURCE_COLLECTION)
    if collection is not None:
        # The collection's own list, hidden per object: walking all_objects while hiding through the view layer crashed
        # Blender 4.5 after the build had imported and removed many objects.
        for obj in list(collection.objects):
            obj.hide_viewport = obj.get('enf_cycle') != key
            obj.hide_render = True
    scene.frame_start, scene.frame_end = 1, frames_of(key)
    if scene.get('enf_run_cycle') != key:
        scene['enf_run_cycle'] = key
    scene.frame_set(1)
    one_click().auto_key(scene)


def prepare(context):
    """Every cycle ready (its touch-up layer, counted as the game has it), the last one shown."""
    for key, _label in CYCLES:
        if bpy.data.actions.get(PREFIX + key + '_draft'):
            item(context, key)
    select_cycle(context, current_cycle(context.scene))


def arrange(context):
    """The workspace as the work needs it: the cycle loaded, the rig in Pose mode, the touch-ups in the Dope Sheet."""
    scene = context.scene
    rig = find_rig(scene)
    if rig is None:
        return True
    # The cycles first: they are ready even where the screen can't be (a window switches its workspace on the next tick).
    prepare(context)
    oc = one_click()
    if not oc.open_workspace(context):
        return False
    for window in context.window_manager.windows:
        main = oc.main_view(window)
        oc.show_tab(main, TAB)
        oc.dope_sheet(window)
        if main is not None:
            oc.select(context, window, main, [rig], rig, pose=True)
    oc.tell(f'달리기 「{label_of(current_cycle(scene))}」 · Space 재생 · 고치고 Ctrl+S 하면 게임에 들어갑니다')
    return True


# ---- Whole-cycle posture: every frame of the touch-up layer, worked out from the pose as it stands now ---------------

def _channel(action, rig, path, index, group):
    """The touch-up action's curve for a bone channel, made if missing (Blender 4.4+ layered actions too)."""
    curve = None
    if getattr(action, 'is_action_legacy', True) and hasattr(action, 'fcurves'):
        curve = action.fcurves.find(path, index=index)
        if curve is None:
            curve = action.fcurves.new(path, index=index, action_group=group)
        return curve
    data = rig.animation_data
    slot = data.action_slot
    for layer in action.layers:
        for strip in layer.strips:
            bag = strip.channelbag(slot, ensure=True)
            curve = bag.fcurves.find(path, index=index)
            if curve is None:
                curve = bag.fcurves.new(path, index=index)
            return curve
    raise RuntimeError('손질 층에 채널을 만들 수 없습니다.')


def _ensure_layered(action, rig):
    """A fresh 4.4+ action has no layer or slot until something is keyed: key the hips once, through Blender's own keying
    (which makes the rig's slot, layer and strip), then set that key to no change (the touch-up layer only adds)."""
    if len(getattr(action, 'layers', [])) or len(getattr(action, 'slots', [])) or len(getattr(action, 'fcurves', [])):
        return
    pb = rig.pose.bones[HIPS]
    pb.keyframe_insert('rotation_quaternion', frame=bpy.context.scene.frame_start, group=HIPS)
    for curve in _draft_curves(action):
        if curve.data_path.endswith('rotation_quaternion'):
            for point in curve.keyframe_points:
                point.co.y = 1.0 if curve.array_index == 0 else 0.0
                point.handle_left.y = point.handle_right.y = point.co.y
            curve.update()


def _evaluated(context, rig, bones, frames):
    """The bones' local rotations (and the hips' location) as they play now, draft and touch-ups, at each frame."""
    scene = context.scene
    keep = scene.frame_current
    out = []
    for frame in frames:
        scene.frame_set(frame)
        context.view_layer.update()
        out.append({n: (rig.pose.bones[n].rotation_quaternion.copy(), rig.pose.bones[n].location.copy()) for n in bones})
    scene.frame_set(keep)
    return out


def _draft_pose(rig, draft, bones, frames):
    """The draft's own local rotations and locations at each frame (the touch-up is what turns it into the wanted pose)."""
    out = []
    for frame in frames:
        pose = {}
        for n in bones:
            q = [1.0, 0.0, 0.0, 0.0]
            loc = [0.0, 0.0, 0.0]
            for curve in _draft_curves(draft):
                if curve.data_path == f'pose.bones["{n}"].rotation_quaternion':
                    q[curve.array_index] = curve.evaluate(frame)
                elif curve.data_path == f'pose.bones["{n}"].location':
                    loc[curve.array_index] = curve.evaluate(frame)
            pose[n] = (Quaternion(q).normalized(), Vector(loc))
        out.append(pose)
    return out


def _draft_curves(action):
    if getattr(action, 'is_action_legacy', True) and hasattr(action, 'fcurves') and len(action.fcurves):
        return list(action.fcurves)
    curves = []
    for layer in getattr(action, 'layers', []):
        for strip in layer.strips:
            for bag in strip.channelbags:
                curves.extend(bag.fcurves)
    return curves


def _scaled(mean, q, amount):
    """q's turn away from mean, scaled by amount (over 1 swings further, which slerp can't do)."""
    if q.dot(mean) < 0:
        q = -q
    delta = mean.inverted() @ q
    axis, angle = delta.to_axis_angle()
    if angle > math.pi:
        angle -= 2 * math.pi
    return mean @ Quaternion(axis, angle * amount)


def _mean(rotations):
    first = rotations[0]
    total = Vector((0.0, 0.0, 0.0, 0.0))
    for q in rotations:
        v = Vector(q) if q.dot(first) >= 0 else -Vector(q)
        total += v
    return Quaternion(total).normalized()


def reshape(context, bones, amount, kind):
    """Every frame of the cycle on the touch-up layer: 'swing' moves each bone's rotation toward (amount under 1) or away
    from (over 1) its mean over the cycle; 'lean' turns it about the body's sideways line by amount degrees; 'bob' scales
    the hips' rise and fall about its mean."""
    scene = context.scene
    rig = find_rig(scene)
    key = current_cycle(scene)
    draft = bpy.data.actions.get(PREFIX + key + '_draft')
    edit = touch_up(PREFIX + key)
    if rig.animation_data.action != edit:
        select_cycle(context, key)
    _ensure_layered(edit, rig)
    frames = list(range(scene.frame_start, scene.frame_end + 1))
    now = _evaluated(context, rig, bones, frames)
    base = _draft_pose(rig, draft, bones, frames)
    right = rig.matrix_world.to_3x3().inverted() @ Vector((-1.0, 0.0, 0.0))
    for n in bones:
        pb = rig.pose.bones[n]
        rest = pb.bone.matrix_local.to_3x3()
        mean = _mean([pose[n][0] for pose in now])
        heights = [pose[n][1].z for pose in now]
        mid = sum(heights) / len(heights)
        curves = [_channel(edit, rig, f'pose.bones["{n}"].rotation_quaternion', i, n) for i in range(4)]
        moves = [_channel(edit, rig, f'pose.bones["{n}"].location', i, n) for i in range(3)] if kind == 'bob' else []
        previous = None
        for i, frame in enumerate(frames):
            q_now, loc_now = now[i][n]
            q_base, loc_base = base[i][n]
            if kind == 'swing':
                target = _scaled(mean, q_now, amount)
            elif kind == 'lean':
                axis = (rest.inverted() @ right).normalized()
                target = q_now @ Quaternion(axis, math.radians(amount))
            else:
                target = q_now
            offset = q_base.inverted() @ target
            if previous is not None and offset.dot(previous) < 0:
                offset.negate()
            previous = offset.copy()
            for c in range(4):
                curves[c].keyframe_points.insert(frame, offset[c], options={'FAST'})
            if kind == 'bob':
                want = loc_now.copy()
                want.z = mid + (loc_now.z - mid) * amount
                for c in range(3):
                    moves[c].keyframe_points.insert(frame, want[c] - loc_base[c], options={'FAST'})
        for curve in curves + moves:
            curve.update()
    rig.update_tag(refresh={'OBJECT'})
    scene.frame_set(scene.frame_current)


def clear_touch_ups(context):
    scene = context.scene
    rig = find_rig(scene)
    edit = touch_up(PREFIX + current_cycle(scene))
    for curve in _draft_curves(edit):
        while len(curve.keyframe_points):
            curve.keyframe_points.remove(curve.keyframe_points[0], fast=True)
        curve.update()
    if rig is not None:
        for n in TRUNK + ARMS + LEGS + HEAD + (HIPS,):
            pb = rig.pose.bones.get(n)
            if pb is not None:
                pb.rotation_quaternion = (1, 0, 0, 0)
                pb.location = (0, 0, 0)
        rig.update_tag(refresh={'OBJECT'})
    scene.frame_set(scene.frame_current)


# ---- Export ---------------------------------------------------------------------------------------------------------

def export_path(key):
    root = project_root()
    return (root / target(key)) if root else None


def export_cycle(context, key):
    """One cycle to the game with its touch-ups (even while 손질 층 켜기/끄기 has them off), marked as sent."""
    oc = one_click()
    scene = context.scene
    rig = find_rig(scene)
    select_cycle(context, key)
    path = export_path(key)
    if path is None:
        raise RuntimeError('ENFILADE 프로젝트를 찾지 못했습니다.')
    path.parent.mkdir(parents=True, exist_ok=True)
    name = PREFIX + key
    clips = path.with_name(name + '.clips.csv')
    oc.write_meta(clips, oc.TEXT_META)
    oc.write_meta(path)
    data = rig.animation_data
    influence = data.action_influence
    data.action_influence = 1.0
    try:
        truefps().export_active_clip(context, rig, str(path))
    finally:
        data.action_influence = influence
    # A loop whose hips keep their sway (as the Mixamo cycles do; the cycles are in place, so there is no travel to drop),
    # facing as Kazusa's rig faces in Unity (the shove workspace's kick: yawOffset 0).
    oc.write_text(clips, CSV_HEADER + f'{name},1,0,0,0,0,0,,\n')
    oc.mark_sent(bpy.data.actions.get(name), fingerprint(key))
    return path


def withhold(key):
    """The cycle's file out of the game (the original clip plays again), kept out until the cycle changes."""
    path = export_path(key)
    removed = []
    if path is not None:
        for p in (path, path.with_name(path.stem + '.clips.csv')):
            if p.exists():
                p.unlink()
                removed.append(p.name)
    one_click().mark_withheld(bpy.data.actions.get(PREFIX + key), fingerprint(key))
    return removed


def send_changed(context, force=False):
    """Every cycle changed since it was last sent (or whose file went missing) to the game; the cycle shown stays shown,
    on its frame. Returns (sent labels, failures), or None with the panel's switch off (force: sends anyway, the
    게임에 보내기 button)."""
    scene = context.scene
    if not is_run_file(scene) or not (force or scene.enf_run.auto_send):
        return None
    oc = one_click()
    shown, frame = current_cycle(scene), scene.frame_current
    sent, failed = [], []
    for key, label in CYCLES:
        if not bpy.data.actions.get(PREFIX + key + '_draft'):
            continue
        try:
            action, _draft = item(context, key)
            if not oc.needs_sending(action, fingerprint(key), in_game(key)):
                continue
            export_cycle(context, key)
            sent.append(label)
        except Exception as error:
            failed.append(f'{label}: {error}')
    if current_cycle(scene) != shown or rig_action_name(scene) != PREFIX + shown:
        select_cycle(context, shown)
    if scene.frame_current != frame:
        scene.frame_set(frame)
    return sent, failed


def rig_action_name(scene):
    rig = find_rig(scene)
    action = rig.animation_data.action if rig is not None and rig.animation_data else None
    return action.name if action is not None else None


# ---- UI -------------------------------------------------------------------------------------------------------------

class ENFRunSettings(bpy.types.PropertyGroup):
    auto_send: BoolProperty(name='저장하면 게임에 보내기', default=True,
                            description='Ctrl+S로 저장할 때 고친 걸음을 모두 게임에 보낸다(Art/Animations/Run의 FBX)')
    lean_step: FloatProperty(name='세우는 각도', default=2.0, min=.5, max=10.0, description='한 번 누를 때 상체를 세우거나 숙이는 각도(도)')
    swing_step: FloatProperty(name='흔들기 배율', default=1.15, min=1.01, max=2.0, description='한 번 누를 때 흔들림을 키우거나 줄이는 배율')


def _ok(op, context):
    if find_rig(context.scene) is None:
        op.report({'ERROR'}, 'KazusaBandRig를 찾지 못했습니다.')
        return False
    if context.object and context.object.mode == 'EDIT':
        bpy.ops.object.mode_set(mode='OBJECT')
    return True


class ENFRUN_OT_pick(bpy.types.Operator):
    bl_idname = 'enf_run.pick'
    bl_label = '걸음 고르기'
    bl_options = {'REGISTER', 'UNDO'}
    key: bpy.props.StringProperty()

    def execute(self, context):
        if not _ok(self, context):
            return {'CANCELLED'}
        select_cycle(context, self.key)
        return {'FINISHED'}


class ENFRUN_OT_reshape(bpy.types.Operator):
    bl_idname = 'enf_run.reshape'
    bl_label = '자세 다듬기'
    bl_options = {'REGISTER', 'UNDO'}
    how: bpy.props.StringProperty()

    def execute(self, context):
        if not _ok(self, context):
            return {'CANCELLED'}
        settings = context.scene.enf_run
        lean, swing = settings.lean_step, settings.swing_step
        # Turning about the body's right (-X here, the rig faces -Y) by a positive angle tips the top back: upright.
        jobs = {
            'upright': (TRUNK, lean / len(TRUNK), 'lean'), 'stoop': (TRUNK, -lean / len(TRUNK), 'lean'),
            'arms_more': (ARMS, swing, 'swing'), 'arms_less': (ARMS, 1 / swing, 'swing'),
            'knees_more': (LEGS, swing, 'swing'), 'knees_less': (LEGS, 1 / swing, 'swing'),
            'bob_less': ((HIPS,), 1 / swing, 'bob'), 'bob_more': ((HIPS,), swing, 'bob'),
            'head_still': (HEAD, 1 / swing, 'swing'), 'hips_less': ((HIPS,), 1 / swing, 'swing'),
        }
        bones, amount, kind = jobs[self.how]
        reshape(context, bones, amount, kind)
        return {'FINISHED'}


class ENFRUN_OT_toggle(bpy.types.Operator):
    bl_idname = 'enf_run.toggle'
    bl_label = '손질 층 켜기/끄기'
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        if not _ok(self, context):
            return {'CANCELLED'}
        scene = context.scene
        scene['enf_run_live'] = not scene.get('enf_run_live', True)
        select_cycle(context, current_cycle(scene))
        self.report({'INFO'}, '손질 층 켜짐' if scene['enf_run_live'] else '손질 층 꺼짐 (시안만 보는 중)')
        return {'FINISHED'}


class ENFRUN_OT_clear(bpy.types.Operator):
    bl_idname = 'enf_run.clear'
    bl_label = '이 걸음 손질 모두 지우기'
    bl_options = {'REGISTER', 'UNDO'}

    def invoke(self, context, event):
        return context.window_manager.invoke_confirm(self, event)

    def execute(self, context):
        if not _ok(self, context):
            return {'CANCELLED'}
        clear_touch_ups(context)
        return {'FINISHED'}


class ENFRUN_OT_seam(bpy.types.Operator):
    bl_idname = 'enf_run.seam'
    bl_label = '고리 이음매 확인'

    def execute(self, context):
        if not _ok(self, context):
            return {'CANCELLED'}
        rig = find_rig(context.scene)
        try:
            seam = truefps().loop_seam(context, rig)
        except Exception as error:
            self.report({'ERROR'}, str(error))
            return {'CANCELLED'}
        self.report({'INFO'}, f'첫·끝 프레임 차이: {seam}')
        return {'FINISHED'}


class ENFRUN_OT_export(bpy.types.Operator):
    bl_idname = 'enf_run.export'
    bl_label = '게임에 보내기'
    bl_description = 'Ctrl+S와 같다: 고친 걸음을 모두 게임에 보내고 작업 파일을 저장한다(저장하면 보내기를 꺼 두어도 보낸다)'

    def execute(self, context):
        if not _ok(self, context):
            return {'CANCELLED'}
        sent, failed = send_changed(context, force=True) or ([], ['달리기 작업 파일이 아닙니다'])
        one_click().send_report(context.scene, sent, failed)
        if bpy.data.filepath:
            bpy.ops.wm.save_mainfile()
        if failed:
            self.report({'ERROR'}, '못 보냄: ' + '; '.join(failed))
            return {'CANCELLED'}
        self.report({'INFO'}, ('보냄: ' + ', '.join(sent) + ' — Unity 창을 누르면 가져가고, 다음 Play부터 원래 클립 대신 쓰인다')
                    if sent else '고친 걸음이 없어 저장만 했습니다')
        return {'FINISHED'}


class ENFRUN_OT_revert(bpy.types.Operator):
    bl_idname = 'enf_run.revert'
    bl_label = '원래 클립으로 되돌리기'
    bl_description = '보낸 파일을 지워 게임이 원래 클립을 다시 쓴다. 이 걸음을 다시 고치면 저장할 때 다시 들어간다'

    def invoke(self, context, event):
        return context.window_manager.invoke_confirm(self, event)

    def execute(self, context):
        key = current_cycle(context.scene)
        removed = withhold(key)
        self.report({'INFO'}, ('지움: ' + ', '.join(removed)) if removed else '보낸 파일이 없습니다')
        return {'FINISHED'}


class ENFRUN_PT_panel(bpy.types.Panel):
    bl_label = '달리기 다듬기'
    bl_idname = 'ENFRUN_PT_panel'
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = TAB

    def draw(self, context):
        layout = self.layout
        scene = context.scene
        key = current_cycle(scene)
        settings = scene.enf_run
        box = layout.box()
        box.label(text='1. 걸음 고르기')
        grid = box.grid_flow(columns=3, even_columns=True)
        for name, label in CYCLES:
            if bpy.data.actions.get(PREFIX + name + '_draft'):
                grid.operator('enf_run.pick', text=label, depress=name == key).key = name
        box.label(text=f'지금: {label_of(key)} ({key}) · {frames_of(key)}프레임, 끝 = 처음')

        box = layout.box()
        box.label(text='2. 걸음 전체를 한 번에 (손질 층)')
        row = box.row(align=True)
        row.operator('enf_run.reshape', text='상체 세우기').how = 'upright'
        row.operator('enf_run.reshape', text='상체 숙이기').how = 'stoop'
        row = box.row(align=True)
        row.operator('enf_run.reshape', text='팔 크게').how = 'arms_more'
        row.operator('enf_run.reshape', text='팔 작게').how = 'arms_less'
        row = box.row(align=True)
        row.operator('enf_run.reshape', text='무릎 더 들기').how = 'knees_more'
        row.operator('enf_run.reshape', text='무릎 덜 들기').how = 'knees_less'
        row = box.row(align=True)
        row.operator('enf_run.reshape', text='출렁임 줄이기').how = 'bob_less'
        row.operator('enf_run.reshape', text='출렁임 늘리기').how = 'bob_more'
        row = box.row(align=True)
        row.operator('enf_run.reshape', text='고개 덜 흔들기').how = 'head_still'
        row.operator('enf_run.reshape', text='골반 덜 돌기').how = 'hips_less'
        box.prop(settings, 'lean_step')
        box.prop(settings, 'swing_step')
        box.label(text='그 밖에는 프레임을 골라 G·R로 직접 (자동 키 켜짐)')

        box = layout.box()
        box.label(text='3. 확인')
        row = box.row(align=True)
        row.operator('enf_run.toggle')
        row.operator('enf_run.seam')
        box.operator('enf_run.clear')

        box = layout.box()
        box.label(text='4. 저장(Ctrl+S)하면 게임에 들어간다')
        box.prop(settings, 'auto_send')
        oc = one_click()
        action = bpy.data.actions.get(PREFIX + key)
        changed = action is not None and action.get(oc.SENT) != fingerprint(key)
        if action is not None and action.get(oc.WITHHELD) and not changed:
            box.label(text=f'게임은 원래 {key}를 쓰는 중: 고치면 저장할 때 들어간다')
        elif not in_game(key):
            box.label(text='아직 게임에 없음: 저장하면 들어간다')
        elif changed:
            box.label(text='고친 것이 있음: 저장하면 게임에 들어간다')
        else:
            box.label(text='게임이 지금 이것을 쓰는 중')
        if scene.get(oc.NOTE):
            box.label(text=str(scene[oc.NOTE])[:60])
        box.operator('enf_run.export', icon='EXPORT')
        box.label(text=f'→ {target(key).as_posix()}')
        box.operator('enf_run.revert')


CLASSES = (ENFRunSettings, ENFRUN_OT_pick, ENFRUN_OT_reshape, ENFRUN_OT_toggle, ENFRUN_OT_clear, ENFRUN_OT_seam,
           ENFRUN_OT_export, ENFRUN_OT_revert, ENFRUN_PT_panel)


def register():
    for cls in CLASSES:
        try:
            bpy.utils.register_class(cls)
        except ValueError:
            bpy.utils.unregister_class(cls)
            bpy.utils.register_class(cls)
    bpy.types.Scene.enf_run = bpy.props.PointerProperty(type=ENFRunSettings)
    one_click().register_workspace(TAB, is_run_file, arrange, send_changed)


def unregister():
    one_click().unregister_workspace(TAB)
    del bpy.types.Scene.enf_run
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)


if __name__ == '__main__':
    register()
