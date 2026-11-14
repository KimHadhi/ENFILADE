"""The class knife in Kazusa's right fist, for the execution workspace's 처형 tab (Blender 4.5 LTS).

The game seats each class's own knife by its row of Tables/Melee/ClassKnives.csv (ExecutionKnifeArm.Wear and Seat): the
model's origin at the wrist plus heldSide, heldPalm, heldAlong hand lengths (wrist to middle knuckle) along the hand's
axes (x away from the thumb, y out of the palm, z toward the knuckles), its blade (from its Pommel marker through the
origin) along bladeX/Y/Z and its cutting side (toward its Edge marker) along edgeX/Y/Z, at its scale. Here the knife of
the class picked in the tab is that same model seated the same way on Kazusa's right wrist, so every pair shows the
knife where the game will have it. Moved or turned there (칼 옮기기), saving writes the numbers that put it where it is
into that class's row: the held, blade or edge numbers that changed and nothing else (the user, 2026-11-14: "칼 위치를
숫자로 하는 이유가 있는지, 왜냐하면 블렌더에서 하면 내가 수정할 때 바로 직관적으로 옮길 수 있으니까 편해서", "그니까
블렌더에서 저장한 게 수치로도 저장되게"). The numbers are written as measured, six decimals (a micrometre at the hand,
a millionth of a way), never rounded to look tidy, and any move counts, however small (the user, 2026-11-14: what is
made in Blender reaches the game whole). Numbers changed outside Blender win on opening: the knife goes where they say.
The knife workspace (Tools/Blender/Knives) does the same for the melee slot's grip (the melee* columns).
"""
import csv
import io
import json
import re
import time
from pathlib import Path

import bpy
from mathutils import Matrix, Vector

TABLES_DIR = Path('Assets/_Project/Data/Resources/Tables')
TABLE = 'ClassKnives'
DEFAULT_ROW = 'default'
COLUMNS = ('heldSide', 'heldPalm', 'heldAlong', 'bladeX', 'bladeY', 'bladeZ', 'edgeX', 'edgeY', 'edgeZ')
GROUPS = (slice(0, 3), slice(3, 6), slice(6, 9))
# ClassKnives.Knife's own numbers, for a row that leaves one empty.
FALLBACK = (0.0, .3, .55, .93, 0.0, .36, 0.0, 0.0, 1.0)
FALLBACK_MODEL = 'ClassProps/Knives/PrimalKnife'
# A number counts as moved past this: far above the float noise of Blender's matrices (about 1e-7 here, the file
# keeping them in single precision), far below any move made by hand (under a thousandth of a millimetre at the hand).
STEP = 1e-5
# Written to six decimals: what the scene says, to well under that.
DIGITS = 6
WRIST = 'Right wrist'
KNUCKLES = {'index': 'IndexFinger1_R', 'middle': 'MiddleFinger1_R', 'ring': 'RingFinger1_R', 'little': 'LittleFinger1_R'}
COLLECTION = '07 칼 (클래스 칼, 클립에는 안 들어감)'
# Files built before 0.9.56 held a fixed preview knife (the MW throwing knife) here.
OLD_COLLECTION = '07 칼 (보기용, 내보내지 않음)'
OLD_KNIFE = '칼 (보기용)'
CLASS_PROP = 'enf_exe_knife'
MODEL_PROP = 'enf_exe_knife_model'
SEAT_PROP = 'enf_exe_knife_seat'
PART_PROP = 'enf_exe_knife_part'
SCENE_PICK = 'enf_exe_knife_pick'
SCENE_MOVED = 'enf_exe_knife_moved'


# ---- The table ------------------------------------------------------------------------------------------------------

def table_file(root):
    """ClassKnives.csv under the project root (the tables are filed in sub-folders and found by name, as the game finds
    them), or None."""
    base = Path(root) / TABLES_DIR
    return next(iter(sorted(base.rglob(TABLE + '.csv'))), None)


def _csv_rows(path):
    if path is None or not Path(path).exists():
        return []
    return list(csv.reader(io.StringIO(Path(path).read_bytes().decode('utf-8-sig'))))


def read_rows(root):
    """ClassKnives.csv's rows in order as [(id, {column: cell})], its 설명 row left out."""
    rows = _csv_rows(table_file(root))
    if not rows:
        return []
    header = [h.strip() for h in rows[0]]
    return [(row[0].strip(), dict(zip(header, row))) for row in rows[1:]
            if row and row[0].strip() and not row[0].startswith('#')]


def _number(cell, fallback):
    try:
        return float(cell) if cell is not None and str(cell).strip() else fallback
    except ValueError:
        return fallback


def numbers_of(row):
    """A row's nine seat numbers as the game reads them (an empty cell is ClassKnives.Knife's own number)."""
    return [_number(row.get(c), f) for c, f in zip(COLUMNS, FALLBACK)]


def scale_of(row):
    return max(.01, _number(row.get('scale'), 1.0))


def canonical(seat):
    """Nine seat numbers as the game takes them (ClassKnives.Way makes the blade and edge unit ways, ExecutionKnifeArm.Seat
    squares the edge to the blade, else takes the palm) and as a measured knife gives them back, to six decimals. Numbers
    typed with an edge not square to the blade then count as where the knife is, not as a move."""
    held, blade, edge = Vector(seat[0:3]), Vector(seat[3:6]), Vector(seat[6:9])
    blade = blade.normalized() if blade.length > 1e-4 else Vector(FALLBACK[3:6]).normalized()
    edge = edge.normalized() if edge.length > 1e-4 else Vector(FALLBACK[6:9])
    edge = edge - blade * edge.dot(blade)
    if edge.length < 1e-4:
        palm = Vector((0.0, 1.0, 0.0))
        edge = palm - blade * palm.dot(blade)
    edge = edge.normalized() if edge.length > 1e-4 else blade.orthogonal().normalized()
    return [round(float(v), DIGITS) for v in (*held, *blade, *edge)]


def differs(a, b):
    return a is None or b is None or len(a) != len(b) or any(abs(x - y) > STEP for x, y in zip(a, b))


def _cell(value):
    text = f'{value:.{DIGITS}f}'.rstrip('0').rstrip('.')
    return '0' if text in ('-0', '') else text


def _spans(text):
    """The records of a CSV text, each a list of its fields' (start, end) spans in the text, quotes included."""
    records, fields = [], []
    start = i = 0
    n = len(text)
    quoted = False
    while i < n:
        c = text[i]
        if quoted:
            if c == '"':
                if i + 1 < n and text[i + 1] == '"':
                    i += 2
                    continue
                quoted = False
            i += 1
        elif c == '"':
            quoted = True
            i += 1
        elif c == ',':
            fields.append((start, i))
            i += 1
            start = i
        elif c in '\r\n':
            fields.append((start, i))
            records.append(fields)
            fields = []
            i += 2 if c == '\r' and i + 1 < n and text[i + 1] == '\n' else 1
            start = i
        else:
            i += 1
    if start < n or fields:
        fields.append((start, n))
        records.append(fields)
    return records


def _field(text, span):
    raw = text[span[0]:span[1]]
    return raw[1:-1].replace('""', '"') if len(raw) >= 2 and raw[0] == '"' and raw[-1] == '"' else raw


def write_cells(path, row_id, cells):
    """These cells written into the row of this id; every other byte of the file is kept as it was (other rows, the 설명
    row, the row's other cells, the line ends). True when the file changed. Written in place and retried: a file Unity
    has open can't be truncated by a plain write on this PC."""
    raw = Path(path).read_bytes()
    bom = '﻿' if raw.startswith(b'\xef\xbb\xbf') else ''
    text = raw.decode('utf-8-sig')
    records = _spans(text)
    if not records:
        raise RuntimeError('ClassKnives.csv가 비어 있습니다.')
    header = [_field(text, s).strip() for s in records[0]]
    missing = [c for c in cells if c not in header]
    if missing:
        raise RuntimeError('ClassKnives.csv에 칸이 없습니다: ' + ', '.join(missing))
    target = next((r for r in records[1:] if r and _field(text, r[0]).strip() == row_id), None)
    if target is None:
        raise RuntimeError(f'ClassKnives.csv에 {row_id} 줄이 없습니다.')
    edits, tail = [], {}
    for column, value in cells.items():
        i = header.index(column)
        if i < len(target):
            a, b = target[i]
            if text[a:b] != value:
                edits.append((a, b, value))
        else:
            tail[i] = value
    if tail:
        # A row shorter than the header: its missing cells added at its end.
        end = target[-1][1]
        extra = [tail.get(i, '') for i in range(len(target), max(tail) + 1)]
        edits.append((end, end, ',' + ','.join(extra)))
    if not edits:
        return False
    for a, b, value in sorted(edits, reverse=True):
        text = text[:a] + value + text[b:]
    data = (bom + text).encode('utf-8')
    for _attempt in range(20):
        try:
            with open(path, 'r+b') as handle:
                handle.seek(0)
                handle.write(data)
                handle.truncate()
            return True
        except OSError:
            time.sleep(.25)
    raise RuntimeError('ClassKnives.csv를 쓰지 못했습니다(Unity가 붙잡고 있음). 다시 저장하면 다시 씁니다.')


# ---- Names for the picker ---------------------------------------------------------------------------------------------

_CACHE = {}


def _soon(key, make, every=2.0):
    """make(), at most every two seconds (the panel asks on every redraw, playback included)."""
    now = time.monotonic()
    known = _CACHE.get(key)
    if known is None or now - known[0] > every:
        known = (now, make())
        _CACHE[key] = known
    return known[1]


def _texts(root):
    path = next(iter(sorted((Path(root) / TABLES_DIR).rglob('Texts.csv'))), None)
    rows = _csv_rows(path)
    if not rows:
        return {}
    header = rows[0]
    ko = header.index('ko') if 'ko' in header else 1
    return {r[0].strip(): r[ko] for r in rows[1:] if len(r) > ko and r[0]}


def choices(root):
    """Every knife row as the picker lists it: (id, '<class> · <knife>', where it is)."""
    def make():
        if root is None:
            return []
        texts = _texts(root)
        titles = {}
        classes = next(iter(sorted((Path(root) / TABLES_DIR).rglob('Classes.csv'))), None)
        rows = _csv_rows(classes)
        if rows and 'title' in rows[0]:
            at = rows[0].index('title')
            titles = {r[0].strip(): texts.get(r[at].strip(), r[0]) for r in rows[1:] if len(r) > at and r[0] and not r[0].startswith('#')}
        out = []
        for row_id, row in read_rows(root):
            who = '기본(줄 없는 모드 클래스)' if row_id == DEFAULT_ROW else titles.get(row_id, row_id)
            model = (row.get('model') or FALLBACK_MODEL).strip()
            name = texts.get((row.get('name') or '').strip(), model.split('/')[-1])
            out.append((row_id, f'{who} · {name}', f'ClassKnives.csv {row_id} 줄 · {model.split("/")[-1]}'))
        return out
    return _soon(('choices', str(root)), make)


def title(root, row_id):
    return next((label for key, label, _ in choices(root) if key == row_id), row_id)


# ---- The model --------------------------------------------------------------------------------------------------------

_FBX = {}


def model_fbx(root, model):
    """The FBX a knife's Resources prefab is made from (the converter makes each a variant of its model), or None."""
    model = (model or FALLBACK_MODEL).strip()
    if model in _FBX and (_FBX[model] is None or _FBX[model].exists()):
        return _FBX[model]
    root = Path(root)
    found = None
    folders = [p for pattern in ('Assets/_Project/*/Resources', 'Assets/_Project/*/*/Resources', 'Assets/*/Resources')
               for p in sorted(root.glob(pattern))]
    for folder in folders:
        if (folder / (model + '.fbx')).exists():
            found = folder / (model + '.fbx')
            break
        prefab = folder / (model + '.prefab')
        if prefab.exists():
            match = re.search(r'm_SourcePrefab: \{fileID: -?\d+, guid: ([0-9a-f]{32})', prefab.read_text(encoding='utf-8', errors='replace'))
            if match:
                found = _fbx_by_guid(root, match.group(1), Path(model).name)
            break
    _FBX[model] = found
    return found


def _fbx_by_guid(root, guid, name):
    guess = root / 'Assets/_Project/Art/Models/Addons/Knives' / name / (name + '.fbx')
    candidates = ([guess] if guess.exists() else []) + sorted((root / 'Assets/_Project/Art/Models').rglob('*.fbx'))
    for fbx in candidates:
        meta = fbx.with_name(fbx.name + '.meta')
        try:
            if meta.exists() and f'guid: {guid}' in meta.read_text(encoding='utf-8', errors='replace')[:400]:
                return fbx
        except OSError:
            continue
    return None


def _broken(image):
    if image is None:
        return True
    try:
        return tuple(image.size) == (0, 0) or Path(bpy.path.abspath(image.filepath)).is_dir()
    except (OSError, ValueError):
        return True


def _paint(fbx, obj):
    """The model's own colour and bumps (Textures/<material>.jpg or .png and <material>_normal.png beside its FBX, as the
    game's import rules hang them on). The FBX names its normal map with no file: the importer leaves an empty picture
    there (pink in Material Preview), which gets the file or goes."""
    folder = Path(fbx).parent / 'Textures'
    for slot in obj.material_slots:
        material = slot.material
        if material is None or not material.use_nodes:
            continue
        nodes, links = material.node_tree.nodes, material.node_tree.links
        name = material.name.split('.')[0]

        def picture(suffix=''):
            return next((folder / (name + suffix + ext) for ext in ('.jpg', '.png') if (folder / (name + suffix + ext)).exists()), None)
        for node in [n for n in nodes if n.type == 'TEX_IMAGE' and _broken(n.image)]:
            empty = node.image
            normal = picture('_normal') if any(l.to_node.type == 'NORMAL_MAP' for l in node.outputs['Color'].links) else None
            if normal is not None:
                node.image = bpy.data.images.load(str(normal), check_existing=True)
                node.image.colorspace_settings.name = 'Non-Color'
            else:
                nodes.remove(node)
            if empty is not None and empty.users == 0:
                bpy.data.images.remove(empty)
        shader = next((n for n in nodes if n.type == 'BSDF_PRINCIPLED'), None)
        colour = picture()
        if shader is None or colour is None or shader.inputs['Base Color'].is_linked:
            continue
        node = nodes.new('ShaderNodeTexImage')
        node.image = bpy.data.images.load(str(colour), check_existing=True)
        links.new(node.outputs['Color'], shader.inputs['Base Color'])


def _collection(scene):
    collection = bpy.data.collections.get(COLLECTION)
    old = bpy.data.collections.get(OLD_COLLECTION)
    if collection is None and old is not None:
        old.name = COLLECTION
        collection = old
    if collection is None:
        collection = bpy.data.collections.new(COLLECTION)
    if collection.name not in scene.collection.children and not any(collection.name in c.children for c in scene.collection.children_recursive):
        scene.collection.children.link(collection)
    return collection


def _remove(objects):
    meshes = {o.data for o in objects if o.type == 'MESH'}
    for obj in objects:
        bpy.data.objects.remove(obj, do_unlink=True)
    for mesh in meshes:
        if mesh.users == 0:
            materials = [m for m in mesh.materials if m is not None]
            bpy.data.meshes.remove(mesh)
            for material in materials:
                if material.users == 0:
                    bpy.data.materials.remove(material)


def _drop_old_preview():
    """The fixed preview knife of files built before 0.9.56 (the MW throwing knife), which the class knife replaces."""
    old = bpy.data.objects.get(OLD_KNIFE)
    if old is not None and CLASS_PROP not in old:
        _remove([old] + list(old.children_recursive))


def holder_in(scene):
    """The knife in her fist: the empty carrying the model (picked and moved by it), or None."""
    return next((o for o in scene.objects if o.type == 'EMPTY' and CLASS_PROP in o), None)


def _make(context, rig, fbx, model):
    """The model imported under a new holder on her right wrist (the holder carries the model's origin and turn, its
    parts keep their own places under it, the markers hidden)."""
    scene = context.scene
    collection = _collection(scene)
    fps, base = scene.render.fps, scene.render.fps_base
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(fbx), use_image_search=False)
    scene.render.fps, scene.render.fps_base = fps, base
    made = [o for o in bpy.data.objects if o not in before]
    holder = bpy.data.objects.new('칼', None)
    holder.empty_display_type = 'ARROWS'
    holder.empty_display_size = .05
    collection.objects.link(holder)
    for obj in made:
        for owner in list(obj.users_collection):
            owner.objects.unlink(obj)
        collection.objects.link(obj)
        if obj.parent is None:
            local = obj.matrix_basis.copy()
            obj.parent = holder
            obj.matrix_parent_inverse = Matrix.Identity(4)
            obj.matrix_basis = local
        obj.hide_select = True
        if obj.type == 'EMPTY':
            obj[PART_PROP] = obj.name.split('.')[0]
            obj.hide_viewport = True
        else:
            obj[PART_PROP] = 'model'
            if obj.type == 'MESH':
                _paint(fbx, obj)
    holder[MODEL_PROP] = model
    # Its size is the table's (scale): moved and turned here, never scaled.
    holder.lock_scale = (True, True, True)
    holder.parent = rig
    holder.parent_type = 'BONE'
    holder.parent_bone = WRIST
    holder.matrix_parent_inverse = Matrix.Identity(4)
    return holder


# ---- The seat ---------------------------------------------------------------------------------------------------------

def _world(obj):
    """An unparented object's world matrix from its own transform (current even before the scene is evaluated)."""
    return obj.matrix_basis.copy() if obj.parent is None else obj.matrix_world.copy()


def hand(rig):
    """Kazusa's right hand at rest as the game reads a hand (ArmBones: the hand bone and the proximal knuckles), in world
    space: the wrist, the hand's axes as a matrix's columns (x away from the thumb, y out of the palm, z toward the middle
    knuckle) and its length (wrist to that knuckle). The knuckles ride the wrist bone, so at rest is as in any pose."""
    world = _world(rig)
    bones = rig.data.bones

    def at(name):
        bone = bones.get(name)
        return world @ bone.head_local if bone is not None else None

    wrist = at(WRIST)
    knuckle = at(KNUCKLES['middle'])
    if knuckle is None:
        knuckle = at(KNUCKLES['index'])
    index = at(KNUCKLES['index'])
    outer = next((p for p in (at(KNUCKLES['little']), at(KNUCKLES['ring']), at(KNUCKLES['middle'])) if p is not None), None)
    run = knuckle - wrist
    along = run.normalized()
    # ArmBones.PalmNow takes Cross(along, index - outer) on a right hand in Unity's left-handed axes; in Blender's
    # right-handed ones the same way out of the palm is the other cross. No fingers: palm down.
    palm = (index - outer).cross(along) if index is not None and outer is not None else Vector()
    if palm.length < 1e-6:
        palm = Vector((0.0, 0.0, -1.0))
    palm = (palm - along * palm.dot(along)).normalized()
    # The game's frame is LookRotation(along, palm) with its x mirrored (ExecutionKnifeArm.Mirror): away from the thumb.
    side = palm.cross(along).normalized()
    return wrist, Matrix((side, palm, along)).transposed(), run.length


def _parent_frame(obj):
    """A bone child's parent frame at rest (Blender holds it from the bone's tail), else its parent's place."""
    parent = obj.parent
    if parent is None:
        return Matrix.Identity(4)
    if obj.parent_type == 'BONE' and parent.type == 'ARMATURE' and obj.parent_bone in parent.data.bones:
        bone = parent.data.bones[obj.parent_bone]
        return _world(parent) @ bone.matrix_local @ Matrix.Translation((0.0, bone.length, 0.0))
    return _world(parent)


def rest_world(holder):
    """Where the knife is on her at rest (world), from the wrist bone's rest and the knife's place on it: the pose shown,
    a hidden knife or a scene not yet evaluated don't change it."""
    return _parent_frame(holder) @ holder.matrix_parent_inverse @ holder.matrix_basis


def _in_holder(holder, obj):
    m = Matrix.Identity(4)
    while obj is not None and obj != holder:
        m = obj.matrix_parent_inverse @ obj.matrix_basis @ m
        obj = obj.parent
    return m


def marks(holder):
    """The model's markers (Pommel, Tip, Edge, as the converter places them) in the holder's frame."""
    return {o[PART_PROP]: _in_holder(holder, o).translation.copy() for o in holder.children_recursive
            if o.type == 'EMPTY' and PART_PROP in o}


def model_ways(holder):
    """The knife's own ways in its holder's frame, as ExecutionKnifeArm.Wear takes them: the blade from its Pommel
    marker through the origin (else toward its Tip), the edge toward its Edge marker, square to the blade. Without
    markers, the converter's frame (the knife workspace's reading): the tip toward -Y, the edge toward -Z."""
    found = marks(holder)
    pommel, tip, edge = found.get('Pommel'), found.get('Tip'), found.get('Edge')
    if pommel is not None and pommel.length > 1e-4:
        blade = -pommel
    elif tip is not None and tip.length > 1e-4:
        blade = tip.copy()
    else:
        blade = Vector((0.0, -1.0, 0.0))
    blade.normalize()
    cut = edge - blade * edge.dot(blade) if edge is not None else Vector()
    if cut.length < 1e-4:
        down = Vector((0.0, 0.0, -1.0))
        cut = down - blade * down.dot(blade)
    if cut.length < 1e-4:
        cut = blade.orthogonal()
    return blade, cut.normalized()


def place(rig, holder, seat, scale):
    """The knife put where nine seat numbers say, at its scale, as ExecutionKnifeArm.Seat puts it: its origin that many
    hand lengths along the hand's axes from the wrist, its blade and edge along those ways."""
    wrist, axes, length = hand(rig)
    held, blade_axes, edge_axes = Vector(seat[0:3]), Vector(seat[3:6]), Vector(seat[6:9])
    blade = axes @ blade_axes
    blade = blade.normalized() if blade.length > 1e-6 else (axes @ Vector(FALLBACK[3:6])).normalized()
    edge = axes @ edge_axes
    edge = edge - blade * edge.dot(blade)
    if edge.length < 1e-4:
        palm = axes @ Vector((0.0, 1.0, 0.0))
        edge = palm - blade * palm.dot(blade)
    edge = edge.normalized() if edge.length > 1e-4 else blade.orthogonal().normalized()
    blade_model, edge_model = model_ways(holder)
    want = Matrix((blade, edge, blade.cross(edge))).transposed()
    have = Matrix((blade_model, edge_model, blade_model.cross(edge_model))).transposed()
    world = (Matrix.Translation(wrist + axes @ (held * length)) @ (want @ have.transposed()).to_4x4()
             @ Matrix.Scale(scale, 4))
    holder.matrix_basis = (_parent_frame(holder) @ holder.matrix_parent_inverse).inverted() @ world


def seat_now(rig, holder):
    """Where the knife sits in her fist now, as the table's nine numbers (six decimals, the ways unit and square)."""
    wrist, axes, length = hand(rig)
    at, turn, _size = rest_world(holder).decompose()
    blade_model, edge_model = model_ways(holder)
    turn = turn.to_matrix()
    blade = (turn @ blade_model).normalized()
    edge = turn @ edge_model
    edge = (edge - blade * edge.dot(blade)).normalized()
    back = axes.transposed()
    held = back @ (at - wrist) / max(length, 1e-6)
    return [round(float(v), DIGITS) for v in (*held, *(back @ blade), *(back @ edge))]


def size_now(holder):
    return rest_world(holder).to_scale().x


def tip_frame(scene, rig):
    """The knife in her fist at rest, in her armature's space, for drafting a pair (execution_drafts.Seat): its blade's
    and edge's ways and its tip; None without a knife."""
    holder = holder_in(scene)
    if holder is None or rig is None:
        return None
    world = rest_world(holder)
    blade_model, edge_model = model_ways(holder)
    tip = marks(holder).get('Tip')
    if tip is None:
        tip = blade_model * .2
    to_arm = _world(rig).inverted()
    turn = to_arm.to_3x3().normalized() @ world.to_3x3().normalized()
    blade = (turn @ blade_model).normalized()
    edge = turn @ edge_model
    return blade, (edge - blade * edge.dot(blade)).normalized(), to_arm @ (world @ tip)


# ---- Kept, picked, saved ----------------------------------------------------------------------------------------------

def moved_away(scene):
    """Knives moved and then picked away from before saving: {class: {'from': nine, 'to': nine}}, written on saving."""
    try:
        found = json.loads(scene.get(SCENE_MOVED, '{}'))
        return found if isinstance(found, dict) else {}
    except (TypeError, ValueError):
        return {}


def _keep_moved(scene, moved):
    text = json.dumps(moved, sort_keys=True)
    if scene.get(SCENE_MOVED, '{}') != text:
        scene[SCENE_MOVED] = text


def _settle(holder):
    """Keys auto-keying put on the knife taken off (it is placed by numbers, never animated); it stays where it is."""
    if holder.animation_data is not None:
        holder.animation_data_clear()


def is_moved(rig, holder):
    return holder is not None and rig is not None and differs(seat_now(rig, holder), list(holder.get(SEAT_PROP, [])))


def ensure(context, rig, root, pick=None):
    """The picked class's knife in her right fist where its row seats it: made (or remade for another model), and put
    where the table says when its numbers changed outside Blender. A part moved here and not yet written stays moved.
    Returns the holder (None when the table has no rows)."""
    scene = context.scene
    _drop_old_preview()
    rows = dict(read_rows(root))
    if not rows or rig is None:
        return None
    holder = holder_in(scene)
    pick = pick or (holder.get(CLASS_PROP) if holder is not None else None) or scene.get(SCENE_PICK) or DEFAULT_ROW
    if pick not in rows:
        pick = DEFAULT_ROW if DEFAULT_ROW in rows else next(iter(rows))
    row = rows[pick]
    model = (row.get('model') or FALLBACK_MODEL).strip()
    if holder is not None and (holder.get(MODEL_PROP) != model or not any(o.type == 'MESH' for o in holder.children_recursive)):
        _remove([holder] + list(holder.children_recursive))
        holder = None
    if holder is None:
        fbx = model_fbx(root, model)
        if fbx is None:
            raise RuntimeError(f'{model} 칼 모델(FBX)을 찾지 못했습니다.')
        holder = _make(context, rig, fbx, model)
    _settle(holder)
    table = canonical(numbers_of(row))
    moved = moved_away(scene)
    if holder.get(CLASS_PROP) == pick and SEAT_PROP in holder:
        start, end = list(holder[SEAT_PROP]), seat_now(rig, holder)
    elif pick in moved:
        change = moved.pop(pick)
        start, end = change['from'], change['to']
        _keep_moved(scene, moved)
    else:
        start = end = table
    # What was moved here stays moved; the rest goes where the table says now.
    want = []
    for group in GROUPS:
        want += end[group] if differs(start[group], end[group]) else table[group]
    scale = scale_of(row)
    if differs(seat_now(rig, holder), want) or abs(size_now(holder) - scale) > 1e-4:
        place(rig, holder, want, scale)
    if list(holder.get(SEAT_PROP, [])) != table:
        holder[SEAT_PROP] = table
    if holder.get(CLASS_PROP) != pick:
        holder[CLASS_PROP] = pick
        holder.name = '칼 · ' + title(root, pick)
    if scene.get(SCENE_PICK) != pick:
        scene[SCENE_PICK] = pick
    return holder


def pick(context, rig, root, row_id):
    """Another class's knife in her fist; the one shown, if moved, is kept to be written on saving."""
    scene = context.scene
    holder = holder_in(scene)
    if holder is not None and holder.get(CLASS_PROP) not in (None, row_id) and is_moved(rig, holder):
        moved = moved_away(scene)
        moved[holder[CLASS_PROP]] = {'from': list(holder[SEAT_PROP]), 'to': seat_now(rig, holder)}
        _keep_moved(scene, moved)
    return ensure(context, rig, root, row_id)


def reset(context, rig, root):
    """The knife back where its row says, its move here (not yet written) dropped."""
    scene = context.scene
    holder = holder_in(scene)
    if holder is None or rig is None:
        return None
    _settle(holder)
    row_id = holder.get(CLASS_PROP)
    row = dict(read_rows(root)).get(row_id, {})
    table = canonical(numbers_of(row))
    place(rig, holder, table, scale_of(row))
    holder[SEAT_PROP] = table
    moved = moved_away(scene)
    if moved.pop(row_id, None) is not None:
        _keep_moved(scene, moved)
    return holder


def send(context, rig, root):
    """On saving: the knife moved in her fist since it was last matched to its row (and any picked away from while
    moved) written into its class's row, only the held, blade or edge numbers that changed. Returns (written labels,
    failures)."""
    scene = context.scene
    holder = holder_in(scene)
    moved = moved_away(scene)
    if holder is not None and rig is not None and CLASS_PROP in holder:
        _settle(holder)
        if is_moved(rig, holder):
            moved[holder[CLASS_PROP]] = {'from': list(holder[SEAT_PROP]), 'to': seat_now(rig, holder)}
    if not moved:
        return [], []
    path = table_file(root)
    shown = holder.get(CLASS_PROP) if holder is not None else None
    written, failed, left = [], [], {}
    for row_id, change in moved.items():
        cells = {}
        for group in GROUPS:
            if differs(change['from'][group], change['to'][group]):
                cells.update({c: _cell(v) for c, v in zip(COLUMNS[group], change['to'][group])})
        try:
            if path is None:
                raise RuntimeError('ClassKnives.csv를 찾지 못했습니다.')
            if cells and write_cells(path, row_id, cells):
                written.append(f'{title(root, row_id)} 칼 쥐는 자리')
            if row_id == shown:
                # Matched to the numbers just written (they round it by under half a millimetre: it isn't moved).
                holder[SEAT_PROP] = canonical(numbers_of(dict(read_rows(root)).get(row_id, {})))
        except Exception as error:
            failed.append(f'{title(root, row_id)} 칼: {error}')
            # The knife shown keeps its move by itself; one picked away from is kept to be written next time.
            if row_id != shown:
                left[row_id] = change
    _keep_moved(scene, left)
    return written, failed
