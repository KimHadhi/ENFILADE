"""ENFILADE zombie animation tools, Blender 4.5 LTS.

Started by ArtSource/Zombies/START_ZOMBIE_ANIMATION.cmd on ENFILADE_Zombie_Animation.blend,
or run this text in Blender's Text Editor (Alt+P). The Zombie tab of the 3D view sidebar:

1. Base clip: any zombie clip (the Nephilite pack, a clip already in the game, a Mixamo
   download in ArtSource/Zombies/Inbox) goes onto the Synty zombie rig as a locked layer.
2. Variations: hunch, head tilt, reaching or dead arms, a stiff leg, twitches, more or
   less motion, baked as a second layer from a few sliders (or at random).
3. Touch-ups: the user's own keys, a third layer on top (NLA blend Combine), so one key
   bends the clip around that frame instead of breaking it.
4. Send to game: one FBX into Assets/_Project/Art/Animations/Zombies/<Slot>/, where Unity
   adds it to that slot's list (ZombieClipFolders) and zombies start picking it.

The rig, the export and the IK/FK and loop tools come from ../TrueFPS/truefps_tools.py.
No preference or automatic-script permission changes are made.
"""
import importlib.util
import json
import math
import random
import re
import sys
from pathlib import Path

import bpy
from bpy.props import BoolProperty, EnumProperty, FloatProperty, IntProperty, PointerProperty, StringProperty
from bpy_extras.io_utils import ImportHelper
from mathutils import Matrix, Quaternion, Vector

bl_info = {'name': 'ENFILADE Zombie Animation', 'author': 'ENFILADE',
           'version': (1, 0, 0), 'blender': (4, 5, 0),
           'location': '3D View > Sidebar > Zombie', 'category': 'Animation'}

PACK = 'Assets/ThirdParty/Nephilite Studios/Zombie Animation Set/Animations'
GAME = 'Assets/_Project/Art/Animations/Zombies'
INBOX = 'ArtSource/Zombies/Inbox'
TRUEFPS = 'Tools/Blender/TrueFPS/truefps_tools.py'
BASE_TRACK, VARY_TRACK = '1 Base (locked)', '2 Variation'
CATEGORY = 'Zombie'
LINEAR = 1  # BezTriple interpolation value of 'LINEAR' for foreach_set
CSV_HEADER = 'clip,loop,inPlace,inPlaceY,inPlaceYaw,yawOffset,cycleOffset,from,to\n'

# Game slot folders (ZombieClipFolders in Unity): folder, label, loops.
SLOTS = (
    ('Walk', '걷기 (Walk)', True),
    ('Run', '뛰기 (Run)', True),
    ('Idle', '서 있기 (Idle)', True),
    ('CalmIdle', '평온하게 서 있기 (CalmIdle)', True),
    ('AlertIdle', '경계하며 서 있기 (AlertIdle)', True),
    ('SearchWalk', '수색하며 걷기 (SearchWalk)', True),
    ('Attack', '휘두르기 (Attack)', False),
    ('Flinch', '움찔 (Flinch)', False),
    ('Stagger', '휘청 (Stagger)', False),
    ('Notice', '알아챔 (Notice)', False),
    ('Rouse', '깨어남 (Rouse)', False),
    ('CrawlAttack', '기면서 휘두르기 (CrawlAttack)', False),
    ('CalmPose', '앉기·먹기 자세 반복 (CalmPose)', True),
    ('CalmPoseGetUp', '그 자세에서 일어나기 (CalmPose _getup)', False),
)
SLOT_LOOPS = {folder: loops for folder, _label, loops in SLOTS}

# The Synty body bones that must all be found to treat a source as the same skeleton.
BODY = ('Hips', 'Spine_01', 'Spine_02', 'Spine_03', 'Neck', 'Head', 'Clavicle_L', 'Shoulder_L', 'Elbow_L',
        'Hand_L', 'Clavicle_R', 'Shoulder_R', 'Elbow_R', 'Hand_R', 'UpperLeg_L', 'LowerLeg_L', 'Ankle_L',
        'Ball_L', 'UpperLeg_R', 'LowerLeg_R', 'Ankle_R', 'Ball_R')
# Other humanoid rigs (Mixamo, Unreal-like), by lower-case name without namespace or punctuation.
CENTER_ALIASES = {
    'Hips': ('hips', 'pelvis'), 'Spine_01': ('spine', 'spine01'), 'Spine_02': ('spine1', 'spine02', 'chest'),
    'Spine_03': ('spine2', 'spine03', 'upperchest'), 'Neck': ('neck', 'neck01'), 'Head': ('head',),
}
SIDE_ALIASES = {
    'Clavicle': ('{S}shoulder', 'clavicle{s}'), 'Shoulder': ('{S}arm', 'upperarm{s}'),
    'Elbow': ('{S}forearm', 'lowerarm{s}'), 'Hand': ('{S}hand', 'hand{s}'),
    'UpperLeg': ('{S}upleg', 'thigh{s}'), 'LowerLeg': ('{S}leg', 'calf{s}'),
    'Ankle': ('{S}foot', 'foot{s}'), 'Ball': ('{S}toebase', 'ball{s}'),
}
FINGER_ALIASES = {'Thumb': 'thumb', 'IndexFinger': 'index', 'Finger': 'middle'}

_library_cache = []  # Blender needs dynamic enum strings kept alive.


# ---- Paths and the shared rig tools ----------------------------------------------------------------------------

def project_root():
    """The ENFILADE checkout: from this file, else from the open .blend (ArtSource/Zombies/...)."""
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


ROOT = project_root()


def _load_truefps():
    if 'truefps_tools' in sys.modules:
        return sys.modules['truefps_tools']
    if ROOT is None:
        raise RuntimeError('Open ENFILADE_Zombie_Animation.blend from the ENFILADE project first.')
    spec = importlib.util.spec_from_file_location('truefps_tools', ROOT / TRUEFPS)
    module = importlib.util.module_from_spec(spec)
    sys.modules['truefps_tools'] = module
    # A tracked __pycache__ file sits beside it; writing a fresh one would show as a change in git every run.
    writes = sys.dont_write_bytecode
    sys.dont_write_bytecode = True
    try:
        spec.loader.exec_module(module)
    finally:
        sys.dont_write_bytecode = writes
    return module


tools = _load_truefps()


def zombie_rig(context):
    rig = tools.find_rig(context)
    return rig if rig is not None and rig.get('truefps_kind') == 'zombie' else None


def original_bones(rig):
    return [n for n in json.loads(rig['truefps_original_bones']) if n in rig.data.bones]


def bone_order(rig, names):
    """The original bones, every parent before its children."""
    wanted, order = set(names), []

    def visit(bone):
        if bone.name in wanted:
            order.append(bone.name)
        for child in bone.children:
            visit(child)

    for bone in rig.data.bones:
        if bone.parent is None:
            visit(bone)
    return order


def library_items(_self, _context):
    """Every clip the tool can load: the pack, the game's zombie folders, the inbox."""
    items = []
    if ROOT is not None:
        for label, folder in (('팩', PACK), ('게임', GAME), ('받은 파일', INBOX)):
            base = ROOT / folder
            if not base.is_dir():
                continue
            for path in sorted(base.rglob('*.fbx')):
                if path.name.lower().endswith('.tpose.fbx'):
                    continue
                name = path.relative_to(base).with_suffix('').as_posix()
                items.append((str(path), f'{label}: {name}', str(path)))
    if not items:
        items.append(('NONE', '불러올 클립이 없습니다', ''))
    _library_cache[:] = items
    return _library_cache


# ---- Reading a source clip ---------------------------------------------------------------------------------------

def _norm(name):
    return re.sub(r'[^a-z0-9]', '', name.split(':')[-1].lower())


def _synty_twin(target, source_names):
    """The same bone on another Synty skeleton: identical, or the Nephilite pack's _L/_R finger names."""
    if target in source_names:
        return target
    if target.endswith(' 1'):
        twin = target[:-2] + '_R'
    elif target.startswith(('Thumb_', 'IndexFinger_', 'Finger_')):
        twin = target + '_L'
    else:
        return None
    return twin if twin in source_names else None


def _aliases(target):
    if target in CENTER_ALIASES:
        return CENTER_ALIASES[target]
    match = re.fullmatch(r'(Clavicle|Shoulder|Elbow|Hand|UpperLeg|LowerLeg|Ankle|Ball)_([LR])', target)
    if match:
        side, s = ('left', 'l') if match.group(2) == 'L' else ('right', 'r')
        return tuple(a.format(S=side, s=s) for a in SIDE_ALIASES[match.group(1)])
    match = re.fullmatch(r'(Thumb|IndexFinger|Finger)_0(\d)( 1)?', target)
    if match:
        side = 'right' if match.group(3) else 'left'
        return (f'{side}hand{FINGER_ALIASES[match.group(1)]}{match.group(2)}',)
    return ()


def map_bones(targets, source_names):
    """Target bone -> source bone, and whether the source is the Synty skeleton itself (copied as it is)."""
    exact = {t: s for t in targets if (s := _synty_twin(t, source_names))}
    if all(b in exact for b in BODY):
        return exact, True
    by_name = {_norm(s): s for s in source_names}
    mapped = {}
    for target in targets:
        for alias in _aliases(target):
            if alias in by_name:
                mapped[target] = by_name[alias]
                break
    missing = [b for b in BODY if b not in mapped]
    if len(missing) > 4:
        raise ValueError('사람 뼈대로 읽을 수 없는 파일입니다 (못 찾은 뼈: ' + ', '.join(missing[:8]) + ')')
    return mapped, False


def _rotation(matrix):
    return matrix.to_3x3().normalized().to_quaternion()


class _Imported:
    """An FBX imported for reading only; everything it added is removed again on exit.

    Blender's importer sets the scene's frame rate to the file's (25 when the file names none, as the pack's In
    Place files) and keys the clip in those frames: `fps` keeps the file's rate and the scene gets its own back.
    """

    def __init__(self, context, path):
        self.context, self.path = context, path

    def __enter__(self):
        data, render = bpy.data, self.context.scene.render
        self.before = {kind: set(getattr(data, kind)) for kind in
                       ('objects', 'armatures', 'meshes', 'materials', 'images', 'actions', 'collections')}
        own_rate = render.fps, render.fps_base
        try:
            bpy.ops.import_scene.fbx(filepath=str(self.path), use_anim=True, automatic_bone_orientation=False,
                                     ignore_leaf_bones=False, use_custom_props=False, use_image_search=False)
            self.fps = render.fps / render.fps_base
        finally:
            render.fps, render.fps_base = own_rate
        new = [o for o in data.objects if o not in self.before['objects']]
        self.armature = next((o for o in new if o.type == 'ARMATURE'), None)
        if self.armature is None:
            raise ValueError('이 파일에는 뼈대가 없습니다: ' + Path(self.path).name)
        return self

    def __exit__(self, *_):
        data = bpy.data
        for kind in ('objects', 'armatures', 'meshes', 'materials', 'images', 'actions', 'collections'):
            for block in [b for b in getattr(data, kind) if b not in self.before[kind]]:
                try:
                    getattr(data, kind).remove(block)
                except (ReferenceError, RuntimeError):
                    pass
        return False


def _ensure_object_mode(context):
    if context.object is not None and context.object.mode != 'OBJECT':
        bpy.ops.object.mode_set(mode='OBJECT')


def sample_clip(context, rig, path, speed=1.0, mirror=False, in_place=True):
    """Read a clip onto the rig: per frame, the original bones' armature-space rotations and the hips' position.

    A Synty skeleton (the Nephilite pack, our own exports) is copied bone for bone: its bones point the same way
    at rest (measured 2026-11-11: 49 of 50 within 2 degrees, same lengths). Any other humanoid (Mixamo) is
    retargeted by each bone's turn from its own rest, aimed along the zombie's rest bone, the hips' travel scaled
    by leg length; a folder's "<folder>.tpose.fbx" stands in for the rest of clips downloaded without skin.
    """
    scene = context.scene
    names = original_bones(rig)
    order = bone_order(rig, names)
    target_rot = _rotation(rig.matrix_world)
    target_rest = {n: rig.data.bones[n].matrix_local for n in names}
    path = Path(path)
    tpose = path.parent / (path.parent.name + '.tpose.fbx')
    old_frame, old_subframe = scene.frame_current, scene.frame_subframe
    _ensure_object_mode(context)
    try:
        with _Imported(context, path) as clip:
            src = clip.armature
            action = src.animation_data.action if src.animation_data else None
            if action is None:
                raise ValueError('이 파일에는 애니메이션이 없습니다: ' + path.name)
            mapping, same = map_bones([n for n in names if n != 'Root'], set(src.data.bones.keys()))
            start, end = action.frame_range
            # Sampled in real time at the workspace's rate: the clip lasts as long as it did in its file.
            own_fps = scene.render.fps / scene.render.fps_base
            seconds = (end - start) / clip.fps
            step = clip.fps * max(.05, speed) / own_fps
            count = max(2, int(math.floor(seconds * own_fps / max(.05, speed) + 1e-4)) + 1)
            rest_info = None
            if same:
                pass
            elif tpose.exists() and tpose != path:
                with _Imported(context, tpose) as ref:
                    rest_info = _rest_info(ref.armature, mapping, rig)
            else:
                rest_info = _rest_info(src, mapping, rig)
            frames = []
            depsgraph = context.evaluated_depsgraph_get()
            convert = rig.matrix_world.inverted() @ src.matrix_world
            convert_rot = _rotation(convert)
            for i in range(count):
                time = min(end, start + i * step)
                scene.frame_set(int(math.floor(time)), subframe=time - math.floor(time))
                depsgraph = context.evaluated_depsgraph_get()
                pose = src.evaluated_get(depsgraph).pose.bones
                rotations, hips = {}, None
                if same:
                    for target, source in mapping.items():
                        rotations[target] = convert_rot @ _rotation(pose[source].matrix)
                    hips = (convert @ pose[mapping['Hips']].matrix).translation
                else:
                    source_rot = _rotation(src.matrix_world)
                    for target, source in mapping.items():
                        world = source_rot @ _rotation(pose[source].matrix)
                        turn = world @ rest_info['source_rest'][target].inverted()
                        aimed = turn @ rest_info['align'][target].inverted() @ rest_info['target_rest'][target]
                        rotations[target] = target_rot.inverted() @ aimed
                    hips_world = src.matrix_world @ pose[mapping['Hips']].matrix.translation
                    moved = (hips_world - rest_info['source_hips']) * rest_info['scale']
                    hips = rig.matrix_world.inverted() @ (rest_info['target_hips'] + moved)
                frames.append((rotations, hips.copy()))
    finally:
        scene.frame_set(old_frame, subframe=old_subframe)
    if mirror:
        frames = [_mirrored(rig, rotations, hips, target_rest) for rotations, hips in frames]
    if in_place:
        frames = _in_place(rig, frames)
    return order, frames


def _rest_info(src, mapping, rig):
    """World rest data for retargeting another skeleton: rests, the aim from its bones to ours, hips and scale."""
    sw, tw = src.matrix_world, rig.matrix_world
    s_rot, t_rot = _rotation(sw), _rotation(tw)
    source_rest = {t: s_rot @ _rotation(src.data.bones[s].matrix_local) for t, s in mapping.items()}
    target_rest = {t: t_rot @ _rotation(rig.data.bones[t].matrix_local) for t in mapping}
    align = {}
    for target in bone_order(rig, list(mapping)):
        bone = rig.data.bones[target]
        child = next((c.name for c in bone.children if c.name in mapping), None)
        if child is not None:
            s_dir = sw @ src.data.bones[mapping[child]].head_local - sw @ src.data.bones[mapping[target]].head_local
            t_dir = tw @ rig.data.bones[child].head_local - tw @ bone.head_local
            if s_dir.length > 1e-6 and t_dir.length > 1e-6:
                align[target] = s_dir.normalized().rotation_difference(t_dir.normalized())
                continue
        parent = bone.parent
        while parent is not None and parent.name not in align:
            parent = parent.parent
        align[target] = align[parent.name].copy() if parent is not None else Quaternion()

    def leg(obj, hips, ankle):
        return (obj.matrix_world @ obj.data.bones[hips].head_local).z - (obj.matrix_world @ obj.data.bones[ankle].head_local).z

    foot = next((b for b in ('Ankle_L', 'Ankle_R', 'LowerLeg_L', 'LowerLeg_R') if b in mapping), None)
    scale = leg(rig, 'Hips', foot) / max(1e-4, leg(src, mapping['Hips'], mapping[foot])) if foot else 1.0
    return {'source_rest': source_rest, 'target_rest': target_rest, 'align': align, 'scale': scale,
            'source_hips': sw @ src.data.bones[mapping['Hips']].head_local,
            'target_hips': tw @ rig.data.bones['Hips'].head_local}


def _mirror_name(name):
    if name.endswith('_L'):
        return name[:-2] + '_R'
    if name.endswith('_R'):
        return name[:-2] + '_L'
    if name.endswith(' 1'):
        return name[:-2]
    if re.fullmatch(r'(Thumb|IndexFinger|Finger)_0\d', name):
        return name + ' 1'
    return name


def _body_axes(rig):
    """The zombie's up, left and forward in armature space, from its rest pose."""
    bones = rig.data.bones
    up = (bones['Head'].head_local - bones['Hips'].head_local).normalized()
    left = bones['Shoulder_L'].head_local - bones['Shoulder_R'].head_local
    left = (left - up * left.dot(up)).normalized()
    return up, left, left.cross(up).normalized()


def _mirrored(rig, rotations, hips, rest):
    """The same pose seen in a mirror across the zombie's middle: left bones take the right ones' turns."""
    _up, left, _forward = _body_axes(rig)
    reflect = Matrix.Identity(3) - 2 * Matrix([[left.x * left.x, left.x * left.y, left.x * left.z],
                                               [left.y * left.x, left.y * left.y, left.y * left.z],
                                               [left.z * left.x, left.z * left.y, left.z * left.z]])
    out = {}
    for name in rotations:
        twin = _mirror_name(name)
        if twin not in rotations:
            twin = name
        turn = (rotations[twin] @ _rotation(rest[twin]).inverted()).to_matrix()
        out[name] = (reflect @ turn @ reflect).to_quaternion() @ _rotation(rest[name])
    middle = rig.data.bones['Hips'].head_local
    return out, middle + reflect @ (hips - middle)


def _in_place(rig, frames):
    """Takes the hips' straight-line travel out (the game moves zombies itself); bob and sway stay."""
    up, _left, _forward = _body_axes(rig)
    first, last = frames[0][1], frames[-1][1]
    travel = (last - first) - up * (last - first).dot(up)
    count = len(frames) - 1
    return [(rotations, hips - travel * (i / max(1, count))) for i, (rotations, hips) in enumerate(frames)]


def solve_bases(rig, order, rotations, hips):
    """Pose-bone (loc, quat) per original bone from armature-space rotations and the hips' position."""
    bones = rig.data.bones
    pose, bases = {}, {}
    for name in order:
        bone = bones[name]
        parent = bone.parent if bone.parent is not None and bone.parent.name in pose else None
        relaxed = (pose[parent.name] @ (parent.matrix_local.inverted() @ bone.matrix_local)) if parent \
            else bone.matrix_local.copy()
        location = hips if name == 'Hips' else relaxed.translation
        rotation = rotations.get(name, relaxed.to_quaternion())
        pose[name] = Matrix.LocRotScale(location, rotation, None)
        kwargs = {'parent_matrix': pose[parent.name], 'parent_matrix_local': parent.matrix_local} if parent else {}
        basis = bone.convert_local_to_pose(pose[name], bone.matrix_local, invert=True, **kwargs)
        loc, quat, _scale = basis.decompose()
        bases[name] = (loc, quat)
    return bases


# ---- Actions and the layer stack ---------------------------------------------------------------------------------

def action_curves(action):
    if action is None:
        return []
    if getattr(action, 'is_action_legacy', True) and hasattr(action, 'fcurves'):
        try:
            return list(action.fcurves)
        except (AttributeError, RuntimeError):
            pass
    curves = []
    for layer in getattr(action, 'layers', []):
        for strip in layer.strips:
            for bag in getattr(strip, 'channelbags', []):
                curves.extend(bag.fcurves)
    return curves


def _ensure_curve(action, rig, path, index, group):
    # Blender 4.4+ (slotted actions); its 4.5 form takes no group, which only sorts channels in the editors.
    ensure = getattr(action, 'fcurve_ensure_for_datablock', None)
    if ensure is not None:
        return ensure(rig, path, index=index)
    curve = action.fcurves.find(path, index=index)
    return curve if curve is not None else action.fcurves.new(path, index=index, action_group=group)


def write_keys(rig, action, frames, channels):
    """Dense linear keys, one per frame: channels maps (data_path, index, group) to one value per frame."""
    data = rig.animation_data
    previous, blend = data.action, data.action_blend_type
    data.action = action
    try:
        for (path, index, group), values in channels.items():
            curve = _ensure_curve(action, rig, path, index, group)
            points = curve.keyframe_points
            points.clear()
            points.add(len(frames))
            co = [0.0] * (2 * len(frames))
            co[0::2] = frames
            co[1::2] = values
            points.foreach_set('co', co)
            points.foreach_set('interpolation', [LINEAR] * len(frames))
            curve.update()
    finally:
        data.action = previous
        data.action_blend_type = blend


def bases_to_channels(names, per_frame, with_location=('Hips',)):
    """(loc, quat) per bone per frame into channel value lists, the quaternion kept on one side."""
    channels = {}
    for name in names:
        rot_path = f'pose.bones["{name}"].rotation_quaternion'
        lists = [[] for _ in range(4)]
        previous = None
        for bases in per_frame:
            quat = bases[name][1].copy()
            if previous is not None and previous.dot(quat) < 0:
                quat.negate()
            previous = quat
            for i in range(4):
                lists[i].append(quat[i])
        for i in range(4):
            channels[(rot_path, i, name)] = lists[i]
        if name in with_location:
            loc_path = f'pose.bones["{name}"].location'
            for i in range(3):
                channels[(loc_path, i, name)] = [bases[name][0][i] for bases in per_frame]
    return channels


def read_bases(rig, action, frames, names):
    """The action's own (loc, quat) per bone per frame, as keyed (no layers above it)."""
    curves = {(c.data_path, c.array_index): c for c in action_curves(action)}
    out = []
    for frame in frames:
        bases = {}
        for name in names:
            rq = [curves.get((f'pose.bones["{name}"].rotation_quaternion', i)) for i in range(4)]
            quat = Quaternion([c.evaluate(frame) if c else (1.0 if i == 0 else 0.0) for i, c in enumerate(rq)])
            quat.normalize()
            lc = [curves.get((f'pose.bones["{name}"].location', i)) for i in range(3)]
            loc = Vector([c.evaluate(frame) if c else 0.0 for c in lc])
            bases[name] = (loc, quat)
        out.append(bases)
    return out


def layer_stack(rig, create=True):
    """The rig's animation data and its base and variation tracks (create=False for drawing, which may not write)."""
    data = rig.animation_data
    if data is None:
        if not create:
            return None, None, None
        data = rig.animation_data_create()
    return data, data.nla_tracks.get(BASE_TRACK), data.nla_tracks.get(VARY_TRACK)


def _strip_action(track):
    return track.strips[0].action if track is not None and len(track.strips) else None


def _put_in_strip(track, action):
    while len(track.strips):
        track.strips.remove(track.strips[0])
    start = int(action.frame_range[0])
    strip = track.strips.new(action.name, start, action)
    if hasattr(strip, 'action_slot') and strip.action_slot is None and len(getattr(action, 'slots', [])):
        strip.action_slot = action.slots[0]
    return strip


def load_base(context, rig, path, settings):
    """Bake a clip into a fresh base layer, empty the variation layer and start a new touch-up action."""
    order, frames = sample_clip(context, rig, path, settings.speed, settings.mirror, settings.in_place)
    names = order
    per_frame = [solve_bases(rig, order, rotations, hips) for rotations, hips in frames]
    numbers = list(range(1, len(per_frame) + 1))
    stem = re.sub(r'[^A-Za-z0-9_.-]', '_', Path(path).stem)
    for prop in [k for k in rig.keys() if k.startswith('ik_')]:
        rig[prop] = 0.0  # The clip drives the bones themselves: every limb in FK.
    data, base_track, vary_track = layer_stack(rig)
    old_base, old_vary = _strip_action(base_track), _strip_action(vary_track)
    old_edit = data.action if data.action is not None and data.action.get('enf_layer') == 'edit' else None
    base = bpy.data.actions.new('base_' + stem)
    base['enf_layer'] = 'base'
    base.use_fake_user = True
    write_keys(rig, base, numbers, bases_to_channels(names, per_frame))
    data.action = None
    for track in (vary_track, base_track):
        if track is not None:
            data.nla_tracks.remove(track)
    base_track = data.nla_tracks.new()
    base_track.name = BASE_TRACK
    strip = _put_in_strip(base_track, base)
    strip.blend_type = 'REPLACE'
    strip.extrapolation = 'HOLD'
    base_track.lock = True
    vary_track = data.nla_tracks.new(prev=base_track)
    vary_track.name = VARY_TRACK
    edit = bpy.data.actions.new('edit_' + stem)
    edit['enf_layer'] = 'edit'
    edit.use_fake_user = True
    data.action = edit
    data.action_blend_type = 'COMBINE'
    data.action_extrapolation = 'HOLD'
    data.action_influence = 1.0
    # The old base and variation can be made again; touch-ups with keys in them stay as actions (fake user).
    for old in (old_base, old_vary, old_edit):
        if old is not None and old.users <= 1 and old.get('enf_layer') and (old is not old_edit or not action_curves(old)):
            bpy.data.actions.remove(old)
    scene = context.scene
    scene.frame_start, scene.frame_end = 1, len(numbers)
    scene.frame_set(1)
    rig['enf_base_source'] = str(path)
    rig['enf_base_name'] = stem
    settings.export_name = stem.removeprefix('enf_')
    return len(numbers)


# ---- Variations --------------------------------------------------------------------------------------------------

def _twitches(rng, length, fps, amount, loop):
    """Sudden jerks: (start frame, bone, armature axis, degrees), one every 0.7 to 1.8 s."""
    if amount <= 0:
        return []
    bones = ('Head', 'Head', 'Head', 'Neck', 'Neck', 'Spine_03', 'Clavicle_L', 'Clavicle_R', 'Shoulder_L', 'Shoulder_R')
    out, at = [], rng.uniform(.2, 1.0) * fps
    while at < length - (0 if loop else 6):
        axis = Vector((rng.uniform(-1, 1), rng.uniform(-1, 1), rng.uniform(-1, 1)))
        if axis.length > 1e-3:
            out.append((at, rng.choice(bones), axis.normalized(), amount * rng.uniform(12, 30)))
        at += rng.uniform(.7, 1.8) * fps
    return out


def _pulse(age):
    """A jerk's strength by frames since it began: snaps in over 2 frames, eases out over about 8."""
    if age < 0 or age > 24:
        return 0.0
    return age / 2.0 if age < 2 else math.exp(-(age - 2) / 4.0)


def vary(rig, bases, settings, fps):
    """The varied pose of every frame from the base pose of every frame (both lists of {bone: (loc, quat)})."""
    up, left, forward = _body_axes(rig)
    bones = rig.data.bones
    rest_inv = {n: _rotation(bones[n].matrix_local).inverted() for n in bases[0]}
    count = len(bases)

    def about(name, axis, degrees):
        return Quaternion((rest_inv[name] @ axis).normalized(), math.radians(degrees))

    def aim(name, child, direction):
        """The turn that points this bone at its child along a body direction, from the parent's rest."""
        d0 = (bones[name].matrix_local.inverted() @ bones[child].head_local)
        d1 = rest_inv[name] @ direction.normalized()
        return d0.normalized().rotation_difference(d1.normalized())

    averages = {}
    if abs(settings.amplify) > 1e-3:
        for name in bases[0]:
            total = Vector((0.0, 0.0, 0.0, 0.0))
            first = bases[0][name][1]
            for frame in bases:
                q = frame[name][1]
                total += Vector(q) if q.dot(first) >= 0 else -Vector(q)
            averages[name] = Quaternion(total.normalized())
    hips_first, hips_last = bases[0]['Hips'][0], bases[-1]['Hips'][0]
    rng = random.Random(settings.seed)
    loop = SLOT_LOOPS.get(settings.export_slot, True)
    jerks = _twitches(rng, count, fps, settings.twitch, loop)
    reach_l = aim('Shoulder_L', 'Elbow_L', forward + left * .2 - up * .2)
    reach_r = aim('Shoulder_R', 'Elbow_R', forward - left * .2 - up * .2)
    hang_l = aim('Shoulder_L', 'Elbow_L', -up + forward * .12 + left * .1)
    hang_r = aim('Shoulder_R', 'Elbow_R', -up + forward * .12 - left * .1)
    out = []
    for index, frame in enumerate(bases):
        new = {name: (loc.copy(), quat.copy()) for name, (loc, quat) in frame.items()}
        if averages:
            k = 1.0 + settings.amplify
            for name, avg in averages.items():
                loc, quat = new[name]
                if avg.dot(quat) < 0:
                    quat = -quat
                delta = avg.inverted() @ quat
                axis, angle = delta.to_axis_angle()
                new[name] = (loc, avg @ Quaternion(axis, angle * k))
            trend = hips_first.lerp(hips_last, index / max(1, count - 1))
            loc, quat = new['Hips']
            new['Hips'] = (trend + (loc - trend) * k, quat)

        def turn(name, axis, degrees):
            if abs(degrees) > 1e-4:
                loc, quat = new[name]
                new[name] = (loc, about(name, axis, degrees) @ quat)

        def toward(name, target, share):
            if share > 1e-4:
                loc, quat = new[name]
                if target.dot(quat) < 0:
                    target = -target
                new[name] = (loc, quat.slerp(target, min(1.0, share)))

        for name, share in (('Spine_01', .4), ('Spine_02', .35), ('Spine_03', .25)):
            turn(name, left, settings.hunch * share)
        # Bent over, it still looks ahead: the neck gives back half the bend.
        turn('Neck', left, -settings.hunch * .45 + settings.head_droop * .5)
        turn('Head', left, settings.head_droop * .5)
        turn('Neck', forward, settings.head_tilt * .3)
        turn('Head', forward, settings.head_tilt * .7)
        for side, reach, hang, dead in (('L', reach_l, hang_l, settings.dead_arm_l),
                                        ('R', reach_r, hang_r, settings.dead_arm_r)):
            toward(f'Shoulder_{side}', reach, settings.reach * .85)
            toward(f'Elbow_{side}', Quaternion(), settings.reach * .6)
            toward(f'Shoulder_{side}', hang, dead * .9)
            toward(f'Elbow_{side}', Quaternion(), dead * .8)
            toward(f'Hand_{side}', Quaternion(), dead * .6)
        for side, stiff in (('L', settings.stiff_leg_l), ('R', settings.stiff_leg_r)):
            toward(f'LowerLeg_{side}', Quaternion(), stiff * .8)
            toward(f'Ankle_{side}', Quaternion(), stiff * .5)
        for start, name, axis, degrees in jerks:
            age = index - start
            if loop and age < 0:
                age += count - 1
            strength = _pulse(age)
            if strength > 1e-3 and name in new:
                turn(name, axis, degrees * strength)
        out.append(new)
    return out


def apply_variation(context, rig, settings):
    data, base_track, vary_track = layer_stack(rig)
    base = _strip_action(base_track)
    if base is None or vary_track is None:
        raise ValueError('먼저 1단계에서 바탕 클립을 불러오세요.')
    start, end = (int(v) for v in base.frame_range)
    frames = list(range(start, end + 1))
    names = bone_order(rig, original_bones(rig))
    bases = read_bases(rig, base, frames, names)
    varied = vary(rig, bases, settings, context.scene.render.fps / context.scene.render.fps_base)
    # Combine blends a layer's quaternion onto the one below by multiplying it on the right, and adds locations.
    deltas = []
    for before, after in zip(bases, varied):
        frame = {}
        for name in names:
            frame[name] = (after[name][0] - before[name][0], before[name][1].inverted() @ after[name][1])
        deltas.append(frame)
    old = _strip_action(vary_track)
    action = bpy.data.actions.new('vary_' + rig.get('enf_base_name', 'clip'))
    action['enf_layer'] = 'vary'
    action.use_fake_user = True
    write_keys(rig, action, frames, bases_to_channels(names, deltas))
    strip = _put_in_strip(vary_track, action)
    strip.blend_type = 'COMBINE'
    strip.extrapolation = 'HOLD'
    vary_track.mute = False
    if old is not None and old.users <= 1:
        bpy.data.actions.remove(old)
    context.view_layer.update()


def randomize(settings):
    rng = random.Random()
    settings.seed = rng.randrange(1, 99999)
    settings.hunch = rng.uniform(0, 28)
    settings.head_tilt = rng.uniform(-22, 22)
    settings.head_droop = rng.uniform(-5, 18)
    settings.reach = rng.uniform(.35, .8) if rng.random() < .3 else 0.0
    one_arm = rng.random() < .2
    settings.dead_arm_l = rng.uniform(.6, 1) if one_arm and rng.random() < .5 else 0.0
    settings.dead_arm_r = rng.uniform(.6, 1) if one_arm and settings.dead_arm_l == 0 else 0.0
    one_leg = rng.random() < .25
    settings.stiff_leg_l = rng.uniform(.4, .85) if one_leg and rng.random() < .5 else 0.0
    settings.stiff_leg_r = rng.uniform(.4, .85) if one_leg and settings.stiff_leg_l == 0 else 0.0
    settings.twitch = rng.uniform(0, .7) if rng.random() < .6 else 0.0
    settings.amplify = rng.uniform(-.15, .45)


# ---- Export into the game ----------------------------------------------------------------------------------------

def export_to_game(context, rig, settings):
    if ROOT is None:
        raise ValueError('ENFILADE 프로젝트 폴더를 찾지 못했습니다.')
    data = rig.animation_data
    if data is None or data.action is None:
        raise ValueError('내보낼 동작이 없습니다. 먼저 클립을 불러오세요.')
    name = re.sub(r'[^A-Za-z0-9_.-]', '_', settings.export_name.strip()).removeprefix('enf_')
    if not name:
        raise ValueError('영문·숫자로 된 이름을 넣으세요.')
    slot = settings.export_slot
    folder, suffix = ('CalmPose', '_getup') if slot == 'CalmPoseGetUp' else (slot, '')
    stem = 'enf_' + name + suffix
    path = ROOT / GAME / folder / (stem + '.fbx')
    if path.exists() and not settings.overwrite:
        raise ValueError(f'{folder}/{stem}.fbx 가 이미 있습니다. 이름을 바꾸거나 "덮어쓰기"를 켜세요.')
    path.parent.mkdir(parents=True, exist_ok=True)
    # The FBX take, and so the Unity clip, is named after the touch-up action.
    other = bpy.data.actions.get(stem)
    if other is not None and other != data.action:
        other.name = stem + '_old'
    data.action.name = stem
    data.action['truefps_loop'] = SLOT_LOOPS[slot]
    tools.export_active_clip(context, rig, str(path))
    # inPlace 1: what travel is left becomes root motion, which a zombie's body drops (it is moved by its agent).
    path.with_suffix('.clips.csv').write_text(
        CSV_HEADER + f'{stem},{int(SLOT_LOOPS[slot])},1,0,0,0,0,,\n', encoding='utf-8')
    return path


# ---- Settings, operators and the panel ---------------------------------------------------------------------------

class ENFZombieSettings(bpy.types.PropertyGroup):
    in_place: BoolProperty(name='제자리', default=True,
                           description='골반이 앞으로 나아가는 거리를 뺀다. 게임에서는 좀비가 스스로 움직이므로 보통 켠다')
    mirror: BoolProperty(name='좌우 반전', default=False, description='왼쪽과 오른쪽을 바꿔 불러온다')
    speed: FloatProperty(name='빠르기', default=1.0, min=.25, max=4.0, description='1보다 크면 빨라지고 짧아진다')
    hunch: FloatProperty(name='몸 숙이기', default=0.0, min=-20, max=45, description='등을 앞으로 굽히는 각도(도)')
    head_tilt: FloatProperty(name='고개 옆으로', default=0.0, min=-35, max=35, description='고개를 옆으로 꺾는 각도(도)')
    head_droop: FloatProperty(name='고개 떨굼', default=0.0, min=-25, max=40, description='고개를 아래로 떨구는 각도(도)')
    reach: FloatProperty(name='팔 앞으로', default=0.0, min=0, max=1, description='두 팔을 앞으로 뻗는 정도')
    dead_arm_l: FloatProperty(name='왼팔 늘어뜨리기', default=0.0, min=0, max=1, description='왼팔을 힘없이 늘어뜨리는 정도')
    dead_arm_r: FloatProperty(name='오른팔 늘어뜨리기', default=0.0, min=0, max=1, description='오른팔을 힘없이 늘어뜨리는 정도')
    stiff_leg_l: FloatProperty(name='왼다리 뻣뻣하게', default=0.0, min=0, max=1, description='왼 무릎을 덜 굽혀 다리를 끈다')
    stiff_leg_r: FloatProperty(name='오른다리 뻣뻣하게', default=0.0, min=0, max=1, description='오른 무릎을 덜 굽혀 다리를 끈다')
    twitch: FloatProperty(name='경련', default=0.0, min=0, max=1, description='머리·어깨가 가끔 툭 꺾이는 세기')
    amplify: FloatProperty(name='움직임 크기', default=0.0, min=-.6, max=1.0,
                           description='0보다 크면 원래 움직임을 과장하고 작으면 줄인다')
    seed: IntProperty(name='경련 씨앗', default=1, min=0, description='경련이 언제 어디서 나는지 정하는 수')
    export_slot: EnumProperty(name='슬롯', items=[(f, label, '') for f, label, _ in SLOTS], default='Walk')
    export_name: StringProperty(name='이름', default='', description='영문·숫자. 파일은 enf_이름.fbx 가 된다')
    overwrite: BoolProperty(name='덮어쓰기', default=False, description='같은 이름의 파일이 있으면 바꾼다')


def _rig_or_report(op, context):
    rig = zombie_rig(context)
    if rig is None:
        op.report({'ERROR'}, 'ENFILADE_Zombie_Animation.blend 를 여세요 (좀비 뼈대가 없습니다).')
    return rig


def _back_to_pose(context, rig):
    if rig.name in context.view_layer.objects:
        for obj in context.selected_objects:
            obj.select_set(False)
        rig.select_set(True)
        context.view_layer.objects.active = rig
        if rig.mode != 'POSE':
            bpy.ops.object.mode_set(mode='POSE')


def _load(op, context, path):
    rig = _rig_or_report(op, context)
    if rig is None:
        return {'CANCELLED'}
    try:
        count = load_base(context, rig, path, context.scene.enf_zombie)
    except Exception as error:  # Reported to the user; the rig keeps its previous layers.
        op.report({'ERROR'}, str(error))
        return {'CANCELLED'}
    finally:
        _back_to_pose(context, rig)
    op.report({'INFO'}, f'{Path(path).name}: {count}프레임을 바탕으로 불러왔습니다')
    return {'FINISHED'}


class ENFZOMBIE_OT_load(bpy.types.Operator):
    bl_idname = 'enf_zombie.load'
    bl_label = '클립 불러오기'
    bl_description = '팩·게임·받은 파일 폴더의 좀비 클립을 골라 바탕으로 깐다'
    bl_options = {'REGISTER', 'UNDO'}
    bl_property = 'clip'
    clip: EnumProperty(name='클립', items=library_items)

    def invoke(self, context, event):
        context.window_manager.invoke_search_popup(self)
        return {'RUNNING_MODAL'}

    def execute(self, context):
        if self.clip == 'NONE':
            return {'CANCELLED'}
        return _load(self, context, self.clip)


class ENFZOMBIE_OT_load_file(bpy.types.Operator, ImportHelper):
    bl_idname = 'enf_zombie.load_file'
    bl_label = '파일에서 불러오기'
    bl_description = '아무 곳에 있는 FBX(믹사모 등 사람 뼈대)를 바탕으로 깐다'
    bl_options = {'REGISTER', 'UNDO'}
    filename_ext = '.fbx'
    filter_glob: StringProperty(default='*.fbx', options={'HIDDEN'})

    def execute(self, context):
        return _load(self, context, self.filepath)


class ENFZOMBIE_OT_reload(bpy.types.Operator):
    bl_idname = 'enf_zombie.reload'
    bl_label = '같은 클립 다시 불러오기'
    bl_description = '제자리·좌우 반전·빠르기를 바꾼 뒤 같은 클립을 다시 깐다 (변형과 손질은 지워진다)'
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        rig = _rig_or_report(self, context)
        source = rig.get('enf_base_source') if rig else None
        if not source or not Path(source).exists():
            self.report({'ERROR'}, '다시 불러올 클립이 없습니다.')
            return {'CANCELLED'}
        return _load(self, context, source)


class ENFZOMBIE_OT_vary(bpy.types.Operator):
    bl_idname = 'enf_zombie.vary'
    bl_label = '변형 적용'
    bl_description = '위 값들로 변형 층을 다시 만든다'
    bl_options = {'REGISTER', 'UNDO'}
    shuffle: BoolProperty(default=False, options={'HIDDEN', 'SKIP_SAVE'})

    def execute(self, context):
        rig = _rig_or_report(self, context)
        if rig is None:
            return {'CANCELLED'}
        settings = context.scene.enf_zombie
        if self.shuffle:
            randomize(settings)
        try:
            apply_variation(context, rig, settings)
        except Exception as error:
            self.report({'ERROR'}, str(error))
            return {'CANCELLED'}
        self.report({'INFO'}, '변형을 적용했습니다. Space로 재생해 보세요')
        return {'FINISHED'}


class ENFZOMBIE_OT_reset_vary(bpy.types.Operator):
    bl_idname = 'enf_zombie.reset_vary'
    bl_label = '값 0으로'
    bl_description = '변형 값을 모두 0으로 되돌리고 변형 층을 비운다'
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        settings = context.scene.enf_zombie
        for prop in ('hunch', 'head_tilt', 'head_droop', 'reach', 'dead_arm_l', 'dead_arm_r', 'stiff_leg_l',
                     'stiff_leg_r', 'twitch', 'amplify'):
            setattr(settings, prop, 0.0)
        rig = zombie_rig(context)
        if rig is not None:
            _data, _base, vary_track = layer_stack(rig)
            if vary_track is not None:
                old = _strip_action(vary_track)
                while len(vary_track.strips):
                    vary_track.strips.remove(vary_track.strips[0])
                if old is not None and old.users <= 1 and old.get('enf_layer'):
                    bpy.data.actions.remove(old)
        return {'FINISHED'}


class ENFZOMBIE_OT_toggle(bpy.types.Operator):
    bl_idname = 'enf_zombie.toggle'
    bl_label = '층 켜기/끄기'
    bl_description = '변형 층이나 손질 층을 잠깐 끄고 켜서 비교한다'
    bl_options = {'REGISTER', 'UNDO'}
    layer: EnumProperty(items=[('VARY', '변형', ''), ('EDIT', '손질', '')])

    def execute(self, context):
        rig = _rig_or_report(self, context)
        if rig is None:
            return {'CANCELLED'}
        data, _base, vary_track = layer_stack(rig)
        if self.layer == 'VARY' and vary_track is not None:
            vary_track.mute = not vary_track.mute
        elif self.layer == 'EDIT':
            data.action_influence = 0.0 if data.action_influence > .5 else 1.0
        return {'FINISHED'}


class ENFZOMBIE_OT_clear_edit(bpy.types.Operator):
    bl_idname = 'enf_zombie.clear_edit'
    bl_label = '손질 모두 지우기'
    bl_description = '손질 층의 키를 모두 지운다 (바탕과 변형은 그대로)'
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        rig = _rig_or_report(self, context)
        if rig is None:
            return {'CANCELLED'}
        data = rig.animation_data
        old = data.action if data is not None else None
        if old is None:
            return {'CANCELLED'}
        name = old.name
        fresh = bpy.data.actions.new(name + '_new')
        fresh['enf_layer'] = 'edit'
        fresh.use_fake_user = True
        data.action = fresh
        data.action_blend_type = 'COMBINE'
        data.action_influence = 1.0
        if old.get('enf_layer') == 'edit':
            bpy.data.actions.remove(old)
            fresh.name = name
        self.report({'INFO'}, '손질을 지웠습니다')
        return {'FINISHED'}


class ENFZOMBIE_OT_key_selected(bpy.types.Operator):
    bl_idname = 'enf_zombie.key_selected'
    bl_label = '고른 뼈 지금 자세 저장'
    bl_description = '고른 뼈의 지금 자세를 이 프레임에 손질 키로 남긴다 (뷰에서 I 를 누른 것과 같다)'
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        rig = _rig_or_report(self, context)
        if rig is None:
            return {'CANCELLED'}
        chosen = [pb for pb in rig.pose.bones if pb.bone.select]
        if not chosen:
            self.report({'WARNING'}, '오른쪽 뷰에서 뼈를 먼저 고르세요')
            return {'CANCELLED'}
        frame = context.scene.frame_current
        for pb in chosen:
            pb.keyframe_insert('rotation_quaternion', frame=frame, group=pb.name)
            if pb.name == 'Hips':
                pb.keyframe_insert('location', frame=frame, group=pb.name)
        self.report({'INFO'}, f'{len(chosen)}개 뼈를 {frame}프레임에 저장했습니다')
        return {'FINISHED'}


class ENFZOMBIE_OT_export(bpy.types.Operator):
    bl_idname = 'enf_zombie.export'
    bl_label = '게임에 보내기'
    bl_description = '바탕+변형+손질을 합친 동작을 고른 슬롯 폴더에 FBX로 내보낸다. Unity가 알아서 목록에 넣는다'
    bl_options = {'REGISTER'}

    def execute(self, context):
        rig = _rig_or_report(self, context)
        if rig is None:
            return {'CANCELLED'}
        try:
            path = export_to_game(context, rig, context.scene.enf_zombie)
        except Exception as error:
            self.report({'ERROR'}, str(error))
            return {'CANCELLED'}
        finally:
            _back_to_pose(context, rig)
        self.report({'INFO'}, f'보냈습니다: {path.parent.name}/{path.name}. Unity를 누르면 들어갑니다')
        return {'FINISHED'}


class ENFZOMBIE_PT_panel(bpy.types.Panel):
    bl_label = 'ENFILADE 좀비 애니메이션'
    bl_idname = 'ENFZOMBIE_PT_panel'
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = CATEGORY

    def draw(self, context):
        layout = self.layout
        rig = zombie_rig(context)
        if rig is None:
            layout.label(text='ENFILADE_Zombie_Animation.blend 를 여세요')
            return
        settings = context.scene.enf_zombie
        data, base_track, vary_track = layer_stack(rig, create=False)
        models = tools.preview_models(context, rig)
        if models:
            row = layout.row(align=True)
            for label, obj in models.items():
                op = row.operator('truefps.preview_model', text=label,
                                  depress=not obj.hide_get(view_layer=context.view_layer))
                op.model = label

        box = layout.box()
        box.label(text='1. 바탕 클립', icon='ARMATURE_DATA')
        source = rig.get('enf_base_source')
        box.label(text='지금: ' + (Path(source).stem if source and base_track else '(없음)'))
        box.operator('enf_zombie.load', icon='VIEWZOOM')
        box.operator('enf_zombie.load_file', icon='FILEBROWSER')
        row = box.row(align=True)
        row.prop(settings, 'in_place', toggle=True)
        row.prop(settings, 'mirror', toggle=True)
        box.prop(settings, 'speed')
        box.operator('enf_zombie.reload', icon='FILE_REFRESH')

        box = layout.box()
        box.label(text='2. 변형 (값을 바꾸고 적용)', icon='MOD_WAVE')
        col = box.column(align=True)
        for prop in ('hunch', 'head_tilt', 'head_droop', 'reach'):
            col.prop(settings, prop, slider=True)
        row = col.row(align=True)
        row.prop(settings, 'dead_arm_l', slider=True)
        row.prop(settings, 'dead_arm_r', slider=True)
        row = col.row(align=True)
        row.prop(settings, 'stiff_leg_l', slider=True)
        row.prop(settings, 'stiff_leg_r', slider=True)
        col.prop(settings, 'twitch', slider=True)
        col.prop(settings, 'amplify', slider=True)
        row = box.row(align=True)
        row.operator('enf_zombie.vary', icon='CHECKMARK')
        shuffle = row.operator('enf_zombie.vary', text='무작위', icon='MOD_NOISE')
        shuffle.shuffle = True
        row = box.row(align=True)
        if vary_track is not None:
            toggle = row.operator('enf_zombie.toggle', text='변형 켜짐' if not vary_track.mute else '변형 꺼짐',
                                  depress=not vary_track.mute, icon='HIDE_OFF' if not vary_track.mute else 'HIDE_ON')
            toggle.layer = 'VARY'
        row.operator('enf_zombie.reset_vary', icon='LOOP_BACK')

        box = layout.box()
        box.label(text='3. 손질 (직접 고치기)', icon='GREASEPENCIL')
        box.label(text='오른쪽 뷰: 뼈 클릭 → R 로 돌리기 → I 로 저장')
        box.label(text='한 프레임에 저장하면 앞뒤로 부드럽게 섞인다')
        box.operator('enf_zombie.key_selected', icon='KEY_HLT')
        row = box.row(align=True)
        if data is not None and data.action is not None:
            on = data.action_influence > .5
            toggle = row.operator('enf_zombie.toggle', text='손질 켜짐' if on else '손질 꺼짐', depress=on,
                                  icon='HIDE_OFF' if on else 'HIDE_ON')
            toggle.layer = 'EDIT'
        row.operator('enf_zombie.clear_edit', icon='TRASH')
        row = box.row(align=True)
        row.operator('truefps.copy_loop_end', text='처음 자세를 끝에', icon='DUPLICATE')
        row.operator('truefps.check_loop', text='반복 이음새 확인', icon='CHECKMARK')

        box = layout.box()
        box.label(text='4. 게임에 보내기', icon='EXPORT')
        box.prop(settings, 'export_slot', text='')
        box.prop(settings, 'export_name')
        box.prop(settings, 'overwrite')
        box.operator('enf_zombie.export', icon='EXPORT')
        folder = 'CalmPose' if settings.export_slot == 'CalmPoseGetUp' else settings.export_slot
        name = settings.export_name.strip() or '이름'
        suffix = '_getup' if settings.export_slot == 'CalmPoseGetUp' else ''
        box.label(text=f'→ Zombies/{folder}/enf_{name}{suffix}.fbx')


CLASSES = (ENFZombieSettings, ENFZOMBIE_OT_load, ENFZOMBIE_OT_load_file, ENFZOMBIE_OT_reload, ENFZOMBIE_OT_vary,
           ENFZOMBIE_OT_reset_vary, ENFZOMBIE_OT_toggle, ENFZOMBIE_OT_clear_edit, ENFZOMBIE_OT_key_selected,
           ENFZOMBIE_OT_export, ENFZOMBIE_PT_panel)


def _show_zombie_tab():
    for window in bpy.context.window_manager.windows:
        for area in window.screen.areas:
            if area.type != 'VIEW_3D' or not area.spaces.active.show_region_ui:
                continue
            for region in area.regions:
                if region.type == 'UI':
                    try:
                        region.active_panel_category = CATEGORY
                    except (TypeError, AttributeError, RuntimeError):
                        pass
    return None


def register():
    # True FPS's register looks its operators up by class name, which Blender files under the idname, so it can't
    # tell that this same module already registered them: only register it when its panel isn't this module's.
    if getattr(bpy.types, 'TRUEFPS_PT_panel', None) is not tools.TRUEFPS_PT_panel:
        tools.register()
    # Running the text again (Alt+P) replaces the classes an earlier run registered.
    for cls in reversed(bpy.app.driver_namespace.get('enf_zombie_classes', [])):
        try:
            bpy.utils.unregister_class(cls)
        except RuntimeError:
            pass
    for cls in CLASSES:
        bpy.utils.register_class(cls)
    bpy.app.driver_namespace['enf_zombie_classes'] = list(CLASSES)
    bpy.types.Scene.enf_zombie = PointerProperty(type=ENFZombieSettings)
    # After True FPS's own start-up (0.2 s), which opens its tab.
    if not bpy.app.background and not bpy.app.timers.is_registered(_show_zombie_tab):
        bpy.app.timers.register(_show_zombie_tab, first_interval=.6)


def unregister():
    if hasattr(bpy.types.Scene, 'enf_zombie'):
        del bpy.types.Scene.enf_zombie
    for cls in reversed(bpy.app.driver_namespace.pop('enf_zombie_classes', []) + list(tools.CLASSES)):
        try:
            bpy.utils.unregister_class(cls)
        except RuntimeError:
            pass


if __name__ == '__main__':
    register()
