"""The Support's mending moves for the shove workspace (Blender 4.5): the hammer and the welding torch in the free left
hand while the gun stays in the right (the user, 2026-11-12: "남는 손으로 망치 손으로 두들기는 거라고. 애니메이션은 네가 만들어서
넣고 내가 블렌더로 세부 수정하면 되지"). Each is a loop on Kazusa's left arm: a locked draft under an empty touch-up layer, as
the shove's moves are, and "게임에 보내기" writes its path table (Resources/Tables/RepairHammer.csv, RepairTorch.csv), which
RepairArm plays on any character's left arm.

Run on the workspace (it adds what is missing and keeps what is there, a polish included):

  blender --background --factory-startup ArtSource/Shove/ENFILADE_Shove_Animation.blend
          --python Tools/Blender/Shove/repair_moves.py -- [--export] [--redraft]

--export sends both moves to the game; --redraft keys the drafts again (never the touch-up layers). The tools in the
hand are previews only, placed by Tables/RepairArms.csv as the game places them.

The drafts (30 fps):
- 수리 · 망치 (15 frames, 0.467 s a stroke): 1 cocked back, head up behind the fist → 4 top of the swing → 6 (0.167 s)
  STRIKE, the handle up and the face on the planks at the crosshair → 8 small rebound → 11 rising → 15 = 1.
- 수리 · 토치 (31 frames, 1 s): the torch held up to the planks, the nozzle forward at the crosshair, the wrist drawing a
  small circle (about 4 degrees) and the hand drifting a few millimetres.
"""
import csv
import importlib.util
import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector

HERE = Path(__file__).resolve().parent
sys.dont_write_bytecode = True
if 'shove_tools' in sys.modules:
    shove = sys.modules['shove_tools']
else:
    spec = importlib.util.spec_from_file_location('shove_tools', HERE / 'shove_tools.py')
    shove = importlib.util.module_from_spec(spec)
    sys.modules['shove_tools'] = shove
    spec.loader.exec_module(shove)

PREVIEW = '06 Gun preview - never exported'
# Drafts: frame -> (wrist from the eyes in game metres in the view's axes: x right, y up, z forward; the tool's handle way
# and the palm's facing in the same axes). The elbow pole (from the eyes, game metres) keeps the elbow down and out.
# Kazusa's left shoulder is 11 cm left, 17 below and 9 behind her eyes and her arm 49 cm long, so the strike's wrist (29 cm
# ahead) has the arm at 86% of its reach, the head of the hammer 40 cm ahead just left of and under the crosshair; cocked,
# the hammer is out of sight to the left.
ARM_DRAFTS = {
    'repair_hammer': {
        1: ((-.16, -.32, .20), (-.25, .75, -.61), (.97, .1, .2)),
        4: ((-.17, -.31, .18), (-.28, .65, -.71), (.97, .1, .2)),
        6: ((-.06, -.345, .29), (0, .90, .44), (.98, 0, .1)),
        8: ((-.07, -.33, .28), (-.03, .97, .20), (.98, 0, .1)),
        11: ((-.12, -.32, .24), (-.15, .90, -.40), (.97, .05, .2)),
        15: ((-.16, -.32, .20), (-.25, .75, -.61), (.97, .1, .2)),
    },
    'repair_torch': {
        1: ((-.07, -.33, .27), (0, .99, .12), (1, 0, 0)),
        9: ((-.065, -.325, .275), (.07, .99, .10), (1, 0, 0)),
        17: ((-.07, -.335, .28), (0, .98, .18), (1, 0, 0)),
        24: ((-.075, -.33, .27), (-.06, .99, .10), (1, 0, 0)),
        31: ((-.07, -.33, .27), (0, .99, .12), (1, 0, 0)),
    },
}
ELBOW_POLE = {'repair_hammer': (-.35, -.50, .10), 'repair_torch': (-.35, -.52, .15)}
# The tools' looks, as RepairLooks makes them in the game when no model is given (game metres, its handle along +y and
# its working end toward +z): name, size, place, turn about x (degrees), colour.
TOOL_PARTS = {
    'hammer': [('Handle', 'cylinder', (.028, .30, .028), (0, 0, 0), 0, (.45, .3, .18)),
               ('Head', 'cube', (.035, .035, .11), (0, .15, .015), 0, (.62, .64, .68)),
               ('Claw', 'cube', (.03, .02, .05), (0, .14, -.06), -25, (.62, .64, .68))],
    'torch': [('Canister', 'cylinder', (.06, .18, .06), (0, 0, 0), 0, (.8, .14, .1)),
              ('Valve', 'cylinder', (.03, .03, .03), (0, .1, 0), 0, (.78, .62, .25)),
              ('Nozzle', 'cylinder', (.014, .14, .014), (0, .13, .06), 60, (.62, .64, .68))],
}
TOOL_OF = {'repair_hammer': 'hammer', 'repair_torch': 'torch'}


def held_rows():
    """Tables/RepairArms.csv's rows: where each tool sits in the hand (the game's numbers)."""
    root = shove.project_root()
    rows = {}
    if root is None:
        return rows
    with open(root / shove.TABLES_DIR / 'RepairArms.csv', encoding='utf-8-sig', newline='') as f:
        for row in csv.DictReader(f):
            if row.get('id') and not row['id'].startswith('#'):
                rows[row['id']] = row
    return rows


def view_basis():
    cam = bpy.data.objects['FPS_Head_Camera']
    m = cam.matrix_world
    return m.translation.copy(), m.col[0].xyz.normalized(), m.col[1].xyz.normalized(), -m.col[2].xyz.normalized()


def in_world(v, basis, point=False):
    origin, right, up, forward = basis
    w = right * v[0] + up * v[1] + forward * v[2]
    return origin + w / shove.GAME_SCALE if point else w


def hand_frame(rig):
    """The left hand as the game reads it (world): the wrist, its run (wrist to middle knuckle), its palm's facing
    (Blender's right-handed cross; GrenadeArm.PalmNow's in Unity's axes) and its length."""
    def head(name):
        return rig.matrix_world @ rig.pose.bones[shove.ARM_BONES[name]].head
    wrist, index, middle, little = head('wrist'), head('index'), head('middle'), head('little')
    along = (middle - wrist).normalized()
    palm = along.cross(index - little)
    palm = (palm - along * palm.dot(along)).normalized()
    return wrist, along, palm, (middle - wrist).length


def basis_of(along, palm):
    z = along.normalized()
    y = (palm - z * palm.dot(z)).normalized()
    return Matrix((z.cross(y) * -1, y, z)).transposed()


def key_arm(rig, context, key):
    """The draft of one mending move: the left hand handle where the draft puts the wrist, turned so the hand holds the
    tool's handle along the draft's way, the elbow pole down and out; the hand's hold on the gun let go."""
    action = bpy.data.actions.new(shove.MOVES[key]['action'] + '_draft')
    action.use_fake_user = True
    action['enf_layer'] = 'draft'
    data = rig.animation_data or rig.animation_data_create()
    # Keyed over nothing: the locked layer underneath (another move's draft) stays out of it.
    track = data.nla_tracks.get(shove.BASE_TRACK)
    if track is not None:
        track.mute = True
    data.action = action
    data.action_blend_type = 'REPLACE'
    data.action_influence = 1.0
    shove.free_left_hand(rig, True)
    hand, elbow = rig.pose.bones[shove.ARM_HAND], rig.pose.bones[shove.ARM_ELBOW]
    # Their own channels as they were (the hand's hold on the gun is a constraint over them), put back after.
    rest_hand, rest_elbow = hand.matrix_basis.copy(), elbow.matrix_basis.copy()
    inverse = rig.matrix_world.inverted()
    for frame, (wrist, handle, palm) in sorted(ARM_DRAFTS[key].items()):
        context.scene.frame_set(frame)
        basis = view_basis()
        target = inverse @ in_world(wrist, basis, point=True)
        matrix = hand.matrix.copy()
        matrix.translation = target
        hand.matrix = matrix
        context.view_layer.update()
        # The hand's run and facing for that handle: the fingers round it, the thumb side along it (Tables/RepairArms
        # handle -x), so the run is the palm's way crossed with the handle's in the view's axes (as the game takes them).
        h, p = Vector(handle).normalized(), Vector(palm)
        p = (p - h * p.dot(h)).normalized()
        along_view = Vector((p.y * h.z - p.z * h.y, p.z * h.x - p.x * h.z, p.x * h.y - p.y * h.x))
        want = basis_of(in_world(along_view, basis), in_world(p, basis))
        _, along_now, palm_now, _ = hand_frame(rig)
        now = basis_of(along_now, palm_now)
        turn = (inverse.to_3x3() @ (want @ now.transposed()) @ rig.matrix_world.to_3x3()).to_4x4()
        head = hand.matrix.translation.copy()
        hand.matrix = Matrix.Translation(head) @ turn @ Matrix.Translation(-head) @ hand.matrix
        context.view_layer.update()
        hand.keyframe_insert('location', frame=frame, group=hand.name)
        hand.keyframe_insert('rotation_quaternion', frame=frame, group=hand.name)
        pole = elbow.matrix.copy()
        pole.translation = inverse @ in_world(ELBOW_POLE[key], basis, point=True)
        elbow.matrix = pole
        context.view_layer.update()
        elbow.keyframe_insert('location', frame=frame, group=elbow.name)
    data.action = None
    hand.matrix_basis, elbow.matrix_basis = rest_hand, rest_elbow
    shove.free_left_hand(rig, False)
    return action


def make_tool(name, collection):
    """A preview of the game's tool, made of the same simple parts (file units)."""
    holder = bpy.data.objects.new(name, None)
    holder.empty_display_size = .02
    collection.objects.link(holder)
    for part, shape, size, at, turn, colour in TOOL_PARTS[name.split('_')[1].lower()]:
        # A unit cube, or a unit cylinder along its own z: the holder's z is the tool's handle (seat_tool).
        if shape == 'cube':
            bpy.ops.mesh.primitive_cube_add(size=1)
        else:
            bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=.5, depth=1)
        obj = bpy.context.active_object
        for owner in list(obj.users_collection):
            owner.objects.unlink(obj)
        collection.objects.link(obj)
        obj.name = f'{name} {part}'
        obj.parent = holder
        # The game's tool axes (x across, y the handle, z the working end) are the holder's (x, z, -y).
        obj.location = Vector((at[0], -at[2], at[1])) / shove.GAME_SCALE
        obj.scale = Vector((size[0], size[2], size[1])) / shove.GAME_SCALE
        obj.rotation_euler = (math.radians(turn), 0, 0)
        material = bpy.data.materials.new(f'{name} {part}')
        material.diffuse_color = (*colour, 1)
        obj.data.materials.append(material)
        obj.hide_select = True
    return holder


def seat_tool(rig, holder, row):
    """The tool in the left hand as the game puts it (RepairArm.Hold): from the wrist along the hand's own axes by the
    table's hand lengths, its handle and working end along the table's ways, then parented to the hand bone."""
    wrist, along, palm, length = hand_frame(rig)
    side = along.cross(palm)  # Unity LookRotation's x for (along, palm), as a right-handed cross here
    frame = lambda v: side * v[0] + palm * v[1] + along * v[2]
    num = lambda k: float(row.get(k) or 0)
    handle = frame((num('handleX'), num('handleY'), num('handleZ'))).normalized()
    face = frame((num('faceX'), num('faceY'), num('faceZ'))).normalized()
    face = (face - handle * face.dot(handle)).normalized()
    at = wrist + frame((num('heldSide'), num('heldPalm'), num('heldAlong'))) * length
    # The game's tool space (x across, y handle, z working end) is this file's (x, z up, -y): the preview's holder is
    # built in the file's axes, so its own z is the handle and its -y the working end.
    rot = Matrix((handle.cross(-face) * -1, -face, handle)).transposed()
    origin = at - handle * (num('grip') / shove.GAME_SCALE)
    holder.parent = None
    holder.matrix_world = Matrix.Translation(origin) @ rot.to_4x4()
    bone = rig.pose.bones[shove.ARM_BONES['wrist']]
    world = holder.matrix_world.copy()
    holder.parent = rig
    holder.parent_type = 'BONE'
    holder.parent_bone = bone.name
    bpy.context.view_layer.update()
    holder.matrix_world = world


def upgrade(context, export=False, redraft=False):
    scene = context.scene
    rig = shove.find_rig(scene)
    if rig is None or 'FPS_Head_Camera' not in bpy.data.objects:
        raise SystemExit('Open ArtSource/Shove/ENFILADE_Shove_Animation.blend first.')
    collection = bpy.data.collections.get(PREVIEW)
    rows = held_rows()
    for key in ('repair_hammer', 'repair_torch'):
        move = shove.MOVES[key]
        draft = bpy.data.actions.get(move['action'] + '_draft')
        if draft is not None and redraft:
            bpy.data.actions.remove(draft)
            draft = None
        if draft is None:
            shove.select_move(context, 'kick')
            key_arm(rig, context, key)
        shove.touch_up(move['action'])
        if 'enf_impact_' + key not in scene:
            scene['enf_impact_' + key] = move['impact']
        tool = move['tool']
        holder = bpy.data.objects.get(tool) or make_tool(tool, collection or scene.collection)
        shove.select_move(context, key)
        scene.frame_set(1)
        context.view_layer.update()
        seat_tool(rig, holder, rows.get(TOOL_OF[key], {}))
    text = bpy.data.texts.get('START HERE - Shove tools.py') or bpy.data.texts.new('START HERE - Shove tools.py')
    text.from_string((HERE / 'shove_tools.py').read_text(encoding='utf-8'))
    sent = []
    if export:
        for key in ('repair_hammer', 'repair_torch'):
            shove.select_move(context, key)
            sent.append(str(shove.export_move(context, key)))
    shove.select_move(context, 'bash_long')
    return sent


if __name__ == '__main__':
    argv = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    shove.register()
    sent = upgrade(bpy.context, '--export' in argv, '--redraft' in argv)
    bpy.ops.wm.save_mainfile(compress=True)
    print('REPAIR MOVES', bpy.data.filepath)
    for path in sent:
        print('EXPORTED', path)
