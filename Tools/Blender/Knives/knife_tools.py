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
Each class knife's own clips from its workshop addon follow, a set per knife (the user, 2026-11-13: "애드온 따오면서 그
모션도 같이 있었을 거 아니야"): knife_addon_port.py reads the addons' viewmodels into addon_clips.json beside this file and
build_knife_workspace.py keys them as drafts on Kazusa (가드, 꺼내기, 베기, 되베기, 찌르기 for the 흑상, 카람빗, 버터플라이, 가위
쉬브 and 프라이멀 나이프), the knife itself in her fist; ClassKnives.csv names each class's tables.
It shares Tools/Blender/OneClick/one_click.py with the other workspaces: opening shows the clip last worked on, Kazusa in
Pose mode with her right hand handle selected, auto-key on, the Dope Sheet; saving (Ctrl+S) sends every clip that changed
to Assets/_Project/Data/Resources/Tables/<table>.csv: per half frame, the wrist and the elbow from the eyes in game metres, the
hand's run (wrist to middle knuckle) and its palm's facing in the head camera's axes as Unity's (x right, y up, z
forward), the fingers' and the thumb's closing, whether the knife is in the fist and the 벰 moment (KnifeSwing reads it,
ExecutionKnifeArm fits it to any character's right arm). Nothing here changes Blender's preferences.
Where a knife sits in the fist is moved here too (the user, 2026-11-14: "블렌더에서 하면 내가 수정할 때 바로 직관적으로 옮길
수 있으니까"): 칼 옮기기 picks the knife in her fist (a knife's own clips show that knife), G and R move it, and saving writes
its seat in the melee slot (ClassKnives.csv meleeHeld*, meleeBlade*, meleeEdge*: the handle's middle in hand lengths from
the wrist, the blade's and the edge's ways in the hand's axes, x away from the thumb, y out of the palm, z the hand's run)
into every row that carries that knife, only when it moved. Numbers changed in the table outside Blender move the knife
here when the file is opened (the numbers and the knife stay one), so either way of changing it works.
The M9 bayonet and the machete, whose addon has no first-person clips, get clips of their own made here (the user,
2026-11-14: "M9 대검이랑 마체테 내가 뽑는 동작 만들어 줄 테니까 … 나는 블렌더에서 애니메이션만 만들면 되게끔 해 줘"): on
opening, each starts as a copy of the draft clip the game plays for them now, with that knife in her fist; it stays out
of the game until it is changed, and saving a changed one writes its table (KnifeMelee<knife><clip>.csv) and names it in
ClassKnives.csv's column for that clip in every row with that knife (the draw then draws it in executions too). A clip
is as long as the timeline's end set for it, or longer when a key lies past that end.
"""
import csv
import io
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
TABLES_DIR = Path('Assets/_Project/Data/Resources/Tables')
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


# ---- The knives' own clips -------------------------------------------------------------------------------------------
# Ported from each knife's workshop addon by knife_addon_port.py (addon_clips.json beside this file): a set per knife, played
# by the classes whose ClassKnives.csv row names its tables, its preview in the fist that knife in its row's fighting grip.
KNIVES = {'Heishang': '흑상', 'Karambit': '카람빗', 'Butterfly': '버터플라이', 'ScissorsShiv': '가위 쉬브', 'PrimalKnife': '프라이멀 나이프'}
PORTED_CLIPS = {'guard': ('가드 (되풀이)', 'Guard'), 'draw': ('꺼내기', 'Draw'), 'slash': ('베기', 'Slash'),
                'backslash': ('되베기', 'Backslash'), 'stab': ('찌르기', 'Stab')}
PORTED_FILE = 'addon_clips.json'


def ported():
    """The ported clips (knife -> its addon's viewmodel and its clips' frames and marks), or none when the file is missing."""
    root = project_root()
    path = root / 'Tools' / 'Blender' / 'Knives' / PORTED_FILE if root is not None else None
    try:
        return json.loads(path.read_text(encoding='utf-8')) if path is not None else {}
    except (OSError, ValueError):
        return {}


def _add_ported():
    for knife, entry in ported().items():
        LOOKS[knife] = 'PREVIEW_Knife_' + knife
        for clip, c in entry.get('clips', {}).items():
            label, table = PORTED_CLIPS[clip]
            CLIPS[f'{knife}.{clip}'] = dict(label=f'{KNIVES.get(knife, knife)} {label}', action=f'enf_knife_{knife.lower()}_{clip}',
                                            table=f'KnifeMelee{knife}{table}', end=int(c['end']), marks=dict(c.get('marks', {})),
                                            look=knife, loop=clip == 'guard', knife=knife,
                                            source=f"{entry.get('source', '')} {c.get('source', '')}".strip())


_add_ported()

# Knives whose addon has no first-person clips (the BF4 pack's): their clips are made here, each starting as a copy of the
# draft clip it stands in for (what the game plays for that knife until then), named in ClassKnives.csv's column for it
# once it is changed and sent.
MADE_HERE = {'M9Bayonet': 'M9 총검', 'Machete': '마체테'}
MADE_COLUMNS = {'draw': 'drawClip', 'guard': 'guardClip', 'slash': 'slashClip', 'backslash': 'backslashClip', 'stab': 'stabClip'}


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
        keep_end(scene, shown)
    clip = CLIPS[key]
    action, draft, _fresh = oc.item_action(context, rig, clip['action'], clip['end'])
    oc.play(rig, action, draft)
    scene[SCENE_CLIP] = key
    end = end_of(action, key)
    scene.frame_start, scene.frame_end = 1, end
    show_marks(scene, marks_of(action, key))
    show_look(scene, key)
    if not (1 <= scene.frame_current <= end):
        scene.frame_set(1)
    return action


# ---- Sending to the game ---------------------------------------------------------------------------------------------

def fingerprint(action, key):
    return oc.fingerprint([action], extra=[end_of(action, key), marks_of(action, key)])


def table_file(root, name, new_in='Clips'):
    """The game's table of this name under the project root. Since 0.9.0 the tables are filed in sub-folders of
    Data/Resources/Tables by subject and found by file name alone, as the game finds them (GameTables); a table that isn't
    there yet is made in new_in."""
    base = Path(root) / TABLES_DIR
    for found in sorted(base.rglob(name + '.csv')):
        return found
    return base / new_in / (name + '.csv')


def table_path(key):
    root = project_root()
    if root is None:
        raise RuntimeError('ENFILADE 프로젝트를 찾지 못했습니다. ArtSource/Knives의 파일을 여세요.')
    return table_file(root, CLIPS[key]['table'])


def table_label(key):
    """The clip's table from the project root, for the panel (never raises)."""
    name = CLIPS[key]['table']
    root = project_root()
    if root is None:
        return str(TABLES_DIR / 'Clips' / (name + '.csv')).replace('\\', '/')
    return str(table_file(root, name).relative_to(root)).replace('\\', '/')


def clip_rows(context, key, marks):
    """The right arm over the clip's frames as the game's clips have it (KnifeSwing)."""
    scene = context.scene
    rig = find_rig(scene)
    fps = scene.render.fps / scene.render.fps_base
    start, end = 1, end_of(bpy.data.actions.get(CLIPS[key]['action']), key)
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
    source = f"워크숍 애드온 {clip['source']}에서 옮긴 모션. " if clip.get('source') else ''
    note = ('"#설명 (블렌더 칼 작업실 ArtSource/Knives의 ' + clip['label'] + ' 클립을 저장(Ctrl+S)하면 쓰는 오른팔. ' + source + loop +
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
    keep_end(scene, current_clip(scene))
    sent, failed = [], []
    for key, clip in CLIPS.items():
        action = bpy.data.actions.get(clip['action'])
        if action is None:
            continue
        if not (force or oc.needs_sending(action, fingerprint(action, key), table_path(key).exists())):
            continue
        try:
            sent.append(send_clip(context, key))
            if clip.get('made'):
                # The game plays it from now on: its column names its table in every row with that knife.
                write_cells(table_file(project_root(), CLASS_KNIVES, 'Melee'), clip['knife'], {clip['column']: clip['table']})
        except Exception as error:
            failed.append(f"{clip['label']}: {error}")
    try:
        sent.extend(send_seats(context))
    except Exception as error:
        failed.append(f'쥐는 자리: {error}')
    return sent, failed


def is_knife_file(scene):
    return find_rig(scene) is not None and any(bpy.data.actions.get(c['action'] + '_draft') for c in CLIPS.values())


def prepare(context):
    """Every clip ready to key into (an editable copy of its draft), and one never sent from here counted as what the game
    has when its table is there (the build sent the drafts)."""
    rig = find_rig(context.scene)
    ready_made_here(context)
    for key, clip in CLIPS.items():
        if bpy.data.actions.get(clip['action'] + '_draft') is None and bpy.data.actions.get(clip['action']) is None:
            continue
        action, _draft, _fresh = oc.item_action(context, rig, clip['action'], clip['end'])
        if oc.SENT not in action and table_path(key).exists():
            oc.mark_sent(action, fingerprint(action, key))
        elif oc.SENT not in action and clip.get('made'):
            # Made here and never sent: the game plays the draft it stands in for until this one changes.
            oc.mark_withheld(action, fingerprint(action, key))
    match_seats(context)


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


# ---- The knife in the fist: moved here, kept as its seat numbers ------------------------------------------------------

CLASS_KNIVES = 'ClassKnives'
# A knife's seat in the melee slot as ClassKnives.csv holds it, in this order, and as the knife was last matched to it.
SEAT_COLUMNS = ('meleeHeldSide', 'meleeHeldPalm', 'meleeHeldAlong', 'meleeBladeX', 'meleeBladeY', 'meleeBladeZ',
                'meleeEdgeX', 'meleeEdgeY', 'meleeEdgeZ')
SEAT_PROP = 'enf_knife_seat'
# ClassKnives' grips' blade ways when a row gives none (ClassKnives.ForwardBlade, ReverseBlade).
FORWARD_BLADE, REVERSE_BLADE = (-.8, 0.0, .6), (.93, 0.0, .36)
# The seat as measured, kept to six decimals (the user's placement is never rounded away: no-lossy-shortcuts); a knife
# counts as moved past this (Blender's own float sums stay well under it; the execution workspace's grip writer uses the same).
SEAT_DECIMALS, SEAT_STEP = 6, 1e-5


def knife_holder(knife):
    """The preview of a knife's own clips in her fist (the build's PREVIEW_Knife_<knife> empty, parent of its model)."""
    obj = bpy.data.objects.get(LOOKS.get(knife, ''))
    return obj if obj is not None and obj.type == 'EMPTY' else None


def look_knife(key):
    """The knife a clip shows in the fist when it is one of the knives' own (a draft's preview is no knife's)."""
    look = CLIPS[key]['look']
    return look if look in KNIVES else None


def hand_axes(rig):
    """The right hand as the game seats a knife in it (world): its wrist, its axes as the columns of a matrix (x away from
    the thumb: the palm's facing across the hand's run, y out of the palm, z the run) and its length."""
    wrist, along, palm, length = hand_frame(rig)
    return wrist, Matrix((palm.cross(along).normalized(), palm, along)).transposed(), length


def parent_frame(obj):
    """An object's parent frame as Blender builds it (a bone parent's: that bone's tail), from the pose as it is now. Worked
    out here rather than read from matrix_world: a knife whose clip is not shown is hidden, and Blender does not evaluate a
    hidden object, so its matrix_world is not where it is."""
    parent = obj.parent
    if parent is None:
        return Matrix.Identity(4)
    if obj.parent_type == 'BONE' and parent.type == 'ARMATURE' and obj.parent_bone in parent.pose.bones:
        bone = parent.pose.bones[obj.parent_bone]
        return world_of(parent) @ bone.matrix @ Matrix.Translation((0.0, bone.bone.length, 0.0))
    return world_of(parent)


def world_of(obj):
    """Where an object is in the world, from its own transform and its parents' (see parent_frame)."""
    if obj.parent is None:
        return obj.matrix_basis.copy()
    return parent_frame(obj) @ obj.matrix_parent_inverse @ obj.matrix_basis


def in_holder(holder, obj):
    """An object under a knife's holder, in the holder's frame (the knife model's own)."""
    m = Matrix.Identity(4)
    while obj is not None and obj != holder:
        m = obj.matrix_parent_inverse @ obj.matrix_basis @ m
        obj = obj.parent
    return m


def model_ways(holder):
    """The knife's own ways in its holder's frame (its model's: the holder carries the model's origin and turn): the blade
    from its Pommel marker through the origin, the edge toward its Edge marker, as the game takes them (ExecutionKnifeArm.Wear)."""
    marks = {o.name.split('.')[0]: in_holder(holder, o).translation for o in holder.children_recursive if o.type == 'EMPTY'}
    pommel = marks.get('Pommel')
    blade = -pommel if pommel is not None and pommel.length > 1e-9 else Vector((0.0, -1.0, 0.0))
    blade.normalize()
    edge = marks.get('Edge', Vector((0.0, 0.0, -1.0)))
    edge = edge - blade * edge.dot(blade)
    if edge.length < 1e-9:
        edge = blade.orthogonal()
    return blade, edge.normalized()


def seat_now(rig, holder):
    """Where the knife sits in the fist now, as the table's nine numbers (SEAT_DECIMALS)."""
    wrist, axes, length = hand_axes(rig)
    place, turn, _size = world_of(holder).decompose()
    blade_model, edge_model = model_ways(holder)
    turn = turn.to_matrix()
    blade = (turn @ blade_model).normalized()
    edge = turn @ edge_model
    edge = (edge - blade * edge.dot(blade)).normalized()
    back = axes.transposed()
    held = back @ (place - wrist) / max(length, 1e-6)
    return [round(float(v), SEAT_DECIMALS) for v in (*held, *(back @ blade), *(back @ edge))]


def seat_holder(rig, holder, seat):
    """The knife put where nine seat numbers say, as the game seats it (and as the build placed it)."""
    wrist, axes, length = hand_axes(rig)
    held, blade_axes, edge_axes = Vector(seat[0:3]), Vector(seat[3:6]), Vector(seat[6:9])
    blade = (axes @ blade_axes).normalized()
    edge = axes @ edge_axes
    edge = edge - blade * edge.dot(blade)
    edge = edge.normalized() if edge.length > 1e-9 else blade.orthogonal().normalized()
    blade_model, edge_model = model_ways(holder)
    want = Matrix((blade, edge, blade.cross(edge))).transposed()
    have = Matrix((blade_model, edge_model, blade_model.cross(edge_model))).transposed()
    size = world_of(holder).to_scale()
    world = (Matrix.Translation(wrist + axes @ (held * length)) @ (want @ have.transposed()).to_4x4()
             @ Matrix.Diagonal((size.x, size.y, size.z, 1.0)))
    holder.matrix_basis = (parent_frame(holder) @ holder.matrix_parent_inverse).inverted() @ world


def table_seats(root):
    """Each knife's seat in the melee slot as ClassKnives.csv holds it now (its first row naming that model), or None for
    a knife whose row leaves a number empty (it keeps the grip's default there)."""
    out = {}
    with open(table_file(root, CLASS_KNIVES, 'Melee'), encoding='utf-8-sig', newline='') as f:
        for row in csv.DictReader(f):
            if (row.get('id') or '').startswith('#') or not row.get('model'):
                continue
            name = row['model'].strip().split('/')[-1]
            if name in out:
                continue
            out[name] = canonical(_melee_seat(row))
    return out


def _cell(row, name, fallback):
    try:
        return float(row.get(name) or '')
    except ValueError:
        return fallback


def _melee_seat(row):
    """A row's melee seat as the game reads it (ClassKnives.Read): each meleeHeld cell else the execution's held one, the
    blade's cells else the grip's way (forward or reverse), the edge's else the fingers' way; a way of no length is the
    fallback whole."""
    held = [_cell(row, n, d) for n, d in (('heldSide', 0.0), ('heldPalm', .3), ('heldAlong', .55))]
    held = [_cell(row, n, h) for n, h in zip(('meleeHeldSide', 'meleeHeldPalm', 'meleeHeldAlong'), held)]
    reverse = (row.get('meleeGrip') or '').strip().lower() == 'reverse'
    ways = []
    for prefix, fallback in (('meleeBlade', REVERSE_BLADE if reverse else FORWARD_BLADE), ('meleeEdge', (0.0, 0.0, 1.0))):
        way = [_cell(row, prefix + axis, f) for axis, f in zip('XYZ', fallback)]
        ways += way if sum(v * v for v in way) > 1e-8 else list(fallback)
    return held + ways


def canonical(seat):
    """Nine seat numbers as the game takes them (ClassKnives.Way, ExecutionKnifeArm.Seat) and as a measured knife gives
    them back: the blade a unit way, the edge a unit way square to it; SEAT_DECIMALS. Numbers typed with an edge not square
    to the blade then still count as where the knife is, not as a move."""
    held, blade, edge = Vector(seat[0:3]), Vector(seat[3:6]), Vector(seat[6:9])
    blade = blade.normalized() if blade.length > 1e-9 else Vector((0.0, 0.0, 1.0))
    edge = edge - blade * edge.dot(blade)
    edge = edge.normalized() if edge.length > 1e-9 else blade.orthogonal().normalized()
    return [round(float(v), SEAT_DECIMALS) for v in (*held, *blade, *edge)]


def write_cells(path, model, cells):
    """These cells written into every row of a table whose model is this knife (its file name), every other line kept as
    it was, byte for byte; the number of rows changed."""
    raw = path.read_bytes()
    bom = '\ufeff' if raw.startswith(b'\xef\xbb\xbf') else ''
    text = raw.decode('utf-8-sig')
    newline = '\r\n' if '\r\n' in text else '\n'
    lines = text.split(newline)
    header = next(csv.reader([lines[0]]))
    col = {name: i for i, name in enumerate(header)}
    missing = [name for name in cells if name not in col]
    if missing:
        raise RuntimeError('ClassKnives.csv에 칸이 없습니다: ' + ', '.join(missing))
    changed = 0
    for n in range(1, len(lines)):
        if not lines[n] or lines[n].lstrip('"').startswith('#'):
            continue
        row = next(csv.reader([lines[n]]))
        if len(row) <= col['model'] or row[col['model']].strip().split('/')[-1] != model:
            continue
        before = list(row)
        row += [''] * (len(header) - len(row))
        for name, value in cells.items():
            row[col[name]] = value
        if row[:len(before)] == before and len(row) == len(before):
            continue
        out = io.StringIO()
        csv.writer(out, lineterminator='').writerow(row)
        lines[n] = out.getvalue()
        changed += 1
    if changed:
        oc.write_text(path, bom + newline.join(lines))
    return changed


def _differs(a, b):
    return a is None or b is None or len(a) != len(b) or any(abs(x - y) > SEAT_STEP for x, y in zip(a, b))


def match_seats(context):
    """On opening: each knife where the table's numbers put it (they win: a move saved here wrote them, so only numbers
    changed outside Blender differ), and those numbers kept as what the knife was last matched to."""
    root = project_root()
    rig = find_rig(context.scene)
    if root is None or rig is None:
        return
    context.view_layer.update()
    seats = table_seats(root)
    for knife in KNIVES:
        holder = knife_holder(knife)
        if holder is None:
            continue
        # The knife is picked and moved by its holder (the build made it a small, unpickable mark).
        holder.hide_select = False
        holder.empty_display_type = 'ARROWS'
        holder.empty_display_size = .05
        table = seats.get(knife)
        if table is None:
            continue
        if _differs(seat_now(rig, holder), table):
            seat_holder(rig, holder, table)
            context.view_layer.update()
        holder[SEAT_PROP] = table


def send_seats(context):
    """On saving: every knife moved in her fist since it was last matched, written into its rows (the labels sent)."""
    root = project_root()
    rig = find_rig(context.scene)
    if root is None or rig is None:
        return []
    context.view_layer.update()
    sent = []
    path = table_file(root, CLASS_KNIVES, 'Melee')
    for knife in KNIVES:
        holder = knife_holder(knife)
        if holder is None:
            continue
        now = seat_now(rig, holder)
        stored = list(holder[SEAT_PROP]) if SEAT_PROP in holder else None
        if not _differs(now, stored):
            continue
        write_cells(path, knife, {c: f'{v:.{SEAT_DECIMALS}f}' for c, v in zip(SEAT_COLUMNS, now)})
        holder[SEAT_PROP] = now
        sent.append(f'{KNIVES[knife]} 쥐는 자리')
    return sent


# ---- The knives whose clips are made here --------------------------------------------------------------------------------

PREVIEW_COLLECTION = '07 Knife preview - never exported'
KNIFE_MODEL = 'Assets/_Project/Art/Models/Addons/Knives/{0}/{0}.fbx'
# A clip's last frame when the user set the timeline's end for it (kept on its action).
END_PROP = 'enf_knife_end'


def _grip_of(knife):
    """A knife's fighting grip as its first ClassKnives.csv row says (forward unless reverse)."""
    try:
        with open(table_file(project_root(), CLASS_KNIVES, 'Melee'), encoding='utf-8-sig', newline='') as f:
            for row in csv.DictReader(f):
                if (row.get('model') or '').strip().split('/')[-1] == knife:
                    return 'reverse' if (row.get('meleeGrip') or '').strip().lower() == 'reverse' else 'forward'
    except (OSError, TypeError):
        pass
    return 'forward'


def _add_made_here():
    for knife, name in MADE_HERE.items():
        KNIVES[knife] = name
        LOOKS[knife] = 'PREVIEW_Knife_' + knife
        for clip, column in MADE_COLUMNS.items():
            # The draft it stands in for: the reverse stab for a reverse-grip knife, as the game picks (ClassKnives.Read).
            base = 'stab_reverse' if clip == 'stab' and _grip_of(knife) == 'reverse' else clip
            label, table = PORTED_CLIPS[clip]
            CLIPS[f'{knife}.{clip}'] = dict(label=f'{name} {label}', action=f'enf_knife_{knife.lower()}_{clip}',
                                            table=f'KnifeMelee{knife}{table}', end=CLIPS[base]['end'],
                                            marks=dict(CLIPS[base]['marks']), look=knife, loop=clip == 'guard', knife=knife,
                                            made=True, base=base, column=column)


def import_fbx(root, rel):
    """A model's objects imported from its FBX (the scene's frame rate kept: the importer sets its own)."""
    scene = bpy.context.scene
    fps, base = scene.render.fps, scene.render.fps_base
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(Path(root) / rel))
    scene.render.fps, scene.render.fps_base = fps, base
    return [o for o in bpy.data.objects if o not in before]


def paint(root, model_rel, obj):
    """The preview's colour from the model's own textures (Textures/<material>.jpg or .png beside its FBX), as the game's import
    rules hang them on; the FBX carries no texture paths."""
    folder = (Path(root) / model_rel).parent / 'Textures'
    for slot in obj.material_slots:
        material = slot.material
        if material is None or not material.use_nodes:
            continue
        name = material.name.split('.')[0]
        picture = next((folder / (name + ext) for ext in ('.jpg', '.png') if (folder / (name + ext)).exists()), None)
        shader = next((n for n in material.node_tree.nodes if n.type == 'BSDF_PRINCIPLED'), None)
        if picture is None or shader is None:
            continue
        node = material.node_tree.nodes.new('ShaderNodeTexImage')
        node.image = bpy.data.images.load(str(picture), check_existing=True)
        material.node_tree.links.new(node.outputs['Color'], shader.inputs['Base Color'])


def add_preview(context, rig, look, model_rel, seat):
    """A knife model in her right fist as the game seats it: imported and painted, its parts under one holder that rides
    the wrist bone (the holder is what 칼 옮기기 picks), at the game's size against her (1 / GAME_SCALE), seated by nine
    seat numbers (seat_holder: the game's own sums)."""
    root = project_root()
    collection = bpy.data.collections.get(PREVIEW_COLLECTION)
    if collection is None:
        collection = bpy.data.collections.new(PREVIEW_COLLECTION)
        context.scene.collection.children.link(collection)
    objects = import_fbx(root, model_rel)
    holder = bpy.data.objects.new(LOOKS[look], None)
    holder.empty_display_type = 'ARROWS'
    holder.empty_display_size = .05
    collection.objects.link(holder)
    for obj in objects:
        for owner in list(obj.users_collection):
            owner.objects.unlink(obj)
        collection.objects.link(obj)
        if obj.parent is None:
            obj.parent = holder
            obj.matrix_parent_inverse = Matrix.Identity(4)
        obj.hide_select = True
        if obj.type == 'MESH':
            paint(root, model_rel, obj)
    holder.parent = rig
    holder.parent_type = 'BONE'
    holder.parent_bone = ARM_BONES['wrist']
    holder.matrix_parent_inverse = Matrix.Identity(4)
    holder.matrix_basis = (parent_frame(holder) @ holder.matrix_parent_inverse).inverted() @ Matrix.Scale(1 / GAME_SCALE, 4)
    context.view_layer.update()
    seat_holder(rig, holder, seat)
    holder[SEAT_PROP] = list(seat)
    context.view_layer.update()
    return holder


def ready_made_here(context):
    """On opening (nothing rebuilt): each knife made here has its preview in her fist, and each of its clips a draft: a
    copy of the clip it stands in for as that clip is now (its keys, marks and end, what the game plays for it), made
    once. A clip not yet sent is kept out of the game until it changes (prepare)."""
    rig = find_rig(context.scene)
    root = project_root()
    if rig is None or root is None:
        return
    for key, clip in CLIPS.items():
        if not clip.get('made') or bpy.data.actions.get(clip['action'] + '_draft') is not None:
            continue
        base = CLIPS[clip['base']]
        source = bpy.data.actions.get(base['action']) or bpy.data.actions.get(base['action'] + '_draft')
        if source is None:
            continue
        draft = source.copy()
        draft.name = clip['action'] + '_draft'
        draft.use_fake_user = True
        draft[oc.LAYER] = 'draft'
        for prop in (oc.SENT, oc.WITHHELD, oc.FROM):
            if prop in draft:
                del draft[prop]
        # The clip's own marks and end start as the copied clip's were (an untouched draft's are its defaults).
        draft[MARKS_PROP] = json.dumps(marks_of(source, clip['base']), sort_keys=True)
        draft[END_PROP] = end_of(source, clip['base'])
    seats = table_seats(root)
    for knife in MADE_HERE:
        if bpy.data.objects.get(LOOKS[knife]) is None and (Path(root) / KNIFE_MODEL.format(knife)).exists():
            add_preview(context, rig, knife, KNIFE_MODEL.format(knife), seats[knife])


def end_of(action, key):
    """A clip's last frame: the timeline's end as the user set it for this clip (kept on its action), else its draft's; and
    no earlier than its last key (a key put past the end makes the clip that long)."""
    end = CLIPS[key]['end']
    if action is not None:
        stored = action.get(END_PROP)
        if stored:
            end = int(stored)
        last = max((p.co.x for curve in oc.curves(action) for p in curve.keyframe_points), default=None)
        if last is not None:
            end = max(end, int(math.ceil(last - 1e-4)))
    return max(end, 2)


def keep_end(scene, key):
    """The timeline's end as the clip's own (on its action) when the user moved it while the clip was shown."""
    action = bpy.data.actions.get(CLIPS[key]['action'])
    if action is None:
        return
    if scene.frame_end != end_of(action, key) and scene.frame_end != action.get(END_PROP):
        action[END_PROP] = int(scene.frame_end)


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
        # Posing again: every move keys its frame (the knife's move had it off).
        oc.auto_key(context.scene, True)
        return {'FINISHED'}


class ENFKNIFE_OT_knife(bpy.types.Operator):
    bl_idname = 'enf_knife.knife'
    bl_label = '칼 옮기기'
    bl_description = '손에 쥔 칼을 고른다. G로 옮기고 R로 돌린 뒤 Ctrl+S 하면 ClassKnives.csv의 근접 칸 쥐는 자리가 바뀐다'
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        knife = look_knife(current_clip(context.scene))
        holder = knife_holder(knife) if knife else None
        if holder is None:
            self.report({'WARNING'}, '이 클립의 칼은 미리보기라 표에 쓰지 않습니다. 칼별 클립을 고르세요')
            return {'CANCELLED'}
        if context.object is not None and context.object.mode != 'OBJECT':
            bpy.ops.object.mode_set(mode='OBJECT')
        for obj in context.view_layer.objects:
            if obj.select_get():
                obj.select_set(False)
        holder.hide_select = False
        holder.select_set(True)
        context.view_layer.objects.active = holder
        # A knife's seat is one place in the fist, not a move over time: no keys while it is moved.
        oc.auto_key(context.scene, False)
        oc.tell(f'{KNIVES[knife]}: G로 옮기고 R로 돌린 뒤 Ctrl+S → 근접 칸 쥐는 자리. 오른손을 누르면 다시 포즈')
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
        group = None
        for k, v in CLIPS.items():
            if v.get('knife') != group:
                # Each knife's own clips (ported from its addon) under its name.
                group = v.get('knife')
                col = box.column(align=True)
                col.label(text=f'{KNIVES.get(group, group)} ' + ('(직접 만드는 클립: 고치기 전엔 1차 시안)' if group in MADE_HERE
                                                                    else '(애드온 모션)'))
            col.operator('enf_knife.pick', text=v['label'], depress=k == key).clip = k
        box.label(text=f'끝: {scene.frame_end}프레임 ({(scene.frame_end - scene.frame_start) / fps:.3f}초)')
        for marker in sorted((m for m in scene.timeline_markers if m.name in MARKS), key=lambda m: m.frame):
            box.label(text=f'{marker.name}: {marker.frame}프레임 ({(marker.frame - scene.frame_start) / fps:.3f}초)')
        box.label(text='표시(벰·쥐기)는 타임라인에서 클릭한 뒤 G로 옮긴다')
        if CLIPS[key].get('loop'):
            box.label(text='되풀이: 끝 프레임이 처음과 같아야 이어진다')
        box.label(text='길이: 타임라인 끝을 옮기거나 끝 뒤에 키를 넣으면 그만큼 길어진다')

        box = layout.box()
        box.label(text='2. 오른손·팔꿈치를 잡아 G로 옮기고 R로 돌리기')
        row = box.row(align=True)
        row.operator('enf_knife.part', text='오른손').bone = ARM_HAND
        row.operator('enf_knife.part', text='팔꿈치 방향').bone = ARM_ELBOW
        box.label(text='자동 키가 켜져 있어 움직이면 그 프레임에 키가 들어간다')
        knife = look_knife(key)
        if knife:
            box.operator('enf_knife.knife', text=f'{KNIVES[knife]} 칼 옮기기 (쥐는 자리)', icon='ORIENTATION_GIMBAL')
            box.label(text='칼을 G·R로 옮기면 Ctrl+S 때 근접 칸 쥐는 자리 숫자로 들어간다')
        else:
            box.label(text='이 칼은 1차 시안 미리보기라 쥐는 자리를 쓰지 않는다')
        box.operator('enf_knife.revert', icon='LOOP_BACK')

        box = layout.box()
        box.label(text='3. Ctrl+S = 바뀐 클립과 옮긴 칼 자리만 게임에 보냄')
        box.operator('enf_knife.send', icon='EXPORT')
        note = scene.get(oc.NOTE)
        if note:
            box.label(text=str(note))
        box.label(text=table_label(key))


_add_made_here()

CLASSES = (ENFKNIFE_OT_pick, ENFKNIFE_OT_part, ENFKNIFE_OT_knife, ENFKNIFE_OT_send, ENFKNIFE_OT_revert, ENFKNIFE_PT_panel)


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
