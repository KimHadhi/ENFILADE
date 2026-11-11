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
    rig['enf_load'] = json.dumps({'in_place': settings.in_place, 'mirror': settings.mirror, 'speed': settings.speed})
    settings.export_name = stem.removeprefix('enf_')
    # A beginner forgets to press I and loses a pose on the next frame: turning a bone keys it (the touch-up layer).
    _sync_auto_key(context)
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

def export_to_game(context, rig, settings, root=None):
    """Exports the open clip into its slot folder (root: a stand-in project folder, for a dry run)."""
    root = Path(root) if root else ROOT
    if root is None:
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
    path = root / GAME / folder / (stem + '.fbx')
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


# ---- Easy touch-ups: body parts by name, moves by direction --------------------------------------------------------

# The picker's rows: a part by its Korean name and its bone. Synty calls the upper arm "Shoulder" and the forearm
# "Elbow", which is exactly what trips a beginner, so the bone names never show.
PARTS = (
    (('머리', 'Head'), ('목', 'Neck'), ('가슴', 'Spine_03'), ('배', 'Spine_02'), ('허리', 'Spine_01'), ('골반', 'Hips')),
    (('왼어깨', 'Clavicle_L'), ('왼위팔', 'Shoulder_L'), ('왼아래팔', 'Elbow_L'), ('왼손', 'Hand_L')),
    (('오른어깨', 'Clavicle_R'), ('오른위팔', 'Shoulder_R'), ('오른아래팔', 'Elbow_R'), ('오른손', 'Hand_R')),
    (('왼허벅지', 'UpperLeg_L'), ('왼정강이', 'LowerLeg_L'), ('왼발', 'Ankle_L')),
    (('오른허벅지', 'UpperLeg_R'), ('오른정강이', 'LowerLeg_R'), ('오른발', 'Ankle_R')),
)
PART_LABELS = {bone: label for row in PARTS for label, bone in row}
# Each part points at the next joint; that end is what a move carries.
TOWARD = {'Hips': 'Spine_01', 'Spine_01': 'Spine_02', 'Spine_02': 'Spine_03', 'Spine_03': 'Neck', 'Neck': 'Head',
          'Clavicle_L': 'Shoulder_L', 'Shoulder_L': 'Elbow_L', 'Elbow_L': 'Hand_L', 'Hand_L': 'Finger_01',
          'Clavicle_R': 'Shoulder_R', 'Shoulder_R': 'Elbow_R', 'Elbow_R': 'Hand_R', 'Hand_R': 'Finger_01 1',
          'UpperLeg_L': 'LowerLeg_L', 'LowerLeg_L': 'Ankle_L', 'Ankle_L': 'Ball_L',
          'UpperLeg_R': 'LowerLeg_R', 'LowerLeg_R': 'Ankle_R', 'Ankle_R': 'Ball_R'}
# Moves carry the part's far end toward a direction of the zombie's own body, or twist the part on its length.
MOVES = (('forward', '앞으로'), ('back', '뒤로'), ('up', '위로'), ('down', '아래로'),
         ('left', '왼쪽'), ('right', '오른쪽'), ('twist_left', '비틀기 ↺'), ('twist_right', '비틀기 ↻'))


def _reflection(axis):
    """The mirror across the plane square to axis."""
    return Matrix.Identity(3) - 2 * Matrix([[axis.x * axis.x, axis.x * axis.y, axis.x * axis.z],
                                            [axis.y * axis.x, axis.y * axis.y, axis.y * axis.z],
                                            [axis.z * axis.x, axis.z * axis.y, axis.z * axis.z]])


def _posed_axes(rig):
    """The zombie's up, left and forward now: its rest axes turned with its hips, so "forward" follows a turn."""
    up, left, forward = _body_axes(rig)
    hips = rig.pose.bones['Hips']
    turn = _rotation(hips.matrix) @ _rotation(hips.bone.matrix_local).inverted()
    return turn @ up, turn @ left, turn @ forward


def part_direction(rig, bone):
    """Where a part points now, in armature space: toward its next joint (the head along the neck)."""
    pose = rig.pose.bones
    if bone == 'Head':
        direction = pose['Head'].head - pose['Neck'].head
    else:
        child = TOWARD.get(bone)
        if child is None or child not in pose:
            return None
        direction = pose[child].head - pose[bone].head
    return direction.normalized() if direction.length > 1e-6 else None


def _keyed_at(rig, bone, frame):
    path = f'pose.bones["{bone}"].rotation_quaternion'
    data = rig.animation_data
    return any(curve.data_path == path and any(abs(p.co.x - frame) < .5 for p in curve.keyframe_points)
               for curve in action_curves(data.action if data else None))


def _key_shown(rig, bone, frame):
    """Keys the pose on screen into the touch-up layer: Blender works out the touch-up that gives it over the layers."""
    pb = rig.pose.bones[bone]
    pb.keyframe_insert('rotation_quaternion', frame=frame, group=bone)
    if bone == 'Hips':
        pb.keyframe_insert('location', frame=frame, group=bone)


def turn_part(context, rig, bone, reach, make_turn):
    """Turns a part at the current frame by make_turn(its armature-space rotation now) and keys it there.

    With reach > 0 the pose reach frames before and after is keyed as it is first, so the change swells and fades
    around this frame instead of spreading to the whole clip: one click is one local, smooth change.
    """
    scene = context.scene
    frame = scene.frame_current
    for at in (frame - reach, frame + reach) if reach > 0 else ():
        if scene.frame_start <= at <= scene.frame_end and not _keyed_at(rig, bone, at):
            scene.frame_set(at)
            _key_shown(rig, bone, at)
    scene.frame_set(frame)
    context.view_layer.update()
    pb = rig.pose.bones[bone]
    posed = _rotation(pb.matrix)
    turn = make_turn(posed)
    # The posed bone is K @ basis for a K its parents and rest set, so turning it by Q in armature space makes the
    # basis basis @ (posed⁻¹ @ Q @ posed).
    pb.rotation_quaternion = pb.rotation_quaternion.copy() @ (posed.inverted() @ turn @ posed)
    _key_shown(rig, bone, frame)
    context.view_layer.update()


def nudge(context, rig, bone, move, degrees, reach):
    """Moves a part's far end toward a body direction by some degrees (or twists it) at the current frame."""
    context.view_layer.update()
    up, left, forward = _posed_axes(rig)
    direction = part_direction(rig, bone)
    if direction is None:
        raise ValueError('이 부위는 움직일 방향을 알 수 없습니다.')
    if move in ('twist_left', 'twist_right'):
        # ↺ turns the front of an upright part toward the zombie's left, as seen from above, hanging parts alike.
        side = 1.0 if direction.dot(up) >= 0 else -1.0
        axis, angle = direction, degrees * side * (1.0 if move == 'twist_left' else -1.0)
    else:
        target = {'forward': forward, 'back': -forward, 'up': up, 'down': -up, 'left': left, 'right': -left}[move]
        axis = direction.cross(target)
        if axis.length < 1e-3:
            raise ValueError('이미 그쪽을 향하고 있습니다. 다른 방향을 눌러 보세요.')
        angle = degrees
    turn = Quaternion(axis.normalized(), math.radians(angle))
    turn_part(context, rig, bone, reach, lambda _posed: turn)


def mirror_part(context, rig, bone, reach):
    """Gives the part on the other side this part's pose, as in a mirror, at the current frame."""
    twin = _mirror_name(bone)
    if twin == bone or twin not in rig.pose.bones:
        raise ValueError('가운데 부위는 반대쪽이 없습니다.')
    context.view_layer.update()
    _up, left, _forward = _body_axes(rig)
    reflect = _reflection(left)
    rest = {n: _rotation(rig.data.bones[n].matrix_local) for n in (bone, twin)}
    turn = (_rotation(rig.pose.bones[bone].matrix) @ rest[bone].inverted()).to_matrix()
    wanted = (reflect @ turn @ reflect).to_quaternion() @ rest[twin]
    turn_part(context, rig, twin, reach, lambda posed: wanted @ posed.inverted())


def _curve_owners(action):
    """(collection, F-curve) pairs of an action, legacy or layered, so a curve can be removed from its owner."""
    if action is None:
        return []
    if getattr(action, 'is_action_legacy', True) and hasattr(action, 'fcurves'):
        try:
            return [(action.fcurves, curve) for curve in action.fcurves]
        except (AttributeError, RuntimeError):
            pass
    pairs = []
    for layer in getattr(action, 'layers', []):
        for strip in layer.strips:
            for bag in getattr(strip, 'channelbags', []):
                pairs.extend((bag.fcurves, curve) for curve in bag.fcurves)
    return pairs


def refresh_animation(context, rig):
    """After F-curves are changed directly (not by keying), Blender keeps evaluating its copy of the old action
    until told otherwise: re-assigning the action makes it rebuild that copy."""
    data = rig.animation_data
    if data is not None and data.action is not None:
        action, slot = data.action, getattr(data, 'action_slot', None)
        action.update_tag()
        data.action = None
        data.action = action
        if slot is not None and getattr(data, 'action_slot', None) != slot:
            data.action_slot = slot
    rig.update_tag(refresh={'OBJECT', 'DATA', 'TIME'})
    context.view_layer.update()


def clear_touch_ups(rig, frame, bones=None):
    """Removes the touch-up keys at one frame (of some bones, or all); returns how many went."""
    data = rig.animation_data
    prefixes = tuple(f'pose.bones["{b}"]' for b in bones) if bones else None
    removed = 0
    for owner, curve in _curve_owners(data.action if data else None):
        if prefixes and not curve.data_path.startswith(prefixes):
            continue
        for point in [p for p in curve.keyframe_points if abs(p.co.x - frame) < .5]:
            curve.keyframe_points.remove(point, fast=True)
            removed += 1
        curve.update()
        # A curve with no key left would read as 0 (a quaternion of 0 is no rotation at all): it goes too.
        if len(curve.keyframe_points) == 0:
            owner.remove(curve)
    return removed


# ---- Recipes: a clip as a small text file (base, options, variations, touch-ups, slot) ------------------------------
# Claude reads and writes these, so the user can ask for a change in words ("head lower around frame 20, the right
# arm hanging") and get the FBX in the game without opening Blender: make_clip.py builds them headless.

RECIPES = 'ArtSource/Zombies/Recipes'
VARY_KEYS = ('hunch', 'head_tilt', 'head_droop', 'reach', 'dead_arm_l', 'dead_arm_r', 'stiff_leg_l', 'stiff_leg_r',
             'twitch', 'amplify', 'seed')
INTERPOLATIONS = ('CONSTANT', 'LINEAR', 'BEZIER')


def _project_path(path):
    """A path inside the project as project-relative with forward slashes, others as they are."""
    try:
        return Path(path).resolve().relative_to(ROOT).as_posix()
    except (ValueError, TypeError):
        return str(path)


def _from_project(path):
    found = Path(path)
    return found if found.is_absolute() else ROOT / found


def recipe_from_scene(context, rig):
    """The open clip as a recipe: its base and load options, the variation values and the touch-up keys as they are."""
    settings = context.scene.enf_zombie
    loaded = json.loads(rig.get('enf_load', '{}'))
    data = rig.animation_data
    keys = {}
    for curve in action_curves(data.action if data else None):
        keys.setdefault(curve.data_path, {})[str(curve.array_index)] = [
            [round(p.co.x, 3), round(p.co.y, 6), p.interpolation] for p in curve.keyframe_points]
    return {
        'version': 1,
        'base': _project_path(rig.get('enf_base_source', '')),
        'in_place': loaded.get('in_place', settings.in_place),
        'mirror': loaded.get('mirror', settings.mirror),
        'speed': loaded.get('speed', settings.speed),
        'vary': {key: round(getattr(settings, key), 3) for key in VARY_KEYS},
        'touch': [],
        'keys': keys,
        'frames': [context.scene.frame_start, context.scene.frame_end],
        'slot': settings.export_slot,
        'name': settings.export_name,
    }


def _write_touch_keys(context, rig, keys):
    """Puts saved touch-up curves back as they were (no remapping: they are the layer's own values)."""
    data = rig.animation_data
    action = data.action
    for path, channels in keys.items():
        group = re.search(r'pose\.bones\["(.+?)"\]', path)
        for index, points in channels.items():
            curve = _ensure_curve(action, rig, path, int(index), group.group(1) if group else '')
            curve.keyframe_points.clear()
            curve.keyframe_points.add(len(points))
            for point, (frame, value, interpolation) in zip(curve.keyframe_points, points):
                point.co = (frame, value)
                point.interpolation = interpolation if interpolation in INTERPOLATIONS else 'BEZIER'
                point.handle_left_type = point.handle_right_type = 'AUTO_CLAMPED'
            curve.update()
    refresh_animation(context, rig)


def apply_recipe(context, rig, recipe):
    """Builds a recipe's clip on the rig: load, variations, saved touch-ups, then the moves it lists in words."""
    settings = context.scene.enf_zombie
    settings.in_place = bool(recipe.get('in_place', True))
    settings.mirror = bool(recipe.get('mirror', False))
    settings.speed = float(recipe.get('speed', 1.0))
    settings.export_slot = recipe.get('slot', settings.export_slot)
    load_base(context, rig, _from_project(recipe['base']), settings)
    for key, value in recipe.get('vary', {}).items():
        if key in VARY_KEYS:
            setattr(settings, key, value)
    if any(abs(getattr(settings, key)) > 1e-6 for key in VARY_KEYS if key != 'seed'):
        apply_variation(context, rig, settings)
    if recipe.get('keys'):
        _write_touch_keys(context, rig, recipe['keys'])
    scene = context.scene
    for move in recipe.get('touch', []):
        scene.frame_set(int(move['frame']))
        if move.get('mirror'):
            mirror_part(context, rig, move['part'], int(move.get('reach', 6)))
        else:
            nudge(context, rig, move['part'], move['move'], float(move['degrees']), int(move.get('reach', 6)))
    if recipe.get('frames'):
        scene.frame_start, scene.frame_end = (int(v) for v in recipe['frames'])
    settings.export_name = recipe.get('name') or settings.export_name
    scene.frame_set(scene.frame_start)


def recipe_items(_self, _context):
    items = []
    if ROOT is not None and (ROOT / RECIPES).is_dir():
        for path in sorted((ROOT / RECIPES).glob('*.json')):
            items.append((str(path), path.stem, str(path)))
    if not items:
        items.append(('NONE', '레시피가 없습니다', ''))
    _recipe_cache[:] = items
    return _recipe_cache


_recipe_cache = []


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
    step: FloatProperty(name='한 번에', default=5.0, min=1.0, max=45.0, description='방향 버튼 한 번에 돌리는 각도(도)')
    reach_frames: IntProperty(name='섞는 길이', default=6, min=0, max=60,
                              description='바꾼 프레임 앞뒤로 이만큼(프레임)에 걸쳐 부드럽게 섞는다. 0이면 이웃한 키까지 퍼진다')
    auto_key: BoolProperty(name='자동 저장', default=True, update=lambda self, context: _sync_auto_key(context),
                           description='뷰에서 뼈를 돌리면 그 프레임에 바로 손질 키가 저장된다 (I를 안 눌러도 된다)')


def _sync_auto_key(context):
    context.scene.tool_settings.use_keyframe_insert_auto = context.scene.enf_zombie.auto_key


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


def _active_part(rig):
    bone = rig.data.bones.active
    return bone.name if bone is not None and bone.name in PART_LABELS else None


class ENFZOMBIE_OT_pick(bpy.types.Operator):
    bl_idname = 'enf_zombie.pick'
    bl_label = '부위 고르기'
    bl_description = '이 부위를 고른다. 아래 방향 버튼이 이 부위를 움직인다'
    bl_options = {'REGISTER', 'UNDO'}
    bone: StringProperty()

    def execute(self, context):
        rig = _rig_or_report(self, context)
        if rig is None or self.bone not in rig.data.bones:
            return {'CANCELLED'}
        _back_to_pose(context, rig)
        for bone in rig.data.bones:
            bone.select = bone.select_head = bone.select_tail = False
        chosen = rig.data.bones[self.bone]
        chosen.select = True
        rig.data.bones.active = chosen
        return {'FINISHED'}


class ENFZOMBIE_OT_nudge(bpy.types.Operator):
    bl_idname = 'enf_zombie.nudge'
    bl_label = '움직이기'
    bl_description = '고른 부위의 끝을 이 방향으로 "한 번에" 각도만큼 움직이고 이 프레임에 저장한다(좀비 기준 왼쪽·오른쪽)'
    bl_options = {'REGISTER', 'UNDO'}
    move: EnumProperty(items=[(key, label, '') for key, label in MOVES])

    def execute(self, context):
        rig = _rig_or_report(self, context)
        if rig is None:
            return {'CANCELLED'}
        part = _active_part(rig)
        if part is None:
            self.report({'WARNING'}, '먼저 위에서 부위를 고르세요')
            return {'CANCELLED'}
        settings = context.scene.enf_zombie
        try:
            nudge(context, rig, part, self.move, settings.step, settings.reach_frames)
        except ValueError as error:
            self.report({'WARNING'}, str(error))
            return {'CANCELLED'}
        return {'FINISHED'}


class ENFZOMBIE_OT_mirror_part(bpy.types.Operator):
    bl_idname = 'enf_zombie.mirror_part'
    bl_label = '반대쪽에 똑같이'
    bl_description = '고른 부위의 지금 자세를 반대쪽 부위에 거울처럼 준다 (이 프레임)'
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        rig = _rig_or_report(self, context)
        part = _active_part(rig) if rig else None
        if part is None:
            self.report({'WARNING'}, '먼저 위에서 왼쪽이나 오른쪽 부위를 고르세요')
            return {'CANCELLED'}
        try:
            mirror_part(context, rig, part, context.scene.enf_zombie.reach_frames)
        except ValueError as error:
            self.report({'WARNING'}, str(error))
            return {'CANCELLED'}
        return {'FINISHED'}


class ENFZOMBIE_OT_clear_frame(bpy.types.Operator):
    bl_idname = 'enf_zombie.clear_frame'
    bl_label = '이 프레임 손질 지우기'
    bl_description = '지금 프레임의 손질 키를 지운다 (고른 부위만, 또는 전부)'
    bl_options = {'REGISTER', 'UNDO'}
    every: BoolProperty(default=False, options={'SKIP_SAVE'})

    def execute(self, context):
        rig = _rig_or_report(self, context)
        if rig is None:
            return {'CANCELLED'}
        part = None if self.every else _active_part(rig)
        if not self.every and part is None:
            self.report({'WARNING'}, '먼저 부위를 고르거나 "전부"를 누르세요')
            return {'CANCELLED'}
        frame = context.scene.frame_current
        count = clear_touch_ups(rig, frame, [part] if part else None)
        refresh_animation(context, rig)
        self.report({'INFO'}, f'{frame}프레임에서 키 {count}개를 지웠습니다')
        return {'FINISHED'}


class ENFZOMBIE_OT_save_recipe(bpy.types.Operator):
    bl_idname = 'enf_zombie.save_recipe'
    bl_label = '레시피로 저장'
    bl_description = '지금 클립(바탕·변형·손질·슬롯)을 ArtSource/Zombies/Recipes/이름.json 으로 남긴다. Claude가 읽고 고칠 수 있다'
    bl_options = {'REGISTER'}

    def execute(self, context):
        rig = _rig_or_report(self, context)
        if rig is None or not rig.get('enf_base_source'):
            self.report({'ERROR'}, '먼저 클립을 불러오세요')
            return {'CANCELLED'}
        settings = context.scene.enf_zombie
        name = re.sub(r'[^A-Za-z0-9_.-]', '_', settings.export_name.strip()).removeprefix('enf_')
        if not name:
            self.report({'ERROR'}, '4단계의 이름을 먼저 넣으세요 (영문·숫자)')
            return {'CANCELLED'}
        path = ROOT / RECIPES / (name + '.json')
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(recipe_from_scene(context, rig), ensure_ascii=False, indent=1), encoding='utf-8')
        self.report({'INFO'}, f'저장했습니다: Recipes/{path.name}')
        return {'FINISHED'}


class ENFZOMBIE_OT_load_recipe(bpy.types.Operator):
    bl_idname = 'enf_zombie.load_recipe'
    bl_label = '레시피 불러오기'
    bl_description = '레시피대로 클립을 다시 만든다 (Claude가 고친 레시피도)'
    bl_options = {'REGISTER', 'UNDO'}
    bl_property = 'recipe'
    recipe: EnumProperty(name='레시피', items=recipe_items)

    def invoke(self, context, event):
        context.window_manager.invoke_search_popup(self)
        return {'RUNNING_MODAL'}

    def execute(self, context):
        rig = _rig_or_report(self, context)
        if rig is None or self.recipe == 'NONE':
            return {'CANCELLED'}
        try:
            apply_recipe(context, rig, json.loads(Path(self.recipe).read_text(encoding='utf-8')))
        except Exception as error:  # Reported to the user.
            self.report({'ERROR'}, str(error))
            return {'CANCELLED'}
        finally:
            _back_to_pose(context, rig)
        self.report({'INFO'}, f'{Path(self.recipe).stem} 레시피대로 만들었습니다')
        return {'FINISHED'}


class ENFZOMBIE_OT_open_guide(bpy.types.Operator):
    bl_idname = 'enf_zombie.open_guide'
    bl_label = '처음이면 안내서'
    bl_description = 'ArtSource/Zombies/START_HERE.md 를 연다'

    def execute(self, context):
        if ROOT is None:
            return {'CANCELLED'}
        bpy.ops.wm.path_open(filepath=str(ROOT / 'ArtSource' / 'Zombies' / 'START_HERE.md'))
        return {'FINISHED'}


class _ZombieSubPanel:
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = CATEGORY
    bl_parent_id = 'ENFZOMBIE_PT_panel'

    @classmethod
    def poll(cls, context):
        return zombie_rig(context) is not None


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
        _data, base_track, _vary = layer_stack(rig, create=False)
        models = tools.preview_models(context, rig)
        if models:
            row = layout.row(align=True)
            for label, obj in models.items():
                op = row.operator('truefps.preview_model', text=label,
                                  depress=not obj.hide_get(view_layer=context.view_layer))
                op.model = label
        source = rig.get('enf_base_source')
        layout.label(text='지금 클립: ' + (Path(source).stem if source and base_track else '(없음)'), icon='ARMATURE_DATA')
        row = layout.row(align=True)
        row.label(text='Space 재생 · Ctrl+Z 되돌리기')
        row.operator('enf_zombie.open_guide', text='', icon='HELP')


class ENFZOMBIE_PT_base(_ZombieSubPanel, bpy.types.Panel):
    bl_label = '1. 바탕 클립'
    bl_idname = 'ENFZOMBIE_PT_base'

    def draw(self, context):
        layout = self.layout
        settings = context.scene.enf_zombie
        layout.operator('enf_zombie.load', icon='VIEWZOOM')
        layout.operator('enf_zombie.load_file', icon='FILEBROWSER')
        row = layout.row(align=True)
        row.prop(settings, 'in_place', toggle=True)
        row.prop(settings, 'mirror', toggle=True)
        layout.prop(settings, 'speed')
        layout.operator('enf_zombie.reload', icon='FILE_REFRESH')


class ENFZOMBIE_PT_vary(_ZombieSubPanel, bpy.types.Panel):
    bl_label = '2. 변형 (값을 바꾸고 적용)'
    bl_idname = 'ENFZOMBIE_PT_vary'

    def draw(self, context):
        layout = self.layout
        settings = context.scene.enf_zombie
        _data, _base, vary_track = layer_stack(zombie_rig(context), create=False)
        col = layout.column(align=True)
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
        row = layout.row(align=True)
        row.operator('enf_zombie.vary', icon='CHECKMARK')
        shuffle = row.operator('enf_zombie.vary', text='무작위', icon='MOD_NOISE')
        shuffle.shuffle = True
        row = layout.row(align=True)
        if vary_track is not None:
            toggle = row.operator('enf_zombie.toggle', text='변형 켜짐' if not vary_track.mute else '변형 꺼짐',
                                  depress=not vary_track.mute, icon='HIDE_OFF' if not vary_track.mute else 'HIDE_ON')
            toggle.layer = 'VARY'
        row.operator('enf_zombie.reset_vary', icon='LOOP_BACK')


class ENFZOMBIE_PT_touch(_ZombieSubPanel, bpy.types.Panel):
    bl_label = '3. 손질 (직접 고치기)'
    bl_idname = 'ENFZOMBIE_PT_touch'

    def draw(self, context):
        layout = self.layout
        rig = zombie_rig(context)
        settings = context.scene.enf_zombie
        data = rig.animation_data
        part = _active_part(rig)
        layout.label(text='① 고칠 프레임으로 가서 ② 부위를 고르고 ③ 방향을 누른다')
        col = layout.column(align=True)
        for parts in PARTS:
            row = col.row(align=True)
            for label, bone in parts:
                op = row.operator('enf_zombie.pick', text=label, depress=part == bone)
                op.bone = bone
        box = layout.box()
        box.label(text=(PART_LABELS[part] + ' 움직이기 (좀비 기준 왼쪽·오른쪽)') if part else '부위를 고르세요',
                  icon='BONE_DATA')
        grid = box.grid_flow(columns=2, align=True)
        grid.enabled = part is not None
        for key, label in MOVES:
            grid.operator('enf_zombie.nudge', text=label).move = key
        row = box.row(align=True)
        row.prop(settings, 'step')
        row.prop(settings, 'reach_frames')
        row = box.row(align=True)
        row.enabled = part is not None
        row.operator('enf_zombie.mirror_part', icon='MOD_MIRROR')
        row.operator('enf_zombie.clear_frame', text='이 부위 지우기', icon='X').every = False
        row = layout.row(align=True)
        row.operator('enf_zombie.clear_frame', text='이 프레임 전부 지우기', icon='TRASH').every = True
        row.operator('enf_zombie.clear_edit', text='손질 모두 지우기', icon='TRASH')
        row = layout.row(align=True)
        row.prop(settings, 'auto_key', toggle=True, icon='RECORD_ON' if settings.auto_key else 'RECORD_OFF')
        if data is not None and data.action is not None:
            on = data.action_influence > .5
            toggle = row.operator('enf_zombie.toggle', text='손질 켜짐' if on else '손질 꺼짐', depress=on,
                                  icon='HIDE_OFF' if on else 'HIDE_ON')
            toggle.layer = 'EDIT'
        layout.label(text='익숙해지면: 오른쪽 뷰에서 뼈 클릭 → R(돌리기)', icon='INFO')
        row = layout.row(align=True)
        row.operator('truefps.copy_loop_end', text='처음 자세를 끝에', icon='DUPLICATE')
        row.operator('truefps.check_loop', text='반복 이음새 확인', icon='CHECKMARK')


class ENFZOMBIE_PT_send(_ZombieSubPanel, bpy.types.Panel):
    bl_label = '4. 게임에 보내기'
    bl_idname = 'ENFZOMBIE_PT_send'

    def draw(self, context):
        layout = self.layout
        settings = context.scene.enf_zombie
        layout.prop(settings, 'export_slot', text='')
        layout.prop(settings, 'export_name')
        layout.prop(settings, 'overwrite')
        layout.operator('enf_zombie.export', icon='EXPORT')
        folder = 'CalmPose' if settings.export_slot == 'CalmPoseGetUp' else settings.export_slot
        name = settings.export_name.strip() or '이름'
        suffix = '_getup' if settings.export_slot == 'CalmPoseGetUp' else ''
        layout.label(text=f'→ Zombies/{folder}/enf_{name}{suffix}.fbx')


class ENFZOMBIE_PT_recipe(_ZombieSubPanel, bpy.types.Panel):
    bl_label = '레시피 (Claude에게 맡기기)'
    bl_idname = 'ENFZOMBIE_PT_recipe'
    bl_options = {'DEFAULT_CLOSED'}

    def draw(self, context):
        layout = self.layout
        layout.label(text='저장하고 파일 이름을 Claude에게 알려 주면')
        layout.label(text='말로 고쳐 달라고 할 수 있다')
        row = layout.row(align=True)
        row.operator('enf_zombie.save_recipe', icon='FILE_TICK')
        row.operator('enf_zombie.load_recipe', icon='FILE_FOLDER')


CLASSES = (ENFZombieSettings, ENFZOMBIE_OT_load, ENFZOMBIE_OT_load_file, ENFZOMBIE_OT_reload, ENFZOMBIE_OT_vary,
           ENFZOMBIE_OT_reset_vary, ENFZOMBIE_OT_toggle, ENFZOMBIE_OT_clear_edit, ENFZOMBIE_OT_key_selected,
           ENFZOMBIE_OT_export, ENFZOMBIE_OT_pick, ENFZOMBIE_OT_nudge, ENFZOMBIE_OT_mirror_part,
           ENFZOMBIE_OT_clear_frame, ENFZOMBIE_OT_save_recipe, ENFZOMBIE_OT_load_recipe, ENFZOMBIE_OT_open_guide,
           ENFZOMBIE_PT_panel, ENFZOMBIE_PT_base, ENFZOMBIE_PT_vary, ENFZOMBIE_PT_touch, ENFZOMBIE_PT_send,
           ENFZOMBIE_PT_recipe)


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
