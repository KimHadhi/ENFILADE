"""The Détente's healing moves for the shove workspace (Blender 4.5): the syringe and the bandage in the free left hand
while the gun stays in the right (the user, 2026-11-18: "데탕트 주사기 기준점 오류: 카메라 기준이 아닌 True FPS 캐릭터
신체/손 기준으로 주사기가 사용되도록"). Until then the syringe rode the camera on a fixed path and no arm moved.

Three moves on Kazusa's left arm and fingers, each a draft for the user to shape (shove_tools.MOVES):
- 주사 · 나에게 (enf_inject_self, 24 frames = 0.8 s): the syringe comes up into view, goes back and down and into the left
  thigh at 타격 (frame 10, 0.3 s), stays while the plunger goes home, comes out and away.
- 주사 · 동료에게 (enf_inject_friend, 22 frames = 0.73 s): cocked back by the shoulder, thrust ahead at 타격 (frame 8,
  0.23 s), held, drawn back and lowered.
- 치료 · 붕대 (enf_bandage, 31 frames = 1 s, a loop): the roll wound round and round ahead of the chest.
The syringe is held in the fist like an ice pick: its plunger at the thumb's side, its needle out past the little finger
(Tables/InjectArms: the handle along -x of the hand, as the hammer's). The game plays each on any character's left arm
(InjectArm) and at 타격 puts the needle's tip on that body's own thigh, or on the friend's body, wherever it is; so the
draft only has to read right on Kazusa.

Nothing here saves the workspace: its user does. shove_tools.prepare calls ensure() each time the file is opened, which
makes in memory whatever is missing (the drafts, their fingers' grip, the previews) and keeps whatever is there, a
polish included; the user's Ctrl+S then keeps it and sends what changed. To send the drafts to the game without the
window (the first time, or after changing the drafts below):

  blender --background --factory-startup ArtSource/Shove/ENFILADE_Shove_Animation.blend
          --python Tools/Blender/Shove/inject_moves.py -- --export [--pictures <folder>]

--pictures renders each move's key poses from the side and from the eyes into the folder (to look at the drafts without
opening Blender). The file is not saved.
"""
import csv
import importlib.util
import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Vector

HERE = Path(__file__).resolve().parent
KEYS = ('inject_self', 'inject_friend', 'bandage')
TABLE = 'InjectArms'
# A draft: frame -> (the wrist from the eyes in game metres, in the view's axes: x right, y up, z forward; the way from
# the fist toward the thumb's side, which is the syringe from its needle to its plunger; the palm's facing).
# Kazusa's left shoulder is 11 cm left, 17 below and 9 behind her eyes and her arm 49 cm long.
DRAFTS = {
    # Into the left thigh: the wrist ends where Kazusa's own thigh needs it for the needle's tip to be on its outside a
    # hand under the hip (measured from her bones, 2026-11-18: 22 cm left of the eyes, 62 under, 9 behind; the arm at
    # 94% of its reach), the needle down and a little inward.
    'inject_self': {
        1: ((-.21, -.47, .12), (-.20, .80, -.56), (.80, .40, -.45)),
        4: ((-.17, -.30, .27), (-.10, .97, -.22), (.90, .15, -.40)),
        7: ((-.27, -.42, .02), (-.35, .90, -.25), (.75, .40, -.50)),
        10: ((-.222, -.617, -.091), (-.30, .94, -.14), (.80, .30, -.52)),
        15: ((-.222, -.617, -.091), (-.30, .94, -.14), (.80, .30, -.52)),
        18: ((-.25, -.53, -.03), (-.33, .91, -.24), (.78, .36, -.51)),
        24: ((-.21, -.47, .12), (-.20, .80, -.56), (.80, .40, -.45)),
    },
    # At a friend ahead: the fist thrust out to 92% of the arm's reach, the needle leading.
    'inject_friend': {
        1: ((-.19, -.46, .14), (-.15, .70, -.70), (.85, .35, -.40)),
        4: ((-.23, -.24, .08), (-.10, .35, -.93), (.92, .15, -.36)),
        8: ((-.11, -.21, .36), (-.05, .12, -.99), (.95, -.25, -.19)),
        13: ((-.11, -.21, .36), (-.05, .12, -.99), (.95, -.25, -.19)),
        16: ((-.15, -.25, .26), (-.08, .22, -.97), (.93, -.10, -.35)),
        22: ((-.19, -.46, .14), (-.15, .70, -.70), (.85, .35, -.40)),
    },
    # The roll held across ahead of the chest (its axis left to right), the wrist drawing a circle 8 cm across.
    'bandage': {
        1: ((-.06, -.27, .33), (-1.0, .0, .0), (.0, -.35, .94)),
        9: ((-.10, -.23, .34), (-.98, .15, .10), (.0, -.20, .98)),
        16: ((-.06, -.19, .35), (-1.0, .0, .0), (.0, -.05, 1.0)),
        24: ((-.02, -.23, .34), (-.98, -.15, -.10), (.0, -.25, .97)),
        31: ((-.06, -.27, .33), (-1.0, .0, .0), (.0, -.35, .94)),
    },
}
ELBOW_POLES = {'inject_self': (-.45, -.40, -.20), 'inject_friend': (-.40, -.50, .05), 'bandage': (-.38, -.52, .12)}
# The things in the hand as DetenteLooks makes them in the game (game metres, the tool's axes: x across, y its handle,
# here the syringe from its needle (-y) to its plunger (+y), z its working end): name, shape, size, place, turn about x
# (degrees), colour.
PARTS = {
    'syringe': [('Barrel', 'cylinder', (.034, .12, .034), (0, 0, 0), 0, (.9, .92, .95)),
                ('Band', 'cylinder', (.037, .012, .037), (0, -.058, 0), 0, (1, .55, .1)),
                ('Rod', 'cylinder', (.012, .07, .012), (0, .095, 0), 0, (.9, .92, .95)),
                ('Thumb', 'cylinder', (.042, .008, .042), (0, .132, 0), 0, (.9, .92, .95)),
                ('Needle', 'cylinder', (.0035, .05, .0035), (0, -.10, 0), 0, (.75, .77, .8))],
    'bandage': [('Roll', 'cylinder', (.06, .07, .06), (0, 0, 0), 0, (.96, .95, .9)),
                ('Tail', 'cube', (.004, .05, .1), (.035, 0, .05), 0, (.96, .95, .9))],
}
TOOLS = {'inject_self': 'syringe', 'inject_friend': 'syringe', 'bandage': 'bandage'}


def _repair(shove):
    """repair_moves.py as a module working on this session's shove_tools: its drafting of an arm move and its previews
    are what these moves are made with too."""
    sys.modules.setdefault('shove_tools', shove)
    if 'enf_repair_moves' in sys.modules:
        return sys.modules['enf_repair_moves']
    spec = importlib.util.spec_from_file_location('enf_repair_moves', HERE / 'repair_moves.py')
    module = importlib.util.module_from_spec(spec)
    sys.modules['enf_repair_moves'] = module
    writes = sys.dont_write_bytecode
    sys.dont_write_bytecode = True
    try:
        spec.loader.exec_module(module)
    finally:
        sys.dont_write_bytecode = writes
    return module


def held_rows(shove):
    """Tables/InjectArms.csv's rows: where each thing sits in the hand (the game's numbers), by row id."""
    rows = {}
    root = shove.project_root()
    if root is None:
        return rows
    path = shove.table_file(root, TABLE, new_in='Classes')
    if not path.exists():
        return rows
    with open(path, encoding='utf-8-sig', newline='') as f:
        for row in csv.DictReader(f):
            if row.get('id') and not row['id'].startswith('#'):
                rows[row['id']] = row
    return rows


ROW_OF = {'inject_self': 'self', 'inject_friend': 'friend', 'bandage': 'bandage'}


def ensure(context, shove, redraft=False):
    """Makes whatever of the three moves this file lacks: the draft, the fingers' grip in it, its empty touch-up, the
    strike's frame, and the preview in the hand. What is there stays as it is. Returns the keys it made a draft for."""
    scene = context.scene
    rig = shove.find_rig(scene)
    if rig is None or 'FPS_Head_Camera' not in bpy.data.objects or not shove.is_shove_file(scene):
        return []
    repair = _repair(shove)
    repair.ARM_DRAFTS.update(DRAFTS)
    repair.ELBOW_POLE.update(ELBOW_POLES)
    for name, parts in PARTS.items():
        repair.TOOL_PARTS.setdefault(name, parts)
    was = shove.current_move(scene)
    collection = bpy.data.collections.get(repair.PREVIEW) or scene.collection
    rows = held_rows(shove)
    made = []
    for key in KEYS:
        move = shove.MOVES[key]
        draft = bpy.data.actions.get(move['action'] + '_draft')
        edit = bpy.data.actions.get(move['action'])
        if draft is not None and redraft:
            bpy.data.actions.remove(draft)
            draft = None
        if draft is None and (edit is None or redraft):
            # Keyed from the start pose with the kick shown, as the mending moves were (the gun in its hold, the legs down).
            shove.select_move(context, 'kick')
            draft = repair.key_arm(rig, context, key)
            shove.key_grip(context, rig, draft, move)
            made.append(key)
        shove.touch_up(move['action'])
        if 'enf_impact_' + key not in scene:
            scene['enf_impact_' + key] = move['impact']
        tool = move['tool']
        row = rows.get(ROW_OF[key], {})
        if bpy.data.objects.get(tool) is None and row:
            holder = repair.make_tool(tool, collection, {})
            shove.select_move(context, key)
            scene.frame_set(1)
            context.view_layer.update()
            repair.seat_tool(rig, holder, row)
    if made:
        shove.select_move(context, was)
        print('ENFILADE: 데탕트 동작 시안을 만들었습니다: ' + ', '.join(shove.MOVES[key]['label'] for key in made))
    return made


def pictures(context, shove, folder):
    """Each move's key poses from the left side and from the eyes, a PNG each (Workbench): what the drafts look like."""
    scene = context.scene
    folder = Path(folder)
    folder.mkdir(parents=True, exist_ok=True)
    rig = shove.find_rig(scene)
    head = bpy.data.objects['FPS_Head_Camera']
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.render.resolution_x, scene.render.resolution_y = 480, 480
    scene.render.image_settings.file_format = 'PNG'
    scene.render.film_transparent = False
    side = bpy.data.objects.new('ENF inject side', bpy.data.cameras.new('ENF inject side'))
    scene.collection.objects.link(side)
    side.data.type = 'ORTHO'
    eyes = head.matrix_world.translation.copy()
    right, up = head.matrix_world.col[0].xyz.normalized(), head.matrix_world.col[1].xyz.normalized()
    forward = -head.matrix_world.col[2].xyz.normalized()
    unit = 1.0 / shove.GAME_SCALE
    written = []
    views = {
        # From the character's left, the whole reach of the arm in frame; from ahead; and through its own eyes.
        'left': (eyes - right * 3 * unit - up * .45 * unit + forward * .1 * unit, right, 1.5 * unit),
        'front': (eyes + forward * 3 * unit - up * .45 * unit, -forward, 1.5 * unit),
    }
    was = scene.camera
    for key in KEYS:
        shove.select_move(context, key)
        for frame in sorted(DRAFTS[key]):
            scene.frame_set(frame)
            context.view_layer.update()
            for name, (at, look, size) in views.items():
                side.location = at
                side.rotation_euler = look.to_track_quat('-Z', 'Y').to_euler()
                side.data.ortho_scale = size
                scene.camera = side
                scene.render.filepath = str(folder / f'{key}_{frame:02d}_{name}.png')
                bpy.ops.render.render(write_still=True)
                written.append(scene.render.filepath)
            scene.camera = head if head.type == 'CAMERA' else was
            if scene.camera is not None and scene.camera.type == 'CAMERA':
                scene.render.filepath = str(folder / f'{key}_{frame:02d}_eyes.png')
                bpy.ops.render.render(write_still=True)
                written.append(scene.render.filepath)
    scene.camera = was
    bpy.data.objects.remove(side, do_unlink=True)
    return written


def main(argv):
    spec = importlib.util.spec_from_file_location('shove_tools', HERE / 'shove_tools.py')
    shove = importlib.util.module_from_spec(spec)
    sys.modules['shove_tools'] = shove
    sys.dont_write_bytecode = True
    spec.loader.exec_module(shove)
    shove.register()
    context = bpy.context
    log = []
    made = ensure(context, shove, '--redraft' in argv)
    log.append('MADE ' + ','.join(made))
    if '--pictures' in argv:
        for path in pictures(context, shove, argv[argv.index('--pictures') + 1]):
            log.append('PICTURE ' + path)
    if '--export' in argv:
        for key in KEYS:
            shove.select_move(context, key)
            log.append('EXPORTED ' + str(shove.export_move(context, key)))
    if '--log' in argv:
        Path(argv[argv.index('--log') + 1]).write_text('\n'.join(log) + '\n', encoding='utf-8')
    # The workspace itself is never saved from here.


if __name__ == '__main__':
    main(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else [])
