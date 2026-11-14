"""Builds ArtSource/Knives/ENFILADE_Knife_Animation.blend from the True FPS start file and, with --export, sends its six
drafts to the game (as saving does). Run from the project root:

  blender --background --factory-startup ArtSource/TrueFPS/ENFILADE_TrueFPS_Start.blend
          --python Tools/Blender/Knives/build_knife_workspace.py --
          ArtSource/Knives/ENFILADE_Knife_Animation.blend [--export] [--force]

It refuses to overwrite an existing workspace unless --force is given (a polish lives in it). The source .blend is never
saved. The previews in Kazusa's right fist are the game's own knives seated as the game seats them (ClassKnives.cs:
handle middle 0.3 hand lengths out of the palm and 0.55 along it; a forward grip's blade out of the thumb's side leaning
the fingers' way, a reverse grip's out of the little finger's): the primal knife for the forward clips, the karambit for the
reverse stab.

The drafts (30 fps; the wrist and the elbow pole from the eyes in game metres, x right, y up, z forward; the hand's run and
its palm's facing; the four fingers' and the thumb's closing). Kazusa's right shoulder is 11 cm right of, 17 below and 9
behind her eyes and her arm 49 cm long:
- 가드 (37, a loop of 1.2 s): the fist low on the right of the view, thumb up, the blade out ahead and up; a slow breath.
- 꺼내기 (12): 1 the open hand at the right hip → 4 쥐기, the fist closes on the knife → 8 rising → 12 the guard.
- 베기 (10): 1 guard → 2 wound out to the right → 3 (0.067 s) 벰 across the middle → 5 through to the left → 8 → 10 guard.
- 되베기 (10): 1 guard → 2 wound across to the left, palm in → 3 벰 → 5 through to the right → 8 → 10 guard.
- 찌르기 (12): 1 cocked back by the ribs (a charge holds the knife here) → 3 벰 straight out → 5 in deep → 8 → 12 guard.
- 역수 찌르기 (12): 1 raised beside the head, the blade hanging below the fist → 3 벰 driven down and ahead → 5 → 8 → 12 guard.
Then each class knife's own clips, ported from its workshop addon (addon_clips.json, written by knife_addon_port.py): every
frame keyed (the touch-up layer takes the polish), that knife in the fist where its ClassKnives.csv row seats it (the
addon's own grip, measured by the port).
"""
import importlib.util
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector

HERE = Path(__file__).resolve().parent
sys.dont_write_bytecode = True
spec = importlib.util.spec_from_file_location('knife_tools', HERE / 'knife_tools.py')
tools = importlib.util.module_from_spec(spec)
sys.modules['knife_tools'] = tools
spec.loader.exec_module(tools)

PREVIEWS = {'forward': 'Assets/_Project/Art/Models/Addons/Knives/PrimalKnife/PrimalKnife.fbx',
            'reverse': 'Assets/_Project/Art/Models/Addons/Knives/Karambit/Karambit.fbx'}
# ClassKnives' seat (hand axes: x away from the thumb, y out of the palm, z the fingers' way; positions in hand lengths).
HELD = Vector((0.0, .3, .55))
BLADE = {'forward': Vector((-.8, 0.0, .6)), 'reverse': Vector((.93, 0.0, .36))}
EDGE = Vector((0.0, 0.0, 1.0))

OPEN, GRIP = (25, 25, 25, 25), (150, 160, 165, 170)
GUARD = ((.16, -.17, .30), (-.05, -.40, .92), (-1.0, 0.0, 0.0), (.32, -.42, .04), GRIP, 85)


def breath(pose, dy, dz):
    (x, y, z), along, palm, (ex, ey, ez), curl, thumb = pose
    return (x, y + dy, z + dz), along, palm, (ex, ey + dy * .6, ez), curl, thumb


DRAFTS = {
    'guard': {1: GUARD, 19: breath(GUARD, .004, -.002), 37: GUARD},
    'draw': {
        1: ((.22, -.60, .04), (.05, -.95, .30), (-1.0, 0.0, 0.0), (.34, -.40, -.12), OPEN, 20),
        4: ((.22, -.62, .05), (.05, -.95, .30), (-1.0, 0.0, 0.0), (.34, -.40, -.12), GRIP, 85),
        8: ((.20, -.36, .18), (.05, -.20, .98), (-1.0, 0.0, 0.0), (.34, -.45, -.06), GRIP, 85),
        12: GUARD,
    },
    'slash': {
        1: GUARD,
        2: ((.30, -.10, .22), (.30, .20, .93), (-.95, .20, .25), (.45, -.28, -.02), GRIP, 85),
        3: ((.06, -.14, .38), (-.55, -.10, .83), (-.35, -.90, -.25), (.28, -.36, .12), GRIP, 85),
        5: ((-.12, -.22, .32), (-.85, -.20, .48), (.10, -.95, -.25), (.12, -.42, .10), GRIP, 85),
        8: ((.08, -.24, .30), (-.30, -.35, .89), (-.80, -.55, 0.0), (.26, -.44, .04), GRIP, 85),
        10: GUARD,
    },
    'backslash': {
        1: GUARD,
        2: ((-.04, -.17, .26), (-.65, .20, .73), (-.25, -.30, -.92), (.20, -.42, -.02), GRIP, 85),
        3: ((.14, -.11, .38), (.45, .10, .89), (-.25, -.95, .15), (.33, -.35, .12), GRIP, 85),
        5: ((.32, -.10, .28), (.85, .05, .52), (-.30, -.95, .10), (.46, -.27, 0.0), GRIP, 85),
        8: ((.22, -.20, .28), (.15, -.35, .92), (-.92, -.35, .15), (.36, -.44, .02), GRIP, 85),
        10: GUARD,
    },
    'stab': {
        1: ((.18, -.28, .12), (.05, -.70, .71), (-1.0, 0.0, 0.0), (.33, -.42, -.20), GRIP, 85),
        3: ((.07, -.15, .39), (-.05, -.55, .83), (-.98, .10, .10), (.24, -.32, .15), GRIP, 85),
        5: ((.07, -.16, .40), (-.05, -.55, .83), (-.98, .10, .10), (.24, -.32, .15), GRIP, 85),
        8: ((.14, -.22, .27), (0.0, -.55, .83), (-1.0, 0.0, 0.0), (.30, -.44, -.02), GRIP, 85),
        12: GUARD,
    },
    'stab_reverse': {
        1: ((.16, .02, .22), (.05, .25, .97), (-1.0, 0.0, 0.0), (.45, -.10, -.02), GRIP, 85),
        3: ((.06, -.24, .38), (0.0, -.20, .98), (-.95, -.30, 0.0), (.26, -.38, .14), GRIP, 85),
        5: ((.06, -.27, .38), (0.0, -.20, .98), (-.95, -.30, 0.0), (.26, -.40, .14), GRIP, 85),
        8: ((.14, -.20, .27), (.05, -.20, .98), (-1.0, 0.0, 0.0), (.33, -.40, 0.0), GRIP, 85),
        12: GUARD,
    },
}


def preview_of(root, look):
    """A look's model and seat (nine numbers, as the game reads them): the two drafts' knives in their grips, else that
    knife as its ClassKnives.csv row seats it in the melee slot (knife_tools.table_seats)."""
    if look in PREVIEWS:
        return PREVIEWS[look], tools.canonical([*HELD, *BLADE[look], *EDGE])
    return tools.KNIFE_MODEL.format(look), tools.table_seats(root)[look]


def ported_poses(key):
    """A ported clip's keys by frame (1-based), as DRAFTS holds the drafts'."""
    clip = tools.CLIPS[key]
    frames = tools.ported()[clip['knife']]['clips'][key.split('.', 1)[1]]['frames']
    return {i + 1: (tuple(w), tuple(a), tuple(p), tuple(e), tuple(c), t) for i, (w, a, p, e, c, t) in enumerate(frames)}


def args():
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    if not argv or argv[0].startswith('--'):
        raise SystemExit('Give the output .blend path after --')
    return Path(argv[0]).resolve(), '--export' in argv, '--force' in argv


# ---- Posing the right arm -------------------------------------------------------------------------------------------

def frame_of(along, palm):
    z = along.normalized()
    y = (palm - z * palm.dot(z)).normalized()
    return Matrix((y.cross(z), y, z)).transposed()


def place_hand(rig, context, wrist_view, along_view, palm_view, basis):
    """The right hand handle where the wrist goes, turned so the hand runs and faces as wanted (view axes)."""
    hand = rig.pose.bones[tools.ARM_HAND]
    matrix = hand.matrix.copy()
    matrix.translation = rig.matrix_world.inverted() @ tools.to_world(wrist_view, basis, point=True)
    hand.matrix = matrix
    context.view_layer.update()
    want = frame_of(tools.to_world(Vector(along_view), basis), tools.to_world(Vector(palm_view), basis))
    _, along_now, palm_now, _ = tools.hand_frame(rig)
    tools.turn_bone(rig, tools.ARM_HAND, want @ frame_of(along_now, palm_now).transposed())
    context.view_layer.update()


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


_last_turn = {}


def key_turn(pb, frame):
    # A turn and its negative are the same turn, but keys blend number by number: each is kept on the last one's side.
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


def pose(rig, context, at):
    tools.reset_rig(rig)
    context.view_layer.update()
    basis = tools.view_basis()
    wrist, along, palm, pole, curl, thumb = at
    place_hand(rig, context, wrist, along, palm, basis)
    place_pole(rig, context, tools.to_world(pole, basis, point=True))
    set_curls(rig, context, curl, thumb)


def key_draft(rig, context, key):
    _last_turn.clear()
    clip = tools.CLIPS[key]
    action = bpy.data.actions.new(clip['action'] + '_draft')
    action.use_fake_user = True
    action['enf_layer'] = 'draft'
    data = rig.animation_data or rig.animation_data_create()
    data.action = action
    data.action_blend_type = 'REPLACE'
    data.action_influence = 1.0
    scene = context.scene
    poses = DRAFTS[key] if key in DRAFTS else ported_poses(key)
    for frame, at in sorted(poses.items()):
        scene.frame_set(frame)
        pose(rig, context, at)
        key_pose(rig, frame)
    data.action = None
    tools.reset_rig(rig)


# ---- The knives in the fist ----------------------------------------------------------------------------------------

def make_preview(root, rig, context, look):
    """The game's knife in the right fist, seated as the game seats it, riding the wrist bone (knife_tools.add_preview)."""
    model, seat = preview_of(root, look)
    tools.reset_rig(rig)
    context.view_layer.update()
    holder = tools.add_preview(context, rig, look, model, seat)
    tools.reset_rig(rig)
    return holder


def main():
    out, export, force = args()
    if out.exists() and not force:
        raise SystemExit(f'{out} exists (a polish may live in it); give --force to build it again')
    root = tools.project_root()
    context = bpy.context
    scene = context.scene
    scene.render.fps, scene.render.fps_base = 30, 1.0
    rig = tools.find_rig(scene)
    if rig is None or tools.CAMERA not in bpy.data.objects:
        raise SystemExit('The True FPS start file (Kazusa and FPS_Head_Camera) is not open')
    for look in tools.LOOKS:
        make_preview(root, rig, context, look)
    for key in tools.CLIPS:
        # A made-here knife's clips are copied from the drafts when the file is first opened (knife_tools.ready_made_here).
        if tools.CLIPS[key].get('made'):
            continue
        key_draft(rig, context, key)
        print('drafted', key)
    scene[tools.SCENE_CLIP] = 'guard'
    tools.select_clip(context, 'guard')
    out.parent.mkdir(parents=True, exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=str(out), copy=False, compress=True)
    if export:
        for key in tools.CLIPS:
            if not tools.CLIPS[key].get('made'):
                print('sent', tools.send_clip(context, key), tools.table_path(key))
        tools.select_clip(context, 'guard')
        bpy.ops.wm.save_mainfile(compress=True)
    print('saved', out)


main()
