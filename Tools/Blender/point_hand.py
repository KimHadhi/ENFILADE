"""ENFILADE: the pointing left hand, drawn in Blender (the user, 2026-11-12: "블렌더로 만들어 줘. 왜냐하면 적 핑 찍을
때 가리키는 애니메이션이 있었으면 하니까").

The game's pointing hand (a held F, and the moment an F tap pings an enemy) takes its shape from
Assets/_Project/Data/Resources/Tables/Clips/PointHand.csv. This script reads that table onto a character's left hand in
Blender and writes the hand you pose back into it.

How to use (Blender 4.x):
1. Import a character, e.g. File > Import > FBX: Assets/ThirdParty/BOOTH/Chocolate rice/0.MAYO/FBX/FBX_MAYO.fbx. Any
   humanoid of the Unity project works: its bone names are read from its .fbx.meta (Unity's humanoid map), else guessed
   from the usual names (Hand.L, IndexProximal_L, LeftHandIndex1...).
2. Scripting tab > Text > Open this file > Run Script. The 3D view's sidebar (N key) gets an "ENFILADE" tab.
3. Select the armature and press "게임 손 불러오기": its LEFT hand's fingers take the shape the game gives that body.
4. Pose the left hand's fingers in Pose Mode (only the 15 finger bones count: thumb, index, middle, ring, little, three
   each). The wrist and the arm don't matter: the game turns the hand so the index points at what is pointed at.
5. Press "게임에 저장": PointHand.csv is written. A game running in Unity takes it as soon as the editor has the focus
   again (the console says "[Tables] PointHand.csv ..."); a running game's /point console command keeps the hand up
   to look at.

What is saved: every finger joint whole, as every Blender hand workspace sends a hand (Tools/Blender/HandPaths/
finger_ways.py; FingerWays in the game): for each of the 15 segments the way it points in the hand's own frame (x from
the wrist to the middle knuckle, y the way the palm faces, z = Unity's Cross(x, y)) and how far it is turned about its
own length past the least turn from the rig's rest (index1X ... thumb3Twist, the one row "Hand"). Those come from where
the bones are and how they are turned, never from their tails or axes, so the game lays the same shape on any humanoid
(PointArm, through the body's own hand: PlayerBodyHands.WearFingerWays), whatever its bone axes or finger lengths.
Before every joint went whole, only each joint's way went (rows Index1 ... Thumb3, in a frame from the index knuckle; the last joint by its
bone's tail), and a finger's turn about its own length was whatever the game's frame had left (the user, 2026-11-14,
on every Blender hand losing something on its way to the game: "근본적으로"). Such a table still loads the old way.
Loading puts the table on the rig the way the game puts it on a hand (finger_ways.wear: HandGrip.WearWays), so what
shows here is what the game shows on this body.
The index's last joint always runs straight on from its middle one, as a pointing finger does to its tip (the user,
2026-11-12: "손가락질 왜 손가락 가장 끝 마디가 굽어진 거임?"): the game holds it so whatever is drawn, and loading shows
it so. Saving keeps what is drawn (a tip drawn bent stays bent in the table, and the save says the game straightens it).
"""
import csv
import datetime
import glob
import importlib.util
import io
import math
import os
import re
import sys
import time

import bpy
from mathutils import Matrix, Vector

TABLE = "PointHand"
# The table's row that carries every finger joint (FingerWays' columns, PokeTables.HandWaysRow in the game).
WAYS_ROW = "Hand"
# The shared finger measure every Blender hand workspace uses, from the project root.
FINGER_WAYS = os.path.join("Tools", "Blender", "HandPaths", "finger_ways.py")


def table_file(root, name, new_in="Clips"):
    """The game's table of this name under the project root. Since 0.9.0 the tables are filed in sub-folders of
    Data/Resources/Tables by subject and found by file name alone, as the game finds them (GameTables); a table that isn't
    there yet is made in new_in."""
    base = os.path.join(root, "Assets", "_Project", "Data", "Resources", "Tables")
    for folder, _, files in sorted(os.walk(base)):
        if name + ".csv" in files:
            return os.path.join(folder, name + ".csv")
    return os.path.join(base, new_in, name + ".csv")
HAND = "LeftHand"
FINGERS = ("Index", "Middle", "Ring", "Little", "Thumb")
JOINTS = ("Proximal", "Intermediate", "Distal")
# PointHand.csv's rows in the game's order, with Unity's humanoid names for their bones.
ROWS = [(f"{finger}{j + 1}", f"Left {finger} {JOINTS[j]}") for finger in FINGERS for j in range(3)]
# The index's tip row, laid along its middle joint's row (a pointing finger is straight to its tip).
STRAIGHT_TIP, STRAIGHT_FROM = "Index3", "Index2"
# The same two as segments of the 15 (finger_ways' order: index, middle, ring, little, thumb, knuckle out).
TIP_SEGMENT, FROM_SEGMENT = 2, 1

_metas = {}


def _key(name):
    return re.sub(r"[\s_.\-]", "", name).lower()


def project_root(context):
    """The Unity project this script sits in (Tools/Blender/..), else the folder typed into the panel."""
    paths = []
    here = globals().get("__file__")
    if here:
        paths.append(bpy.path.abspath(here))
    for text in bpy.data.texts:
        if text.filepath and os.path.basename(bpy.path.abspath(text.filepath)).lower() == "point_hand.py":
            paths.append(bpy.path.abspath(text.filepath))
    for path in paths:
        root = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(path)), "..", ".."))
        if os.path.isdir(os.path.join(root, "Assets", "_Project")):
            return root
    typed = bpy.path.abspath(getattr(context.scene, "enfilade_project", "") or "")
    if typed and os.path.isdir(os.path.join(typed, "Assets", "_Project")):
        return os.path.abspath(typed)
    return None


def finger_ways(root):
    """The shared finger measure (finger_ways.py), loaded by its path once, as every hand workspace loads it
    (sys.modules 'enf_finger_ways'), without a __pycache__ beside it. A copy loaded in this Blender before it could put a
    table on a rig (no wear) is read again."""
    module = sys.modules.get("enf_finger_ways")
    if module is not None and hasattr(module, "wear"):
        return module
    spec = importlib.util.spec_from_file_location("enf_finger_ways", os.path.join(root, FINGER_WAYS))
    module = importlib.util.module_from_spec(spec)
    sys.modules["enf_finger_ways"] = module
    writes = sys.dont_write_bytecode
    sys.dont_write_bytecode = True
    try:
        spec.loader.exec_module(module)
    finally:
        sys.dont_write_bytecode = writes
    return module


def _human_of(meta):
    """Unity's humanoid map in an .fbx.meta: humanoid name -> bone name, or None."""
    try:
        stamp = os.path.getmtime(meta)
    except OSError:
        return None
    cached = _metas.get(meta)
    if cached and cached[0] == stamp:
        return cached[1]
    human = None
    try:
        with open(meta, encoding="utf-8", errors="replace") as f:
            text = f.read()
        if "humanDescription" in text:
            pairs = re.findall(r"- boneName: (.+?)\r?\n\s+humanName: (.+?)\r?\n", text)
            human = {h.strip(): b.strip().strip("'\"") for b, h in pairs} or None
    except OSError:
        human = None
    _metas[meta] = (stamp, human)
    return human


def _guess(names):
    """The left hand's bones by the usual names, when no .fbx.meta of the project knows this armature."""
    by_key = {_key(n): n for n in names}

    def find(options):
        for option in options:
            hit = by_key.get(_key(option))
            if hit:
                return hit
        return None

    found = {HAND: find(["Hand.L", "Hand_L", "LeftHand", "Left Hand", "Left wrist", "Wrist.L", "J_Bip_L_Hand", "mixamorig:LeftHand"])}
    for finger in FINGERS:
        for j, joint in enumerate(JOINTS):
            n = j + 1
            found[f"Left {finger} {joint}"] = find([
                f"{finger} {joint}.L", f"{finger}{joint}_L", f"{finger}{joint}.L", f"Left{finger}{joint}", f"Left {finger} {joint}",
                f"LeftHand{finger}{n}", f"mixamorig:LeftHand{finger}{n}", f"{finger}{n}.L", f"{finger}{n}_L", f"{finger}Finger{n}_L",
                f"{finger}Finger{n}.L", f"{finger.lower()}_0{n}_l", f"{finger}_0{n}_L", f"J_Bip_L_{finger}{n}",
            ])
    return found if found[HAND] and found["Left Index Proximal"] else None


def bone_map(context, armature):
    """Humanoid name -> this armature's bone, from the project's .fbx.meta that fits it best, else by name."""
    names = {b.name for b in armature.data.bones}
    root = project_root(context)
    best = None
    if root:
        for meta in glob.glob(os.path.join(root, "Assets", "**", "*.fbx.meta"), recursive=True):
            human = _human_of(meta)
            if not human or human.get(HAND) not in names or human.get("Left Index Proximal") not in names:
                continue
            score = sum(1 for _, h in ROWS if human.get(h) in names)
            if best is None or score > best[0]:
                best = (score, meta, human)
    if best:
        return best[2], os.path.relpath(best[1], root)
    guessed = _guess(names)
    return (guessed, "이름으로 찾음") if guessed else (None, None)


def chains_of(armature, bones):
    """The left hand's fingers as finger_ways takes them (finger -> its three bones, knuckle out; the thumb from its root
    in the palm), and the humanoid names of the bones this armature lacks: a hand is sent whole or not at all, as the
    game reads it (FingerWays.Read)."""
    pose = armature.pose.bones
    chains, missing = {}, []
    for finger in FINGERS:
        names = tuple(bones.get(f"Left {finger} {joint}") for joint in JOINTS)
        missing += [f"Left {finger} {joint}" for joint, name in zip(JOINTS, names) if not _has(pose, name)]
        chains[finger.lower()] = names
    if not _has(pose, bones.get(HAND)):
        missing.insert(0, HAND)
    return chains, missing


def _frame(pose, bones):
    """The hand's own frame from where its bones are (armature space), as the tables of joint ways alone measured it: F wrist
    to index knuckle, A across toward the index, B the back of the hand."""
    hand = pose[bones[HAND]].head
    index = pose[bones["Left Index Proximal"]].head
    side = None
    for name in ("Left Little Proximal", "Left Ring Proximal", "Left Middle Proximal"):
        if _has(pose, bones.get(name)):
            side = pose[bones[name]].head
            break
    if side is None:
        raise RuntimeError("새끼·약지·중지 뿌리 뼈가 없어 손바닥 방향을 알 수 없습니다.")
    f = (index - hand).normalized()
    a = index - side
    a = (a - f * a.dot(f)).normalized()
    b = a.cross(f)
    return f, a, b


def _joint_way(pose, bones, row_index):
    """A finger joint's way out as the tables of joint ways alone took it: to its next joint, the last to its child (an end
    bone) or its tail."""
    human = ROWS[row_index][1]
    name = bones.get(human)
    if not _has(pose, name):
        return None
    bone = pose[name]
    target = None
    if row_index % 3 < 2:
        next_name = bones.get(ROWS[row_index + 1][1])
        if _has(pose, next_name):
            target = pose[next_name].head
    if target is None:
        target = bone.children[0].head if bone.children else bone.tail
    way = target - bone.head
    return way.normalized() if way.length > 1e-6 else None


def _has(pose, name):
    """A bone of that name is in the pose (a missing name is None, which the collection won't take)."""
    return bool(name) and name in pose


def _armature(context):
    obj = context.active_object
    if obj and obj.type == "ARMATURE":
        return obj
    for obj in context.scene.objects:
        if obj.type == "ARMATURE":
            return obj
    return None


def _write(path, text):
    data = text.encode("utf-8")
    # Unity may map the table it has loaded: write in place (never truncate on open), and wait a little if it is held.
    for _ in range(40):
        try:
            if os.path.exists(path):
                with open(path, "r+b") as f:
                    f.seek(0)
                    f.write(data)
                    f.truncate()
            else:
                with open(path, "wb") as f:
                    f.write(data)
            return True
        except OSError:
            time.sleep(0.25)
    return False


def _read(path):
    """The rows of a table of joint ways alone (from before every joint went whole): row id -> its joint's way (x, y, z)."""
    with open(path, encoding="utf-8-sig") as f:
        text = f.read()
    rows = {}
    for record in csv.reader(io.StringIO(text)):
        if len(record) >= 4 and record[0] and not record[0].startswith("#") and record[0] != "id":
            try:
                rows[record[0].strip()] = Vector((float(record[1]), float(record[2]), float(record[3])))
            except ValueError:
                pass
    return rows


def _ways_row(path, fw):
    """The table's every-joint row (WAYS_ROW) as finger_ways' 15 (x, y, z, twist), or None for a table without it: its
    columns are found by name, whatever their case, as the game finds them (CsvTable); a hand is read whole or not at
    all (FingerWays.Read)."""
    with open(path, encoding="utf-8-sig", newline="") as f:
        records = list(csv.reader(f))
    if not records:
        return None
    header = {name.strip().lower(): c for c, name in enumerate(records[0])}
    for record in records[1:]:
        if not record or record[0].strip().lower() != WAYS_ROW.lower():
            continue
        cells = []
        for column in fw.columns():
            at = header.get(column.lower())
            text = record[at].strip() if at is not None and at < len(record) else ""
            if not text:
                return None
            try:
                cells.append(float(text))
            except ValueError:
                return None
        ways = []
        for i in range(0, len(cells), 4):
            way = Vector(cells[i:i + 3])
            if way.length < 1e-3:
                return None
            way.normalize()
            ways.append((way.x, way.y, way.z, cells[i + 3]))
        return ways
    return None


def straight_tip(ways):
    """The ways with the index's last segment along its middle one, its turn about itself kept: the game's pointing finger
    (PointArm), straight to its tip whatever is drawn."""
    held = list(ways)
    held[TIP_SEGMENT] = (*ways[FROM_SEGMENT][:3], ways[TIP_SEGMENT][3])
    return held


def tip_bend(ways):
    """How far (degrees) the drawn index's last segment turns off its middle one."""
    a, b = ways[FROM_SEGMENT][:3], ways[TIP_SEGMENT][:3]
    cross = (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])
    return math.degrees(math.atan2(math.sqrt(sum(c * c for c in cross)), sum(p * q for p, q in zip(a, b))))


def table_text(fw, ways, made):
    """PointHand.csv as the game reads it: the description row, where it was made, and the one row of every finger
    joint's way and twist (finger_ways' columns and cells, to the sixth decimal)."""
    note = ('"#설명 (블렌더로 그린 가리키는 왼손. Tools/Blender/point_hand.py가 쓰고 읽는다. 손가락 마디마다 손 자기 틀에서 '
            '가리키는 쪽과 제 길이를 축으로 더 돈 각도(index1X~thumb3Twist)라서 블렌더에서 잡은 모양이 비틀림까지 그대로 '
            '어느 휴머노이드에나 나온다(PointArm → PlayerBodyHands.WearFingerWays. 투척·수리·칼 작업실과 같은 칸). '
            '검지 끝 마디는 게임이 가운데 마디에서 곧게 잇는다. 손으로 고치지 말고 블렌더에서 고쳐 다시 저장한다. '
            'Hand 줄을 지우면 Poke.csv Point.* 각도로 만든다)"')
    # A cell never holds a comma (the tables are plain comma lists).
    cells = [c.replace(",", " ·") for c in fw.descriptions()]
    lines = [
        "﻿" + ",".join(["id"] + fw.columns()),
        note + "," + ",".join(cells),
        "#만든 곳," + made.replace(",", " "),
        WAYS_ROW + "," + ",".join(fw.cells(ways)),
    ]
    return "\n".join(lines) + "\n"


class ENFILADE_OT_point_hand_save(bpy.types.Operator):
    """Writes the left hand's finger pose to the game (PointHand.csv)"""
    bl_idname = "enfilade.point_hand_save"
    bl_label = "게임에 저장"

    def execute(self, context):
        root = project_root(context)
        armature = _armature(context)
        if not root or not armature:
            self.report({"ERROR"}, "프로젝트 폴더나 아마추어를 찾지 못했습니다.")
            return {"CANCELLED"}
        bones, source = bone_map(context, armature)
        if not bones:
            self.report({"ERROR"}, "왼손 뼈를 찾지 못했습니다(프로젝트의 FBX를 가져와 주세요).")
            return {"CANCELLED"}
        chains, missing = chains_of(armature, bones)
        if missing:
            self.report({"ERROR"}, "왼손 손가락 15마디가 모두 있어야 저장합니다. 없는 뼈: " + ", ".join(missing))
            return {"CANCELLED"}
        context.view_layer.update()
        fw = finger_ways(root)
        ways = fw.measure(armature, bones[HAND], chains)
        made = " / ".join([armature.name, os.path.basename(bpy.data.filepath) or "저장 안 한 blend", source or "",
                           datetime.datetime.now().strftime("%Y-%m-%d %H:%M")])
        path = table_file(root, TABLE)
        if not _write(path, table_text(fw, ways, made)):
            self.report({"ERROR"}, "PointHand.csv를 쓰지 못했습니다(다른 프로그램이 잡고 있음).")
            return {"CANCELLED"}
        bend = tip_bend(ways)
        tip = f" 검지 끝 마디가 {bend:.1f}° 굽어 있어 게임은 가운데 마디에서 곧게 잇습니다(표에는 그린 그대로)." if bend > .5 else ""
        self.report({"INFO"}, f"손가락 마디 15개를 방향·비틀림까지 PointHand.csv에 저장했습니다.{tip} Unity를 누르면 실행 중인 게임에 바로 들어갑니다.")
        return {"FINISHED"}


class ENFILADE_OT_point_hand_load(bpy.types.Operator):
    """Poses the left hand's fingers as the game has them (PointHand.csv)"""
    bl_idname = "enfilade.point_hand_load"
    bl_label = "게임 손 불러오기"
    bl_options = {"REGISTER", "UNDO"}

    def execute(self, context):
        root = project_root(context)
        armature = _armature(context)
        if not root or not armature:
            self.report({"ERROR"}, "프로젝트 폴더나 아마추어를 찾지 못했습니다.")
            return {"CANCELLED"}
        path = table_file(root, TABLE)
        if not os.path.exists(path):
            self.report({"ERROR"}, "PointHand.csv가 없습니다.")
            return {"CANCELLED"}
        bones, _ = bone_map(context, armature)
        if not bones:
            self.report({"ERROR"}, "왼손 뼈를 찾지 못했습니다(프로젝트의 FBX를 가져와 주세요).")
            return {"CANCELLED"}
        fw = finger_ways(root)
        ways = _ways_row(path, fw)
        if ways is not None:
            chains, missing = chains_of(armature, bones)
            if missing:
                self.report({"ERROR"}, "왼손 손가락 15마디가 모두 있어야 게임처럼 놓습니다. 없는 뼈: " + ", ".join(missing))
                return {"CANCELLED"}
            # Every joint as the game puts it on this body's hand, the index straight to its tip as PointArm holds it.
            fw.wear(armature, bones[HAND], chains, straight_tip(ways), update=context.view_layer.update)
            self.report({"INFO"}, "게임의 손 모양을 왼손 손가락 마디 15개에 방향·비틀림까지 놓았습니다.")
            return {"FINISHED"}
        # A table of joint ways alone (rows Index1 ... Thumb3, from before every joint went whole), laid as it was then.
        table = _read(path)
        context.view_layer.update()
        pose = armature.pose.bones
        f, a, b = _frame(pose, bones)
        posed = 0
        # Knuckle first, then out, each joint after its parent has moved.
        for i, (row, human) in enumerate(ROWS):
            want = table.get(STRAIGHT_FROM) if row == STRAIGHT_TIP and STRAIGHT_FROM in table else table.get(row)
            name = bones.get(human)
            if want is None or not _has(pose, name):
                continue
            want = (f * want.x + a * want.y + b * want.z).normalized()
            now = _joint_way(pose, bones, i)
            if now is None:
                continue
            bone = pose[name]
            turn = now.rotation_difference(want).to_matrix().to_4x4()
            head = bone.head.copy()
            bone.matrix = Matrix.Translation(head) @ turn @ Matrix.Translation(-head) @ bone.matrix
            context.view_layer.update()
            posed += 1
        self.report({"INFO"}, f"예전 표(마디 방향만)의 손 모양을 왼손 손가락 마디 {posed}개에 놓았습니다. 저장하면 비틀림까지 담긴 새 표가 됩니다.")
        return {"FINISHED"}


class ENFILADE_PT_point_hand(bpy.types.Panel):
    bl_label = "ENFILADE 가리키는 손"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "ENFILADE"

    def draw(self, context):
        layout = self.layout
        if project_root(context) is None:
            layout.prop(context.scene, "enfilade_project")
        armature = _armature(context)
        layout.label(text=f"아마추어: {armature.name if armature else '없음'}")
        layout.label(text="왼손 손가락 15마디(방향·비틀림)만 씁니다")
        layout.operator(ENFILADE_OT_point_hand_load.bl_idname, icon="IMPORT")
        layout.operator(ENFILADE_OT_point_hand_save.bl_idname, icon="EXPORT")


CLASSES = (ENFILADE_OT_point_hand_save, ENFILADE_OT_point_hand_load, ENFILADE_PT_point_hand)


def register():
    bpy.types.Scene.enfilade_project = bpy.props.StringProperty(
        name="프로젝트 폴더", description="ENFILADE Unity 프로젝트 폴더 (스크립트가 그 안에 있으면 저절로 찾음)", subtype="DIR_PATH")
    for cls in CLASSES:
        try:
            bpy.utils.unregister_class(getattr(bpy.types, cls.__name__))
        except (AttributeError, RuntimeError):
            pass
        bpy.utils.register_class(cls)


if __name__ == "__main__":
    register()
