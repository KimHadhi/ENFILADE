"""ENFILADE knife workspace, Blender 4.5 LTS.

ArtSource/Knives/START_KNIFE_ANIMATION.cmd opens ArtSource/Knives/ENFILADE_Knife_Animation.blend with truefps_tools.py
and then this (the user, 2026-11-13: "클래스 별로 기본 근접 슬롯에 칼 들게 해 줘"; the manager: the knife's clips as a first
draft the user polishes in Blender, as the shove and the throws are). The class knife in the melee slot (MeleeKnife) is
played on the right arm from these clips, drafted on Kazusa's right arm (CTRL_hand.R, CTRL_elbow.R and the right
fingers) with the game's knives in her fist as the game seats them (Tables/ClassKnives: a forward grip, and a reverse one
for the reverse stab):
- 꺼내기: from the hip to the guard as the melee slot is picked (쥐기: the knife comes into the fist).
- 가드 (되풀이): the knife held ready between swings, a slow breath.
- 베기 / 되베기: the light attack, forehand then backhand, right to left and back (벰: the blade meets).
- 찌르기: the charged attack, a riposte and a beat chain's finisher, from the cocked fist (its first frame is where a charge
  draws the knife back) straight out.
- 역수 찌르기: a reverse-grip knife's (the Ghost's karambit, the Detente's shiv), driven down from above.
It shares Tools/Blender/OneClick/one_click.py with the other workspaces: opening shows the clip last worked on, Kazusa in
Pose mode with her right hand handle selected, auto-key on, the Dope Sheet; saving (Ctrl+S) sends every clip that changed
to Assets/_Project/Resources/Tables/<table>.csv: per half frame, the wrist and the elbow from the eyes in game metres, the
hand's run (wrist to middle knuckle) and its palm's facing in the head camera's axes as Unity's (x right, y up, z
forward), the fingers' and the thumb's closing, whether the knife is in the fist and the 벰 moment (KnifeSwing reads it,
ExecutionKnifeArm fits it to any character's right arm). Nothing here changes Blender's preferences.
"""
import importlib.util
import json
import math
import os
import sys
from pathlib import Path

import bpy
from bpy.props import StringProperty
from mathutils import Matrix, Vector

bl_info = {'name': 'ENFILADE Knives', 'author': 'ENFILADE', 'version': (1, 0, 0), 'blender': (4, 5, 0),
           'location': '3D View > Sidebar > 칼', 'category': 'Animation'}

CATEGORY = '칼'
# Kazusa in the game is this many times her size in this file (eyes 1.37 here, 1.6 m in game: PlayerBody's fit).
GAME_SCALE = 1.166
CAMERA = 'FPS_Head_Camera'
TABLES_DIR = Path('Assets/_Project/Resources/Tables')
ONE_CLICK = os.path.join('Tools', 'Blender', 'OneClick', 'one_click.py')
# Two samples a frame at 30 fps, so the game's curve follows the polish closely.
SAMPLES_PER_FRAME = 2
ARM_HAND, ARM_ELBOW = 'CTRL_hand.R', 'CTRL_elbow.R'
ARM_BONES = dict(shoulder='Right arm', elbow='Right elbow', wrist='Right wrist')
FINGERS = {
    'index': ('IndexFinger1_R', 'IndexFinger2_R', 'IndexFinger3_R'),
    'middle': ('MiddleFinger1_R', 'MiddleFinger2_R', 'MiddleFinger3_R'),
    'ring': ('RingFinger1_R', 'RingFinger2_R', 'RingFinger3_R'),
    'little': ('LittleFinger1_R', 'LittleFinger2_R', 'LittleFinger3_R'),
    'thumb': ('Thumb0_R', 'Thumb1_R', 'Thumb2_R'),
}
FOUR = ('index', 'middle', 'ring', 'little')
# A finger's whole curl shared over its joints, knuckle first (the game closes them so: ExecutionKnifeArm.Fingers).
CURL_SHARE = (.4, .35, .25)
# The thumb's two bends are this much of what the game calls the thumb's closing (90: right over the handle).
THUMB_SCALE = 1.3

# The timeline marks: the blade meeting what it cuts, and the knife coming into the fist (a draw).
HIT, GRAB = '벰', '쥐기'
MARKS = (HIT, GRAB)

# The clips. look: which preview knife is in the fist. marks: the frames of the moments (the user moves them on the
# timeline; each clip keeps its own). loop: played round and round. table: the game's file.
CLIPS = {
    'draw': dict(label='꺼내기', action='enf_knife_draw', table='KnifeMeleeDraw', end=12, marks={GRAB: 4}, look='forward'),
    'guard': dict(label='가드 (되풀이)', action='enf_knife_guard', table='KnifeMeleeGuard', end=37, marks={}, look='forward', loop=True),
    'slash': dict(label='베기', action='enf_knife_slash', table='KnifeMeleeSlash', end=10, marks={HIT: 3}, look='forward'),
    'backslash': dict(label='되베기', action='enf_knife_backslash', table='KnifeMeleeBackslash', end=10, marks={HIT: 3}, look='forward'),
    'stab': dict(label='찌르기', action='enf_knife_stab', table='KnifeMeleeStab', end=12, marks={HIT: 3}, look='forward'),
    'stab_reverse': dict(label='역수 찌르기', action='enf_knife_stab_reverse', table='KnifeMeleeStabReverse', end=12, marks={HIT: 3},
                         look='reverse'),
}
# The previews in the fist (never exported): the game's own models, seated as the game seats them.
LOOKS = {'forward': 'PREVIEW_Knife_Forward', 'reverse': 'PREVIEW_Knife_Reverse'}
MARKS_PROP = 'enf_knife_marks'
SCENE_CLIP = 'enf_knife_clip'
HEADER = ['id', 'cycle', 'wristX', 'wristY', 'wristZ', 'elbowX', 'elbowY', 'elbowZ', 'alongX', 'alongY', 'alongZ',
          'palmX', 'palmY', 'palmZ', 'curl', 'thumb', 'held', 'hit']


def project_root():
    """The ENFILADE checkout: from this file, else from the open .blend (ArtSource/Knives/...)."""
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


def _one_click():
    """The shared one-click module, loaded once and without a __pycache__ beside it (one there would show up in git)."""
    if 'enf_one_click' in sys.modules:
        return sys.modules['enf_one_click']
    root = project_root()
    if root is None:
        raise RuntimeError('ENFILADE 프로젝트 안의 ArtSource/Knives 파일을 여세요.')
    spec = importlib.util.spec_from_file_location('enf_one_click', str(root / ONE_CLICK))
    module = importlib.util.module_from_spec(spec)
    sys.modules['enf_one_click'] = module
    writes = sys.dont_write_bytecode
    sys.dont_write_bytecode = True
    try:
        spec.loader.exec_module(module)
    finally:
        sys.dont_write_bytecode = writes
    return module


oc = _one_click()


# ---- The rig, the view and the right hand ---------------------------------------------------------------------------

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


def hand_frame(rig):
    """The right hand as the game reads it (world): the wrist, its run (wrist to middle knuckle), its palm's facing and its
    length. A right hand's index and little fingers lie the other way round from a left's, so its cross is taken the other
    way (the throwables workspace's is the left's; ExecutionKnifeArm.PalmNow's in Unity's axes)."""
    wrist = head(rig, ARM_BONES['wrist'])
    index, middle, little = head(rig, FINGERS['index'][0]), head(rig, FINGERS['middle'][0]), head(rig, FINGERS['little'][0])
    along = (middle - wrist).normalized()
    palm = (index - little).cross(along)
    palm = (palm - along * palm.dot(along)).normalized()
    return wrist, along, palm, (middle - wrist).length


_rest_ways = {}


def rest_way(rig, name):
    """The way a finger bone runs along its finger, in the bone's own axes (toward the next joint; the last one on the way
    the one before it ran), as the rig stands at rest: this rig's finger bones point their tails across the finger."""
    key = (rig.name, name)
    if key not in _rest_ways:
        bones = rig.data.bones
        names = next(chain for chain in FINGERS.values() if name in chain)
        i = names.index(name)
        bone = bones[name]
        d = bones[names[i + 1]].head_local - bone.head_local if i + 1 < len(names) else bone.head_local - bones[names[i - 1]].head_local
        _rest_ways[key] = (bone.matrix_local.to_3x3().inverted() @ d).normalized()
    return _rest_ways[key]


def bone_way(rig, name):
    return (rig.matrix_world.to_3x3() @ rig.pose.bones[name].matrix.to_3x3() @ rest_way(rig, name)).normalized()


def _angle(a, b):
    return math.degrees(a.angle(b)) if a.length > 1e-9 and b.length > 1e-9 else 0.0


def curls(rig):
    """Each finger's curl (degrees: the hand's run to the first segment, then each joint) and the thumb's closing."""
    _, along, _, _ = hand_frame(rig)
    out = {}
    for finger in FOUR:
        s = [bone_way(rig, n) for n in FINGERS[finger]]
        out[finger] = _angle(along, s[0]) + _angle(s[0], s[1]) + _angle(s[1], s[2])
    t = [bone_way(rig, n) for n in FINGERS['thumb']]
    out['thumb'] = (_angle(t[0], t[1]) + _angle(t[1], t[2])) * THUMB_SCALE
    return out


def turn_bone(rig, name, matrix_world_turn):
    """Turns a pose bone about its own head by a world-space turn (3x3)."""
    pb = rig.pose.bones[name]
    inverse = rig.matrix_world.to_3x3().inverted()
    turn = (inverse @ matrix_world_turn @ rig.matrix_world.to_3x3()).to_4x4()
    at = pb.matrix.translation.copy()
    pb.matrix = Matrix.Translation(at) @ turn @ Matrix.Translation(-at) @ pb.matrix


def bend_finger(rig, finger, degrees):
    """Closes (or opens, below 0) a finger by that much in all, shared over its joints as the game shares a curl."""
    _, along, palm, _ = hand_frame(rig)
    shares = (0.0, .5, .5) if finger == 'thumb' else CURL_SHARE
    for name, share in zip(FINGERS[finger], shares):
        if share <= 0:
            continue
        bpy.context.view_layer.update()
        axis = along.cross(palm) if finger != 'thumb' else bone_way(rig, name).cross(palm)
        if axis.length < 1e-6:
            continue
        turn_bone(rig, name, Matrix.Rotation(math.radians(degrees * share), 3, axis.normalized()))


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


# ---- Clips: which one is shown, its marks ---------------------------------------------------------------------------

def current_clip(scene):
    key = scene.get(SCENE_CLIP, 'guard')
    return key if key in CLIPS else 'guard'


def marks_of(action, key):
    """A clip's marks: as the user last moved them (kept on its action), else its draft's."""
    stored = action.get(MARKS_PROP) if action is not None else None
    if stored:
        try:
            return {name: int(frame) for name, frame in json.loads(stored).items()}
        except (ValueError, TypeError):
            pass
    return dict(CLIPS[key]['marks'])


def keep_marks(scene, key):
    """The timeline's marks as the clip's own (on its action), so they go with it into its fingerprint."""
    action = bpy.data.actions.get(CLIPS[key]['action'])
    if action is None:
        return
    marks = {m.name: m.frame for m in scene.timeline_markers if m.name in MARKS}
    text = json.dumps(marks, sort_keys=True)
    if action.get(MARKS_PROP) != text:
        action[MARKS_PROP] = text


def show_marks(scene, marks):
    for marker in [m for m in scene.timeline_markers if m.name in MARKS]:
        scene.timeline_markers.remove(marker)
    for name, frame in marks.items():
        scene.timeline_markers.new(name, frame=frame)


def show_look(scene, key):
    look = CLIPS[key]['look']
    for name, obj_name in LOOKS.items():
        obj = bpy.data.objects.get(obj_name)
        if obj is not None:
            hidden = name != look
            if obj.hide_viewport != hidden:
                obj.hide_viewport = hidden
            if obj.hide_render != hidden:
                obj.hide_render = hidden
            for child in obj.children_recursive:
                child.hide_viewport = hidden
                child.hide_render = hidden


def select_clip(context, key):
    """Shows one clip: its action on the rig (an editable copy of its draft), its frames, its marks and its knife."""
    scene = context.scene
    rig = find_rig(scene)
    if rig is None:
        return None
    shown = current_clip(scene)
    if shown in CLIPS and shown != key:
        keep_marks(scene, shown)
    clip = CLIPS[key]
    action, draft, _fresh = oc.item_action(context, rig, clip['action'], clip['end'])
    oc.play(rig, action, draft)
    scene[SCENE_CLIP] = key
    scene.frame_start, scene.frame_end = 1, clip['end']
    show_marks(scene, marks_of(action, key))
    show_look(scene, key)
    if not (1 <= scene.frame_current <= clip['end']):
        scene.frame_set(1)
    return action


# ---- Sending to the game ---------------------------------------------------------------------------------------------

def fingerprint(action, key):
    return oc.fingerprint([action], extra=[CLIPS[key]['end'], marks_of(action, key)])


def table_path(key):
    root = project_root()
    if root is None:
        raise RuntimeError('ENFILADE 프로젝트를 찾지 못했습니다. ArtSource/Knives의 파일을 여세요.')
    return root / TABLES_DIR / (CLIPS[key]['table'] + '.csv')


def clip_rows(context, key, marks):
    """The right arm over the clip's frames as the game's clips have it (KnifeSwing)."""
    scene = context.scene
    rig = find_rig(scene)
    fps = scene.render.fps / scene.render.fps_base
    start, end = 1, CLIPS[key]['end']
    grab = marks.get(GRAB, start)
    hit = marks.get(HIT)
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
        rows.append(dict(cycle=(at - start) / fps, wrist=to_view(wrist, basis, True), elbow=to_view(elbow, basis, True),
                         along=to_view(along, basis), palm=to_view(palm, basis),
                         curl=sum(c[f] for f in FOUR) / 4.0, thumb=c['thumb'],
                         held=at >= grab - 1e-6, hit=hit is not None and abs(at - hit) < 1e-6))
    return rows


def table_text(context, key, marks):
    clip = CLIPS[key]
    loop = '되풀이 클립: 끝 줄이 첫 줄과 같아야 이어진다. ' if clip.get('loop') else ''
    note = ('"#설명 (블렌더 칼 작업실 ArtSource/Knives의 ' + clip['label'] + ' 클립을 저장(Ctrl+S)하면 쓰는 오른팔. ' + loop +
            '손으로 고치지 말고 블렌더에서 고친다. 어느 칼이 어느 클립을 쓰는지는 ClassKnives.csv. MeleeKnife가 근접 칸의 칼로 틀고 '
            'ExecutionKnifeArm이 어느 캐릭터 오른팔에든 맞춘다)"')
    cells = ['클립의 초', '눈에서 손목까지 오른쪽(m)', '위(m)', '앞(m)', '눈에서 팔꿈치까지 오른쪽(m). 팔꿈치가 이쪽으로 굽는다', '위(m)', '앞(m)',
             '손이 뻗은 쪽(손목→가운데 손가락 뿌리) 오른쪽', '위', '앞', '손바닥이 향한 쪽 오른쪽', '위', '앞',
             '네 손가락이 굽은 정도의 평균(°)', '엄지가 접힌 정도(90이면 손잡이 위로 다 덮음)', '1이면 칼이 손에 있다(쥐기부터)',
             '1이면 이 순간에 칼날이 닿는다(벰)']
    cells = [c.replace(',', ' ·') for c in cells]
    lines = ['﻿' + ','.join(HEADER), note + ',' + ','.join(cells)]
    for n, row in enumerate(clip_rows(context, key, marks)):
        values = [f'k{n:03d}', f'{row["cycle"]:.4f}']
        values += [f'{v:.4f}' for v in (*row['wrist'], *row['elbow'], *row['along'], *row['palm'])]
        values += [f'{row["curl"]:.1f}', f'{row["thumb"]:.1f}', '1' if row['held'] else '0', '1' if row['hit'] else '0']
        lines.append(','.join(values))
    return '\n'.join(lines) + '\n'


def send_clip(context, key):
    """One clip to the game: its table written (a bare .meta first for a new one), and marked as sent as it is now."""
    scene = context.scene
    rig = find_rig(scene)
    clip = CLIPS[key]
    shown = current_clip(scene)
    frame = scene.frame_current
    action = select_clip(context, key)
    marks = marks_of(action, key)
    path = table_path(key)
    try:
        oc.write_meta(path, oc.TEXT_META)
        oc.write_text(path, table_text(context, key, marks))
    finally:
        if shown != key:
            select_clip(context, shown)
        scene.frame_set(frame)
    oc.mark_sent(action, fingerprint(action, key))
    return clip['label']


def send_changed(context, force=False):
    """Every clip that changed since it was last sent (or whose table is missing); the clip shown stays shown."""
    scene = context.scene
    rig = find_rig(scene)
    if rig is None:
        return None
    keep_marks(scene, current_clip(scene))
    sent, failed = [], []
    for key, clip in CLIPS.items():
        action = bpy.data.actions.get(clip['action'])
        if action is None:
            continue
        if not (force or oc.needs_sending(action, fingerprint(action, key), table_path(key).exists())):
            continue
        try:
            sent.append(send_clip(context, key))
        except Exception as error:
            failed.append(f"{clip['label']}: {error}")
    return sent, failed


def is_knife_file(scene):
    return find_rig(scene) is not None and any(bpy.data.actions.get(c['action'] + '_draft') for c in CLIPS.values())


def prepare(context):
    """Every clip ready to key into (an editable copy of its draft), and one never sent from here counted as what the game
    has when its table is there (the build sent the drafts)."""
    rig = find_rig(context.scene)
    for key, clip in CLIPS.items():
        if bpy.data.actions.get(clip['action'] + '_draft') is None and bpy.data.actions.get(clip['action']) is None:
            continue
        action, _draft, _fresh = oc.item_action(context, rig, clip['action'], clip['end'])
        if oc.SENT not in action and table_path(key).exists():
            oc.mark_sent(action, fingerprint(action, key))


def arrange(context):
    """The workspace as the work needs it: the clip last worked on, Kazusa in Pose mode with her right hand handle
    selected, auto-key on, the Dope Sheet, the 칼 tab."""
    scene = context.scene
    rig = find_rig(scene)
    if rig is None:
        return True
    prepare(context)
    if not oc.open_workspace(context):
        return False
    key = current_clip(scene)
    select_clip(context, key)
    oc.auto_key(scene, True)
    for window in context.window_manager.windows:
        main = oc.main_view(window)
        oc.show_tab(main, CATEGORY)
        oc.dope_sheet(window)
        if main is not None:
            oc.select(context, window, main, [rig], rig, pose=True, bones=(ARM_HAND,))
    oc.tell(f"칼 「{CLIPS[key]['label']}」 · 오른손을 잡아 고치고 Ctrl+S 하면 게임에 들어갑니다")
    return True


# ---- Panel ------------------------------------------------------------------------------------------------------------

class ENFKNIFE_OT_pick(bpy.types.Operator):
    bl_idname = 'enf_knife.pick'
    bl_label = '칼 클립 고르기'
    bl_options = {'REGISTER', 'UNDO'}
    clip: StringProperty()

    def execute(self, context):
        if self.clip not in CLIPS:
            return {'CANCELLED'}
        select_clip(context, self.clip)
        return {'FINISHED'}


class ENFKNIFE_OT_part(bpy.types.Operator):
    bl_idname = 'enf_knife.part'
    bl_label = '부위 고르기'
    bl_options = {'REGISTER', 'UNDO'}
    bone: StringProperty()

    def execute(self, context):
        rig = find_rig(context.scene)
        if rig is None or self.bone not in rig.pose.bones:
            return {'CANCELLED'}
        if context.object != rig or context.mode != 'POSE':
            for obj in context.view_layer.objects:
                obj.select_set(obj == rig)
            context.view_layer.objects.active = rig
            bpy.ops.object.mode_set(mode='POSE')
        for bone in rig.data.bones:
            bone.select = bone.select_head = bone.select_tail = bone.name == self.bone
        rig.data.bones.active = rig.data.bones[self.bone]
        return {'FINISHED'}


class ENFKNIFE_OT_send(bpy.types.Operator):
    bl_idname = 'enf_knife.send'
    bl_label = '지금 게임에 보내기'
    bl_description = '바뀐 클립을 저장하지 않고 지금 보낸다(저장하면 저절로 보낸다)'

    def execute(self, context):
        result = send_changed(context, force=False)
        if result:
            oc.send_report(context.scene, *result)
        return {'FINISHED'}


class ENFKNIFE_OT_revert(bpy.types.Operator):
    bl_idname = 'enf_knife.revert'
    bl_label = '처음 시안으로 되돌리기'
    bl_description = '보이는 클립을 Claude가 처음 만든 시안으로 되돌린다(저장하면 게임에도 들어간다)'
    bl_options = {'REGISTER', 'UNDO'}

    def invoke(self, context, event):
        return context.window_manager.invoke_confirm(self, event)

    def execute(self, context):
        key = current_clip(context.scene)
        oc.revert(CLIPS[key]['action'])
        action = bpy.data.actions.get(CLIPS[key]['action'])
        if action is not None and MARKS_PROP in action:
            del action[MARKS_PROP]
        select_clip(context, key)
        return {'FINISHED'}


class ENFKNIFE_PT_panel(bpy.types.Panel):
    bl_label = 'ENFILADE 근접 칼 동작'
    bl_idname = 'ENFKNIFE_PT_panel'
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = CATEGORY

    def draw(self, context):
        scene = context.scene
        layout = self.layout
        key = current_clip(scene)
        fps = scene.render.fps / scene.render.fps_base
        box = layout.box()
        box.label(text='1. 클립 고르기')
        col = box.column(align=True)
        for k, v in CLIPS.items():
            col.operator('enf_knife.pick', text=v['label'], depress=k == key).clip = k
        box.label(text=f'끝: {scene.frame_end}프레임 ({(scene.frame_end - scene.frame_start) / fps:.3f}초)')
        for marker in sorted((m for m in scene.timeline_markers if m.name in MARKS), key=lambda m: m.frame):
            box.label(text=f'{marker.name}: {marker.frame}프레임 ({(marker.frame - scene.frame_start) / fps:.3f}초)')
        box.label(text='표시(벰·쥐기)는 타임라인에서 클릭한 뒤 G로 옮긴다')
        if CLIPS[key].get('loop'):
            box.label(text='되풀이: 끝 프레임이 처음과 같아야 이어진다')

        box = layout.box()
        box.label(text='2. 오른손·팔꿈치를 잡아 G로 옮기고 R로 돌리기')
        row = box.row(align=True)
        row.operator('enf_knife.part', text='오른손').bone = ARM_HAND
        row.operator('enf_knife.part', text='팔꿈치 방향').bone = ARM_ELBOW
        box.label(text='자동 키가 켜져 있어 움직이면 그 프레임에 키가 들어간다')
        box.operator('enf_knife.revert', icon='LOOP_BACK')

        box = layout.box()
        box.label(text='3. Ctrl+S = 바뀐 클립만 게임에 보냄')
        box.operator('enf_knife.send', icon='EXPORT')
        note = scene.get(oc.NOTE)
        if note:
            box.label(text=str(note))
        box.label(text=str(TABLES_DIR / (CLIPS[key]['table'] + '.csv')).replace('\\', '/'))


CLASSES = (ENFKNIFE_OT_pick, ENFKNIFE_OT_part, ENFKNIFE_OT_send, ENFKNIFE_OT_revert, ENFKNIFE_PT_panel)


def register():
    for cls in CLASSES:
        if hasattr(bpy.types, cls.__name__):
            try:
                bpy.utils.unregister_class(getattr(bpy.types, cls.__name__))
            except RuntimeError:
                pass
        bpy.utils.register_class(cls)
    oc.register_workspace('칼', is_knife_file, arrange, send_changed)


def unregister():
    oc.unregister_workspace('칼')
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)


if __name__ == '__main__':
    register()
