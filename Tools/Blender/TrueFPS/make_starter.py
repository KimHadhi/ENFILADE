"""Create the neutral pose and an editable reach exercise for True FPS."""
import importlib.util
import json
import math
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Quaternion, Vector

HERE = Path(__file__).resolve().parent
READY_WRIST = (0.155, -0.290, 1.200)


def hand_frame(rig, side):
    suffix = side[0]
    sign = 1 if side == 'Left' else -1
    bones = rig.data.bones
    wrist = bones[f'{side} wrist'].head_local
    knuckles = [bones[f'{finger}Finger1_{suffix}'].head_local for finger in ('Index', 'Middle', 'Ring', 'Little')]
    forward = (sum(knuckles, Vector()) / len(knuckles) - wrist).normalized()
    across = knuckles[-1] - knuckles[0]
    across = (across - forward * across.dot(forward)).normalized()
    back = sign * forward.cross(across)
    return forward, across, back


def place_hand(context, rig, side, target):
    """Set an IK wrist, find the downward elbow plane, then align the palm."""
    suffix = side[0]
    sign = 1 if side == 'Left' else -1
    bones = rig.pose.bones
    hand = bones[f'CTRL_hand.{suffix}']
    pole = bones[f'CTRL_elbow.{suffix}']
    elbow = bones[f'MCH_hand_lower.{suffix}']
    shoulder = bones[f'{side} arm'].head.copy()
    chord = (target - shoulder).normalized()
    down = Vector((sign * .25, 0, -1))
    down = (down - chord * down.dot(chord)).normalized()
    hand_matrix = hand.matrix.copy()
    hand_matrix.translation = target
    hand.matrix = hand_matrix
    pole_matrix = pole.matrix.copy()
    midpoint = (shoulder + target) * .5

    def set_pole(angle):
        pole_matrix.translation = midpoint + (Quaternion(chord, angle) @ down) * .35
        pole.matrix = pole_matrix
        context.view_layer.update()
        offset = elbow.head - shoulder
        bend = offset - chord * offset.dot(chord)
        return bend.normalized().dot(down)

    # The imported rig's elbow roll affects the pole's angular offset.
    angle = max((i * math.tau / 24 for i in range(24)), key=set_pole)
    angle = max((angle + i * math.tau / 576 for i in range(-12, 13)), key=set_pole)
    set_pole(angle)

    forward, across, back = hand_frame(rig, side)
    rest = Matrix((forward, across, sign * back)).transposed()
    aim = (elbow.tail - elbow.head).normalized()
    palm_back = Vector((sign * .65, 0, 1))
    palm_back = (palm_back - aim * palm_back.dot(aim)).normalized()
    palm_across = sign * palm_back.cross(aim)
    posed = Matrix((aim, palm_across, sign * palm_back)).transposed()
    wrist_rotation = posed @ rest.transposed()
    hand_matrix = (wrist_rotation @ hand.bone.matrix_local.to_3x3()).to_4x4()
    hand_matrix.translation = target
    hand.matrix = hand_matrix
    context.view_layer.update()


def pose_ready_hands(context, rig):
    x, y, z = READY_WRIST
    for side, sign in [('Left', 1), ('Right', -1)]:
        place_hand(context, rig, side, Vector((sign * x, y, z)))
        forward, _, back = hand_frame(rig, side)
        hinge = forward.cross(-back).normalized()
        for finger, curl in [('Index', 8), ('Middle', 10), ('Ring', 13), ('Little', 16)]:
            for joint, weight in [(1, 1.0), (2, .7), (3, .3)]:
                bone = rig.pose.bones[f'{finger}Finger{joint}_{side[0]}']
                local_hinge = bone.bone.matrix_local.to_3x3().inverted() @ hinge
                bone.rotation_quaternion = Quaternion(local_hinge, math.radians(curl * weight))
    context.view_layer.update()


def create_starter(output):
    if output.exists():
        raise FileExistsError(f'Choose a new output path: {output}')
    spec = importlib.util.spec_from_file_location('truefps_tools', HERE / 'truefps_tools.py')
    tools = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(tools)
    context = bpy.context
    rig = tools.find_rig(context)
    scene = context.scene
    if rig is None or rig.get('truefps_rig_version', 0) < 2:
        raise RuntimeError('Open the FullRig workspace first.')
    controls = tools.control_names(rig)
    if rig.animation_data.action:
        rig.animation_data.action.use_fake_user = True
    action = bpy.data.actions.new('enf_practice_reach')
    action.use_fake_user = True
    action.use_frame_range = True
    action.frame_start, action.frame_end = 1, 31
    rig.animation_data.action = action
    scene.frame_start, scene.frame_end = 1, 31
    scene.frame_set(1)
    tools.set_default_ik(rig)
    tools.set_default_pose(rig, controls)
    context.view_layer.update()
    pose_ready_hands(context, rig)
    rig['truefps_default_pose'] = json.dumps({
        name: {attr: list(getattr(rig.pose.bones[name], attr))
               for attr in ('location', 'rotation_quaternion', 'scale')}
        for name in controls
    })
    tools.key_controls(rig, 1)
    tools.key_controls(rig, 31)
    scene.frame_set(16)
    place_hand(context, rig, 'Right', Vector((-.125, -.365, 1.215)))
    # Match the quaternion hemisphere to the neutral keys to avoid a spin.
    neutral = json.loads(rig['truefps_default_pose'])
    for name in ('CTRL_hand.R', 'CTRL_elbow.R'):
        bone = rig.pose.bones[name]
        if bone.rotation_quaternion.dot(Quaternion(neutral[name]['rotation_quaternion'])) < 0:
            bone.rotation_quaternion.negate()
        for attr in ('location', 'rotation_quaternion'):
            bone.keyframe_insert(data_path=attr, frame=16, group=name)
    for name, frame in [('Ready', 1), ('Reach', 16), ('Return', 31)]:
        action.pose_markers.new(name).frame = frame
    scene.frame_set(1)
    for bone in rig.data.bones:
        bone.select = False
    rig.data.bones['CTRL_hand.R'].select = True
    rig.data.bones.active = rig.data.bones['CTRL_hand.R']
    for area in context.screen.areas:
        if area.type == 'DOPESHEET_EDITOR' and area.spaces.active.mode == 'ACTION':
            area.spaces.active.show_pose_markers = True
            region = next(r for r in area.regions if r.type == 'WINDOW')
            with context.temp_override(area=area, region=region):
                bpy.ops.action.view_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(output), check_existing=False)


if __name__ == '__main__':
    create_starter(Path(sys.argv[sys.argv.index('--') + 1]))
