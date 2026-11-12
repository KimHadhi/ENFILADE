"""ENFILADE pose workspace: a "Pose" tab in the 3D view's sidebar (N) for touching up the results show's victory poses
on Mayo by hand (the user, 2026-11-12: "세부 조정은 방법 알려 주면 내가 알아서 해 줄 테니까"; guide:
ArtSource/Poses/START_HERE.md). ArtSource/Poses/START_POSES.cmd opens ENFILADE_Poses_Mayo.blend with it; a .blend opened
by hand runs it from the "START HERE - Pose tools.py" text (Alt+P in the Scripting tab).

It opens ready to work in, and saving sends the work to the game (the user, 2026-11-13: "내가 해야 하는 잡일을 최대한
줄여. 나는 딱 오직 인간만이 할 수 있는 것만 해내면 되게끔"; the shared part is Tools/Blender/OneClick/one_click.py):
- On open: the Animation workspace, Mayo in Pose mode on the last pose's struck frame (9), auto-key on, the Dope Sheet
  showing every bone's keys (a row per bone, closed) with the summary row, the Pose tab, the small view from the front.
- A pose is struck at frame 9 and held to 75 with a breath at 42 (make_victory_poses.py). Changing it at frame 9 is
  enough: leaving frame 9 (playing, or going to another frame) and saving key 42 and 75 from it, 42 keeping its breath.
- Saving (Ctrl+S) sends every pose that changed since it was last sent (or never was) to
  Assets/_Project/Resources/Poses/<name>.fbx, where Unity takes it onto Mayo's avatar (PoseImport).
The recipes, the rig helpers and the export come from make_victory_poses.py beside it.
"""
import importlib.util
import os
import sys
from pathlib import Path

import bpy
from bpy.app.handlers import persistent
from bpy.props import BoolProperty, StringProperty
from mathutils import Quaternion, Vector

CATEGORY = "Pose"
WORKSPACE = "Animation"
ONE_CLICK = os.path.join("Tools", "Blender", "OneClick", "one_click.py")
# The Chest's rotation channels: at the breath (42) the chest is turned a hair up from the struck pose.
CHEST = 'pose.bones["Chest"].rotation_quaternion'


def project_root():
    """The ENFILADE checkout: from this file, else from the open .blend (ArtSource/Poses/...)."""
    starts = []
    try:
        starts.append(os.path.abspath(__file__))
    except NameError:
        pass
    if bpy.data.filepath:
        starts.append(os.path.abspath(bpy.data.filepath))
    for start in starts:
        folder = os.path.dirname(start)
        while True:
            if os.path.isdir(os.path.join(folder, "Assets", "_Project")) and os.path.isdir(os.path.join(folder, "ProjectSettings")):
                return folder
            parent = os.path.dirname(folder)
            if parent == folder:
                break
            folder = parent
    return None


ROOT = project_root()


def _module(name, rel):
    """A project tool module, loaded once and without a __pycache__ beside it (one there would show up in git)."""
    if name in sys.modules:
        return sys.modules[name]
    if ROOT is None:
        raise RuntimeError("Open ENFILADE_Poses_Mayo.blend from inside the ENFILADE project first.")
    spec = importlib.util.spec_from_file_location(name, os.path.join(ROOT, rel))
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    writes = sys.dont_write_bytecode
    sys.dont_write_bytecode = True
    try:
        spec.loader.exec_module(module)
    finally:
        sys.dont_write_bytecode = writes
    return module


poses = _module("enf_victory_poses", os.path.join("Tools", "Blender", "Poses", "make_victory_poses.py"))
oc = _module("enf_one_click", ONE_CLICK)

# The poses' names in the tab; any other enf_pose_ action shows by its own name.
NAMES = {
    "enf_pose_fist": "주먹 (MVP)",
    "enf_pose_vsign": "V",
    "enf_pose_point": "손가락질",
    "enf_pose_salute": "경례",
    "enf_pose_hips": "허리에 손",
}

# The parts the tab picks, left and right as she has them.
PARTS = (
    ("머리", "Head"), ("목", "Neck"), ("가슴", "Chest"), ("허리", "Spine"), ("골반", "Hips"),
)
SIDES = (
    ("위팔", "Upper_arm"), ("아래팔", "Lower_arm"), ("손", "Hand"), ("어깨", "Shoulder"), ("허벅지", "Upper_leg"), ("종아리", "Lower_leg"),
)
SHAPES = (("주먹", "fist"), ("V", "v"), ("손가락", "point"), ("펴기", "flat"), ("편하게", "relaxed"))


def rig_object():
    arm = bpy.context.view_layer.objects.active
    if arm is not None and arm.type == 'ARMATURE':
        return arm
    return next((o for o in bpy.context.view_layer.objects if o.type == 'ARMATURE'), None)


def current_action(arm):
    return arm.animation_data.action if arm is not None and arm.animation_data else None


def pose_mode(context, arm):
    for o in context.view_layer.objects:
        o.select_set(o == arm)
    context.view_layer.objects.active = arm
    if context.mode != 'POSE':
        try:
            bpy.ops.object.mode_set(mode='POSE')
        except RuntimeError:
            pass


def label_of(action):
    return NAMES.get(action.name, action.name)


# ---- The hold follows the strike ---------------------------------------------------------------------------------------

def _curve_sets(action):
    """An action's curves by data path: {path: {index: curve}}."""
    out = {}
    for curve in oc.curves(action):
        out.setdefault(curve.data_path, {})[curve.array_index] = curve
    return out


def hold_in_step(action):
    """Frame 75 is the struck pose (frame 9) and 42 is too but for the chest's breath."""
    for curve in oc.curves(action):
        at = curve.evaluate(poses.STRIKE)
        if abs(curve.evaluate(poses.HOLD) - at) > 1e-5:
            return False
        if curve.data_path != CHEST and abs(curve.evaluate(poses.BREATH) - at) > 1e-5:
            return False
    return True


def _put(curve, frame, value):
    """The curve's key at frame set to value (made if missing); True if it changed."""
    point = next((p for p in curve.keyframe_points if abs(p.co.x - frame) < .5), None)
    if point is None:
        curve.keyframe_points.insert(frame, value, options={'FAST'})
        return True
    change = value - point.co.y
    if abs(change) < 1e-9:
        return False
    point.co.y = value
    point.handle_left.y += change
    point.handle_right.y += change
    return True


def follow_strike(action):
    """The hold keyed from the struck pose, straight on the curves (no frame change, so it is safe while frames change):
    frame 75 as frame 9, and 42 as 9 with the chest keeping the breath it had (its turn from 75 to 42). Returns whether
    anything changed."""
    if hold_in_step(action):
        return False
    changed = set()
    for path, channels in _curve_sets(action).items():
        if path == CHEST and len(channels) == 4:
            struck = Quaternion([channels[i].evaluate(poses.STRIKE) for i in range(4)])
            held = Quaternion([channels[i].evaluate(poses.HOLD) for i in range(4)])
            breath = held.inverted() @ Quaternion([channels[i].evaluate(poses.BREATH) for i in range(4)])
            breathing = struck @ breath
            if breathing.dot(struck) < 0:
                breathing.negate()
            for i in range(4):
                if _put(channels[i], poses.HOLD, struck[i]) | _put(channels[i], poses.BREATH, breathing[i]):
                    changed.add(channels[i])
            continue
        for curve in channels.values():
            at = curve.evaluate(poses.STRIKE)
            if _put(curve, poses.HOLD, at) | _put(curve, poses.BREATH, at):
                changed.add(curve)
    for curve in changed:
        curve.update()
    return bool(changed)


_LAST_FRAME = [None]


@persistent
def _frame_changed(scene, *_unused):
    """Leaving the struck frame (9), where the pose is changed: the hold follows it at once, so playing shows it held."""
    last, _LAST_FRAME[0] = _LAST_FRAME[0], scene.frame_current
    if last != poses.STRIKE or scene.frame_current == poses.STRIKE:
        return
    try:
        if not is_pose_file(scene):
            return
        action = current_action(rig_object())
        if action is not None and action.name.startswith(poses.PREFIX):
            follow_strike(action)
    except (ReferenceError, AttributeError, RuntimeError):
        pass


# ---- Sending to the game --------------------------------------------------------------------------------------------

def is_pose_file(scene):
    arm = rig_object()
    return arm is not None and "Upper_arm.R" in arm.data.bones and any(a.name.startswith(poses.PREFIX) for a in bpy.data.actions)


def fbx_of(action):
    return Path(poses.OUT) / (action.name + ".fbx")


def fingerprint(action):
    return oc.fingerprint([action], extra=[action.name])


def changed(action):
    return oc.needs_sending(action, fingerprint(action), fbx_of(action).exists())


def send(context, action):
    """One pose to the game (its FBX, a bare .meta first for a new one), marked as sent."""
    arm = rig_object()
    if arm is None:
        raise RuntimeError("마요 뼈대를 못 찾았어요.")
    follow_strike(action)
    scene = context.scene
    frame, mode = scene.frame_current, context.mode
    if mode != 'OBJECT' and context.object is not None:
        bpy.ops.object.mode_set(mode='OBJECT')
    try:
        oc.write_meta(fbx_of(action))
        path = poses.export(poses.Rig(arm), action)
    finally:
        if mode == 'POSE':
            pose_mode(context, arm)
        scene.frame_set(frame)
    oc.mark_sent(action, fingerprint(action))
    return path


def withhold(action):
    """The pose's FBX out of the game (the show stops using it), kept out until the pose changes."""
    path = fbx_of(action)
    removed = []
    if path.exists():
        path.unlink()
        removed.append(path.name)
    oc.mark_withheld(action, fingerprint(action))
    return removed


def send_changed(context):
    """Every pose that changed since it was last sent (or never was) to the game; the pose shown stays shown."""
    scene = context.scene
    if not scene.enf_pose.auto_send:
        return None
    arm = rig_object()
    if arm is None:
        return None
    shown = current_action(arm)
    sent, failed = [], []
    for action in poses.pose_actions():
        follow_strike(action)
        if not changed(action):
            continue
        try:
            send(context, action)
            sent.append(label_of(action))
        except Exception as error:
            failed.append(f"{label_of(action)}: {error}")
    if arm.animation_data and arm.animation_data.action != shown:
        arm.animation_data.action = shown
    return sent, failed


def prepare(context):
    """Every pose ready to show: channels a row per bone (closed), and a pose never sent from here counted as what the
    game has when its FBX is there (the build exported them all)."""
    for action in poses.pose_actions():
        oc.group_channels(action)
        if oc.SENT not in action and fbx_of(action).exists():
            oc.mark_sent(action, fingerprint(action))


def arrange(context):
    """The workspace as the work needs it: the Animation screen, Mayo in Pose mode on the struck frame with auto-key, the
    Dope Sheet, the Pose tab, the small view from the front."""
    scene = context.scene
    arm = rig_object()
    if arm is None:
        return True
    # The poses first: they are ready even where the screen can't be (a window switches its workspace on the next tick).
    prepare(context)
    if not oc.open_workspace(context, WORKSPACE):
        return False
    if current_action(arm) is None:
        first = next(iter(poses.pose_actions()), None)
        if first is not None:
            arm.animation_data_create()
            arm.animation_data.action = first
    poses.set_fps()
    oc.auto_key(scene, True)
    r = poses.Rig(arm)
    middle = (r.world_head("Hips") + r.world_head("Head")) * .5
    middle.z = (r.world_head("Head").z + r.world_head("Foot.L").z) * .5
    for window in context.window_manager.windows:
        main = oc.main_view(window)
        oc.show_tab(main, CATEGORY)
        oc.dope_sheet(window)
        for area in oc.views(window):
            if area != main:
                oc.frame_view(area, middle, r.F, 1.5, ortho=True)
        if main is not None:
            oc.select(context, window, main, [arm], arm, pose=True)
    if scene.frame_current != poses.STRIKE:
        scene.frame_set(poses.STRIKE)
    _LAST_FRAME[0] = scene.frame_current
    action = current_action(arm)
    oc.tell(f"승리 포즈 「{label_of(action) if action else '없음'}」 · 9프레임에서 고치고 Ctrl+S 하면 게임에 들어갑니다")
    return True


# ---- UI ---------------------------------------------------------------------------------------------------------------

class ENFPoseSettings(bpy.types.PropertyGroup):
    auto_send: BoolProperty(name="저장하면 게임에 보내기", default=True,
                            description="Ctrl+S로 저장할 때 바뀐 포즈를 모두 게임에 보낸다(Resources/Poses의 FBX)")


class ENFPOSE_OT_pick(bpy.types.Operator):
    bl_idname = "enf_pose.pick"
    bl_label = "포즈 고르기"
    bl_description = "이 포즈를 띄우고 포즈를 잡은 프레임(9)으로 간다"
    bl_options = {'REGISTER', 'UNDO'}
    action: StringProperty()

    def execute(self, context):
        arm = rig_object()
        action = bpy.data.actions.get(self.action)
        if arm is None or action is None:
            self.report({'ERROR'}, "포즈나 마요 뼈대를 못 찾았어요.")
            return {'CANCELLED'}
        shown = current_action(arm)
        if shown is not None and shown.name.startswith(poses.PREFIX):
            follow_strike(shown)
        arm.animation_data_create()
        arm.animation_data.action = action
        poses.set_fps()
        context.scene.frame_set(poses.STRIKE)
        _LAST_FRAME[0] = poses.STRIKE
        pose_mode(context, arm)
        return {'FINISHED'}


class ENFPOSE_OT_part(bpy.types.Operator):
    bl_idname = "enf_pose.part"
    bl_label = "부위 고르기"
    bl_description = "이 뼈만 골라 둔다. 3D 화면에 마우스를 두고 R을 누르면 돌아가고, R 다음 X·Y·Z로 축을 고정한다"
    bl_options = {'REGISTER', 'UNDO'}
    bone: StringProperty()

    def execute(self, context):
        arm = rig_object()
        if arm is None or self.bone not in arm.pose.bones:
            self.report({'ERROR'}, f"{self.bone} 뼈가 없어요.")
            return {'CANCELLED'}
        pose_mode(context, arm)
        for bone in arm.data.bones:
            bone.select = bone.select_head = bone.select_tail = False
        target = arm.data.bones[self.bone]
        target.select = True
        arm.data.bones.active = target
        return {'FINISHED'}


class ENFPOSE_OT_shape(bpy.types.Operator):
    bl_idname = "enf_pose.shape"
    bl_label = "손 모양"
    bl_description = "이 손의 손가락을 곧게 편 다음 이 모양으로 접는다(손목과 팔은 그대로). 자동 키로 지금 프레임에 저장된다"
    bl_options = {'REGISTER', 'UNDO'}
    side: StringProperty()
    shape: StringProperty()

    def execute(self, context):
        arm = rig_object()
        if arm is None:
            return {'CANCELLED'}
        r = poses.Rig(arm)
        bones = r.finger_bones(self.side)
        r.rest(set(bones))
        r.hand_shape(self.side, self.shape)
        # Set from code, so auto-key doesn't see it: keyed here as auto-key would.
        if context.scene.tool_settings.use_keyframe_insert_auto:
            frame = context.scene.frame_current
            for name in bones:
                pb = arm.pose.bones.get(name)
                if pb is not None:
                    pb.keyframe_insert("rotation_quaternion", frame=frame, group=name)
        return {'FINISHED'}


class ENFPOSE_OT_key(bpy.types.Operator):
    bl_idname = "enf_pose.key"
    bl_label = "보이는 자세를 이 포즈로"
    bl_description = ("지금 보이는 자세를 이 포즈로 9·42·75 프레임에 저장한다(42는 가슴이 살짝 올라온 숨). 9프레임에서 고쳤다면 "
                      "누르지 않아도 된다. 1프레임의 편히 선 자세는 그대로")
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        arm = rig_object()
        action = current_action(arm)
        if action is None:
            self.report({'ERROR'}, "먼저 포즈를 고르세요.")
            return {'CANCELLED'}
        poses.key_pose(poses.Rig(arm))
        self.report({'INFO'}, f"{action.name}: 이 자세로 저장했어요. Ctrl+S로 저장하면 게임에 들어갑니다.")
        return {'FINISHED'}


class ENFPOSE_OT_send(bpy.types.Operator):
    bl_idname = "enf_pose.send"
    bl_label = "지금 게임에 보내기"
    bl_description = "이 포즈를 Assets/_Project/Resources/Poses/<이름>.fbx로 지금 보내고 작업 파일도 저장한다. Unity로 돌아가면 알아서 다시 읽는다"
    bl_options = {'REGISTER'}

    def execute(self, context):
        arm = rig_object()
        action = current_action(arm)
        if action is None or not action.name.startswith(poses.PREFIX):
            self.report({'ERROR'}, "enf_pose_로 시작하는 포즈를 고르세요.")
            return {'CANCELLED'}
        try:
            path = send(context, action)
        except Exception as error:
            self.report({'ERROR'}, str(error))
            return {'CANCELLED'}
        context.scene.frame_set(poses.STRIKE)
        if bpy.data.filepath:
            bpy.ops.wm.save_mainfile()
        self.report({'INFO'}, f"보냈어요: {os.path.relpath(path, ROOT)}. Unity로 돌아가면 다시 읽어요.")
        return {'FINISHED'}


class ENFPOSE_OT_withhold(bpy.types.Operator):
    bl_idname = "enf_pose.withhold"
    bl_label = "게임에서 빼기"
    bl_description = "이 포즈의 FBX를 게임에서 지운다(결과 연출이 더는 쓰지 않는다). 이 포즈를 다시 고치면 저장할 때 다시 들어간다"
    bl_options = {'REGISTER'}

    def invoke(self, context, event):
        return context.window_manager.invoke_confirm(self, event)

    def execute(self, context):
        action = current_action(rig_object())
        if action is None or not action.name.startswith(poses.PREFIX):
            self.report({'ERROR'}, "enf_pose_로 시작하는 포즈를 고르세요.")
            return {'CANCELLED'}
        removed = withhold(action)
        self.report({'INFO'}, ("지웠어요: " + ", ".join(removed)) if removed else "게임에 들어간 FBX가 없어요.")
        return {'FINISHED'}


class ENFPOSE_OT_reset(bpy.types.Operator):
    bl_idname = "enf_pose.reset"
    bl_label = "처음 만든 대로 되돌리기"
    bl_description = "이 포즈에 손댄 것을 모두 버리고 Claude가 처음 만든 레시피 그대로 다시 만든다. 저장하면 게임에도 들어간다"
    bl_options = {'REGISTER', 'UNDO'}

    def invoke(self, context, event):
        return context.window_manager.invoke_confirm(self, event)

    def execute(self, context):
        arm = rig_object()
        action = current_action(arm)
        if action is None or action.name not in poses.POSES:
            self.report({'ERROR'}, "레시피가 있는 포즈(주먹·V·손가락질·경례·허리에 손)만 되돌릴 수 있어요.")
            return {'CANCELLED'}
        rebuilt = poses.rebuild(poses.Rig(arm), action.name)
        oc.group_channels(rebuilt)
        arm.animation_data.action = rebuilt
        context.scene.frame_set(poses.STRIKE)
        return {'FINISHED'}


class ENFPOSE_OT_new(bpy.types.Operator):
    bl_idname = "enf_pose.new"
    bl_label = "새 포즈로 복사"
    bl_description = "지금 포즈를 복사해 새 이름의 포즈를 만든다. 저장하면 게임에 들어가고 결과 화면이 차례로 쓴다"
    bl_options = {'REGISTER', 'UNDO'}
    pose_name: StringProperty(name="이름 (영어·숫자)", default="mine")

    def invoke(self, context, event):
        return context.window_manager.invoke_props_dialog(self)

    def execute(self, context):
        arm = rig_object()
        action = current_action(arm)
        clean = "".join(c for c in self.pose_name.lower() if c.isascii() and (c.isalnum() or c == "_")).strip("_")
        if action is None or not clean:
            self.report({'ERROR'}, "포즈를 고르고 영어·숫자 이름을 넣으세요.")
            return {'CANCELLED'}
        name = poses.PREFIX + clean
        if bpy.data.actions.get(name) is not None:
            self.report({'ERROR'}, f"{name}은 이미 있어요.")
            return {'CANCELLED'}
        copy = action.copy()
        copy.name = name
        copy.use_fake_user = True
        for prop in (oc.SENT, oc.WITHHELD):
            if prop in copy:
                del copy[prop]
        arm.animation_data.action = copy
        context.scene.frame_set(poses.STRIKE)
        return {'FINISHED'}


class ENFPOSE_OT_guide(bpy.types.Operator):
    bl_idname = "enf_pose.guide"
    bl_label = "설명서 열기"
    bl_description = "ArtSource/Poses/START_HERE.md를 연다"

    def execute(self, context):
        path = os.path.join(ROOT, "ArtSource", "Poses", "START_HERE.md") if ROOT else ""
        if path and os.path.isfile(path) and hasattr(os, "startfile"):
            os.startfile(path)
            return {'FINISHED'}
        self.report({'WARNING'}, path or "START_HERE.md를 못 찾았어요.")
        return {'CANCELLED'}


class ENFPOSE_PT_panel(bpy.types.Panel):
    bl_label = "ENFILADE 승리 포즈"
    bl_idname = "ENFPOSE_PT_panel"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = CATEGORY

    def draw(self, context):
        layout = self.layout
        scene = context.scene
        arm = rig_object()
        action = current_action(arm)

        box = layout.box()
        box.label(text="1. 포즈 고르기", icon='ARMATURE_DATA')
        grid = box.grid_flow(columns=2, even_columns=True, align=True)
        for each in poses.pose_actions():
            op = grid.operator(ENFPOSE_OT_pick.bl_idname, text=NAMES.get(each.name, each.name), depress=each == action)
            op.action = each.name
        box.label(text=f"지금: {action.name if action else '없음'}  (프레임 {scene.frame_current})")

        box = layout.box()
        box.label(text="2. 9프레임에서 부위 고르고 R로 돌리기", icon='BONE_DATA')
        row = box.row(align=True)
        for label, bone in PARTS:
            row.operator(ENFPOSE_OT_part.bl_idname, text=label).bone = bone
        for side, word in (("R", "오른쪽"), ("L", "왼쪽")):
            box.label(text=f"{word} (캐릭터 기준)")
            grid = box.grid_flow(columns=3, even_columns=True, align=True)
            for label, bone in SIDES:
                grid.operator(ENFPOSE_OT_part.bl_idname, text=label).bone = f"{bone}.{side}"
        box.label(text="손 모양 (손가락만 바뀐다)")
        for side, word in (("R", "오른손"), ("L", "왼손")):
            row = box.row(align=True)
            row.label(text=word)
            for label, shape in SHAPES:
                op = row.operator(ENFPOSE_OT_shape.bl_idname, text=label)
                op.side, op.shape = side, shape
        if scene.frame_current != poses.STRIKE:
            box.label(text="포즈는 9프레임에서 고친다(1은 편히 선 자세)", icon='INFO')

        box = layout.box()
        box.label(text="3. 저장(Ctrl+S)하면 게임에 들어간다", icon='EXPORT')
        settings = scene.enf_pose
        box.prop(settings, "auto_send")
        if action is not None and action.name.startswith(poses.PREFIX):
            if action.get(oc.WITHHELD) and action.get(oc.SENT) == fingerprint(action):
                box.label(text="게임에서 뺀 포즈: 고치면 저장할 때 다시 들어간다")
            elif not fbx_of(action).exists():
                box.label(text="아직 게임에 없음: 저장하면 들어간다")
            elif action.get(oc.SENT) != fingerprint(action):
                box.label(text="고친 것이 있음: 저장하면 게임에 들어간다")
            else:
                box.label(text="게임이 지금 이 포즈를 쓰는 중")
        if scene.get(oc.NOTE):
            box.label(text=str(scene[oc.NOTE])[:60])
        col = box.column(align=True)
        col.operator(ENFPOSE_OT_send.bl_idname, icon='EXPORT')
        col.operator(ENFPOSE_OT_key.bl_idname, icon='KEYFRAME_HLT')
        row = box.row(align=True)
        row.operator(ENFPOSE_OT_reset.bl_idname, icon='LOOP_BACK')
        row.operator(ENFPOSE_OT_new.bl_idname, icon='DUPLICATE')
        box.operator(ENFPOSE_OT_withhold.bl_idname, icon='X')
        layout.operator(ENFPOSE_OT_guide.bl_idname, icon='HELP')


CLASSES = (ENFPoseSettings, ENFPOSE_OT_pick, ENFPOSE_OT_part, ENFPOSE_OT_shape, ENFPOSE_OT_key, ENFPOSE_OT_send,
           ENFPOSE_OT_withhold, ENFPOSE_OT_reset, ENFPOSE_OT_new, ENFPOSE_OT_guide, ENFPOSE_PT_panel)


def _drop_frame_handler():
    for handler in list(bpy.app.handlers.frame_change_pre):
        if getattr(handler, '__name__', '') == '_frame_changed' and getattr(handler, '__module__', '') in {__name__, 'pose_tools'}:
            bpy.app.handlers.frame_change_pre.remove(handler)


def register():
    # Running the text again (Alt+P) replaces the classes an earlier run registered.
    for cls in reversed(bpy.app.driver_namespace.get("enf_pose_classes", [])):
        try:
            bpy.utils.unregister_class(cls)
        except RuntimeError:
            pass
    for cls in CLASSES:
        bpy.utils.register_class(cls)
    bpy.app.driver_namespace["enf_pose_classes"] = list(CLASSES)
    bpy.types.Scene.enf_pose = bpy.props.PointerProperty(type=ENFPoseSettings)
    _drop_frame_handler()
    bpy.app.handlers.frame_change_pre.append(_frame_changed)
    oc.register_workspace("승리 포즈", is_pose_file, arrange, send_changed)


def unregister():
    oc.unregister_workspace("승리 포즈")
    _drop_frame_handler()
    if hasattr(bpy.types.Scene, "enf_pose"):
        del bpy.types.Scene.enf_pose
    for cls in reversed(bpy.app.driver_namespace.pop("enf_pose_classes", [])):
        try:
            bpy.utils.unregister_class(cls)
        except RuntimeError:
            pass


if __name__ == "__main__":
    register()
