"""Integration checks and offline previews; runs in a disposable Blender process.

Does not save the loaded authoring file. A QA motion is exported and reimported
to verify constraint baking, clip naming, rest pose and skeleton preservation.
"""
import bpy
import importlib.util
import json
import math
import sys
from pathlib import Path
from mathutils import Vector, Quaternion

HERE = Path(__file__).resolve().parent
OUT = Path(bpy.data.filepath).parent / 'Verification'
OUT.mkdir(parents=True, exist_ok=True)
rig = next(o for o in bpy.context.scene.objects if o.get('truefps_rig'))
scene = bpy.context.scene
names = json.loads(rig['truefps_original_bones'])
controls = json.loads(rig['truefps_controls'])
rest = {n:rig.data.bones[n].matrix_local.copy() for n in names}
parents = {n:rig.data.bones[n].parent.name if rig.data.bones[n].parent else None for n in names}
report = {'blender':bpy.app.version_string,'original_bones':len(names),'controls':len(controls)}
assert len(names)==371
assert len([p for p in rig.pose.bones for c in p.constraints if c.type=='IK'])==4
assert all(n in names for n in ['Center','Hips','HeadBone','Left wrist','Right ankle'])
assert len([a for a in bpy.context.screen.areas if a.type=='VIEW_3D'])==2
assert any(a.type=='DOPESHEET_EDITOR' and a.spaces.active.mode=='ACTION' for a in bpy.context.screen.areas)
cam = scene.camera
scene.frame_set(1)
bpy.context.view_layer.update()
direction = cam.matrix_world.to_quaternion() @ Vector((0,0,-1))
assert direction.dot(Vector((0,-1,0))) > .999, tuple(direction)
assert abs(cam.data.lens-18) < 1e-6
report['head_camera_forward'] = list(direction)
report['head_camera_position'] = list(cam.matrix_world.translation)
for side,suffix in [('Left','L'),('Right','R')]:
    for kind,end in [('hand','wrist'),('foot','ankle')]:
        ctrl=rig.pose.bones[f'CTRL_{kind}.{suffix}']
        saved_location=ctrl.location.copy()
        ctrl.location.x += 2
        bpy.context.view_layer.update()
        gap=(rig.pose.bones[f'{side} {end}'].head-rig.pose.bones[f'MCH_{kind}_lower.{suffix}'].tail).length
        assert gap < .0001, (kind,suffix,gap)
        ctrl.location=saved_location
        bpy.context.view_layer.update()
report['unreachable_ik_targets']='PASS: hands and feet stay attached'

# Workbench renders inspect the real skinned body without requiring UI access.
scene.render.engine = 'BLENDER_WORKBENCH'
scene.render.resolution_x,scene.render.resolution_y = 1280,720
scene.render.resolution_percentage=100
scene.display.shading.light = 'STUDIO'
scene.display.shading.color_type = 'MATERIAL'
scene.display.shading.show_shadows=True
scene.display.shading.show_cavity=True
scene.display.shading.background_type='WORLD'
scene.world.color=(.035,.045,.06)
head_col = bpy.data.collections['02 Character - head (external view only)']
head_col.hide_render=True
scene.render.filepath=str(OUT/'01_HeadView.png')
bpy.ops.render.render(write_still=True)
head_col.hide_render=False
external_data = bpy.data.cameras.new('QA_External')
external = bpy.data.objects.new('QA_External',external_data)
scene.collection.objects.link(external)
external.location=(2.3,-4,2.1)
external.rotation_euler=(Vector((0,0,.85))-external.location).to_track_quat('-Z','Y').to_euler()
external_data.lens=60
scene.camera=external
scene.render.filepath=str(OUT/'02_Character.png')
bpy.ops.render.render(write_still=True)
scene.camera=cam
if '--render-only' in sys.argv:
    print('TRUEFPS_RENDER_PASS',str(OUT))
    raise SystemExit(0)

# Exercise simultaneous torso, hand, foot and head animation, then bake it.
rig.animation_data.action = bpy.data.actions.new('enf_qa_reach')
rig.animation_data.action.use_fake_user=True
default=json.loads(rig['truefps_default_pose'])
for frame in [1,16,31]:
    scene.frame_set(frame)
    for n in controls:
        pb=rig.pose.bones[n]
        for attr in ['location','rotation_quaternion','scale']:
            setattr(pb,attr,default[n][attr])
    if frame==16:
        rig.pose.bones['CTRL_hand.L'].location += Vector((0,.035,-.045))
        rig.pose.bones['CTRL_foot.R'].location += Vector((0,.018,.035))
        rig.pose.bones['Hips'].location += Vector((0,-.03,0))
        rig.pose.bones['HeadBone'].rotation_quaternion = Quaternion((1,0,0),math.radians(8))
        rig.pose.bones['Chest'].rotation_quaternion = Quaternion((0,1,0),math.radians(4))
    for n in controls:
        for attr in ['location','rotation_quaternion','scale']:
            rig.pose.bones[n].keyframe_insert(data_path=attr,frame=frame,group=n)

samples={}
max_ik_error=0
for frame in [1,8,16,24,31]:
    scene.frame_set(frame)
    bpy.context.view_layer.update()
    samples[frame]={n:(rig.matrix_world @ rig.pose.bones[n].matrix).copy() for n in names}
    for kind in ['hand','foot']:
        for side in ['L','R']:
            e=(rig.pose.bones[f'MCH_{kind}_lower.{side}'].tail-rig.pose.bones[f'CTRL_{kind}.{side}'].head).length
            max_ik_error=max(max_ik_error,e)
assert max_ik_error<.001, max_ik_error
report['max_ik_endpoint_error_metres']=max_ik_error

spec=importlib.util.spec_from_file_location('truefps_tools',HERE/'truefps_tools.py')
module=importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)
module.register()
preview=next(a.spaces.active for a in bpy.context.screen.areas if a.type=='VIEW_3D' and a.spaces.active.region_3d.view_perspective=='CAMERA')
assert preview.local_view is not None and preview.camera == cam
if not bpy.app.background:
    assert bpy.data.objects['Body'].local_view_get(preview)
    assert rig.local_view_get(preview)
    assert not bpy.data.objects['Head'].local_view_get(preview)
report['local_view_membership'] = 'Checked in GUI; Blender background membership getter is unsupported'
scene.frame_set(8)
before_action=rig.animation_data.action
before_mode=rig.mode
fbx=OUT/'enf_qa_reach.fbx'
# The module's public export entry point is called below.
module.export_active_clip(bpy.context,rig,str(fbx))
assert rig.animation_data.action == before_action
assert scene.frame_current == 8
assert (scene.frame_start,scene.frame_end)==(1,31)
assert rig.mode == before_mode
assert all(n in rig.data.bones for n in controls)
assert len(rig.data.bones)==len(names)+24
for n in names:
    assert max(abs(rig.data.bones[n].matrix_local[r][c]-rest[n][r][c]) for r in range(4) for c in range(4))<1e-6

if rig.mode != 'OBJECT':
    bpy.ops.object.mode_set(mode='OBJECT')
before_objects=set(bpy.data.objects)
before_actions=set(bpy.data.actions)
bpy.ops.import_scene.fbx(filepath=str(fbx),use_anim=True,automatic_bone_orientation=False)
new_objects=set(bpy.data.objects)-before_objects
assert len(new_objects)==1, [(o.name,o.type) for o in new_objects]
imported=next(iter(new_objects))
assert imported.type=='ARMATURE'
assert set(imported.data.bones.keys())==set(names)
assert {n:imported.data.bones[n].parent.name if imported.data.bones[n].parent else None for n in names}==parents
rest_error=max(abs(imported.data.bones[n].matrix_local[r][c]-rest[n][r][c]) for n in names for r in range(4) for c in range(4))
assert rest_error < .0001, rest_error
report['roundtrip_max_rest_matrix_difference']=rest_error
new_actions=set(bpy.data.actions)-before_actions
assert len(new_actions)==1,[a.name for a in new_actions]
assert 'enf_qa_reach' in next(iter(new_actions)).name
pos_error=angle_error=0
for frame,poses in samples.items():
    scene.frame_set(frame)
    bpy.context.view_layer.update()
    for n,expected in poses.items():
        actual=imported.matrix_world @ imported.pose.bones[n].matrix
        pos_error=max(pos_error,(expected.translation-actual.translation).length)
        angle_error=max(angle_error,expected.to_quaternion().rotation_difference(actual.to_quaternion()).angle)
assert pos_error<.0005, pos_error
assert angle_error<.002, angle_error
report['roundtrip_max_position_error_metres']=pos_error
report['roundtrip_max_angle_error_degrees']=math.degrees(angle_error)
report['export_objects']=len(new_objects)
report['export_actions']=[a.name for a in new_actions]
context=bpy.context
context.view_layer.objects.active=rig
old_action=rig.animation_data.action
assert bpy.ops.truefps.new_action(clip_name='enf_operator_check',end_frame=61)=={'FINISHED'}
assert old_action.use_fake_user and rig.animation_data.action != old_action
assert scene.frame_end==61 and scene.frame_current==1
for b in rig.data.bones:
    b.select=False
rig.data.bones['CTRL_hand.L'].select=True
rig.pose.bones['CTRL_hand.L'].location.x += .1
assert bpy.ops.truefps.reset_selected()=={'FINISHED'}
assert (rig.pose.bones['CTRL_hand.L'].location-Vector(default['CTRL_hand.L']['location'])).length<1e-6
assert bpy.ops.truefps.key_controls()=={'FINISHED'}
report['editor_operators']='PASS: New Action, Reset Selected, Key All Controls'
report['status']='PASS'
(OUT/'validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print('TRUEFPS_VERIFICATION_PASS',json.dumps(report))
