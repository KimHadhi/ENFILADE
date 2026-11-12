"""ENFILADE victory poses for the results show (the user, 2026-11-12: "포즈 네가 블렌더나 그런 거 써서 직접 만들어 ...
마요나 이치고 기반으로 만들면 돼", after the Warzone victory video).

Run headless (Blender 4.5):
    blender.exe -b --python Tools/Blender/Poses/make_victory_poses.py -- [--fresh] [--render <folder>] [--no-export]

The first run (or --fresh) imports Mayo (BOOTH, Chocolate rice) and builds each pose from the recipes below by pointing
her bones in directions given in her own frame (right, up, forward), so the poses read the same on any humanoid in
Unity. Each pose is one action at 30 fps: frame 1 a relaxed stand, frame 9 the pose struck, held to frame 75 with a
breath (the chest a hair up at 42). It saves the workspace to ArtSource/Poses/ENFILADE_Poses_Mayo.blend, where the
poses are touched up by hand (ArtSource/Poses/START_HERE.md, the Pose tab of pose_tools.py), and exports every action
as its own FBX (armature only; Y up, -Z forward, bone axes Y and X, and the unit scale in the file with the armature at 1
("FBX All"): the way Mayo's own FBX was written, so her bones and their frames match hers exactly; Blender's own default
scale put the armature at 100 in centimetres, and Unity read her legs as mismatched) to Assets/_Project/Resources/Poses,
where the editor imports it as a
humanoid clip on Mayo's avatar (Editor/Import/PoseImport.cs) and the show finds it (ResultsMannequin).
Once the workspace exists, a plain run never rebuilds it: touch-ups live there. It exports the workspace's poses as they
are (unless --no-export); --fresh throws the touch-ups away and starts again from the recipes.
pose_tools.py loads this file as a module for the recipes, the rig and the export.
"""
import math
import os
import sys

import bpy
from mathutils import Matrix, Quaternion, Vector


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


ROOT = project_root() or os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
MAYO = os.path.join(ROOT, "Assets", "ThirdParty", "BOOTH", "Chocolate rice", "0.MAYO", "FBX", "FBX_MAYO.fbx")
OUT = os.path.join(ROOT, "Assets", "_Project", "Resources", "Poses")
BLEND = os.path.join(ROOT, "ArtSource", "Poses", "ENFILADE_Poses_Mayo.blend")
TOOLS = os.path.join(ROOT, "Tools", "Blender", "Poses", "pose_tools.py")
TOOLS_TEXT = "START HERE - Pose tools.py"

FPS = 30
STRIKE, BREATH, HOLD = 9, 42, 75
# The chest's rise at the breath (degrees, about her right axis; negative lifts it).
BREATH_DEGREES = -1.2
PREFIX = "enf_pose_"

FINGERS = ("Thumb", "Index", "Middle", "Ring", "Little")
JOINTS = ("Proximal", "Intermediate", "Distal")


def unit(*parts):
    v = Vector((0.0, 0.0, 0.0))
    for p in parts:
        v += p
    return v.normalized()


class Rig:
    def __init__(self, arm):
        self.arm = arm
        self.mw = arm.matrix_world.copy()
        self.inv = self.mw.inverted()
        # Her own frame, from her rest pose (so a turned head doesn't skew it): up is the world's, forward from the head to
        # the eyes, right from her left shoulder to her right.
        eyes = (self.rest_head("Eye.L") + self.rest_head("Eye.R")) * .5
        fwd = eyes - self.rest_head("Head")
        fwd.z = 0.0
        self.F = fwd.normalized()
        self.U = Vector((0.0, 0.0, 1.0))
        right = self.rest_head("Upper_arm.R") - self.rest_head("Upper_arm.L")
        right.z = 0.0
        self.R = right.normalized()

    def rest_head(self, name):
        return self.mw @ self.arm.data.bones[name].head_local

    def world_head(self, name):
        return self.mw @ self.arm.pose.bones[name].head

    def world(self, name):
        return self.mw @ self.arm.pose.bones[name].matrix

    def set_world(self, name, m):
        self.arm.pose.bones[name].matrix = self.inv @ m
        bpy.context.view_layer.update()

    def side(self, s):
        """The way out from the body on side s ('L' or 'R')."""
        return self.R if s == "R" else -self.R

    def rest(self, names=None):
        for pb in self.arm.pose.bones:
            if names is not None and pb.name not in names:
                continue
            pb.rotation_mode = 'QUATERNION'
            pb.matrix_basis = Matrix.Identity(4)
        bpy.context.view_layer.update()

    def aim(self, name, direction, roll_axis=None, roll_to=None):
        """Points the bone (its Y axis) along direction; with roll_axis (0 = X, 2 = Z of the bone) turned about it toward roll_to."""
        if name not in self.arm.pose.bones:
            return
        m = self.world(name)
        rot = m.to_3x3()
        y = rot.col[1].normalized()
        d = direction.normalized()
        rot = y.rotation_difference(d).to_matrix() @ rot
        if roll_axis is not None and roll_to is not None:
            a = rot.col[roll_axis]
            a = (a - d * a.dot(d)).normalized()
            b = (roll_to - d * roll_to.dot(d)).normalized()
            if a.length > 1e-4 and b.length > 1e-4:
                rot = a.rotation_difference(b).to_matrix() @ rot
        out = rot.to_4x4()
        out.translation = m.translation
        self.set_world(name, out)

    def turn(self, name, axis, degrees):
        """Turns the bone about a world axis through its head."""
        if name not in self.arm.pose.bones:
            return
        m = self.world(name)
        q = Quaternion(axis.normalized(), math.radians(degrees))
        out = (q.to_matrix() @ m.to_3x3()).to_4x4()
        out.translation = m.translation
        self.set_world(name, out)

    # ---- hands ----

    def hand_axes(self, s):
        """The hand's finger way, its across way (index to little) and its palm's outward normal, measured as it stands."""
        hand = self.world_head("Hand." + s)
        mid = self.world_head("Middle Proximal." + s)
        idx = self.world_head("Index Proximal." + s)
        lit = self.world_head("Little Proximal." + s)
        finger = (mid - hand).normalized()
        across = (lit - idx).normalized()
        palm = across.cross(finger) if s == "L" else finger.cross(across)
        return finger, across, palm.normalized()

    def orient_hand(self, s, finger_to, palm_to):
        """The hand's fingers along finger_to and its palm facing palm_to."""
        name = "Hand." + s
        finger, _, _ = self.hand_axes(s)
        m = self.world(name)
        q = finger.rotation_difference(finger_to.normalized())
        out = (q.to_matrix() @ m.to_3x3()).to_4x4()
        out.translation = m.translation
        self.set_world(name, out)
        finger, _, palm = self.hand_axes(s)
        f = finger.normalized()
        a = (palm - f * palm.dot(f)).normalized()
        b = (palm_to - f * palm_to.dot(f)).normalized()
        if a.length > 1e-4 and b.length > 1e-4:
            m = self.world(name)
            out = (a.rotation_difference(b).to_matrix() @ m.to_3x3()).to_4x4()
            out.translation = m.translation
            self.set_world(name, out)

    def finger_bones(self, s):
        return [f"{f} {j}.{s}" for f in FINGERS for j in JOINTS]

    def curl(self, s, finger, angles, spread=0.0):
        """Bends one finger's three joints toward the palm by these angles (degrees), spread sideways first."""
        names = [f"{finger} {j}.{s}" for j in JOINTS]
        if names[0] not in self.arm.pose.bones:
            return
        _, across, palm = self.hand_axes(s)
        if spread:
            self.turn(names[0], palm, spread if s == "L" else -spread)
        for name, angle in zip(names, angles):
            if not angle or name not in self.arm.pose.bones:
                continue
            m = self.world(name)
            y = m.to_3x3().col[1].normalized()
            if finger == "Thumb":
                # The thumb folds across the palm, toward the little finger's side.
                axis = y.cross(palm + across * .8)
            else:
                axis = y.cross(palm)
            if axis.length < 1e-4:
                continue
            self.turn(name, axis, angle)

    def hand_shape(self, s, shape):
        """One hand's fingers from straight into a shape: fist, v, point, flat, or relaxed."""
        fist = (78, 95, 62)
        relaxed = (18, 24, 14)
        if shape == "fist":
            for f in FINGERS[1:]:
                self.curl(s, f, fist)
            self.curl(s, "Thumb", (20, 30, 28))
        elif shape == "v":
            self.curl(s, "Index", (2, 0, 0), spread=-9)
            self.curl(s, "Middle", (2, 0, 0), spread=7)
            self.curl(s, "Ring", fist)
            self.curl(s, "Little", fist)
            self.curl(s, "Thumb", (24, 34, 30))
        elif shape == "point":
            self.curl(s, "Index", (0, 2, 0))
            for f in ("Middle", "Ring", "Little"):
                self.curl(s, f, fist)
            self.curl(s, "Thumb", (10, 18, 10))
        elif shape == "flat":
            for f in FINGERS[1:]:
                self.curl(s, f, (4, 4, 2))
            self.curl(s, "Thumb", (6, 6, 0))
        else:
            for i, f in enumerate(FINGERS[1:]):
                self.curl(s, f, tuple(a + i * 4 for a in relaxed))
            self.curl(s, "Thumb", (8, 10, 6))


# ---- the poses, in her own frame: R her right, U up, F forward ----

def arm_down(r, s, out=.16, forward=.06, bend=.35):
    S = r.side(s)
    r.aim(f"Upper_arm.{s}", unit(-r.U * .97, S * out, r.F * forward))
    r.aim(f"Lower_arm.{s}", unit(-r.U * .93, S * (out * .3), r.F * bend))
    r.orient_hand(s, unit(-r.U * .95, r.F * (bend * .5)), -S)


def hand_on_hip(r, s):
    S = r.side(s)
    r.aim(f"Upper_arm.{s}", unit(-r.U * .62, S * .68, -r.F * .18))
    r.aim(f"Lower_arm.{s}", unit(-r.U * .42, -S * .82, r.F * .26))
    r.orient_hand(s, unit(-r.U * .72, -S * .45, r.F * .12), -S)
    r.hand_shape(s, "flat")


def idle(r):
    for s in ("L", "R"):
        arm_down(r, s)
        r.hand_shape(s, "relaxed")


def fist_pump(r):
    # The MVP: a fist thrown up high, chest up, chin up.
    r.turn("Chest", r.R, -7)
    r.turn("Head", r.R, -9)
    S = r.side("R")
    r.aim("Upper_arm.R", unit(r.U * .82, S * .45, r.F * .28))
    r.aim("Lower_arm.R", unit(r.U * .98, r.F * .12, -S * .06))
    r.orient_hand("R", unit(r.U * .97, r.F * .1), unit(-r.F * .55, -S * .45))
    r.hand_shape("R", "fist")
    arm_down(r, "L", out=.2, bend=.55)
    r.hand_shape("L", "fist")
    # Feet a little apart (a turn about forward carries a foot toward her left for her left leg).
    r.turn("Upper_leg.L", r.F, 5)
    r.turn("Upper_leg.R", r.F, -5)


def v_sign(r):
    # Anime's own: a V by the cheek, the other hand on the hip, head tilted toward the V.
    S = r.side("R")
    r.turn("Spine", r.F, 3)
    r.turn("Head", r.F, 9)
    r.aim("Upper_arm.R", unit(-r.U * .42, S * .66, r.F * .62))
    r.aim("Lower_arm.R", unit(r.U * .92, -S * .14, r.F * .12))
    r.orient_hand("R", unit(r.U * .95, -S * .05, r.F * .08), r.F)
    r.hand_shape("R", "v")
    hand_on_hip(r, "L")


def point(r):
    # Points straight at the camera, the other hand on the hip.
    S = r.side("R")
    r.turn("Chest", r.U, -10)
    r.turn("Head", r.R, 4)
    r.aim("Upper_arm.R", unit(r.F * .8, S * .52, r.U * .06))
    r.aim("Lower_arm.R", unit(r.F * .9, S * .3, r.U * .1))
    r.orient_hand("R", unit(r.F * .9, S * .3, r.U * .08), unit(-r.U * .75, -S * .66))
    r.hand_shape("R", "point")
    hand_on_hip(r, "L")


def salute(r):
    # A salute at the brow, chest up.
    S = r.side("R")
    r.turn("Chest", r.R, -4)
    r.turn("Head", r.R, -4)
    r.aim("Upper_arm.R", unit(S * .82, r.F * .42, r.U * .2))
    r.aim("Lower_arm.R", unit(-S * .5, r.U * .75, r.F * .38))
    r.orient_hand("R", unit(-S * .72, r.U * .38, r.F * .25), unit(r.F * .75, -r.U * .6))
    r.hand_shape("R", "flat")
    arm_down(r, "L", out=.08, bend=.12)
    r.hand_shape("L", "flat")


def hands_on_hips(r):
    # The power stand: both hands on the hips, chest out.
    r.turn("Chest", r.R, -6)
    r.turn("Head", r.R, -3)
    hand_on_hip(r, "L")
    hand_on_hip(r, "R")
    r.turn("Upper_leg.L", r.F, 6)
    r.turn("Upper_leg.R", r.F, -6)


POSES = {
    PREFIX + "fist": fist_pump,
    PREFIX + "vsign": v_sign,
    PREFIX + "point": point,
    PREFIX + "salute": salute,
    PREFIX + "hips": hands_on_hips,
}


def set_fps():
    render = bpy.context.scene.render
    render.fps, render.fps_base = FPS, 1.0
    bpy.context.scene.frame_start, bpy.context.scene.frame_end = 1, HOLD


def key_all(r, frame):
    for pb in r.arm.pose.bones:
        pb.rotation_mode = 'QUATERNION'
        pb.keyframe_insert("rotation_quaternion", frame=frame)
        if pb.name == "Hips":
            pb.keyframe_insert("location", frame=frame)


def key_pose(r):
    """The pose as it stands now becomes the struck pose: keyed at the strike and the hold, with the breath between."""
    key_all(r, STRIKE)
    key_all(r, HOLD)
    chest = r.arm.pose.bones.get("Chest")
    kept = chest.matrix_basis.copy() if chest else None
    if chest:
        r.turn("Chest", r.R, BREATH_DEGREES)
    key_all(r, BREATH)
    if chest:
        chest.matrix_basis = kept
        bpy.context.view_layer.update()


def build(r, name, pose):
    """A new action for this pose from its recipe: relaxed at frame 1, struck at 9, held with a breath to 75."""
    action = bpy.data.actions.new(name)
    action.use_fake_user = True
    r.arm.animation_data_create()
    r.arm.animation_data.action = action
    r.rest()
    idle(r)
    key_all(r, 1)
    r.rest()
    pose(r)
    key_pose(r)
    return action


def rebuild(r, name):
    """Throws this pose's touch-ups away: its action made again from its recipe, under the same name."""
    recipe = POSES.get(name)
    if recipe is None:
        return None
    old = bpy.data.actions.get(name)
    if old is not None:
        old.name = name + "__old"
    action = build(r, name, recipe)
    if old is not None:
        bpy.data.actions.remove(old)
    return action


def pose_actions():
    return sorted((a for a in bpy.data.actions if a.name.startswith(PREFIX)), key=lambda a: a.name)


def export(r, action):
    """This action as Assets/_Project/Resources/Poses/<its name>.fbx (armature only), for Unity."""
    os.makedirs(OUT, exist_ok=True)
    set_fps()
    view_layer = bpy.context.view_layer
    was_active = view_layer.objects.active
    was_selected = [o for o in view_layer.objects if o.select_get()]
    was_action = r.arm.animation_data.action if r.arm.animation_data else None
    r.arm.animation_data_create()
    r.arm.animation_data.action = action
    for o in view_layer.objects:
        o.select_set(o == r.arm)
    view_layer.objects.active = r.arm
    path = os.path.join(OUT, action.name + ".fbx")
    bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={'ARMATURE'}, add_leaf_bones=False,
                             bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False,
                             bake_anim_force_startend_keying=True, apply_scale_options='FBX_SCALE_ALL',
                             axis_forward='-Z', axis_up='Y')
    r.arm.animation_data.action = was_action or action
    for o in view_layer.objects:
        o.select_set(o in was_selected)
    view_layer.objects.active = was_active
    print("EXPORTED", path)
    return path


def render(r, folder):
    os.makedirs(folder, exist_ok=True)
    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.render.resolution_x, scene.render.resolution_y = 240, 360
    scene.render.film_transparent = False
    cam_data = bpy.data.cameras.new("Preview")
    cam_data.lens = 50
    cam = bpy.data.objects.new("Preview", cam_data)
    scene.collection.objects.link(cam)
    target = Vector((0.0, 0.0, 0.62))
    scene.camera = cam
    views = {"front": r.F, "side": (r.F + r.R).normalized()}
    for action in pose_actions():
        r.arm.animation_data.action = action
        scene.frame_set(STRIKE)
        for view, toward in views.items():
            cam.location = target + toward * 2.9 + r.U * .05
            cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
            scene.render.filepath = os.path.join(folder, action.name + "_" + view + ".png")
            bpy.ops.render.render(write_still=True)
            print("RENDERED", scene.render.filepath)
    bpy.data.objects.remove(cam)
    bpy.data.cameras.remove(cam_data)


def armature():
    return next(o for o in bpy.data.objects if o.type == 'ARMATURE')


def embed_tools():
    """The Pose tab's script inside the workspace, for Alt+P when the .blend is opened by hand."""
    if not os.path.isfile(TOOLS):
        return
    text = bpy.data.texts.get(TOOLS_TEXT) or bpy.data.texts.new(TOOLS_TEXT)
    with open(TOOLS, encoding="utf-8") as f:
        text.from_string(f.read())


def main():
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    fresh = "--fresh" in args or not os.path.isfile(BLEND)
    folder = args[args.index("--render") + 1] if "--render" in args else None
    if fresh:
        bpy.ops.wm.read_factory_settings(use_empty=True)
        bpy.ops.import_scene.fbx(filepath=MAYO)
        set_fps()
        r = Rig(armature())
        actions = [build(r, name, recipe) for name, recipe in POSES.items()]
    else:
        bpy.ops.wm.open_mainfile(filepath=BLEND)
        r = Rig(armature())
        actions = pose_actions()
        print("WORKSPACE KEPT", BLEND, "(--fresh rebuilds it from the recipes and drops its touch-ups)")
    if "--no-export" not in args:
        for action in actions:
            export(r, action)
    if folder:
        render(r, folder)
    if fresh:
        os.makedirs(os.path.dirname(BLEND), exist_ok=True)
        r.arm.animation_data.action = bpy.data.actions[PREFIX + "fist"]
        bpy.context.scene.frame_set(STRIKE)
        embed_tools()
        bpy.ops.wm.save_as_mainfile(filepath=BLEND, compress=True)
        print("SAVED", BLEND)


if __name__ == "__main__":
    main()
