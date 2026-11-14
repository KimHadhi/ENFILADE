"""Builds ArtSource/Executions/ENFILADE_Execution_Animation.blend from the True FPS start file: Kazusa's True FPS rig and
the zombie workspace's Synty rig in one scene at the game's size, the default class's knife in her right fist where the
game seats it (execution_knife.py, ClassKnives.csv), the hold points the hands copy while they hold the zombie, and
every pair's drafts (execution_pairs.py) as the key poses the user edits, the knife's tip put where they stab, each kept
beside it as <name>_draft (execution_tools.py). Run from the project root:

  blender --background --factory-startup ArtSource/TrueFPS/ENFILADE_TrueFPS_Start.blend
          --python Tools/Blender/Executions/build_execution_workspace.py -- ArtSource/Executions/ENFILADE_Execution_Animation.blend
          [--export] [--force] [--only=back,front]

--export sends every pair to the game (what saving in Blender does: FBX clips and Tables/Executions.csv rows).
It refuses to overwrite an existing workspace unless --force is given: the user's touch-ups live in it. The source
.blend files (the True FPS start file and ArtSource/Zombies/ENFILADE_Zombie_Animation.blend) are only read.
"""
import importlib.util
import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector

HERE = Path(__file__).resolve().parent
sys.dont_write_bytecode = True
ROOT = HERE.parents[2]
spec = importlib.util.spec_from_file_location('execution_tools', HERE / 'execution_tools.py')
exe = importlib.util.module_from_spec(spec)
sys.modules['execution_tools'] = exe
spec.loader.exec_module(exe)

ZOMBIE_FILE = ROOT / 'ArtSource/Zombies/ENFILADE_Zombie_Animation.blend'
ZOMBIE_LOOK = 'Zombie_Hoodie_Male_01'
ZOMBIE_COLLECTION = '06 좀비 (Synty, 게임 크기)'


def args():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    out = next((a for a in argv if not a.startswith('--')), None)
    if out is None:
        raise SystemExit('Give the output .blend path after --')
    only = [k for a in argv if a.startswith('--only=') for k in a[len('--only='):].split(',') if k]
    return Path(out).resolve(), '--export' in argv, '--force' in argv, only


def bring_zombie(context):
    """The zombie workspace's Synty rig with one of its looks, as it is there (its own size, at the origin, facing -Y)."""
    with bpy.data.libraries.load(str(ZOMBIE_FILE), link=False) as (source, into):
        into.objects = [n for n in source.objects if n in ('Armature', ZOMBIE_LOOK)]
    collection = bpy.data.collections.new(ZOMBIE_COLLECTION)
    context.scene.collection.children.link(collection)
    rig = None
    for obj in into.objects:
        if obj is None:
            continue
        collection.objects.link(obj)
        if obj.type == 'ARMATURE':
            rig = obj
        else:
            obj.hide_viewport = obj.hide_render = False
            obj.hide_select = True
    if rig is None or rig.get('truefps_kind') != 'zombie':
        raise SystemExit('The zombie workspace has no Synty rig named Armature.')
    rig.name = '좀비 리그'
    # Its clip in the zombie workspace is not this file's: every pair keys its own.
    rig.animation_data_clear()
    for prop in [k for k in rig.keys() if k.startswith('ik_')]:
        rig[prop] = 0.0
    return rig


def calibrate_ik(context, rig):
    """The True FPS rig's leg IK bends the knees sideways (its pole angles are 0 and 180 degrees, a quarter turn off), so
    a low hip crossed the knees. In this file only, each limb's pole angle is the one that makes its knee or elbow bend
    toward its own pole: tried on a bent limb, the best of four quarter turns. The other workspaces keep theirs."""
    pose = rig.pose.bones
    limbs = (('MCH_foot_lower.L', 'Left leg', 'Left knee', 'Left ankle', 'CTRL_knee.L', 'Hips'),
             ('MCH_foot_lower.R', 'Right leg', 'Right knee', 'Right ankle', 'CTRL_knee.R', 'Hips'),
             ('MCH_hand_lower.L', 'Left arm', 'Left elbow', 'Left wrist', 'CTRL_elbow.L', 'CTRL_hand.L'),
             ('MCH_hand_lower.R', 'Right arm', 'Right elbow', 'Right wrist', 'CTRL_elbow.R', 'CTRL_hand.R'))
    keep = {n: pose[n].matrix_basis.copy() for n in ('Hips', 'CTRL_hand.L', 'CTRL_hand.R')}
    for limb in ('arm_L', 'arm_R', 'leg_L', 'leg_R'):
        rig['ik_' + limb] = 1.0
    # Bend every limb: the hips down a hand, the hands in toward the chest.
    hips = pose['Hips'].matrix.copy()
    hips.translation.z -= .12
    pose['Hips'].matrix = hips
    for side, sign in (('L', 1.0), ('R', -1.0)):
        hand = pose[f'CTRL_hand.{side}'].matrix.copy()
        hand.translation = Vector((sign * .18, -.22, 1.05))
        pose[f'CTRL_hand.{side}'].matrix = hand
    for chain, root, middle, end, pole, _ in limbs:
        con = next(c for c in pose[chain].constraints if c.type == 'IK')
        start = con.pole_angle
        best, best_angle = None, start
        for turn in (0.0, .5, 1.0, 1.5):
            # Kept within ±180 degrees: Blender clamps a pole angle past it rather than wrapping it.
            angle = start + math.pi * turn
            con.pole_angle = math.atan2(math.sin(angle), math.cos(angle))
            context.view_layer.update()
            mid = (pose[root].head + pose[end].head) * .5
            bend = pose[middle].head - mid
            want = pose[pole].head - mid
            score = bend.normalized().dot(want.normalized()) if bend.length > 1e-4 and want.length > 1e-4 else -2.0
            if best is None or score > best:
                best, best_angle = score, con.pole_angle
        con.pole_angle = math.atan2(math.sin(best_angle), math.cos(best_angle))
        print(f'  IK {chain}: pole angle {math.degrees(start):.1f} -> {math.degrees(con.pole_angle):.1f} (toward pole {best:.2f})', flush=True)
    for name, basis in keep.items():
        pose[name].matrix_basis = basis
    context.view_layer.update()


def main():
    out, export, force, only = args()
    if out.exists() and not force:
        raise SystemExit(f'{out} exists; add --force to build it again.')
    context = bpy.context
    scene = context.scene
    scene.render.fps, scene.render.fps_base = 30, 1.0
    exe.register()
    player = exe.player_rig(scene)
    if player is None:
        raise SystemExit('Open ArtSource/TrueFPS/ENFILADE_TrueFPS_Start.blend first.')
    player.animation_data_clear()
    calibrate_ik(context, player)
    camera = bpy.data.objects.get('FPS_Head_Camera')
    if camera is not None:
        camera.hide_viewport = camera.hide_render = True
    for name in ('Preview focus board', 'Preview scale pillar', 'Preview scale pillar.001'):
        obj = bpy.data.objects.get(name)
        if obj is not None:
            obj.hide_viewport = obj.hide_render = True
    zombie = bring_zombie(context)
    context.view_layer.update()
    # The default class's knife in her fist, seated as the game seats it, before any pair moves her: the drafts put its
    # tip where they stab.
    player.matrix_world = Matrix.Scale(exe.drafts().GAME_SCALE, 4)
    context.view_layer.update()
    exe.knife().ensure(context, player, ROOT, exe.knife().DEFAULT_ROW)
    exe.ensure_holds(scene)
    p = exe.pairs()
    keys = [k for k in p.ORDER if not only or k in only]
    for key in keys:
        print('Pair', key, flush=True)
        exe.make_pair(context, key)
    text = bpy.data.texts.get('START HERE - Execution tools.py') or bpy.data.texts.new('START HERE - Execution tools.py')
    text.from_string((HERE / 'execution_tools.py').read_text(encoding='utf-8'))
    exe.select_pair(context, keys[0], keep=False)
    exported = []
    if export:
        for key in keys:
            exported += [str(path) for path in exe.export_pair(context, key)]
            exe.mark_sent(key)
    exe.select_pair(context, keys[0], keep=False)
    # This save is the build's own: sending to the game is --export's call, not the save handler's.
    exe._drop_handlers()
    out.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(out), copy=False, compress=True)
    print('EXECUTION WORKSPACE', out, flush=True)
    for path in exported:
        print('EXPORTED', path, flush=True)


main()
