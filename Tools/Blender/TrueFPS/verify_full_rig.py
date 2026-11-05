"""User-facing rig integration checks; never saves the loaded authoring file.

Run Blender with ENFILADE_TrueFPS_FullRig.blend, then --python this file.
Tests visible/selectable finger and limb controls, effective skin deformation,
FK/IK matching, and one mixed-mode animation's FBX round trip.
"""
import bpy
import importlib.util
import json
import math
from pathlib import Path
from mathutils import Vector, Quaternion

HERE = Path(__file__).resolve().parent
OUT = Path(bpy.data.filepath).parent / 'Verification'
OUT.mkdir(parents=True, exist_ok=True)
spec = importlib.util.spec_from_file_location('truefps_tools', HERE / 'truefps_tools.py')
tools = importlib.util.module_from_spec(spec)
spec.loader.exec_module(tools)
tools.register()
scene = bpy.context.scene
rig = next(o for o in scene.objects if o.get('truefps_rig'))
bpy.context.view_layer.objects.active = rig
original = json.loads(rig['truefps_original_bones'])
controls = set(json.loads(rig['truefps_controls']))
rest = {n: rig.data.bones[n].matrix_local.copy() for n in original}
parents = {n: rig.data.bones[n].parent.name if rig.data.bones[n].parent else None for n in original}
fingers = [n for n in original if n.startswith(('Thumb', 'IndexFinger', 'MiddleFinger', 'RingFinger', 'LittleFinger'))]
limbs = {}
for side, suffix in [('Left', 'L'), ('Right', 'R')]:
    limbs['arm_' + suffix] = ([side+' arm', side+' elbow', side+' wrist'], 'hand', 'elbow', suffix)
    limbs['leg_' + suffix] = ([side+' leg', side+' knee', side+' ankle'], 'foot', 'knee', suffix)
major = [n for values in limbs.values() for n in values[0]]
assert len(fingers) == 30
assert len(major) == 12
report = {'blender': bpy.app.version_string, 'source': Path(bpy.data.filepath).name}

for name in fingers + major:
    bone = rig.data.bones[name]
    assert name in controls, ('missing keyable control', name)
    assert not bone.hide and not bone.hide_select, ('hidden/locked bone', name)
    assert any(getattr(c, 'is_visible_effectively', c.is_visible) for c in bone.collections), ('hidden collection', name)
    assert not all(rig.pose.bones[name].lock_rotation), ('rotation locked', name)
report['visible_selectable_fingers'] = len(fingers)
report['visible_selectable_major_limb_bones'] = len(major)

def update():
    rig.update_tag()
    bpy.context.view_layer.update()

def pose_matrices(names):
    update()
    evaluated = rig.evaluated_get(bpy.context.evaluated_depsgraph_get())
    return {n: evaluated.pose.bones[n].matrix.copy() for n in names}

def difference(a, b):
    return ((a.translation-b.translation).length,
            a.to_quaternion().rotation_difference(b.to_quaternion()).angle)

# Detach animation in this disposable process so key evaluation cannot undo
# the individual manipulation tests. Preserve the loaded pose explicitly.
loaded_pose = pose_matrices(original)
rig.animation_data.action = None
update()
max_ik_to_fk_position = 0.0
max_ik_to_fk_rotation = 0.0
for limb, (names, kind, pole, suffix) in limbs.items():
    tools.switch_limb(bpy.context, rig, limb, 'FK', key=False)
    update()
    before = pose_matrices(names)
    assert float(rig['ik_'+limb]) == 0.0
    # Every major FK joint, including wrists and ankles, must really rotate.
    for name in names:
        pb = rig.pose.bones[name]
        basis = pb.matrix_basis.copy()
        initial = pose_matrices([name])[name]
        pb.rotation_quaternion = pb.rotation_quaternion @ Quaternion((1,0,0), math.radians(12))
        changed = pose_matrices([name])[name]
        assert difference(initial, changed)[1] > math.radians(8), ('ineffective FK rotation', name)
        pb.matrix_basis = basis
        update()
    tools.switch_limb(bpy.context, rig, limb, 'IK', key=False)
    before = pose_matrices(names)
    tools.switch_limb(bpy.context, rig, limb, 'FK', key=False)
    after = pose_matrices(names)
    for name in names:
        position, rotation = difference(before[name], after[name])
        max_ik_to_fk_position = max(max_ik_to_fk_position, position)
        max_ik_to_fk_rotation = max(max_ik_to_fk_rotation, rotation)
    # Matching back to IK must retain the endpoint pose and bend direction.
    before = pose_matrices(names)
    tools.switch_limb(bpy.context, rig, limb, 'IK', key=False)
    after = pose_matrices(names)
    endpoint_position, endpoint_rotation = difference(before[names[-1]], after[names[-1]])
    assert endpoint_position < .001, ('FK to IK endpoint jump', limb, endpoint_position)
    assert endpoint_rotation < .005, ('FK to IK endpoint rotation jump', limb, endpoint_rotation)
    assert (before[names[1]].translation-after[names[1]].translation).length < .003, ('FK to IK bend jump', limb)
    ctrl = rig.pose.bones[f'CTRL_{kind}.{suffix}']
    saved = ctrl.location.copy()
    ctrl.location.x += 2
    update()
    gap = (rig.pose.bones[names[-1]].head-rig.pose.bones[f'MCH_{kind}_lower.{suffix}'].tail).length
    assert gap < .0001, ('detached endpoint', limb, gap)
    ctrl.location = saved
    update()
assert max_ik_to_fk_position < .0001, max_ik_to_fk_position
assert max_ik_to_fk_rotation < .002, max_ik_to_fk_rotation
report['fk_rotatable_major_bones'] = len(major)
report['ik_to_fk_max_position_error_m'] = max_ik_to_fk_position
report['ik_to_fk_max_rotation_error_degrees'] = math.degrees(max_ik_to_fk_rotation)
report['fk_to_ik_endpoint_and_bend_match'] = 'PASS'
report['unreachable_ik_targets'] = 'PASS'

# A visible bone is not enough: rotating each finger must move weighted skin.
meshes = [o for o in scene.objects if o.type == 'MESH' and any(m.type == 'ARMATURE' and m.object == rig for m in o.modifiers)]
weighted = {}
for name in fingers:
    descendants = {name} | {b.name for b in rig.data.bones[name].children_recursive}
    candidates = []
    for obj in meshes:
        groups = {g.index for g in obj.vertex_groups if g.name in descendants}
        if not groups:
            continue
        for vertex in obj.data.vertices:
            weight = sum(g.weight for g in vertex.groups if g.group in groups)
            if weight > .15:
                candidates.append((weight, obj, vertex.index))
    candidates.sort(key=lambda row: row[0], reverse=True)
    # Sample across the weighted surface, rather than only vertices on a joint.
    stride = max(1, len(candidates)//24)
    weighted[name] = [(obj,index) for _,obj,index in candidates[::stride][:24]]
    assert weighted[name], ('no weighted skin for finger or descendants', name)

def skin_points(samples):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    cache = {obj: obj.evaluated_get(depsgraph) for obj,_ in samples}
    return [cache[obj].matrix_world @ cache[obj].data.vertices[index].co for obj,index in samples]

minimum_skin_movement = float('inf')
for name in fingers:
    pb = rig.pose.bones[name]
    basis = pb.matrix_basis.copy()
    update()
    before = skin_points(weighted[name])
    pb.rotation_quaternion = pb.rotation_quaternion @ Quaternion((1,0,0), math.radians(22))
    update()
    after = skin_points(weighted[name])
    movement = max((a-b).length for a,b in zip(before,after))
    assert movement > .00005, ('finger skin did not move', name, movement)
    minimum_skin_movement = min(minimum_skin_movement, movement)
    pb.matrix_basis = basis
    update()
report['finger_skin_deformation_tests'] = len(fingers)
report['minimum_tested_finger_skin_motion_m'] = minimum_skin_movement

# Author a short clip that uses both FK and IK mode keys, then export only
# the original skeleton and compare all sampled transforms after reimport.
for limb in limbs:
    tools.switch_limb(bpy.context, rig, limb, 'FK', key=False)
scene.frame_start,scene.frame_end = 1,31
rig.animation_data.action = bpy.data.actions.new('enf_full_rig_qa')
rig.animation_data.action.use_fake_user = True
scene.frame_set(1)
tools.key_controls(rig,1)
scene.frame_set(11)
rig.pose.bones['Left leg'].rotation_quaternion @= Quaternion((1,0,0),math.radians(10))
rig.pose.bones['Right elbow'].rotation_quaternion @= Quaternion((0,0,1),math.radians(12))
rig.pose.bones['IndexFinger1_L'].rotation_quaternion @= Quaternion((1,0,0),math.radians(25))
tools.key_controls(rig,11)
scene.frame_set(21)
tools.switch_limb(bpy.context,rig,'arm_L','IK',key=True)
rig.pose.bones['CTRL_hand.L'].location += Vector((0,.015,-.02))
tools.key_controls(rig,21)
scene.frame_set(31)
tools.switch_limb(bpy.context,rig,'leg_R','IK',key=True)
rig.pose.bones['CTRL_foot.R'].location += Vector((0,.01,.015))
tools.key_controls(rig,31)
samples = {}
for frame in [1,6,11,16,21,26,31]:
    scene.frame_set(frame)
    samples[frame] = {n: rig.matrix_world @ m for n,m in pose_matrices(original).items()}
scene.frame_set(11)
before_action = rig.animation_data.action
before_mode = rig.mode
fbx = OUT/'enf_full_rig_qa.fbx'
tools.export_active_clip(bpy.context,rig,str(fbx))
assert rig.animation_data.action == before_action
assert rig.mode == before_mode and scene.frame_current == 11
assert (scene.frame_start,scene.frame_end) == (1,31)
if rig.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
old_objects,old_actions = set(bpy.data.objects),set(bpy.data.actions)
bpy.ops.import_scene.fbx(filepath=str(fbx),use_anim=True,automatic_bone_orientation=False)
objects = set(bpy.data.objects)-old_objects
actions = set(bpy.data.actions)-old_actions
assert len(objects) == 1 and len(actions) == 1
imported = next(iter(objects))
assert imported.type == 'ARMATURE'
assert set(imported.data.bones.keys()) == set(original)
assert {n:imported.data.bones[n].parent.name if imported.data.bones[n].parent else None for n in original} == parents
rest_error = max(abs(imported.data.bones[n].matrix_local[r][c]-rest[n][r][c]) for n in original for r in range(4) for c in range(4))
assert rest_error < .0001, rest_error
position_error = rotation_error = 0.0
for frame,poses in samples.items():
    scene.frame_set(frame)
    update()
    for name,expected in poses.items():
        actual = imported.matrix_world @ imported.pose.bones[name].matrix
        position,rotation = difference(expected,actual)
        position_error=max(position_error,position)
        rotation_error=max(rotation_error,rotation)
assert position_error < .0005, position_error
assert rotation_error < .002, rotation_error
report['mixed_fk_ik_roundtrip_max_position_error_m'] = position_error
report['mixed_fk_ik_roundtrip_max_rotation_error_degrees'] = math.degrees(rotation_error)
report['roundtrip_rest_matrix_error'] = rest_error
report['exported_bones'] = len(imported.data.bones)
report['exported_objects'] = len(objects)
report['exported_actions'] = len(actions)
report['status'] = 'PASS'
(OUT/'full_rig_validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('TRUEFPS_FULL_RIG_VERIFICATION_PASS',json.dumps(report))
