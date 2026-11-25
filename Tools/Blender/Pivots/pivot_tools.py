"""ENFILADE pivot workspace, Blender 4.5 LTS.

START_PIVOT_ANIMATION.cmd opens ArtSource/Pivots/ENFILADE_Pivot_Animation.blend with truefps_tools.py and then this.
A pivot is the legs changing their way under a body that keeps facing ahead (the view): a jog to the left planting its
left foot out wide and pushing off to the right, a walk ahead stepping back. The game's legs mix BIPER's eight ways'
cycles, and with the keys turned faster than a step the mix drags the feet on the ground between them (the legs' test of
2026-11-25); a pivot clip has the foot plant and push instead. The user makes them here (the user, 2026-11-25: "내가 직접
만들어 보지 뭐", "그 작업실이랑 가이드라인 좀 만들어 줘 레퍼런스랑").

Each pivot is a draft on Kazusa's True FPS rig, made by build_pivot_workspace.py from BIPER's own cycles retargeted as a
humanoid avatar retargets them: the old way's cycle up to the moment its outside foot comes down (디딤), an empty span the
user animates (the plant and the push), and the new way's cycle from the moment that foot pushes off. The draft becomes
the editable action the user keys into (one_click.item_action: a key-pose draft). Three timeline marks go with each pivot
and are kept per pivot: 들어옴 (where the game may come into it from the old way's cycle), 디딤 (the body stops over the
planted foot) and 나감 (where the new way's cycle takes over again).
- On open: the last pivot, the rig in Pose mode, the Dope Sheet with the keys and the marks, the 피벗 tab.
- 피벗 고르기 picks one: its action, its frames, its marks, and BIPER's own two cycles beside Kazusa (보기용).
- Saving (Ctrl+S) sends every pivot changed since it was last sent: the original skeleton baked to
  Assets/_Project/Art/Animations/Pivots/Pivot_<key>.fbx with its .clips.csv (one play, travel kept: root motion) and
  Pivot_<key>.marks.csv (the three marks in seconds). Unity takes it with Kazusa's avatar (ModelImportRules). A pivot
  nobody changed stays out of the game; 게임에서 빼기 deletes its files.
Nothing here changes Blender's preferences.
"""
import importlib.util
import sys
from pathlib import Path

import bpy
from bpy.props import BoolProperty

bl_info = {'name': 'ENFILADE Pivots', 'author': 'ENFILADE', 'version': (1, 0, 0), 'blender': (4, 5, 0),
           'location': '3D View > Sidebar > 피벗', 'category': 'Animation'}

TRUEFPS = Path('Tools/Blender/TrueFPS/truefps_tools.py')
ONE_CLICK = Path('Tools/Blender/OneClick/one_click.py')
TAB = '피벗'
PIVOT_DIR = Path('Assets/_Project/Art/Animations/Pivots')
PREFIX = 'enf_pivot_'
CLIP_PREFIX = 'Pivot_'
CSV_HEADER = 'clip,loop,inPlace,inPlaceY,inPlaceYaw,yawOffset,cycleOffset,from,to\n'
REFERENCE_COLLECTION = '레퍼런스 (보기용, 내보내지 않음)'
# The three marks of a pivot, in the order they come.
MARKS = ('들어옴', '디딤', '나감')

# The pivots: key, label, step (1 first, to see that the idea works in the game), the old way's cycle, the new way's
# (BIPER Female Runner, as BodyMoves.csv plain lists them: FL is the left strafe, FR the right, L45 ahead-left, L135
# back-left, B back; ahead is the walk or the jog itself).
JOG = {'F': 'Jog_Lfoot', 'B': 'StrafeJog_B', 'L': 'StrafeJog_FL', 'R': 'StrafeJog_FR',
       'FL': 'StrafeJog_L45', 'FR': 'StrafeJog_R45', 'BL': 'StrafeJog_L135', 'BR': 'StrafeJog_R135'}
WALK = {'F': 'Walk_Lfoot', 'B': 'StrafeWalk_B', 'L': 'StrafeWalk_FL', 'R': 'StrafeWalk_FR',
        'FL': 'StrafeWalk_L45', 'FR': 'StrafeWalk_R45', 'BL': 'StrafeWalk_L135', 'BR': 'StrafeWalk_R135'}
WAY_LABEL = {'F': '앞', 'B': '뒤', 'L': '왼', 'R': '오', 'FL': '왼앞', 'FR': '오앞', 'BL': '왼뒤', 'BR': '오뒤'}
# Which way each goes (degrees, + to the right), for which foot plants (the one on the side away from the new way).
WAY_ANGLE = {'F': 0, 'FR': 45, 'R': 90, 'BR': 135, 'B': 180, 'BL': -135, 'L': -90, 'FL': -45}
STEPS = {1: (('L', 'R'), ('R', 'L')),
         2: (('F', 'B'), ('B', 'F'), ('FL', 'BR'), ('BR', 'FL'), ('FR', 'BL'), ('BL', 'FR')),
         3: (('F', 'L'), ('F', 'R'), ('B', 'L'), ('B', 'R'), ('L', 'F'), ('L', 'B'), ('R', 'F'), ('R', 'B'))}


def _pivots():
    out = []
    for step, pairs in STEPS.items():
        for gait, ways, name in (('Jog', JOG, '조깅'), ('Walk', WALK, '걷기')):
            # Step 1 is the jog's two alone; the walk's left and right reversals come with step 2.
            if step == 1 and gait == 'Walk':
                continue
            for a, b in pairs:
                out.append((f'{gait}_{a}_{b}', f'{name} {WAY_LABEL[a]}→{WAY_LABEL[b]}', step, ways[a], ways[b], gait, a, b))
    for a, b in STEPS[1]:
        out.append((f'Walk_{a}_{b}', f'걷기 {WAY_LABEL[a]}→{WAY_LABEL[b]}', 2, WALK[a], WALK[b], 'Walk', a, b))
    return tuple(out)


PIVOTS = _pivots()
STEP_LABELS = {1: '1단계 · 먼저 이것만 (A·D 번갈아)', 2: '2단계 · 뒤집기 나머지', 3: '3단계 · 90° 꺾기 (다이아몬드)'}
# BIPER's clips beside Kazusa to look at while animating, in a row behind her: a lateral push from standing, a run's
# brake, a run's plant and turn back (the same performer's own pivots ahead).
EXTRA_REFERENCES = (('QuickShift_L', '왼쪽으로 박차기 (회피)'), ('QuickShift_R', '오른쪽으로 박차기 (회피)'),
                    ('QuickShift_B', '뒤로 박차기 (회피)'), ('Jog_Stop_Lfoot', '조깅 멈춤'),
                    ('Jog_TurnL_Lfoot', '조깅 뒤로 돌기 (앞 피벗)'))


def project_root():
    """The ENFILADE checkout: from this file, else from the open .blend (ArtSource/Pivots/...)."""
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
        raise RuntimeError('ENFILADE 프로젝트 안의 ArtSource/Pivots에서 파일을 열어 주세요.')
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


def one_click():
    return _module('enf_one_click', ONE_CLICK)


def find_rig(scene):
    return next((o for o in scene.objects if o.type == 'ARMATURE' and o.get('truefps_rig')), None)


def pivot(key):
    return next((p for p in PIVOTS if p[0] == key), None)


def label_of(key):
    p = pivot(key)
    return p[1] if p else key


def is_pivot_file(scene):
    return find_rig(scene) is not None and any(bpy.data.actions.get(PREFIX + p[0] + '_draft') for p in PIVOTS)


def current_pivot(scene):
    key = scene.get('enf_pivot', PIVOTS[0][0])
    return key if bpy.data.actions.get(PREFIX + key + '_draft') else PIVOTS[0][0]


def target(key):
    """Where a pivot goes in the game, from the project root."""
    return PIVOT_DIR / (CLIP_PREFIX + key + '.fbx')


def in_game(key):
    root = project_root()
    return root is not None and (root / target(key)).exists()


def frames_of(key):
    """The pivot's length (frames): what its action was last set to, else its draft's."""
    action = bpy.data.actions.get(PREFIX + key) or bpy.data.actions.get(PREFIX + key + '_draft')
    return int(action.get('enf_frames', action.frame_range[1])) if action else 2


def marks_of(key):
    """The pivot's three marks (frame each), as last set on it, else as its draft was made."""
    action = bpy.data.actions.get(PREFIX + key) or bpy.data.actions.get(PREFIX + key + '_draft')
    draft = bpy.data.actions.get(PREFIX + key + '_draft')
    out = {}
    for name in MARKS:
        for source in (action, draft):
            if source is not None and ('enf_mark_' + name) in source:
                out[name] = int(source['enf_mark_' + name])
                break
    return out


def keep_scene_marks(scene):
    """The scene's marks and frame range back onto the pivot shown (they are the scene's while it is shown)."""
    key = scene.get('enf_pivot')
    action = bpy.data.actions.get(PREFIX + key) if key else None
    if action is None:
        return
    for marker in scene.timeline_markers:
        if marker.name in MARKS and action.get('enf_mark_' + marker.name) != marker.frame:
            action['enf_mark_' + marker.name] = marker.frame
    if action.get('enf_frames') != scene.frame_end:
        action['enf_frames'] = scene.frame_end


def fingerprint(key):
    """What a pivot is now: its keys (and eases), its length and its marks."""
    name = PREFIX + key
    marks = marks_of(key)
    return one_click().fingerprint([bpy.data.actions.get(name)], extra=[key, frames_of(key), [marks.get(m) for m in MARKS]])


def item(context, key):
    """The pivot's editable action. One made ready untouched is the draft, which never goes to the game (withheld)."""
    oc = one_click()
    action, _draft, fresh = oc.item_action(context, find_rig(context.scene), PREFIX + key, frames_of(key))
    if fresh:
        for name in MARKS:
            draft = bpy.data.actions.get(PREFIX + key + '_draft')
            if draft is not None and ('enf_mark_' + name) in draft:
                action['enf_mark_' + name] = draft['enf_mark_' + name]
        if 'enf_frames' not in action:
            action['enf_frames'] = frames_of(key)
        (oc.mark_sent if in_game(key) else oc.mark_withheld)(action, fingerprint(key))
    return action


def select_pivot(context, key):
    """Shows one pivot: its action, its frames, its marks and BIPER's two cycles it goes between, beside Kazusa."""
    scene = context.scene
    rig = find_rig(scene)
    if rig is None or bpy.data.actions.get(PREFIX + key + '_draft') is None:
        raise ValueError('이 피벗의 시안이 없습니다. build_pivot_workspace.py로 작업 파일을 다시 만들어야 합니다.')
    if scene.get('enf_pivot') and scene.get('enf_pivot') != key:
        keep_scene_marks(scene)
    action = item(context, key)
    one_click().play(rig, action)
    scene.frame_start, scene.frame_end = 1, frames_of(key)
    for marker in list(scene.timeline_markers):
        if marker.name in MARKS:
            scene.timeline_markers.remove(marker)
    for name, frame in marks_of(key).items():
        scene.timeline_markers.new(name, frame=frame)
    p = pivot(key)
    collection = bpy.data.collections.get(REFERENCE_COLLECTION)
    if collection is not None and p is not None:
        # The collection's own list, hidden per object (walking all_objects while hiding crashed Blender 4.5 after a build).
        for obj in list(collection.objects):
            clip, slot = obj.get('enf_reference'), obj.get('enf_slot')
            shown = obj.get('enf_role') != 'cycle' or (slot == 'from' and clip == p[3]) or (slot == 'to' and clip == p[4])
            obj.hide_viewport = not shown
            obj.hide_render = True
    if scene.get('enf_pivot') != key:
        scene['enf_pivot'] = key
    scene.frame_set(1)
    one_click().auto_key(scene)


def prepare(context):
    for p in PIVOTS:
        if bpy.data.actions.get(PREFIX + p[0] + '_draft'):
            item(context, p[0])
    select_pivot(context, current_pivot(context.scene))


def arrange(context):
    """The workspace as the work needs it: the pivot loaded, the rig in Pose mode, its keys and marks in the Dope Sheet."""
    scene = context.scene
    rig = find_rig(scene)
    if rig is None:
        return True
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
    oc.tell(f'피벗 「{label_of(current_pivot(scene))}」 · 디딤 표시 근처를 채우고 Ctrl+S 하면 게임에 들어갑니다')
    return True


# ---- Export ---------------------------------------------------------------------------------------------------------

def export_pivot(context, key):
    """One pivot to the game: the FBX (travel kept), its clip list and its marks, marked as sent."""
    oc = one_click()
    scene = context.scene
    rig = find_rig(scene)
    select_pivot(context, key)
    root = project_root()
    if root is None:
        raise RuntimeError('ENFILADE 프로젝트를 찾지 못했습니다.')
    path = root / target(key)
    path.parent.mkdir(parents=True, exist_ok=True)
    clip = CLIP_PREFIX + key
    clips, marks = path.with_name(clip + '.clips.csv'), path.with_name(clip + '.marks.csv')
    oc.write_meta(clips, oc.TEXT_META)
    oc.write_meta(marks, oc.TEXT_META)
    oc.write_meta(path)
    truefps().export_active_clip(context, rig, str(path))
    # One play, its travel and its rise kept (the plant's dip and the push's lift), turned half round as every whole-body
    # clip on the player's body is (run_tools.export_cycle says why).
    oc.write_text(clips, CSV_HEADER + f'{clip},0,0,0,0,180,0,,\n')
    fps = scene.render.fps / scene.render.fps_base
    found = marks_of(key)
    oc.write_text(marks, 'clip,' + ','.join(('enter', 'plant', 'exit')) + '\n'
                  + clip + ',' + ','.join(f'{(found[m] - 1) / fps:.4f}' if m in found else '' for m in MARKS) + '\n')
    oc.mark_sent(bpy.data.actions.get(PREFIX + key), fingerprint(key))
    return path


def withhold(key):
    """The pivot's files out of the game, kept out until it changes."""
    root = project_root()
    removed = []
    if root is not None:
        path = root / target(key)
        clip = CLIP_PREFIX + key
        for p in (path, path.with_name(clip + '.clips.csv'), path.with_name(clip + '.marks.csv')):
            for f in (p, p.with_name(p.name + '.meta')):
                if f.exists():
                    f.unlink()
                    removed.append(f.name)
    one_click().mark_withheld(bpy.data.actions.get(PREFIX + key), fingerprint(key))
    return removed


def send_changed(context, force=False):
    """Every pivot changed since it was last sent (or whose file went missing) to the game; the one shown stays shown, on
    its frame. Returns (sent labels, failures), or None with the panel's switch off (force sends anyway)."""
    scene = context.scene
    if not is_pivot_file(scene) or not (force or scene.enf_pivot_settings.auto_send):
        return None
    oc = one_click()
    keep_scene_marks(scene)
    shown, frame = current_pivot(scene), scene.frame_current
    sent, failed = [], []
    for p in PIVOTS:
        key, label = p[0], p[1]
        if not bpy.data.actions.get(PREFIX + key + '_draft'):
            continue
        try:
            action = item(context, key)
            if not oc.needs_sending(action, fingerprint(key), in_game(key)):
                continue
            export_pivot(context, key)
            sent.append(label)
        except Exception as error:
            failed.append(f'{label}: {error}')
    select_pivot(context, shown)
    if scene.frame_current != frame:
        scene.frame_set(frame)
    return sent, failed


# ---- UI -------------------------------------------------------------------------------------------------------------

class ENFPivotSettings(bpy.types.PropertyGroup):
    auto_send: BoolProperty(name='저장하면 게임에 보내기', default=True,
                            description='Ctrl+S로 저장할 때 고친 피벗을 모두 게임에 보낸다(Art/Animations/Pivots의 FBX)')


def _ok(op, context):
    if find_rig(context.scene) is None:
        op.report({'ERROR'}, 'KazusaBandRig를 찾지 못했습니다.')
        return False
    if context.object and context.object.mode == 'EDIT':
        bpy.ops.object.mode_set(mode='OBJECT')
    return True


class ENFPIVOT_OT_pick(bpy.types.Operator):
    bl_idname = 'enf_pivot.pick'
    bl_label = '피벗 고르기'
    bl_options = {'REGISTER', 'UNDO'}
    key: bpy.props.StringProperty()

    def execute(self, context):
        if not _ok(self, context):
            return {'CANCELLED'}
        select_pivot(context, self.key)
        return {'FINISHED'}


class ENFPIVOT_OT_references(bpy.types.Operator):
    bl_idname = 'enf_pivot.references'
    bl_label = '레퍼런스 보이기/숨기기'

    def execute(self, context):
        collection = bpy.data.collections.get(REFERENCE_COLLECTION)
        layer = context.view_layer.layer_collection.children.get(REFERENCE_COLLECTION) if collection else None
        if layer is None:
            self.report({'ERROR'}, '레퍼런스가 없습니다')
            return {'CANCELLED'}
        layer.hide_viewport = not layer.hide_viewport
        return {'FINISHED'}


class ENFPIVOT_OT_revert(bpy.types.Operator):
    bl_idname = 'enf_pivot.revert'
    bl_label = '시안으로 되돌리기'
    bl_description = '이 피벗의 키를 처음 시안으로 되돌린다(표시도). 되돌릴 수 없으니 확인한다'
    bl_options = {'REGISTER', 'UNDO'}

    def invoke(self, context, event):
        return context.window_manager.invoke_confirm(self, event)

    def execute(self, context):
        if not _ok(self, context):
            return {'CANCELLED'}
        key = current_pivot(context.scene)
        fresh = one_click().revert(PREFIX + key)
        draft = bpy.data.actions.get(PREFIX + key + '_draft')
        for name in MARKS:
            if draft is not None and ('enf_mark_' + name) in draft:
                fresh['enf_mark_' + name] = draft['enf_mark_' + name]
        fresh['enf_frames'] = int(draft.get('enf_frames', draft.frame_range[1])) if draft else frames_of(key)
        context.scene['enf_pivot'] = ''
        select_pivot(context, key)
        return {'FINISHED'}


class ENFPIVOT_OT_export(bpy.types.Operator):
    bl_idname = 'enf_pivot.export'
    bl_label = '게임에 보내기'
    bl_description = 'Ctrl+S와 같다: 고친 피벗을 모두 게임에 보내고 작업 파일을 저장한다'

    def execute(self, context):
        if not _ok(self, context):
            return {'CANCELLED'}
        sent, failed = send_changed(context, force=True) or ([], ['피벗 작업 파일이 아닙니다'])
        one_click().send_report(context.scene, sent, failed)
        if bpy.data.filepath:
            bpy.ops.wm.save_mainfile()
        if failed:
            self.report({'ERROR'}, '못 보냄: ' + '; '.join(failed))
            return {'CANCELLED'}
        self.report({'INFO'}, ('보냄: ' + ', '.join(sent) + ' — Unity 창을 누르면 가져갑니다') if sent else '고친 피벗이 없어 저장만 했습니다')
        return {'FINISHED'}


class ENFPIVOT_OT_withhold(bpy.types.Operator):
    bl_idname = 'enf_pivot.withhold'
    bl_label = '게임에서 빼기'
    bl_description = '보낸 파일을 지운다. 이 피벗을 다시 고치면 저장할 때 다시 들어간다'

    def invoke(self, context, event):
        return context.window_manager.invoke_confirm(self, event)

    def execute(self, context):
        removed = withhold(current_pivot(context.scene))
        self.report({'INFO'}, ('지움: ' + ', '.join(removed)) if removed else '보낸 파일이 없습니다')
        return {'FINISHED'}


class ENFPIVOT_PT_panel(bpy.types.Panel):
    bl_label = '피벗 만들기'
    bl_idname = 'ENFPIVOT_PT_panel'
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = TAB

    def draw(self, context):
        layout = self.layout
        scene = context.scene
        key = current_pivot(scene)
        p = pivot(key)
        box = layout.box()
        box.label(text='1. 피벗 고르기')
        for step, label in STEP_LABELS.items():
            box.label(text=label)
            grid = box.grid_flow(columns=2, even_columns=True)
            for q in PIVOTS:
                if q[2] == step and bpy.data.actions.get(PREFIX + q[0] + '_draft'):
                    grid.operator('enf_pivot.pick', text=q[1], depress=q[0] == key).key = q[0]

        box = layout.box()
        box.label(text=f'2. 지금: {label_of(key)}  ({frames_of(key)}프레임, 30fps)')
        if p is not None:
            box.label(text=f'{p[3]} → {p[4]} (옆의 막대 뼈대 둘)')
        marks = marks_of(key)
        box.label(text='표시: ' + ' · '.join(f'{m} {marks[m]}' for m in MARKS if m in marks))
        box.label(text='디딤 앞뒤의 빈 구간을 채운다: 바깥 발 디딤 → 버팀 → 밀기')
        box.label(text='상체·시선은 계속 앞(-Y). 끝 길이는 타임라인 끝으로')
        row = box.row(align=True)
        row.operator('enf_pivot.references', icon='HIDE_OFF')
        row.operator('enf_pivot.revert')

        box = layout.box()
        box.label(text='3. 저장(Ctrl+S)하면 게임에 들어간다')
        box.prop(scene.enf_pivot_settings, 'auto_send')
        oc = one_click()
        action = bpy.data.actions.get(PREFIX + key)
        changed = action is not None and action.get(oc.SENT) != fingerprint(key)
        if action is not None and action.get(oc.WITHHELD) and not changed:
            box.label(text='아직 시안 그대로: 고치면 저장할 때 들어간다')
        elif not in_game(key):
            box.label(text='아직 게임에 없음: 저장하면 들어간다')
        elif changed:
            box.label(text='고친 것이 있음: 저장하면 게임에 들어간다')
        else:
            box.label(text='게임에 들어가 있음')
        if scene.get(oc.NOTE):
            box.label(text=str(scene[oc.NOTE])[:60])
        box.operator('enf_pivot.export', icon='EXPORT')
        box.label(text=f'→ {target(key).as_posix()}')
        box.operator('enf_pivot.withhold')


CLASSES = (ENFPivotSettings, ENFPIVOT_OT_pick, ENFPIVOT_OT_references, ENFPIVOT_OT_revert, ENFPIVOT_OT_export,
           ENFPIVOT_OT_withhold, ENFPIVOT_PT_panel)


def register():
    for cls in CLASSES:
        try:
            bpy.utils.register_class(cls)
        except ValueError:
            bpy.utils.unregister_class(cls)
            bpy.utils.register_class(cls)
    bpy.types.Scene.enf_pivot_settings = bpy.props.PointerProperty(type=ENFPivotSettings)
    one_click().register_workspace(TAB, is_pivot_file, arrange, send_changed)


def unregister():
    one_click().unregister_workspace(TAB)
    del bpy.types.Scene.enf_pivot_settings
    for cls in reversed(CLASSES):
        bpy.utils.unregister_class(cls)


if __name__ == '__main__':
    register()
