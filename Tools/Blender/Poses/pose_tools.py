"""ENFILADE pose workspace: a "Pose" tab in the 3D view's sidebar (N) for touching up the results show's victory poses
on Mayo by hand (the user, 2026-11-12: "세부 조정은 방법 알려 주면 내가 알아서 해 줄 테니까"; guide:
ArtSource/Poses/START_HERE.md). ArtSource/Poses/START_POSES.cmd opens ENFILADE_Poses_Mayo.blend with it; a .blend opened
by hand runs it from the "START HERE - Pose tools.py" text (Alt+P in the Scripting tab).

1. Pick a pose. 2. Pick a part (or a hand shape) and turn it with R. 3. Save the stance (it keys the strike, the breath
and the hold; frame 1, the relaxed stand, is left alone), then send it to the game (the FBX Unity reads, and the .blend
saved). The recipes, the rig helpers and the export come from make_victory_poses.py beside it.
"""
import importlib.util
import os
import sys

import bpy
from bpy.props import StringProperty

CATEGORY = "Pose"


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


def _load_recipes():
    name = "enf_victory_poses"
    if name in sys.modules:
        return sys.modules[name]
    if ROOT is None:
        raise RuntimeError("Open ENFILADE_Poses_Mayo.blend from inside the ENFILADE project first.")
    spec = importlib.util.spec_from_file_location(name, os.path.join(ROOT, "Tools", "Blender", "Poses", "make_victory_poses.py"))
    module = importlib.util.module_from_spec(spec)
    sys.modules[name] = module
    # No __pycache__ beside it: it would show up as a change in git.
    writes = sys.dont_write_bytecode
    sys.dont_write_bytecode = True
    try:
        spec.loader.exec_module(module)
    finally:
        sys.dont_write_bytecode = writes
    return module


poses = _load_recipes()

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
        arm.animation_data_create()
        arm.animation_data.action = action
        poses.set_fps()
        context.scene.frame_set(poses.STRIKE)
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
    bl_description = "이 손의 손가락을 곧게 편 다음 이 모양으로 접는다(손목과 팔은 그대로)"
    bl_options = {'REGISTER', 'UNDO'}
    side: StringProperty()
    shape: StringProperty()

    def execute(self, context):
        arm = rig_object()
        if arm is None:
            return {'CANCELLED'}
        r = poses.Rig(arm)
        r.rest(set(r.finger_bones(self.side)))
        r.hand_shape(self.side, self.shape)
        return {'FINISHED'}


class ENFPOSE_OT_key(bpy.types.Operator):
    bl_idname = "enf_pose.key"
    bl_label = "이 자세로 저장"
    bl_description = "지금 자세를 이 포즈로 저장한다(9·42·75 프레임에 키, 42는 가슴이 살짝 올라온 숨). 1프레임의 편히 선 자세는 그대로"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        arm = rig_object()
        action = current_action(arm)
        if action is None:
            self.report({'ERROR'}, "먼저 포즈를 고르세요.")
            return {'CANCELLED'}
        poses.key_pose(poses.Rig(arm))
        self.report({'INFO'}, f"{action.name}: 저장했어요. '게임에 보내기'를 누르면 Unity로 갑니다.")
        return {'FINISHED'}


class ENFPOSE_OT_send(bpy.types.Operator):
    bl_idname = "enf_pose.send"
    bl_label = "게임에 보내기"
    bl_description = "이 포즈를 Assets/_Project/Resources/Poses/<이름>.fbx로 내보내고 작업 파일을 저장한다. Unity로 돌아가면 알아서 다시 읽는다"
    bl_options = {'REGISTER'}

    def execute(self, context):
        arm = rig_object()
        action = current_action(arm)
        if action is None or not action.name.startswith(poses.PREFIX):
            self.report({'ERROR'}, "enf_pose_로 시작하는 포즈를 고르세요.")
            return {'CANCELLED'}
        mode = context.mode
        if mode != 'OBJECT':
            bpy.ops.object.mode_set(mode='OBJECT')
        path = poses.export(poses.Rig(arm), action)
        if mode == 'POSE':
            pose_mode(context, arm)
        context.scene.frame_set(poses.STRIKE)
        if bpy.data.filepath:
            bpy.ops.wm.save_mainfile()
        self.report({'INFO'}, f"보냈어요: {os.path.relpath(path, ROOT)}. Unity로 돌아가면 다시 읽어요.")
        return {'FINISHED'}


class ENFPOSE_OT_reset(bpy.types.Operator):
    bl_idname = "enf_pose.reset"
    bl_label = "처음 만든 대로 되돌리기"
    bl_description = "이 포즈에 손댄 것을 모두 버리고 Claude가 처음 만든 레시피 그대로 다시 만든다"
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
        arm.animation_data.action = rebuilt
        context.scene.frame_set(poses.STRIKE)
        return {'FINISHED'}


class ENFPOSE_OT_new(bpy.types.Operator):
    bl_idname = "enf_pose.new"
    bl_label = "새 포즈로 복사"
    bl_description = "지금 포즈를 복사해 새 이름의 포즈를 만든다. 게임에 보내면 결과 화면이 차례로 쓴다"
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
        arm = rig_object()
        action = current_action(arm)

        box = layout.box()
        box.label(text="1. 포즈 고르기", icon='ARMATURE_DATA')
        grid = box.grid_flow(columns=2, even_columns=True, align=True)
        for each in poses.pose_actions():
            op = grid.operator(ENFPOSE_OT_pick.bl_idname, text=NAMES.get(each.name, each.name), depress=each == action)
            op.action = each.name
        box.label(text=f"지금: {action.name if action else '없음'}  (프레임 {context.scene.frame_current})")

        box = layout.box()
        box.label(text="2. 부위 고르고 R로 돌리기", icon='BONE_DATA')
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

        box = layout.box()
        box.label(text="3. 저장하고 보내기", icon='EXPORT')
        col = box.column(align=True)
        col.scale_y = 1.3
        col.operator(ENFPOSE_OT_key.bl_idname, icon='KEYFRAME_HLT')
        col.operator(ENFPOSE_OT_send.bl_idname, icon='EXPORT')
        box.label(text="프레임을 옮기기 전에 '이 자세로 저장'!", icon='ERROR')
        row = box.row(align=True)
        row.operator(ENFPOSE_OT_reset.bl_idname, icon='LOOP_BACK')
        row.operator(ENFPOSE_OT_new.bl_idname, icon='DUPLICATE')
        layout.operator(ENFPOSE_OT_guide.bl_idname, icon='HELP')


CLASSES = (ENFPOSE_OT_pick, ENFPOSE_OT_part, ENFPOSE_OT_shape, ENFPOSE_OT_key, ENFPOSE_OT_send, ENFPOSE_OT_reset,
           ENFPOSE_OT_new, ENFPOSE_OT_guide, ENFPOSE_PT_panel)


def _start():
    """Once Blender's window is up: the sidebar open on the Pose tab, Mayo in pose mode on the MVP's struck frame."""
    try:
        arm = rig_object()
        window = bpy.context.window_manager.windows[0]
        for area in window.screen.areas:
            if area.type != 'VIEW_3D':
                continue
            space = area.spaces.active
            space.show_region_ui = True
            for region in area.regions:
                if region.type == 'UI':
                    try:
                        region.active_panel_category = CATEGORY
                    except (TypeError, AttributeError, RuntimeError):
                        pass
            if arm is not None:
                region = next((r for r in area.regions if r.type == 'WINDOW'), None)
                with bpy.context.temp_override(window=window, area=area, region=region):
                    pose_mode(bpy.context, arm)
                    if current_action(arm) is not None:
                        bpy.context.scene.frame_set(poses.STRIKE)
            break
    except Exception as error:  # A start-up nicety: never stop the tools for it.
        print("pose_tools start-up:", error)
    return None


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
    if not bpy.app.background and not bpy.app.timers.is_registered(_start):
        bpy.app.timers.register(_start, first_interval=.5)


def unregister():
    for cls in reversed(bpy.app.driver_namespace.pop("enf_pose_classes", [])):
        try:
            bpy.utils.unregister_class(cls)
        except RuntimeError:
            pass


if __name__ == "__main__":
    register()
