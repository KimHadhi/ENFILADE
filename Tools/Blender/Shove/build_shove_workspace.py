"""Builds ArtSource/Shove/ENFILADE_Shove_Animation.blend from the True FPS start file and, with --export, sends its
three drafts to the game (the same buttons the user presses). Run from the project root:

  blender --background --factory-startup ArtSource/TrueFPS/ENFILADE_TrueFPS_Start.blend
          --python Tools/Blender/Shove/build_shove_workspace.py -- ArtSource/Shove/ENFILADE_Shove_Animation.blend --export

It refuses to overwrite an existing workspace unless --force is given. The source .blend is never saved.

The drafts (30 fps; the shove hits from the press, the kick's foot lands at 0.3 s):
- 총 휘두르기 · 긴 총: 1 hold → 2 (0.033 s) draws back → 3 (0.067 s) STRIKE, thrust 20 cm, swung 30 degrees round and
  canted → 5 follow-through → 8 half back → 12 settles → 14 hold.
- 총 휘두르기 · 권총: 1 hold → 2 cocks the muzzle up → 3 (0.067 s) STRIKE, chops forward and down → 5 → 8 → 12 → 13.
- 발차기: 1 stand → 4 lifts → 7 (0.2 s) knee up → 10 (0.3 s) FOOT LANDS, sole forward at chest height → 12 recoil →
  15 knee back → 18 down → 20 stand.
"""
import importlib.util
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Euler, Matrix, Vector

HERE = Path(__file__).resolve().parent
# No __pycache__ beside the tools in the checkout.
sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location('shove_tools', HERE / 'shove_tools.py')
shove = importlib.util.module_from_spec(spec)
sys.modules['shove_tools'] = shove
spec.loader.exec_module(shove)

GUN_FILES = {'GUN_AK105': 'Assets/ThirdParty/KINEMATION/TacticalShooterPack/Meshes/Weapons/AK105/SKM_AK105.FBX',
             'GUN_R08': 'Assets/ThirdParty/KINEMATION/TacticalShooterPack/Meshes/Weapons/R08/SKM_R08.FBX'}
PREVIEW = '06 Gun preview - never exported'

# Gun drafts: frame -> (move x right, y forward, z up in this file's units; pitch, roll, yaw in degrees: pitch + is the
# muzzle up, roll + tips the top right, yaw + swings the muzzle left).
GUN_DRAFTS = {
    'enf_bash_long': {1: ((0, 0, 0), (0, 0, 0)), 2: ((.01, -.045, .01), (5, 4, -6)), 3: ((-.045, .17, .035), (9, -26, 30)),
                      5: ((-.05, .18, .03), (11, -30, 34)), 8: ((-.025, .08, .015), (5, -13, 15)), 12: ((0, -.008, 0), (0, 0, 0)),
                      14: ((0, 0, 0), (0, 0, 0))},
    'enf_bash_pistol': {1: ((0, 0, 0), (0, 0, 0)), 2: ((0, -.035, .025), (16, 0, 0)), 3: ((-.025, .14, -.045), (-32, -18, 9)),
                        5: ((-.025, .13, -.06), (-37, -20, 11)), 8: ((-.01, .05, -.02), (-12, -7, 4)), 12: ((0, 0, 0), (0, 0, 0)),
                        13: ((0, 0, 0), (0, 0, 0))},
}
# The kick: frame -> (right ankle handle off its rest: right, forward, up; toes up in degrees), and the knee's aim off
# its rest (forward, up). The foot's reach is kept within the leg (LegReach).
# At the strike the foot is at the bottom of the first-person view looking straight ahead (the camera sees 29.5 degrees
# down), so the player sees their own kick land, as teammates do.
KICK_FOOT = {1: ((0, 0, 0), 0), 4: ((.005, .10, .19), 15), 7: ((.005, .26, .62), 40), 10: ((.012, .64, 1.0), 70),
             12: ((.008, .58, .93), 65), 15: ((.005, .28, .50), 30), 18: ((.003, .09, .12), 10), 20: ((0, 0, 0), 0)}
KICK_KNEE = {1: (0, 0), 7: (.26, .44), 10: (.46, .58), 12: (.42, .52), 15: (.26, .40), 20: (0, 0)}
LEG_REACH = .97


def args():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    out = next((a for a in argv if not a.startswith('--')), None)
    if out is None:
        raise SystemExit('Give the output .blend path after --')
    return Path(out).resolve(), '--export' in argv, '--force' in argv


def import_gun(root, name, rel, collection, parent):
    """The gun's mesh under GUN: its FBX root is the game's weapon bone, turned to face along +Y and shrunk to Kazusa's
    size in this file (the game holds a true-size gun in her fitted hands)."""
    scene = bpy.context.scene
    fps, base = scene.render.fps, scene.render.fps_base
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(root / rel))
    scene.render.fps, scene.render.fps_base = fps, base
    made = [o for o in bpy.data.objects if o not in before]
    holder = bpy.data.objects.new(name, None)
    holder.empty_display_size = .02
    collection.objects.link(holder)
    holder.parent = parent
    holder.rotation_euler = (0, 0, math.pi)
    holder.scale = (1 / shove.GAME_SCALE,) * 3
    for obj in made:
        for owner in list(obj.users_collection):
            owner.objects.unlink(obj)
        collection.objects.link(obj)
        if obj.parent is None:
            obj.parent = holder
            obj.matrix_parent_inverse = Matrix.Identity(4)
        obj.hide_select = obj.type != 'EMPTY'
        if obj.type == 'ARMATURE':
            obj.show_in_front = False
            obj.data.display_type = 'STICK'
            obj.hide_viewport = False
    return holder


def key_gun(gun, name, keys):
    action = bpy.data.actions.new(name + '_draft')
    action.use_fake_user = True
    action['enf_layer'] = 'draft'
    data = gun.animation_data or gun.animation_data_create()
    data.action = action
    for frame, (move, (pitch, roll, yaw)) in sorted(keys.items()):
        gun.location = move
        gun.rotation_quaternion = Euler((math.radians(pitch), math.radians(roll), math.radians(yaw)), 'XYZ').to_quaternion()
        gun.keyframe_insert('location', frame=frame, group=shove.GUN)
        gun.keyframe_insert('rotation_quaternion', frame=frame, group=shove.GUN)
    data.action = None
    gun.location, gun.rotation_quaternion = (0, 0, 0), (1, 0, 0, 0)
    return action


def hands_on_gun(rig, gun, context):
    """Kazusa's hand handles on the long gun's grips at its hold, then following the gun (Child Of), so the arms swing
    with a bash here as the game's contacts swing them. Wrists from KINEMATION's own palms on the AK105 (FPSBody
    contacts), in GUN's axes and this file's units."""
    wrists = {'CTRL_hand.R': Vector((.016, -.157, -.097)), 'CTRL_hand.L': Vector((-.021, .107, -.046))}
    defaults = json.loads(rig.get('truefps_default_pose', '{}'))
    context.view_layer.update()
    for name, local in wrists.items():
        pb = rig.pose.bones[name]
        world = gun.matrix_world @ local
        matrix = pb.matrix.copy()
        matrix.translation = rig.matrix_world.inverted() @ world
        pb.matrix = matrix
        context.view_layer.update()
        con = pb.constraints.new('CHILD_OF')
        con.name = 'Hold the gun (shove workspace)'
        con.target = gun
        con.set_inverse_pending = True
        defaults[name] = {'location': list(pb.location), 'rotation_quaternion': list(pb.rotation_quaternion), 'scale': list(pb.scale)}
    rig['truefps_default_pose'] = json.dumps(defaults)
    context.view_layer.update()


def key_kick(rig, context):
    action = bpy.data.actions.new(shove.MOVES['kick']['action'] + '_draft')
    action.use_fake_user = True
    action['enf_layer'] = 'draft'
    data = rig.animation_data or rig.animation_data_create()
    data.action = action
    right, forward, up = shove.body_axes(rig)
    foot, knee = rig.pose.bones['CTRL_foot.R'], rig.pose.bones['CTRL_knee.R']
    hip = rig.pose.bones['Right leg'].head.copy()
    leg = (rig.pose.bones['Right knee'].head - hip).length + (rig.pose.bones['Right ankle'].head - rig.pose.bones['Right knee'].head).length
    rig['ik_leg_R'] = 1.0
    rig.keyframe_insert('["ik_leg_R"]', frame=1, group='IK / FK mode')
    rest_foot, rest_knee = foot.matrix.copy(), knee.matrix.copy()
    for frame, ((x, y, z), toes) in sorted(KICK_FOOT.items()):
        matrix = rest_foot.copy()
        at = matrix.translation + right * x + forward * y + up * z
        # Never past the leg's length from the hip: a locked straight leg pops.
        reach = at - hip
        if reach.length > leg * LEG_REACH:
            at = hip + reach.normalized() * leg * LEG_REACH
        turn = Matrix.Rotation(math.radians(toes), 4, right)
        head = matrix.translation.copy()
        matrix = Matrix.Translation(at) @ turn @ Matrix.Translation(-head) @ matrix
        foot.matrix = matrix
        context.view_layer.update()
        foot.keyframe_insert('location', frame=frame, group=foot.name)
        foot.keyframe_insert('rotation_quaternion', frame=frame, group=foot.name)
    for frame, (y, z) in sorted(KICK_KNEE.items()):
        matrix = rest_knee.copy()
        matrix.translation = matrix.translation + forward * y + up * z
        knee.matrix = matrix
        context.view_layer.update()
        knee.keyframe_insert('location', frame=frame, group=knee.name)
    data.action = None
    foot.matrix, knee.matrix = rest_foot, rest_knee
    return action


def main():
    out, export, force = args()
    if out.exists() and not force:
        raise SystemExit(f'{out} exists; add --force to build it again.')
    root = shove.project_root() or Path.cwd()
    context = bpy.context
    scene = context.scene
    shove.register()
    rig = shove.find_rig(scene)
    camera = bpy.data.objects['FPS_Head_Camera']
    collection = bpy.data.collections.new(PREVIEW)
    scene.collection.children.link(collection)

    hold = bpy.data.objects.new(shove.GUN_HOLD, None)
    hold.empty_display_type = 'PLAIN_AXES'
    hold.empty_display_size = .03
    collection.objects.link(hold)
    hold.parent = camera
    hold.matrix_parent_inverse = Matrix.Identity(4)
    hold.location = shove.MOVES['bash_long']['hold']
    # The gun's axes from the camera's: x right, y along the barrel (the camera looks down its -Z), z up.
    hold.rotation_euler = (-math.pi / 2, 0, 0)
    hold.hide_select = True
    gun = bpy.data.objects.new(shove.GUN, None)
    gun.empty_display_type = 'ARROWS'
    gun.empty_display_size = .06
    gun.rotation_mode = 'QUATERNION'
    collection.objects.link(gun)
    gun.parent = hold
    gun.matrix_parent_inverse = Matrix.Identity(4)
    for name, rel in GUN_FILES.items():
        import_gun(root, name, rel, collection, gun)
    context.view_layer.update()
    hands_on_gun(rig, gun, context)

    for name, keys in GUN_DRAFTS.items():
        key_gun(gun, name, keys)
    key_kick(rig, context)
    for key, move in shove.MOVES.items():
        shove.touch_up(move['action'])
        scene['enf_impact_' + key] = move['impact']

    text = bpy.data.texts.get('START HERE - Shove tools.py') or bpy.data.texts.new('START HERE - Shove tools.py')
    text.from_string((HERE / 'shove_tools.py').read_text(encoding='utf-8'))
    exported = []
    if export:
        for key in shove.MOVES:
            shove.select_move(context, key)
            exported.append(str(shove.export_move(context, key)))
    shove.select_move(context, 'bash_long')
    out.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(out), copy=False, compress=True)
    print('SHOVE WORKSPACE', out)
    for path in exported:
        print('EXPORTED', path)


main()
