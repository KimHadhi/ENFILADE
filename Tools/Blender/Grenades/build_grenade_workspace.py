"""Builds ArtSource/Grenades/ENFILADE_Grenade_Animation.blend from the True FPS start file and, with --export, sends its
seven drafts to the game (the same buttons the user presses). Run from the project root:

  blender --background --factory-startup ArtSource/TrueFPS/ENFILADE_TrueFPS_Start.blend
          --python Tools/Blender/Grenades/build_grenade_workspace.py --
          ArtSource/Grenades/ENFILADE_Grenade_Animation.blend --mw <addon folder> [--export] [--force]

<addon folder>: GMod's "[MW] Quick Throwing Knife" (workshop 3650480912) unpacked with Tools/Workshop/workshop.py
extract, outside the repo (models/weapons/eq_knife/c_eq_knife.mdl inside it). It refuses to overwrite an existing
workspace unless --force is given (a polish lives in it). The source .blend is never saved.

Kazusa holds the AK105 in her right hand as the shove workspace has it; her left arm is free for the clips. The previews
in her left hand are the game's own models seated as the game seats them (Tables/Grenades held*, grip*): the frag, and
the MW2019 throwing knife (Art/Models/Addons/Grenades/MWThrowingKnife, Tools/Blender/Addons/MWThrowingKnife.json).

The drafts (30 fps; positions from the eyes in game metres, x right, y up, z forward):
- 수류탄 · 꺼내기 (14): 1 on the gun → 4 down to the belt pouch, open → 6 GRAB → 9 up past the gun → 11 the ring hooked
  on the gun hand's thumb → 12 PIN, pulled away left → 14 the hold (0.433 s).
- 수류탄 · 대기 (37, a loop of 1.2 s): up at the lower left, the hand upright round the grenade, a slow breath (4 mm).
- 수류탄 · 던지기 (19): 1 the hold → 3 a dip → 5 cocked beside the head → 7 the whip → 8 (0.233 s) RELEASE at full
  reach toward the crosshair, fingers opening → 10 follow-through down → 13 → 16 → 19 back on the gun (0.6 s).
- 수류탄 · 톡 던지기 (19): 1 on the gun → 3 the pouch → 4 GRAB → 6 PIN on the way up → 8 cocked → 10 (0.3 s) RELEASE →
  12 follow-through → 15 → 19 on the gun.
- 나이프 · 꺼내기 / 던지기: MW's @draw (19 frames) and @throw (26, the knife gone from frame 11) bone for bone: the right
  arm mirrored onto the left, every frame the wrist, the elbow's way, the hand's turn and each finger joint's way, from
  the viewmodel's camera; at this project's weapon size (0.01905 m a unit, the size its hands are ours) and spread from
  its 50.2 degree view to this camera's 58.7 (the game's 90 at 16:9), so the knife sits where GMod showed it.
- 나이프 · 대기 (49, a loop of 1.6 s): MW's @idle (a still pose) with a breath like the grenade's.
"""
import importlib.util
import json
import math
import os
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector

HERE = Path(__file__).resolve().parent
# No __pycache__ beside the tools in the checkout.
sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location('grenade_tools', HERE / 'grenade_tools.py')
tools = importlib.util.module_from_spec(spec)
sys.modules['grenade_tools'] = tools
spec.loader.exec_module(tools)

GUN_FILE = 'Assets/ThirdParty/KINEMATION/TacticalShooterPack/Meshes/Weapons/AK105/SKM_AK105.FBX'
# Where the AK105 is held from the eyes (the shove workspace's GUN_hold: GunBakes gunFromEyes / GAME_SCALE, camera axes)
# and where KINEMATION's palms hold it, in the gun's axes and this file's units (FPSBody contacts).
GUN_HOLD = (0.0620, -0.0748, -0.2599)
GRIPS = {'CTRL_hand.R': Vector((.016, -.157, -.097)), 'CTRL_hand.L': Vector((-.021, .107, -.046))}
HOLD_CONSTRAINT = 'Hold the gun (throw workspace)'
PREVIEW = '06 Throwable preview - never exported'
MODELS = {'grenade': 'Assets/_Project/Art/Models/Addons/Grenades/GrenadeFrag/GrenadeFrag.fbx',
          'knife': 'Assets/_Project/Art/Models/Addons/Grenades/MWThrowingKnife/MWThrowingKnife.fbx'}
GRENADES_TABLE = 'Assets/_Project/Resources/Tables/Grenades.csv'
# Which row of Tables/Grenades each preview is seated by.
HELD_ROW = {'grenade': 'frag', 'knife': 'knife'}

# The MW viewmodel: its bones' units at this project's weapon size, and its field of view against this camera's.
VM_UNIT = 0.01905
GAME_VFOV = 2 * math.degrees(math.atan(math.tan(math.radians(45)) * 9 / 16))
MW_VFOV = 2 * math.degrees(math.atan(math.tan(math.radians(32)) * .75))
SPREAD = math.tan(math.radians(GAME_VFOV / 2)) / math.tan(math.radians(MW_VFOV / 2))
MW_MDL = 'models/weapons/eq_knife/c_eq_knife.mdl'
MW_ARM = dict(shoulder='j_shoulder_ri', elbow='j_elbow_ri', wrist='j_wrist_ri')
MW_FINGERS = {'index': ('j_index_ri_1', 'j_index_ri_2', 'j_index_ri_3'), 'middle': ('j_mid_ri_1', 'j_mid_ri_2', 'j_mid_ri_3'),
              'ring': ('j_ring_ri_1', 'j_ring_ri_2', 'j_ring_ri_3'), 'little': ('j_pinky_ri_1', 'j_pinky_ri_2', 'j_pinky_ri_3'),
              'thumb': ('j_thumb_ri_1', 'j_thumb_ri_2', 'j_thumb_ri_3')}
MW_KNIFE = 'tag_accessory'
# Where the fist closes on what it holds, from the wrist in hand lengths (across, out of the palm, along): the game holds
# the knife's own grip point there on any hand (Tables/Grenades held*, grip*).
FIST = Vector((0.0, .3, .85))

# The grenade drafts: frame -> (wrist, hand run, palm facing, elbow pole, (index, middle, ring, little) curl, thumb),
# from the eyes in game metres in the view's axes; 'GUN' is the left hand on the AK105's handguard (the start pose).
# Kazusa's left shoulder is 11 cm left, 17 below and 9 behind her eyes and her arm 49 cm long.
OPEN, GRIP, LOOSE = (25, 25, 25, 25), (150, 160, 165, 170), (40, 45, 50, 55)
HOLD_POSE = ((-.18, -.18, .27), (-.12, .86, .49), (.86, .05, -.5), (-.36, -.42, .06), GRIP, 85)


def breath(pose, dy, dz, tilt):
    (x, y, z), (ax, ay, az), palm, (ex, ey, ez), curl, thumb = pose
    return (x, y + dy, z + dz), (ax + tilt, ay, az), palm, (ex, ey + dy * .6, ez), curl, thumb


GRENADE_DRAFTS = {
    'grenade_draw': {
        1: 'GUN',
        4: ((-.17, -.46, .10), (.10, -.95, .30), (.70, .10, -.70), (-.32, -.42, -.10), OPEN, 20),
        6: ((-.16, -.48, .12), (.05, -.95, .30), (.75, .0, -.65), (-.32, -.42, -.10), GRIP, 70),
        9: ((-.09, -.32, .22), (.10, .35, .93), (.80, .45, -.40), (-.34, -.45, -.02), GRIP, 85),
        11: ((-.03, -.25, .28), (.25, .55, .80), (.85, .20, -.50), (-.30, -.42, .05), GRIP, 85),
        12: ((-.10, -.22, .27), (.0, .75, .66), (.88, .15, -.45), (-.34, -.42, .05), GRIP, 85),
        14: HOLD_POSE,
    },
    'grenade_hold': {
        1: HOLD_POSE,
        10: breath(HOLD_POSE, .004, -.002, .02),
        19: HOLD_POSE,
        28: breath(HOLD_POSE, -.004, .002, -.02),
        37: HOLD_POSE,
    },
    'grenade_throw': {
        1: HOLD_POSE,
        3: ((-.20, -.17, .21), (-.30, .90, .25), (.85, .20, -.45), (-.38, -.40, .02), GRIP, 85),
        5: ((-.26, .00, .02), (-.10, .60, -.80), (.60, .55, .55), (-.42, .02, -.06), GRIP, 85),
        7: ((-.13, .03, .29), (-.05, .92, .38), (.15, -.35, .92), (-.32, -.08, .14), GRIP, 80),
        8: ((-.05, .00, .40), (.05, .20, .98), (.10, -.60, .80), (-.26, -.16, .20), LOOSE, 30),
        10: ((.02, -.20, .36), (.15, -.50, .85), (-.10, -.85, -.50), (-.26, -.34, .14), OPEN, 20),
        13: ((-.02, -.32, .28), (.10, -.60, .79), (.10, -.80, -.55), (-.30, -.40, .05), OPEN, 20),
        16: ((-.01, -.27, .36), (.20, -.15, .97), (.40, -.85, -.30), (-.30, -.40, .08), OPEN, 20),
        19: 'GUN',
    },
    'grenade_tap': {
        1: 'GUN',
        3: ((-.17, -.46, .10), (.10, -.95, .30), (.70, .10, -.70), (-.32, -.42, -.10), OPEN, 20),
        4: ((-.16, -.48, .12), (.05, -.95, .30), (.75, .0, -.65), (-.32, -.42, -.10), GRIP, 70),
        6: ((-.05, -.30, .24), (.20, .45, .87), (.85, .30, -.45), (-.32, -.44, .02), GRIP, 85),
        8: ((-.24, -.02, .05), (-.10, .65, -.75), (.60, .55, .55), (-.42, .0, -.04), GRIP, 85),
        9: ((-.13, .03, .29), (-.05, .92, .38), (.15, -.35, .92), (-.32, -.08, .14), GRIP, 80),
        10: ((-.05, .00, .40), (.05, .20, .98), (.10, -.60, .80), (-.26, -.16, .20), LOOSE, 30),
        12: ((.02, -.20, .36), (.15, -.50, .85), (-.10, -.85, -.50), (-.26, -.34, .14), OPEN, 20),
        15: ((-.02, -.30, .30), (.10, -.60, .79), (.10, -.80, -.55), (-.30, -.40, .05), OPEN, 20),
        19: 'GUN',
    },
}
KNIFE_HOLD_FRAMES = 49


def args():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    if not argv or argv[0].startswith('--'):
        raise SystemExit('Give the output .blend path after --')
    if '--mw' not in argv or argv.index('--mw') + 1 >= len(argv):
        raise SystemExit('Give the unpacked [MW] Quick Throwing Knife folder with --mw')
    return Path(argv[0]).resolve(), Path(argv[argv.index('--mw') + 1]).resolve(), '--export' in argv, '--force' in argv


# ---- The gun in the right hand ---------------------------------------------------------------------------------------

def import_fbx(root, rel):
    scene = bpy.context.scene
    fps, base = scene.render.fps, scene.render.fps_base
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(root / rel))
    scene.render.fps, scene.render.fps_base = fps, base
    return [o for o in bpy.data.objects if o not in before]


def gun_in_hand(root, rig, camera, collection, context):
    hold = bpy.data.objects.new('GUN_hold', None)
    hold.empty_display_type = 'PLAIN_AXES'
    hold.empty_display_size = .03
    collection.objects.link(hold)
    hold.parent = camera
    hold.matrix_parent_inverse = Matrix.Identity(4)
    hold.location = GUN_HOLD
    # The gun's axes from the camera's: x right, y along the barrel (the camera looks down its -Z), z up.
    hold.rotation_euler = (-math.pi / 2, 0, 0)
    hold.hide_select = True
    gun = bpy.data.objects.new('GUN', None)
    gun.empty_display_type = 'ARROWS'
    gun.empty_display_size = .06
    collection.objects.link(gun)
    gun.parent = hold
    gun.matrix_parent_inverse = Matrix.Identity(4)
    model = bpy.data.objects.new('GUN_AK105', None)
    model.empty_display_size = .02
    collection.objects.link(model)
    model.parent = gun
    # The gun's FBX root is the game's weapon bone, turned to face along +Y and shrunk to Kazusa's size in this file.
    model.rotation_euler = (0, 0, math.pi)
    model.scale = (1 / tools.GAME_SCALE,) * 3
    for obj in import_fbx(root, GUN_FILE):
        for owner in list(obj.users_collection):
            owner.objects.unlink(obj)
        collection.objects.link(obj)
        if obj.parent is None:
            obj.parent = model
            obj.matrix_parent_inverse = Matrix.Identity(4)
        obj.hide_select = obj.type != 'EMPTY'
        if obj.type == 'ARMATURE':
            obj.show_in_front = False
            obj.data.display_type = 'STICK'
    context.view_layer.update()
    defaults = json.loads(rig.get('truefps_default_pose', '{}'))
    for name, local in GRIPS.items():
        pb = rig.pose.bones[name]
        matrix = pb.matrix.copy()
        matrix.translation = rig.matrix_world.inverted() @ (gun.matrix_world @ local)
        pb.matrix = matrix
        context.view_layer.update()
        if name == 'CTRL_hand.R':
            con = pb.constraints.new('CHILD_OF')
            con.name = HOLD_CONSTRAINT
            con.target = gun
            con.set_inverse_pending = True
        # The left hand's start pose is on the handguard; the clips take it from there and bring it back.
        defaults[name] = {'location': list(pb.location), 'rotation_quaternion': list(pb.rotation_quaternion), 'scale': list(pb.scale)}
    rig['truefps_default_pose'] = json.dumps(defaults)
    context.view_layer.update()
    return gun


# ---- Posing the left arm ---------------------------------------------------------------------------------------------

def place_hand(rig, context, wrist_view, along_view, palm_view, basis):
    """The left hand handle where the wrist goes, turned so the hand runs and faces as wanted (view axes)."""
    hand = rig.pose.bones[tools.ARM_HAND]
    inverse = rig.matrix_world.inverted()
    matrix = hand.matrix.copy()
    matrix.translation = inverse @ tools.to_world(wrist_view, basis, point=True)
    hand.matrix = matrix
    context.view_layer.update()
    want = frame_of(tools.to_world(Vector(along_view), basis), tools.to_world(Vector(palm_view), basis))
    _, along_now, palm_now, _ = tools.hand_frame(rig)
    now = frame_of(along_now, palm_now)
    tools.turn_bone(rig, tools.ARM_HAND, want @ now.transposed())
    context.view_layer.update()


def frame_of(along, palm):
    z = along.normalized()
    y = (palm - z * palm.dot(z)).normalized()
    return Matrix((z.cross(y) * -1, y, z)).transposed()


def place_pole(rig, context, world):
    elbow = rig.pose.bones[tools.ARM_ELBOW]
    matrix = elbow.matrix.copy()
    matrix.translation = rig.matrix_world.inverted() @ world
    elbow.matrix = matrix
    context.view_layer.update()


def set_curls(rig, context, curl, thumb):
    """The fingers closed to these curls (index, middle, ring, little) and the thumb to its closing, from where they are."""
    for _ in range(3):
        context.view_layer.update()
        now = tools.curls(rig)
        for finger, want in zip(tools.FOUR, curl):
            tools.bend_finger(rig, finger, want - now[finger])
        tools.bend_finger(rig, 'thumb', (thumb - now['thumb']) / tools.THUMB_SCALE)
    context.view_layer.update()


def align_fingers(rig, context, ways, basis):
    """Each left finger segment laid along the way wanted (view axes), knuckle out, from the start pose."""
    for finger, segs in ways.items():
        for name, way in zip(tools.FINGERS[finger], segs):
            context.view_layer.update()
            now = tools.bone_way(rig, name)
            want = tools.to_world(Vector(way), basis)
            if now.length < 1e-9 or want.length < 1e-9:
                continue
            tools.turn_bone(rig, name, now.normalized().rotation_difference(want.normalized()).to_matrix())
    context.view_layer.update()


# Each bone's last keyed turn in the draft being keyed: a turn and its negative are the same turn, but keys are blended
# number by number, so each key is kept on the side of the last one (else a half frame between them turns the long way).
_last_turn = {}


def key_turn(pb, frame):
    q = pb.rotation_quaternion.copy()
    last = _last_turn.get(pb.name)
    if last is not None and q.dot(last) < 0:
        q.negate()
        pb.rotation_quaternion = q
    _last_turn[pb.name] = q
    pb.keyframe_insert('rotation_quaternion', frame=frame, group=pb.name)


def key_pose(rig, frame):
    hand, elbow = rig.pose.bones[tools.ARM_HAND], rig.pose.bones[tools.ARM_ELBOW]
    hand.keyframe_insert('location', frame=frame, group=hand.name)
    key_turn(hand, frame)
    elbow.keyframe_insert('location', frame=frame, group=elbow.name)
    for names in tools.FINGERS.values():
        for name in names:
            key_turn(rig.pose.bones[name], frame)


def begin_draft(rig, key):
    _last_turn.clear()
    action = bpy.data.actions.new(tools.MOVES[key]['action'] + '_draft')
    action.use_fake_user = True
    action['enf_layer'] = 'draft'
    data = rig.animation_data or rig.animation_data_create()
    track = data.nla_tracks.get(tools.BASE_TRACK)
    if track is not None:
        track.mute = True
    data.action = action
    data.action_blend_type = 'REPLACE'
    data.action_influence = 1.0
    return action


def end_draft(rig):
    rig.animation_data.action = None
    tools.reset_rig(rig)


def key_grenade(rig, context, key):
    begin_draft(rig, key)
    scene = context.scene
    for frame, pose in sorted(GRENADE_DRAFTS[key].items()):
        scene.frame_set(frame)
        tools.reset_rig(rig)
        context.view_layer.update()
        if pose != 'GUN':
            basis = tools.view_basis()
            wrist, along, palm, pole, curl, thumb = pose
            place_hand(rig, context, wrist, along, palm, basis)
            place_pole(rig, context, tools.to_world(pole, basis, point=True))
            set_curls(rig, context, curl, thumb)
        key_pose(rig, frame)
    end_draft(rig)


# ---- The MW knife ---------------------------------------------------------------------------------------------------

class MW:
    """GMod's [MW] Quick Throwing Knife viewmodel (c_eq_knife.mdl), read with SourceIO: per frame of a clip, its right
    arm mirrored onto the left in the view's axes (Unity's: x right, y up, z forward), at this project's weapon size
    and spread to this camera's field of view."""

    def __init__(self, addon, root):
        import addon_utils
        addon_utils.enable('SourceIO-master', default_set=False, persistent=False)
        from SourceIO.library.models.mdl.v49.mdl_file import MdlV49
        from SourceIO.library.utils import FileBuffer
        self.addon = addon
        self.path = addon / MW_MDL.replace('/', os.sep)
        with FileBuffer(str(self.path)) as f:
            self.mdl = MdlV49.from_buffer(f)
        self.index = {b.name: i for i, b in enumerate(self.mdl.bones)}
        self.descs = {d.name.lstrip('@'): i for i, d in enumerate(self.mdl.anim_descs)}
        self.knife = self.knife_frame(root)

    @staticmethod
    def view_of(v):
        """Source axes (x forward, y left, z up) to Unity's camera axes, mirrored left-right (the right hand's to the left)."""
        return Vector((v.y, v.z, v.x))

    @staticmethod
    def unview(v):
        """The view's axes back to Source's (undoing view_of)."""
        return Vector((v.z, v.x, v.y))

    def points_of(self, rel):
        """A model's mesh points as SourceIO reads them (its weapon size, Source's axes), the import removed after."""
        path = self.addon / rel.replace('/', os.sep)
        before = set(bpy.data.objects)
        bpy.ops.sourceio.mdl(filepath=str(path), directory=str(path.parent) + os.sep, files=[{'name': path.name}],
                             import_animations=False, write_qc=False, discover_resources=False)
        made = [o for o in bpy.data.objects if o not in before]
        mesh = next(o for o in made if o.type == 'MESH')
        points = [mesh.matrix_world @ v.co for v in mesh.data.vertices]
        meshes = {o.data for o in made if o.type == 'MESH'}
        arms = {o.data for o in made if o.type == 'ARMATURE'}
        for obj in made:
            bpy.data.objects.remove(obj, do_unlink=True)
        for data in meshes:
            bpy.data.meshes.remove(data)
        for data in arms:
            bpy.data.armatures.remove(data)
        return points

    def knife_frame(self, root):
        """The game's knife model in the viewmodel's knife bone (tag_accessory, raw units): its origin, the way to its
        point and its broad side up, as Tools/Blender/Addons/MWThrowingKnife.json made it from the world model. The
        viewmodel's knife and the world model are the same mesh in other frames: the turn and shift between them are
        found from all their points (least squares), so the game's model sits in the hand where MW's knife sat."""
        import numpy as np
        recipe = json.loads((root / 'Tools/Blender/Addons/MWThrowingKnife.json').read_text(encoding='utf-8'))
        # pose_to_bone: the bone's inverse bind (3 rows of 4; SourceIO may keep it as 4 rows of 3).
        bind = np.array(self.mdl.bones[self.index[MW_KNIFE]].pose_to_bone, dtype=float)
        if bind.shape == (4, 3):
            bind = bind.T
        to_bone = Matrix([[float(v) for v in bind[r]] for r in range(3)] + [[0.0, 0.0, 0.0, 1.0]])
        # The viewmodel's knife, skinned whole to tag_accessory: its bind-pose points taken into that bone's frame, in
        # metres at the weapon size (SourceIO scales by it and leaves the axes).
        held = [to_bone @ (p / VM_UNIT) * VM_UNIT for p in self.points_of(MW_MDL)]
        world = self.points_of(recipe['mdl'])
        P, Q = np.array([list(p) for p in held]), np.array([list(q) for q in world])
        R, t, miss, mirrored = fit_shape(P, Q)
        print('MW KNIFE viewmodel to world model: rms %.5f m over %d/%d points%s' % (miss, len(P), len(Q), ' (mirrored)' if mirrored else ''))
        back = np.linalg.inv(R)
        origin = Vector(back @ (np.array(recipe['origin']['point']) - t)) / VM_UNIT
        tip = Vector(back @ np.array(recipe['tip'], dtype=float)).normalized()
        up = Vector(back @ np.array(recipe['up'], dtype=float)).normalized()
        return origin, tip, up

    def frames(self, anim):
        """Per frame: wrist, elbow (spread, game metres), hand run and palm, each finger's three segment ways, and the
        knife's middle (spread, metres), point way and broad way; in the view's axes."""
        from mathutils import Quaternion
        n = self.descs[anim]
        data = self.mdl.animations[n]
        bones = self.mdl.bones
        out = []
        for fr in range(int(data.shape[0])):
            pos_all, rot_all = data['pos'], data['rot']
            world = [None] * len(bones)
            for i, b in enumerate(bones):
                r = rot_all[fr, i]
                local = Matrix.Translation(Vector(pos_all[fr, i])) @ Quaternion((r[3], r[0], r[1], r[2])).to_matrix().to_4x4()
                world[i] = local if b.parent_id == -1 else world[b.parent_id] @ local
            view = world[self.index['tag_camera']].inverted()

            def at(name):
                return self.view_of((view @ world[self.index[name]]).to_translation()) * VM_UNIT

            def axis_of(name, local):
                return self.view_of((view @ world[self.index[name]]).to_3x3() @ local).normalized()

            def spread(v):
                return Vector((v.x * SPREAD, v.y * SPREAD, v.z))

            wrist, elbow, shoulder = at(MW_ARM['wrist']), at(MW_ARM['elbow']), at(MW_ARM['shoulder'])
            index, middle, little = at(MW_FINGERS['index'][0]), at(MW_FINGERS['middle'][0]), at(MW_FINGERS['little'][0])
            along = (middle - wrist).normalized()
            palm = -unity_cross(along, index - little)
            palm = (palm - along * palm.dot(along)).normalized()
            ways = {}
            for finger, names in MW_FINGERS.items():
                j = [at(name) for name in names]
                first, second = j[1] - j[0], j[2] - j[1]
                # The last segment has no joint beyond it: the last joint's own length axis, found as the one its
                # parent joint's way to it lies along.
                parent_local = (view @ world[self.index[names[1]]]).to_3x3().inverted() @ self.unview(second)
                axis = Vector((0, 0, 0))
                k = max(range(3), key=lambda i: abs(parent_local[i]))
                axis[k] = 1 if parent_local[k] > 0 else -1
                ways[finger] = (first.normalized(), second.normalized(), axis_of(names[2], axis))
            knife_origin, knife_tip, knife_up = self.knife
            k = view @ world[self.index[MW_KNIFE]]
            out.append(dict(wrist=spread(wrist), elbow=spread(elbow), along=along, palm=palm, ways=ways,
                            raw_wrist=wrist, knife=self.view_of(k @ knife_origin) * VM_UNIT,
                            knife_tip=self.view_of(k.to_3x3() @ knife_tip).normalized(),
                            knife_up=self.view_of(k.to_3x3() @ knife_up).normalized(),
                            hand=(middle - wrist).length))
        return out


def fit_shape(P, Q):
    """The turn and shift that lay point cloud P on Q (the same knife's mesh in two frames, its points in another
    order): their principal axes lined up both ways round, then nearest points matched and refit (ICP) from each start;
    the closest fit wins. Returns R, t (Q ~ R P + t), the rms miss (m) and whether it had to be mirrored."""
    import numpy as np
    from mathutils.kdtree import KDTree
    tree = KDTree(len(Q))
    for i, q in enumerate(Q):
        tree.insert(Vector(q), i)
    tree.balance()

    def axes(X):
        c = X.mean(axis=0)
        w, v = np.linalg.eigh(np.cov((X - c).T))
        return c, v[:, np.argsort(w)[::-1]]  # columns: longest, broad, thin

    def kabsch(A, B, mirror):
        # The best turn from A to B with the handedness wanted (a mirror only where the start was one).
        ca, cb = A.mean(axis=0), B.mean(axis=0)
        U, _, Vt = np.linalg.svd((A - ca).T @ (B - cb))
        flip = (-1.0 if mirror else 1.0) * (1.0 if np.linalg.det(Vt.T @ U.T) > 0 else -1.0)
        R = Vt.T @ np.diag([1.0, 1.0, flip]) @ U.T
        return R, cb - R @ ca

    cp, ap = axes(P)
    cq, aq = axes(Q)
    best = None
    for s1 in (1, -1):
        for s2 in (1, -1):
            for s3 in (1, -1):
                R = aq @ np.diag([s1, s2, s3]) @ ap.T
                mirror = np.linalg.det(R) < 0
                t = cq - R @ cp
                for _ in range(25):
                    moved = P @ R.T + t
                    pairs = np.array([tree.find(Vector(m))[1] for m in moved])
                    R, t = kabsch(P, Q[pairs], mirror)
                moved = P @ R.T + t
                miss = float(np.sqrt(np.mean([tree.find(Vector(m))[2] ** 2 for m in moved])))
                if best is None or miss < best[2]:
                    best = (R, t, miss, mirror)
    return best


def unity_cross(a, b):
    """Unity's Vector3.Cross on vectors in Unity's axes."""
    return Vector((a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x))


def key_knife(rig, context, key, frames):
    begin_draft(rig, key)
    scene = context.scene
    pole_way = None
    for n, f in enumerate(frames):
        frame = n + 1
        scene.frame_set(frame)
        tools.reset_rig(rig)
        context.view_layer.update()
        basis = tools.view_basis()
        place_hand(rig, context, f['wrist'], f['along'], f['palm'], basis)
        # The elbow's way off the line from this shoulder to the wrist, from the viewmodel's elbow, pushed out so the
        # bend goes that way.
        shoulder = tools.head(rig, tools.ARM_BONES['shoulder'])
        wrist = tools.head(rig, tools.ARM_BONES['wrist'])
        elbow = tools.to_world(f['elbow'], basis, point=True)
        line = (wrist - shoulder).normalized()
        off = elbow - shoulder
        off = off - line * off.dot(line)
        if off.length > .01:
            pole_way = off.normalized()
        if pole_way is not None:
            place_pole(rig, context, shoulder + (wrist - shoulder) * .5 + pole_way * .35)
        align_fingers(rig, context, f['ways'], basis)
        key_pose(rig, frame)
    end_draft(rig)


def key_knife_hold(rig, context, idle):
    """MW's idle (a still pose) with a slow breath: the hand rises 4 mm and tips a little over 1.6 s."""
    begin_draft(rig, 'knife_hold')
    scene = context.scene
    last = KNIFE_HOLD_FRAMES
    for frame in (1, 13, 25, 37, last):
        phase = (frame - 1) / (last - 1) * 2 * math.pi
        scene.frame_set(frame)
        tools.reset_rig(rig)
        context.view_layer.update()
        basis = tools.view_basis()
        wrist = idle['wrist'] + Vector((0, .004 * math.sin(phase), -.002 * math.sin(phase)))
        along = (idle['along'] + idle['palm'] * .02 * math.sin(phase)).normalized()
        place_hand(rig, context, wrist, along, idle['palm'], basis)
        shoulder = tools.head(rig, tools.ARM_BONES['shoulder'])
        hand = tools.head(rig, tools.ARM_BONES['wrist'])
        elbow = tools.to_world(idle['elbow'], basis, point=True)
        line = (hand - shoulder).normalized()
        off = elbow - shoulder
        off = (off - line * off.dot(line)).normalized()
        place_pole(rig, context, shoulder + (hand - shoulder) * .5 + off * .35)
        align_fingers(rig, context, idle['ways'], basis)
        key_pose(rig, frame)
    end_draft(rig)


def knife_held(idle):
    """How the game holds the knife, from MW's idle (unspread, the viewmodel's own sizes): where the fist closes (FIST,
    hand lengths), the model's own point there (its grip, m, in the model's axes: x across, y its broad side up, z back
    from the point) and its turn in the hand's frame (Unity's Euler angles: z, then x, then y)."""
    along, palm = idle['along'], idle['palm']
    side = unity_cross(palm, along)
    hand = Matrix((side, palm, along)).transposed()  # columns: the hand frame's x, y, z in the view's axes
    z = -idle['knife_tip']
    y = (idle['knife_up'] - z * idle['knife_up'].dot(z)).normalized()
    x = unity_cross(y, z)
    knife = Matrix((x, y, z)).transposed()
    fist = idle['raw_wrist'] + hand @ (FIST * idle['hand'])
    grip = knife.transposed() @ (fist - idle['knife'])
    turn = hand.transposed() @ knife
    ex = math.degrees(math.asin(max(-1.0, min(1.0, -turn[1][2]))))
    ey = math.degrees(math.atan2(turn[0][2], turn[2][2]))
    ez = math.degrees(math.atan2(turn[1][0], turn[1][1]))
    return FIST, grip, Vector((ex, ey, ez))


# ---- Previews in the left hand ----------------------------------------------------------------------------------------

def held_numbers(root, row_id):
    import csv
    with open(root / GRENADES_TABLE, encoding='utf-8-sig', newline='') as f:
        for row in csv.DictReader(f):
            if row.get('id') == row_id:
                num = lambda k, d: float(row.get(k) or d)
                return (Vector((num('heldAcross', .12), num('heldPalm', .6), num('heldAlong', .9))),
                        Vector((num('heldTurnX', 0), num('heldTurnY', 0), num('heldTurnZ', 90))),
                        Vector((num('gripX', 0), num('gripY', 0), num('gripZ', 0))))
    return Vector((.12, .6, .9)), Vector((0, 0, 90)), Vector((0, 0, 0))


def unity_euler(e):
    """Unity's Quaternion.Euler(x, y, z) as a 3x3 on Unity's numbers (z first, then x, then y)."""
    return (Matrix.Rotation(math.radians(e.y), 3, 'Y') @ Matrix.Rotation(math.radians(e.x), 3, 'X')
            @ Matrix.Rotation(math.radians(e.z), 3, 'Z'))


def make_preview(root, name, rel, collection):
    holder = bpy.data.objects.new(tools.LOOKS[name], None)
    holder.empty_display_size = .02
    collection.objects.link(holder)
    for obj in import_fbx(root, rel):
        for owner in list(obj.users_collection):
            owner.objects.unlink(obj)
        if obj.type != 'MESH':
            bpy.data.objects.remove(obj, do_unlink=True)
            continue
        collection.objects.link(obj)
        world = obj.matrix_world.copy()
        obj.parent = holder
        obj.matrix_parent_inverse = Matrix.Identity(4)
        obj.matrix_basis = Matrix.Scale(1 / tools.GAME_SCALE, 4) @ world
        obj.name = f'{tools.LOOKS[name]} model'
        obj.hide_select = True
    return holder


def seat_preview(rig, context, holder, held_at, turn, grip):
    """The preview where the game puts the model (GrenadeArm.Hold): the holder at the wrist plus the hand's frame times
    held_at hand lengths, turned by the hand's frame times turn; the model in it moved back by its grip. The model's
    axes in this file are Unity's (x, z, y): Art/Models imports swap them back (ModelImportRules)."""
    context.view_layer.update()
    basis = tools.view_basis()
    wrist, along, palm, length = tools.hand_frame(rig)
    along_v, palm_v = tools.to_view(along, basis), tools.to_view(palm, basis)
    side_v = unity_cross(palm_v, along_v)
    hand = Matrix((side_v, palm_v, along_v)).transposed()
    model = hand @ unity_euler(turn)
    at = tools.to_view(wrist, basis, True) + hand @ (held_at * length * tools.GAME_SCALE) - model @ grip
    cols = [tools.to_world(model.col[0], basis), tools.to_world(model.col[2], basis), tools.to_world(model.col[1], basis)]
    rot = Matrix((cols[0], cols[1], cols[2])).transposed().to_4x4()
    holder.parent = None
    holder.matrix_world = Matrix.Translation(tools.to_world(at, basis, point=True)) @ rot
    bone = rig.pose.bones[tools.ARM_BONES['wrist']]
    world = holder.matrix_world.copy()
    holder.parent = rig
    holder.parent_type = 'BONE'
    holder.parent_bone = bone.name
    context.view_layer.update()
    holder.matrix_world = world


def main():
    out, addon, export, force = args()
    if out.exists() and not force:
        raise SystemExit(f'{out} exists; add --force to build it again.')
    root = tools.project_root() or Path.cwd()
    context = bpy.context
    scene = context.scene
    tools.register()
    rig = tools.find_rig(scene)
    camera = bpy.data.objects[tools.CAMERA]
    collection = bpy.data.collections.new(PREVIEW)
    scene.collection.children.link(collection)
    gun_in_hand(root, rig, camera, collection, context)
    for old in list(bpy.data.actions):
        if old.name in ('enf_new_clip', 'enf_practice_reach'):
            old.use_fake_user = False

    mw = MW(addon, root)
    draw, throw, idle = mw.frames('draw'), mw.frames('throw'), mw.frames('idle')[0]
    held_at, grip, turn = knife_held(idle)
    print('KNIFE HELD at %.3f %.3f %.3f (hand lengths) grip %.4f %.4f %.4f (m) turn %.1f %.1f %.1f' % (*held_at, *grip, *turn))
    print('SPREAD %.4f (game %.2f, MW %.2f degrees)' % (SPREAD, GAME_VFOV, MW_VFOV))
    for key in ('grenade_draw', 'grenade_hold', 'grenade_throw', 'grenade_tap'):
        key_grenade(rig, context, key)
    key_knife(rig, context, 'knife_draw', draw)
    key_knife(rig, context, 'knife_throw', throw)
    key_knife_hold(rig, context, idle)
    for key, move in tools.MOVES.items():
        tools.touch_up(move['action'])

    # The previews seated on the hold's pose (they follow the hand bone after).
    tools.select_move(context, 'grenade_hold')
    context.view_layer.update()
    for name, rel in MODELS.items():
        holder = make_preview(root, name, rel, collection)
        if name == 'knife':
            seat_preview(rig, context, holder, held_at, turn, grip)
        else:
            at, rot, grab = held_numbers(root, HELD_ROW[name])
            seat_preview(rig, context, holder, at, rot, grab)

    text = bpy.data.texts.get('START HERE - Throw tools.py') or bpy.data.texts.new('START HERE - Throw tools.py')
    text.from_string((HERE / 'grenade_tools.py').read_text(encoding='utf-8'))
    exported = []
    if export:
        for key in tools.MOVES:
            tools.select_move(context, key)
            exported.append(str(tools.export_clip(context, key)))
    tools.select_move(context, 'grenade_throw')
    out.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(out), copy=False, compress=True)
    print('THROW WORKSPACE', out)
    for path in exported:
        print('EXPORTED', path)


main()
