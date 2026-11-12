# Blender (headless): the HUD's weapon and killer icons, drawn from the guns' own models.
# usage: blender -b --factory-startup --python Tools/Blender/weapon_icons.py -- [key ...] [--preview <folder>] [--out <folder>]
#   no keys: every icon. --preview also writes a contact sheet per style there (grey back, for looking them over).
#   --out writes the PNGs somewhere else than the game's Resources (to try a change out).
#
# What it writes: Assets/_Project/Art/Resources/UI/WeaponIcons/<Style>/<key>.png for the three looks the HUD can use
# (WeaponIcons.csv Look.style): Solid (filled, thin cuts between the parts), Outline (a stroke round the shape, the inside
# faint) and Line (a technical drawing: thin outline and the parts' edges). White on transparent: the HUD tints them.
# A .meta is written only for a new PNG, so import settings changed in Unity stay. It also writes
# Assets/_Project/Data/Data/Resources/Tables/Weapons/WeaponIconSpans.csv: where each picture's drawing lies on its canvas (uv) and how much
# of a gun's length from the muzzle back is its barrel end (front), for the kill feed's flairs that burn only the barrel
# (KillFlair.shader). A run of some keys only updates their rows.
#
# Why from the models (user, 2026-11-12: "무기 아이콘도 이제 좀 바꿀 수 있나? 그래야지 이제 킬로그나 무기 슬롯에 쓸 수
# 있지"): every gun is drawn from its own FBX, side on, muzzle to the right, as kill feeds show them. The KINEMATION
# guns are put together the way the game's prefabs (Data/Guns/TacticalGuns, FPSPackGuns) have them: each bone is
# posed where the prefab has it (a stock pulled in, a bolt back) and each magazine or iron sight sits where the prefab
# hangs it. Unity and Blender read the same FBX into different axes; the bones of every gun show the one map between
# them (Blender = (-x, -z, y) of Unity), so a part's place in the prefab can be carried over exactly. Optics, lights,
# grips and suppressors are left off: an icon names the gun, not the player's setup.
#
# Sizes keep their order: a gun's drawn length is the canvas width times (its length / LONGEST) ** LENGTH_POWER, so a
# pistol comes out a bit under half a rifle's length instead of a fifth (which would be unreadable), and anything too
# tall for the canvas is drawn smaller. Every icon is the same canvas (CANVAS), so any box of that shape shows them
# all at their right sizes, and a modder's own PNG of the same canvas (Documents\ENFILADE\WeaponIcons\<id>.png) fits in.
#
# The killers that are not a weapon (fists, a shove, an execution, a counter, an explosion, a fall) are drawn here as
# flat shapes (SHAPES) and go through the same three looks.
import json
import math
import os
import re
import sys
import hashlib

import bpy
import mathutils
import numpy as np

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = DEFAULT_OUT = os.path.join(ROOT, "Assets", "_Project", "Art", "Resources", "UI", "WeaponIcons")
STYLES = ("Solid", "Outline", "Line")
CANVAS = (512, 160)          # final pixels, every icon
SUPER = 4                    # rendered at this many times the canvas, then averaged down (anti-aliasing)
LONGEST = 1.05               # m: a gun this long fills FILL of the width
FILL = .96
LENGTH_POWER = .5
MOST_HEIGHT = .86            # share of the canvas height a gun may take
SHAPE_HEIGHT = .8            # share of the canvas height a flat killer icon may take
SHAPE_WIDTH = .46            # and of its width (a knife about a pistol's length)
# Lines, in final pixels (the icons are drawn for about a third of their size on screen: slots, menus).
CUT = 3.4                    # Solid: the cut between two parts
CUT_KEEP = 4.0               # Solid: cuts stop this far inside the outline, so the outline is never notched
OUTLINE = 6.0                # Outline: the stroke round the shape
OUTLINE_FILL = .2            # Outline: the inside's opacity
OUTLINE_INNER = .55          # Outline: the parts' edges' opacity
LINE_OUTER = 4.0             # Line: the outline
LINE_INNER = 2.6             # Line: the parts' edges
CREASE_DEGREES = 60.0        # a fold sharper than this between two pixels is a drawn edge
DEPTH_STEP = 14.0            # a depth jump of this many pixel widths between two pixels is a drawn edge (one part in front)
CUT_LENGTH = 40.0            # Solid/Outline: a depth edge shorter than this (final px, joined) is left out
LINE_LENGTH = 16.0           # Line: an edge shorter than this is left out

TAC = "Assets/ThirdParty/KINEMATION/TacticalShooterPack/Meshes/Weapons/"
FPS = "Assets/ThirdParty/KINEMATION/FPSAnimationPack/Animations/"
GUNS_PREFABS = "Assets/_Project/Data/Guns/"
ADDON = "Assets/_Project/Art/Models/Addons/Weapons/"
GRENADES = "Assets/_Project/Art/Models/Addons/Grenades/"
KNIVES = "Assets/_Project/Art/Models/Addons/Knives/"

# key -> how to put it together. prefab/body: the game's prefab and the body FBX's root in it (its bones are posed as
# there). parts: (the part's FBX root in the prefab, its FBX). hide: mesh objects left out. drop: bones whose vertices
# are left out (rounds and shells that the prefab keeps out of sight).
GUNS = {
    "MX16A4": dict(fbx=FPS + "MX16A4/Weapon/A_W_MX16A4_Pose.FBX", front=.42),
    "M1911": dict(fbx=FPS + "M1911/Weapon/A_W_M1911_Idle.FBX", front=.6),
    "AK105": dict(prefab="TacticalGuns/AK105.prefab", front=.45, body=("AK105/SKM_AK105", TAC + "AK105/SKM_AK105.FBX"), parts=[
        ("AK105/SKM_AK105/AK105_Skeleton/Body/Magazine_Parent/SKM_Mag_AK105", TAC + "AK105/SKM_Mag_AK105.FBX")]),
    "Herrington1187": dict(prefab="TacticalGuns/Herrington1187.prefab", front=.45,
                           body=("Herrington1187/SKM_Herrington_11-87_Police", TAC + "Herrington_11-87/SKM_Herrington_11-87_Police.FBX"),
                           drop=["Cartridge"]),
    "MKR9": dict(prefab="TacticalGuns/MKR9.prefab", front=.5, body=("MKR9/SKM_MKR9", TAC + "MKR9/SKM_MKR9.FBX"), parts=[
        ("MKR9/SKM_MKR9/MKR-9_Rig/Body/Mag_Primary/SKM_MKR9_Mag", TAC + "MKR9/Magazine/SKM_MKR9_Mag.FBX"),
        ("MKR9/SKM_MKR9/MKR-9_Rig/Body/ScopeMount/A_MKR9_IronSights/SM_MKR9_IronSights(Clone)", TAC + "MKR9/SM_MKR9_IronSights.fbx")]),
    "MX68": dict(prefab="TacticalGuns/MX68.prefab", front=.45, body=("MX68/SKM_MX68", TAC + "MX68/SKM_MX68.FBX"), parts=[
        ("MX68/SKM_MX68/MX68_Rig/Body/ScopeMount/A_MX68_IronSights/SM_IronSights_MX68(Clone)", TAC + "MX68/Attachments/SM_IronSights_MX68.FBX")]),
    "Mk14EBR": dict(prefab="TacticalGuns/Mk14EBR.prefab", front=.45, body=("Mk14EBR/SKM_Mk14EBR", TAC + "Mk14EBR/SKM_Mk14EBR.FBX"), parts=[
        ("Mk14EBR/SKM_Mk14EBR/Mk14EBR_Body/Body/Mag/SKM_Mk14EBR_Mag", TAC + "Mk14EBR/SKM_Mk14EBR_Mag.FBX")]),
    "R08": dict(prefab="TacticalGuns/R08.prefab", front=.55, body=("R08/SKM_R08", TAC + "R08/SKM_R08.FBX"), drop=["Speedloader"]),
    "SRM12": dict(prefab="TacticalGuns/SRM12.prefab", front=.42, body=("SRM12/SKM_Oryx_SRM-12_Body", TAC + "SRM-12/SKM_Oryx_SRM-12_Body.FBX"), parts=[
        ("SRM12/SKM_Oryx_SRM-12_Body/Body/Magazine/SKM_Mag_SRM-12", TAC + "SRM-12/Attachments/Magazine/SKM_Mag_SRM-12.FBX"),
        ("SRM12/SKM_Oryx_SRM-12_Body/Body/ScopeMount/A_Oryx_SRM-12_IronSights/SKM_TR15_RearSight(Clone)", TAC + "TR15/Attachments/Ironsights/SKM_TR15_RearSight.FBX"),
        ("SRM12/SKM_Oryx_SRM-12_Body/Body/ScopeMount/A_Oryx_SRM-12_IronSights/SKM_TR15_FrontSight(Clone)", TAC + "TR15/Attachments/Ironsights/SKM_TR15_FrontSight.FBX")]),
    "TR15": dict(prefab="TacticalGuns/TR15.prefab", front=.45, body=("TR15/SKM_TR15_Body", TAC + "TR15/SKM_TR15_Body.FBX"), parts=[
        ("TR15/SKM_TR15_Body/Body/Magazine/SKM_Mag_TR15", TAC + "TR15/Attachments/Magazine/SKM_Mag_TR15.FBX"),
        ("TR15/SKM_TR15_Body/Body/ScopeMount/A_TR15_IronSights/SKM_TR15_RearSight", TAC + "TR15/Attachments/Ironsights/SKM_TR15_RearSight.FBX"),
        ("TR15/SKM_TR15_Body/Body/ScopeMount/A_TR15_IronSights/SKM_TR15_FrontSight", TAC + "TR15/Attachments/Ironsights/SKM_TR15_FrontSight.FBX")]),
    "WK11Viper": dict(prefab="TacticalGuns/WK11Viper.prefab", front=.6, body=("WK11Viper/SKM_WK-11_Viper_Body", TAC + "WK-11_Viper/SKM_WK-11_Viper_Body.FBX"), parts=[
        ("WK11Viper/SKM_WK-11_Viper_Body/Body/Magazine/SKM_Mag_WK-11_Viper", TAC + "WK-11_Viper/Magazine/SKM_Mag_WK-11_Viper.FBX")]),
    # The workshop weapons (addon_weapon_to_fbx.py): already true size, tip along Unity +Z, top along Unity +Y.
    "ZZZKatana": dict(fbx=ADDON + "ZZZKatana/ZZZKatana.fbx", hide=["Scabbard"], front=.75),
    "YamatoKatana": dict(fbx=ADDON + "YamatoKatana/YamatoKatana.fbx", hide=["Sheath"], front=.75),
    "CavalrySword": dict(fbx=ADDON + "CavalrySword/CavalrySword.fbx", front=.75),
    "KraitP68": dict(fbx=ADDON + "KraitP68/KraitP68.fbx", hide=["Suppressor", "Light"], front=.6),
    # The class throwables (Throwables; ArcCW GSO's CS:GO grenades): the knife lies flat with its point (its Tip) to the
    # right; the grenades stand upright and each fills the canvas height, since one scale for all made a frag 10 px tall in
    # the kill feed's 22 px box. The disinfectant canister's red cross is only paint, so its body gets a cross cut in.
    "ThrowingKnife": dict(fbx=GRENADES + "ThrowingKnife/ThrowingKnife.fbx", point="Tip", front=.6),
    "GrenadeFrag": dict(fbx=GRENADES + "GrenadeFrag/GrenadeFrag.fbx", front=1.0),
    "GrenadeSmoke": dict(fbx=GRENADES + "GrenadeSmoke/GrenadeSmoke.fbx", front=1.0),
    "GrenadeFlash": dict(fbx=GRENADES + "GrenadeFlash/GrenadeFlash.fbx", front=1.0),
    "GrenadeCanister": dict(fbx=GRENADES + "GrenadeCanister/GrenadeCanister.fbx", emblem="cross", front=1.0),
    # The class knives in the melee slot (ClassKnives.csv icon, MeleeKnife): flat, the point (their Tip) to the right, as
    # the throwing knife lies.
    "Heishang": dict(fbx=KNIVES + "Heishang/Heishang.fbx", point="Tip", front=.6),
    "M9Bayonet": dict(fbx=KNIVES + "M9Bayonet/M9Bayonet.fbx", point="Tip", front=.6),
    "Machete": dict(fbx=KNIVES + "Machete/Machete.fbx", point="Tip", front=.6),
    "Karambit": dict(fbx=KNIVES + "Karambit/Karambit.fbx", point="Tip", front=.6),
    "Butterfly": dict(fbx=KNIVES + "Butterfly/Butterfly.fbx", point="Tip", front=.6),
    "ScissorsShiv": dict(fbx=KNIVES + "ScissorsShiv/ScissorsShiv.fbx", point="Tip", front=.6),
    "PrimalKnife": dict(fbx=KNIVES + "PrimalKnife/PrimalKnife.fbx", point="Tip", front=.6),
}

# Unity -> Blender for these FBX files: x flips, Unity's z (forward) is Blender's -y, Unity's y (up) is Blender's z.
UNITY_TO_BLENDER = np.array([[-1, 0, 0, 0], [0, 0, -1, 0], [0, 1, 0, 0], [0, 0, 0, 1]], dtype=float)
# Blender's FBX import keeps these files' centimetres as a 0.01 scale on each root, and a bone's (or a single-mesh
# root's) own axes are turned 90° about x from Unity's.
ROOT_SCALE = np.diag([.01, .01, .01, 1.])
TURNED = np.array([[.01, 0, 0, 0], [0, 0, -.01, 0], [0, .01, 0, 0], [0, 0, 0, 1]], dtype=float)


# ---- the game's prefab ----------------------------------------------------------------------------------------------

class Prefab:
    """GameObject names and Transforms of a Unity prefab (text YAML)."""

    def __init__(self, path):
        docs, cur = {}, None
        with open(path, encoding="utf-8", errors="ignore") as h:
            for line in h:
                m = re.match(r"--- !u!(\d+) &(-?\d+)", line)
                if m:
                    cur = {"cls": int(m.group(1)), "id": m.group(2), "lines": []}
                    docs[cur["id"]] = cur
                elif cur is not None:
                    cur["lines"].append(line.strip())
        self.names, self.tr = {}, {}
        for d in docs.values():
            if d["cls"] == 1:
                self.names[d["id"]] = _field(d, "m_Name") or ""
            elif d["cls"] == 4:
                self.tr[d["id"]] = {"go": _ref(_field(d, "m_GameObject")), "father": _ref(_field(d, "m_Father")),
                                    "pos": _vec(_field(d, "m_LocalPosition")), "rot": _vec(_field(d, "m_LocalRotation")),
                                    "scale": _vec(_field(d, "m_LocalScale"))}
        self.by_path = {self.path(t): t for t in self.tr}

    def chain(self, tid):
        out = []
        while tid and tid in self.tr:
            out.append(tid)
            tid = self.tr[tid]["father"]
        return out

    def path(self, tid):
        return "/".join(self.names.get(self.tr[t]["go"], "") for t in reversed(self.chain(tid)))

    def name(self, tid):
        return self.names.get(self.tr[tid]["go"], "")

    def world(self, tid):
        m = np.eye(4)
        for t in reversed(self.chain(tid)):
            v = self.tr[t]
            m = m @ trs(v["pos"], v["rot"], v["scale"])
        return m

    def under(self, tid):
        out, todo = [], [tid]
        while todo:
            t = todo.pop()
            out.append(t)
            todo.extend(c for c, v in self.tr.items() if v["father"] == t)
        return out


def _field(doc, key):
    for s in doc["lines"]:
        if s.startswith(key + ":"):
            return s[len(key) + 1:].strip()
    return None


def _ref(text):
    m = re.search(r"fileID: (-?\d+)", text or "")
    return m.group(1) if m and m.group(1) != "0" else None


def _vec(text):
    return [float(x) for x in re.findall(r"[xyzw]: (-?[\d.eE+-]+)", text or "")]


def trs(p, q, s):
    x, y, z, w = q
    r = np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                  [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                  [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
    m = np.eye(4)
    m[:3, :3] = r * np.array(s)[None, :]
    m[:3, 3] = p
    return m


def to_blender(unity):
    return UNITY_TO_BLENDER @ unity @ np.linalg.inv(UNITY_TO_BLENDER)


# ---- building one gun -----------------------------------------------------------------------------------------------

def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_fbx(path):
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=os.path.join(ROOT, path), use_anim=False)
    return [o for o in bpy.data.objects if o not in before]


def pose_from_prefab(arm, pf, root_tid):
    """Pose every bone of the body where the game's prefab has it (relative to the body's root)."""
    inv_root = np.linalg.inv(pf.world(root_tid))
    named = {}
    for t in pf.under(root_tid):
        named.setdefault(pf.name(t), t)
    arm_inv = np.linalg.inv(np.array(arm.matrix_world))
    order, todo = [], [b for b in arm.data.bones if b.parent is None]
    while todo:
        b = todo.pop(0)
        order.append(b)
        todo.extend(b.children)
    moved = 0
    for b in order:
        t = named.get(b.name)
        if t is None:
            continue
        want = to_blender(inv_root @ pf.world(t)) @ TURNED
        local = arm_inv @ want
        rest = np.array(b.matrix_local)
        if np.abs(local - rest).max() > 1e-4:
            moved += 1
        arm.pose.bones[b.name].matrix = mathutils.Matrix(local.tolist())
        bpy.context.view_layer.update()
    return moved


def place_part(objects, pf, root_tid, part_path):
    """Put an imported part (its FBX's root objects) where the prefab hangs it on the body."""
    tid = pf.by_path.get(part_path)
    if tid is None:
        raise KeyError("prefab has no " + part_path)
    where = to_blender(np.linalg.inv(pf.world(root_tid)) @ pf.world(tid))
    for ob in objects:
        if ob.parent is not None:
            continue
        own = TURNED if ob.type == 'MESH' else ROOT_SCALE
        ob.matrix_world = mathutils.Matrix((where @ own).tolist())


def drop_bones(objects, bones):
    """Leave out the vertices that belong mostly to these bones (rounds kept out of sight in the game)."""
    import bmesh
    for ob in objects:
        if ob.type != 'MESH':
            continue
        groups = {g.index: g.name for g in ob.vertex_groups}
        doomed = set()
        for v in ob.data.vertices:
            best, weight = None, 0.0
            for g in v.groups:
                if g.weight > weight:
                    best, weight = groups.get(g.group), g.weight
            if best in bones:
                doomed.add(v.index)
        if not doomed:
            continue
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        bm.verts.ensure_lookup_table()
        bmesh.ops.delete(bm, geom=[bm.verts[i] for i in doomed], context='VERTS')
        bm.to_mesh(ob.data)
        bm.free()


def build_gun(key):
    recipe = GUNS[key]
    reset()
    objects = []
    if "fbx" in recipe:
        objects += import_fbx(recipe["fbx"])
    else:
        pf = Prefab(os.path.join(ROOT, GUNS_PREFABS + recipe["prefab"]))
        root_path, body_fbx = recipe["body"]
        root_tid = pf.by_path[root_path]
        body = import_fbx(body_fbx)
        objects += body
        arm = next(o for o in body if o.type == 'ARMATURE')
        moved = pose_from_prefab(arm, pf, root_tid)
        print("[icons] %s: %d bones posed as in the prefab" % (key, moved))
        for part_path, part_fbx in recipe.get("parts", []):
            part = import_fbx(part_fbx)
            place_part(part, pf, root_tid, part_path)
            objects += part
    hide = set(recipe.get("hide", []))
    for ob in list(objects):
        if ob.type == 'MESH' and ob.name.split(".")[0] in hide:
            bpy.data.objects.remove(ob)
            objects.remove(ob)
    if recipe.get("drop"):
        drop_bones(objects, set(recipe["drop"]))
    # Point the named empty's end to the right (the camera's right is -y): turned half round about z when it points the
    # other way. turn: degrees more about z (to show a grenade's lever side on).
    turn = recipe.get("turn", 0.0)
    if recipe.get("point"):
        tip = next((o for o in objects if o.name.split(".")[0] == recipe["point"]), None)
        if tip is not None and tip.matrix_world.translation.y > 0:
            turn += 180.0
    if turn:
        spin = mathutils.Matrix.Rotation(math.radians(turn), 4, 'Z')
        for ob in objects:
            if ob.parent is None:
                ob.matrix_world = spin @ ob.matrix_world
        bpy.context.view_layer.update()
    return [o for o in objects if o.type == 'MESH']


def mesh_bounds(meshes):
    """Bounds of the evaluated (posed) meshes in world space."""
    depsgraph = bpy.context.evaluated_depsgraph_get()
    lo = np.full(3, np.inf)
    hi = np.full(3, -np.inf)
    for ob in meshes:
        ev = ob.evaluated_get(depsgraph)
        mesh = ev.to_mesh()
        if len(mesh.vertices):
            co = np.empty(len(mesh.vertices) * 3)
            mesh.vertices.foreach_get("co", co)
            co = co.reshape(-1, 3)
            m = np.array(ob.matrix_world)
            w = co @ m[:3, :3].T + m[:3, 3]
            lo = np.minimum(lo, w.min(0))
            hi = np.maximum(hi, w.max(0))
        ev.to_mesh_clear()
    return lo, hi


# ---- rendering the passes -------------------------------------------------------------------------------------------

def render_passes(meshes, tmp, tall=None):
    """Side view (looking along +x: the muzzle, -y, to the right; z up) at SUPER x the canvas. Returns alpha, normal,
    depth and part index as arrays, top row first, and the metres one rendered pixel spans. tall: draw to the scale where
    this many metres fill MOST_HEIGHT of the canvas (things that stand, as the throwables), instead of by length."""
    scene = bpy.context.scene
    lo, hi = mesh_bounds(meshes)
    length, height = hi[1] - lo[1], hi[2] - lo[2]
    W, H = CANVAS
    if tall:
        per_m = MOST_HEIGHT * H / max(tall, height)
        if length * per_m > FILL * W:
            per_m = FILL * W / length
    else:
        share = min(FILL, FILL * (length / LONGEST) ** LENGTH_POWER)
        per_m = share * W / length
        if height * per_m > MOST_HEIGHT * H:
            per_m = MOST_HEIGHT * H / height
    cam = bpy.data.objects.new("Icon Camera", bpy.data.cameras.new("Icon Camera"))
    scene.collection.objects.link(cam)
    scene.camera = cam
    cam.data.type = 'ORTHO'
    cam.data.ortho_scale = W / per_m
    cam.data.clip_start, cam.data.clip_end = .01, 20
    centre = (lo + hi) / 2
    cam.location = (lo[0] - 2.0, centre[1], centre[2])
    cam.rotation_euler = (math.pi / 2, 0, -math.pi / 2)
    for i, ob in enumerate(meshes):
        ob.pass_index = i + 1
    scene.render.engine = 'CYCLES'
    scene.cycles.samples = 1
    scene.cycles.use_denoising = False
    scene.cycles.filter_width = .01
    scene.cycles.max_bounces = 0
    scene.render.film_transparent = True
    scene.render.resolution_x, scene.render.resolution_y = W * SUPER, H * SUPER
    scene.render.resolution_percentage = 100
    layer = scene.view_layers[0]
    layer.use_pass_normal = layer.use_pass_z = layer.use_pass_object_index = True
    scene.use_nodes = True
    tree = scene.node_tree
    for n in list(tree.nodes):
        tree.nodes.remove(n)
    rl = tree.nodes.new("CompositorNodeRLayers")
    out = tree.nodes.new("CompositorNodeOutputFile")
    out.base_path = tmp
    out.format.file_format = 'OPEN_EXR'
    out.format.color_depth = '32'
    out.file_slots.clear()
    sockets = {"image": "Image", "normal": "Normal", "depth": "Depth", "index": "IndexOB"}
    for slot, socket in sockets.items():
        out.file_slots.new(slot)
        tree.links.new(rl.outputs[socket], out.inputs[slot])
    scene.frame_current = 1
    bpy.ops.render.render(write_still=False)
    planes = {}
    for slot in sockets:
        path = os.path.join(tmp, slot + "0001.exr")
        img = bpy.data.images.load(path)
        w, h = img.size
        px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, img.channels)
        planes[slot] = np.flipud(px)
        bpy.data.images.remove(img)
        os.remove(path)
    alpha = planes["image"][..., 3]
    normal = planes["normal"][..., :3]
    depth = planes["depth"][..., 0]
    index = planes["index"][..., 0]
    return alpha, normal, depth, index, 1.0 / (per_m * SUPER)


# ---- the looks ------------------------------------------------------------------------------------------------------

def disk(radius):
    r = int(math.ceil(radius))
    return [(dy, dx) for dy in range(-r, r + 1) for dx in range(-r, r + 1) if dy * dy + dx * dx <= radius * radius + 1e-6]


def dilate(mask, radius):
    if radius <= 0:
        return mask.copy()
    H, W = mask.shape
    out = np.zeros_like(mask)
    for dy, dx in disk(radius):
        ys, yd = (slice(0, H - dy), slice(dy, H)) if dy >= 0 else (slice(-dy, H), slice(0, H + dy))
        xs, xd = (slice(0, W - dx), slice(dx, W)) if dx >= 0 else (slice(-dx, W), slice(0, W + dx))
        out[yd, xd] |= mask[ys, xs]
    return out


def erode(mask, radius):
    return ~dilate(~mask, radius)


def edges(mask, normal=None, depth=None, index=None, pixel_m=1.0):
    """Where two parts meet (separate meshes), one part stands in front of another (a depth jump), and the surface
    folds sharply: three maps, so each look can take what reads at its weight."""
    parts, steps, folds = (np.zeros_like(mask) for _ in range(3))
    crease = math.cos(math.radians(CREASE_DEGREES))
    H, W = mask.shape
    for dy, dx in ((0, 1), (1, 0), (1, 1), (1, -1)):
        a = (slice(0, H - dy), slice(max(0, -dx), W - max(0, dx)))
        b = (slice(dy, H), slice(max(0, dx), W - max(0, -dx)))
        both = mask[a] & mask[b]
        for found, hit in (
                (parts, (np.abs(index[a] - index[b]) > .5) if index is not None else None),
                (steps, (np.abs(depth[a] - depth[b]) > DEPTH_STEP * pixel_m * (1.4 if dx and dy else 1.0))
                 if depth is not None else None),
                (folds, (((normal[a] * normal[b]).sum(-1) / np.maximum(
                    np.linalg.norm(normal[a], axis=-1) * np.linalg.norm(normal[b], axis=-1), 1e-6)) < crease)
                 if normal is not None else None)):
            if hit is None:
                continue
            hit &= both
            found[a] |= hit
            found[b] |= hit
    return parts, steps, folds


NEIGHBOURS = ((0, 1), (0, -1), (1, 0), (-1, 0), (1, 1), (1, -1), (-1, 1), (-1, -1))


def shifted(a, dy, dx, fill):
    H, W = a.shape
    out = np.full_like(a, fill)
    ys, yd = (slice(0, H - dy), slice(dy, H)) if dy >= 0 else (slice(-dy, H), slice(0, H + dy))
    xs, xd = (slice(0, W - dx), slice(dx, W)) if dx >= 0 else (slice(-dx, W), slice(0, W + dx))
    out[yd, xd] = a[ys, xs]
    return out


def keep_long(edge, length):
    """The edge map without its short bits (screws, vents, a rail's teeth): only joined runs of at least this many
    final pixels stay. Labels flow to the smallest index of each run, with pointer jumps to get there fast."""
    if not edge.any():
        return edge
    H, W = edge.shape
    n = H * W
    lab = np.where(edge, np.arange(n, dtype=np.int64).reshape(H, W), n)
    for _ in range(400):
        m = lab
        for dy, dx in NEIGHBOURS:
            m = np.minimum(m, shifted(lab, dy, dx, n))
        flat = np.where(edge, m, n).ravel()
        for _ in range(6):
            ok = flat < n
            nxt = flat.copy()
            nxt[ok] = flat[flat[ok]]
            flat = np.minimum(flat, nxt)
        m = flat.reshape(H, W)
        if np.array_equal(m, lab):
            break
        lab = m
    counts = np.bincount(lab[edge], minlength=n + 1)
    # A marked edge is about two rendered pixels wide (both sides of the jump), so a run's pixels are ~2 x its length.
    return edge & (counts[lab] >= 2 * SUPER * length)


def looks(mask, parts, steps, folds):
    """The three looks at the rendered size, as opacity 0-1."""
    s = SUPER
    bold = parts | keep_long(steps, CUT_LENGTH)
    fine = parts | keep_long(steps | folds, LINE_LENGTH)
    inside = erode(mask, CUT_KEEP * s)
    cut = dilate(bold & inside, CUT * s / 2)
    solid = (mask & ~cut).astype(np.float32)
    ring = (mask & ~erode(mask, OUTLINE * s)).astype(np.float32)
    inner_line = dilate(bold & erode(mask, OUTLINE * s * .6), CUT * s / 2).astype(np.float32)
    outline = np.maximum.reduce([ring, mask * OUTLINE_FILL, inner_line * OUTLINE_INNER])
    line_outer = (mask & ~erode(mask, LINE_OUTER * s)).astype(np.float32)
    line_inner = (dilate(fine, LINE_INNER * s / 2) & mask).astype(np.float32)
    line = np.maximum(line_outer, line_inner)
    return {"Solid": solid, "Outline": outline, "Line": line}


def shrink(plane):
    H, W = plane.shape
    return plane.reshape(H // SUPER, SUPER, W // SUPER, SUPER).mean(axis=(1, 3))


# ---- the flat killer icons -------------------------------------------------------------------------------------------
# Shapes in a 100 x 100 box (y up), each a list of (part, polygon); parts meeting draw a cut/edge between them.

def circle(cx, cy, r, steps=48, start=0.0, end=360.0):
    return [(cx + r * math.cos(math.radians(start + (end - start) * i / steps)),
             cy + r * math.sin(math.radians(start + (end - start) * i / steps))) for i in range(steps + 1)]


def quad(p0, p1, p2, steps=16):
    """A quadratic curve from p0 toward p1 to p2, without p0 (to chain)."""
    return [((1 - t) ** 2 * p0[0] + 2 * (1 - t) * t * p1[0] + t * t * p2[0],
             (1 - t) ** 2 * p0[1] + 2 * (1 - t) * t * p1[1] + t * t * p2[1]) for t in (i / steps for i in range(1, steps + 1))]


def capsule(x0, y0, x1, y1, r, steps=12):
    a = math.atan2(y1 - y0, x1 - x0)
    pts = []
    for i in range(steps + 1):
        t = a + math.pi / 2 + math.pi * i / steps
        pts.append((x0 + r * math.cos(t), y0 + r * math.sin(t)))
    for i in range(steps + 1):
        t = a - math.pi / 2 + math.pi * i / steps
        pts.append((x1 + r * math.cos(t), y1 + r * math.sin(t)))
    return pts


def star(cx, cy, outer, inner, points, turn=0.0, jitter=()):
    pts = []
    for i in range(points * 2):
        r = outer if i % 2 == 0 else inner
        if jitter and i % 2 == 0:
            r *= jitter[(i // 2) % len(jitter)]
        a = math.radians(turn + 180.0 * i / points)
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def arrow_arc(cx, cy, r, width, start, end, head):
    """A thick arc from start to end degrees (counter-clockwise when end > start) with an arrow head at its end."""
    steps = 40
    outer = circle(cx, cy, r + width / 2, steps, start, end)
    inner = circle(cx, cy, r - width / 2, steps, start, end)
    e = math.radians(end)
    d = 1 if end > start else -1
    tip_a = e + d * head / r
    tip = (cx + r * math.cos(tip_a), cy + r * math.sin(tip_a))
    wing_o = (cx + (r + width * 1.25) * math.cos(e), cy + (r + width * 1.25) * math.sin(e))
    wing_i = (cx + (r - width * 1.25) * math.cos(e), cy + (r - width * 1.25) * math.sin(e))
    return outer + [wing_o, tip, wing_i] + inner[::-1]


SHAPES = {
    # A combat knife, point to the right (the execution mark's knife, CombatUIAssets, laid flat): handle with its
    # pommel, guard, and a blade whose belly curves up to a clipped point.
    "execution": [
        ("handle", [(3, 47), (6, 43.5), (12, 42), (34, 43), (36, 44.5), (36, 55.5), (34, 57), (12, 58), (6, 56.5), (3, 53)]),
        ("guard", [(36, 37), (41, 37), (41, 63), (36, 63)]),
        ("blade", [(41, 42)] + quad((41, 42), (86, 35), (99, 51)) + quad((99, 51), (88, 57.5), (73, 60)) + [(41, 60)]),
    ],
    # A burst: the blast and its hot core.
    "explosion": [
        ("blast", star(50, 50, 50, 26, 11, turn=8, jitter=(1, .86, .95, .8, 1, .9, .84, .97, .88, .93, .82))),
        ("core", star(50, 50, 24, 13, 7, turn=-10)),
    ],
    # The return arrow of a counter: an arc turning back on itself.
    "counter": [
        ("arrow", arrow_arc(52, 50, 30, 13, 200, -95, 22)),
    ],
    # A shove: an open hand, palm out, fingers up.
    "shove": [
        ("palm", [(26, 4), (70, 4), (76, 24), (78, 54), (24, 54), (22, 30)]),
        ("index", capsule(31, 52, 30, 86, 7)),
        ("middle", capsule(44, 54, 44, 94, 7)),
        ("ring", capsule(57, 54, 58, 90, 7)),
        ("little", capsule(70, 50, 72, 78, 6.2)),
        ("thumb", capsule(24, 30, 6, 52, 7)),
    ],
    # A headshot, Counter-Strike's way (the user, 2026-11-12: "헤드샷은 Counter Strike처럼 두개골 모양이었으면 좋겠어 직관적이게"):
    # a skull from the front, its dome, cheekbones and jaw; eyes, nose and the gaps between the teeth cut through (parts
    # named hole...). The kill feed shows it after the weapon in a square (the canvas's middle), so it fits a square.
    "headshot": [
        ("skull", circle(50, 57, 35, 40, 180, 0) + [(85, 50), (81, 41), (74, 35), (72, 27), (69, 21), (67, 7), (33, 7), (31, 21),
                                                         (28, 27), (26, 35), (19, 41), (15, 50)]),
        ("hole eye left", circle(35.5, 45, 10.5, 24)),
        ("hole eye right", circle(64.5, 45, 10.5, 24)),
        ("hole nose", [(50, 37), (55.5, 26.5), (50, 28.5), (44.5, 26.5)]),
        ("hole tooth 1", [(38.6, 8), (41.2, 8), (41.2, 18.5), (38.6, 18.5)]),
        ("hole tooth 2", [(45.1, 7), (47.7, 7), (47.7, 19), (45.1, 19)]),
        ("hole tooth 3", [(52.3, 7), (54.9, 7), (54.9, 19), (52.3, 19)]),
        ("hole tooth 4", [(58.8, 8), (61.4, 8), (61.4, 18.5), (58.8, 18.5)]),
    ],
    # A fall: an arrow straight down onto the ground.
    "fall": [
        ("arrow", [(40, 98), (60, 98), (60, 44), (76, 44), (50, 16), (24, 44), (40, 44)]),
        ("ground", [(6, 2), (94, 2), (94, 10), (6, 10)]),
    ],
}


# Flat icons the game already has as pictures, put through the same looks.
PICTURES = {
    "fist": "Assets/_Project/Art/Art/Resources/UI/Icons/Fist.png",
}


def cross_in(mask):
    """A plus sign cut into the middle of a shape's body (the canister's painted red cross): arms a third of the body's
    width long and a ninth thick, centred a little below the middle of its height (under the cap)."""
    rows = np.where(mask.any(1))[0]
    cols = np.where(mask.any(0))[0]
    out = np.zeros_like(mask)
    if len(rows) == 0:
        return out
    top, bottom, left, right = rows[0], rows[-1], cols[0], cols[-1]
    cy, cx = top + (bottom - top) * .58, (left + right) / 2
    arm, half = (right - left) * .34, (right - left) * .11
    ys, xs = np.mgrid[0:mask.shape[0], 0:mask.shape[1]]
    out |= (np.abs(xs - cx) <= half) & (np.abs(ys - cy) <= arm)
    out |= (np.abs(ys - cy) <= half) & (np.abs(xs - cx) <= arm)
    return out & mask


def flat_shape(key):
    """A flat killer icon as (mask, part joins, no depth edges, no folds) at the rendered size. Its own outline is fitted
    into SHAPE_HEIGHT of the canvas height and SHAPE_WIDTH of its width, centred."""
    W, H = CANVAS[0] * SUPER, CANVAS[1] * SUPER
    if key in PICTURES:
        return picture_shape(PICTURES[key], W, H)
    polys = [poly for _, poly in SHAPES[key]]
    holes = [name.startswith("hole") for name, _ in SHAPES[key]]
    xs_all = [x for poly in polys for x, _ in poly]
    ys_all = [y for poly in polys for _, y in poly]
    x0, x1, y0, y1 = min(xs_all), max(xs_all), min(ys_all), max(ys_all)
    scale = min(SHAPE_HEIGHT * H / (y1 - y0), SHAPE_WIDTH * W / (x1 - x0))
    ox = W / 2 - (x0 + x1) / 2 * scale
    oy = H / 2 + (y0 + y1) / 2 * scale
    ys, xs = np.mgrid[0:H, 0:W]
    px, py = xs + .5, ys + .5
    parts = np.zeros((H, W), np.int32)
    for n, poly in enumerate(polys):
        pts = [(ox + x * scale, oy - y * scale) for x, y in poly]
        inside = np.zeros((H, W), bool)
        j = len(pts) - 1
        for i in range(len(pts)):
            xi, yi = pts[i]
            xj, yj = pts[j]
            cross = ((yi > py) != (yj > py)) & (px < (xj - xi) * (py - yi) / ((yj - yi) if yj != yi else 1e-9) + xi)
            inside ^= cross
            j = i
        # A hole cuts through what is under it; a part covers it.
        parts[inside] = 0 if holes[n] else n + 1
    mask = parts > 0
    joins, _, _ = edges(mask, index=parts.astype(np.float32))
    none = np.zeros_like(mask)
    return mask, joins, none, none


def picture_shape(path, W, H):
    """A flat icon from a white-on-transparent picture already in the game (Resources/UI/Icons), fitted the same way."""
    img = bpy.data.images.load(os.path.join(ROOT, path))
    w, h = img.size
    px = np.flipud(np.array(img.pixels[:], dtype=np.float32).reshape(h, w, img.channels))
    bpy.data.images.remove(img)
    alpha = px[..., 3] if px.shape[2] == 4 else px[..., 0]
    rows = np.where(alpha.max(1) > .5)[0]
    cols = np.where(alpha.max(0) > .5)[0]
    alpha = alpha[rows[0]:rows[-1] + 1, cols[0]:cols[-1] + 1]
    h, w = alpha.shape
    scale = min(SHAPE_HEIGHT * H / h, SHAPE_WIDTH * W / w)
    sh, sw = max(1, int(round(h * scale))), max(1, int(round(w * scale)))
    yi = np.minimum((np.arange(sh) / scale).astype(int), h - 1)
    xi = np.minimum((np.arange(sw) / scale).astype(int), w - 1)
    mask = np.zeros((H, W), bool)
    y0, x0 = (H - sh) // 2, (W - sw) // 2
    mask[y0:y0 + sh, x0:x0 + sw] = alpha[yi][:, xi] > .5
    none = np.zeros_like(mask)
    return mask, none, none, none


# ---- writing --------------------------------------------------------------------------------------------------------

META = """fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 1
    sRGBTexture: 1
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 1
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 2048
  textureSettings:
    serializedVersion: 2
    filterMode: 2
    aniso: 1
    mipBias: 0
    wrapU: 1
    wrapV: 1
    wrapW: 1
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: 0
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: 100
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  spriteGenerateFallbackPhysicsShape: 1
  alphaUsage: 1
  alphaIsTransparency: 1
  spriteTessellationDetail: -1
  textureType: 0
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 0
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 2048
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 2
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    customData:
    physicsShape: []
    bones: []
    spriteID:
    internalID: 0
    vertices: []
    indices:
    edges: []
    weights: []
    secondaryTextures: []
    spriteCustomMetadata:
      entries: []
    nameFileIdTable: {{}}
  mipmapLimitGroupName:
  pSDRemoveMatte: 0
  userData:
  assetBundleName:
  assetBundleVariant:
"""


def table_file(root, name, new_in="Clips"):
    """The game's table of this name under the project root. Since 0.9.0 the tables are filed in sub-folders of
    Data/Resources/Tables by subject and found by file name alone, as the game finds them (GameTables); a table that isn't
    there yet is made in new_in."""
    base = os.path.join(root, "Assets", "_Project", "Data", "Resources", "Tables")
    for folder, _, files in sorted(os.walk(base)):
        if name + ".csv" in files:
            return os.path.join(folder, name + ".csv")
    return os.path.join(base, new_in, name + ".csv")


SPANS = table_file(ROOT, "WeaponIconSpans", new_in="Weapons")
SPANS_NOTE = ("#설명 (Tools/Blender/weapon_icons.py가 씀: 그림마다 그린 것이 캔버스(512x160)의 어디에 있는지 uv로(왼쪽 0 오른쪽 1, 아래 0 위 1) "
              "그리고 총구에서부터 몇 할이 총열 쪽인지(front). 킬 피드의 CHARGE 불꽃이 총구에서 총열까지만 태우는 데 쓴다(KillFlair.shader). "
              "모드의 그림은 줄이 없으면 캔버스 전체로 본다)")


def span_of(mask, front):
    """Where the drawing lies on its canvas, in uv (v up), and its barrel's share from the muzzle back."""
    rows = np.where(mask.any(1))[0]
    cols = np.where(mask.any(0))[0]
    H, W = mask.shape
    if len(rows) == 0:
        return (0.0, 1.0, 0.0, 1.0, front)
    return (cols[0] / W, (cols[-1] + 1) / W, 1.0 - (rows[-1] + 1) / H, 1.0 - rows[0] / H, front)


def write_spans(spans, elsewhere):
    """Updates the span table's rows for the icons just drawn (the game's, or one beside --out's pictures)."""
    path = os.path.join(OUT, "WeaponIconSpans.csv") if elsewhere else SPANS
    rows = {}
    if os.path.exists(path):
        with open(path, encoding="utf-8-sig") as f:
            for line in f.read().splitlines()[1:]:
                cells = line.split(",")
                if len(cells) >= 6 and cells[0] and not cells[0].startswith("#") and not cells[0].startswith('"#'):
                    rows[cells[0]] = cells[1:6]
    for key, (left, right, bottom, top, front) in spans.items():
        rows[key] = ["%.4f" % left, "%.4f" % right, "%.4f" % bottom, "%.4f" % top, "%.2f" % front]
    text = "id,left,right,bottom,top,front\n" + '"' + SPANS_NOTE + '",,,,,\n'
    text += "".join("%s,%s\n" % (key, ",".join(rows[key])) for key in sorted(rows, key=str.lower))
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8-sig", newline="\n") as f:
        f.write(text)
    meta = path + ".meta"
    rel = project_path(path)
    if rel and not os.path.exists(meta):
        guid = hashlib.md5(("ENFILADE weapon icon " + rel).encode("utf-8")).hexdigest()
        with open(meta, "w", encoding="utf-8", newline="\n") as f:
            f.write("fileFormatVersion: 2\nguid: %s\nTextScriptImporter:\n  externalObjects: {}\n  userData: \n"
                    "  assetBundleName: \n  assetBundleVariant: \n" % guid)


def write_png(path, opacity):
    """White with the given opacity, top row first."""
    h, w = opacity.shape
    rgba = np.ones((h, w, 4), np.float32)
    rgba[..., 3] = np.clip(opacity, 0, 1)
    img = bpy.data.images.new("icon", w, h, alpha=True)
    img.pixels = np.flipud(rgba).ravel()
    img.filepath_raw = path
    img.file_format = 'PNG'
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save()
    bpy.data.images.remove(img)
    meta = path + ".meta"
    rel = project_path(path)
    if rel and not os.path.exists(meta):
        guid = hashlib.md5(("ENFILADE weapon icon " + rel).encode("utf-8")).hexdigest()
        with open(meta, "w", encoding="utf-8", newline="\n") as f:
            f.write(META.format(guid=guid))


def project_path(path):
    """The path inside the project (forward slashes), or None for a folder outside it (no .meta there)."""
    full = os.path.abspath(path)
    if not full.lower().startswith(os.path.join(ROOT, "Assets").lower()):
        return None
    return os.path.relpath(full, ROOT).replace("\\", "/")


def folder_meta(path):
    os.makedirs(path, exist_ok=True)
    meta = path + ".meta"
    rel = project_path(path)
    if rel is None or os.path.exists(meta):
        return
    guid = hashlib.md5(("ENFILADE weapon icon folder " + rel).encode("utf-8")).hexdigest()
    with open(meta, "w", encoding="utf-8", newline="\n") as f:
        f.write("fileFormatVersion: 2\nguid: %s\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {}\n"
                "  userData: \n  assetBundleName: \n  assetBundleVariant: \n" % guid)


def sheet(path, planes):
    """Every icon of one look on grey, two to a row, for looking them over."""
    W, H = CANVAS
    cols = 2
    rows = (len(planes) + cols - 1) // cols
    out = np.full((rows * (H + 8), cols * (W + 8), 4), .2, np.float32)
    out[..., 3] = 1
    for n, (_, a) in enumerate(planes):
        y, x = (n // cols) * (H + 8) + 4, (n % cols) * (W + 8) + 4
        out[y:y + H, x:x + W, :3] = out[y:y + H, x:x + W, :3] * (1 - a[..., None]) + a[..., None]
    img = bpy.data.images.new("sheet", out.shape[1], out.shape[0], alpha=True)
    img.pixels = np.flipud(out).ravel()
    img.filepath_raw = path
    img.file_format = 'PNG'
    img.save()
    bpy.data.images.remove(img)


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    global OUT
    preview = None
    if "--preview" in args:
        at = args.index("--preview")
        preview = args[at + 1]
        del args[at:at + 2]
    if "--out" in args:  # somewhere else than the game's Resources, to try a change out
        at = args.index("--out")
        OUT = os.path.abspath(args[at + 1])
        del args[at:at + 2]
    keys = args or list(GUNS) + list(SHAPES) + list(PICTURES)
    tmp = os.path.join(bpy.app.tempdir or os.environ.get("TEMP", "."), "enfilade_icons")
    os.makedirs(tmp, exist_ok=True)
    folder_meta(OUT)
    for style in STYLES:
        folder_meta(os.path.join(OUT, style))
    done = {style: [] for style in STYLES}
    spans = {}
    for key in keys:
        if key in GUNS:
            meshes = build_gun(key)
            alpha, normal, depth, index, pixel_m = render_passes(meshes, tmp, GUNS[key].get("tall"))
            mask = alpha > .5
            if GUNS[key].get("emblem") == "cross":
                mask &= ~cross_in(mask)
            parts, steps, folds = edges(mask, normal, depth, index, pixel_m)
        elif key in SHAPES or key in PICTURES:
            mask, parts, steps, folds = flat_shape(key)
        else:
            print("[icons] unknown key", key)
            continue
        for style, plane in looks(mask, parts, steps, folds).items():
            small = shrink(plane)
            write_png(os.path.join(OUT, style, key + ".png"), small)
            done[style].append((key, small))
        spans[key] = span_of(mask, GUNS[key].get("front", .5) if key in GUNS else 1.0)
        print("[icons] %s done" % key)
    write_spans(spans, OUT != DEFAULT_OUT)
    if preview:
        os.makedirs(preview, exist_ok=True)
        for style in STYLES:
            sheet(os.path.join(preview, "sheet_%s.png" % style), done[style])
    print("[icons] wrote %d icons x %d looks to %s" % (len(done[STYLES[0]]), len(STYLES), OUT))


main()
