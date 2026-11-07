"""Build ENFILADE's non-destructive Kazusa animation workspace in Blender 4.5.

Run with --background --factory-startup --python this_file -- [output.blend].
The original FBX, rest matrices, bone names and parent hierarchy are preserved.
"""
import bpy
import json
import math
import sys
import importlib.util
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion

HERE = Path(__file__).resolve().parent
ROOT = HERE.parents[2]
SOURCE = ROOT / 'Assets/_Project/Art/Models/Characters/KazusaBand/KazusaBand.fbx'
args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUTPUT = Path(args[0]) if args else ROOT / 'ArtSource/TrueFPS/ENFILADE_TrueFPS.blend'
if OUTPUT.exists():
    raise FileExistsError('Refusing to overwrite an existing workspace. Pass a new output path after --.')
OUTPUT.parent.mkdir(parents=True, exist_ok=True)

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(SOURCE), use_anim=False,
                         automatic_bone_orientation=False, ignore_leaf_bones=False)
rig = next(o for o in bpy.context.scene.objects if o.type == 'ARMATURE')
original_names = list(rig.data.bones.keys())
original_rest = {b.name: b.matrix_local.copy() for b in rig.data.bones}
rig['truefps_rig'] = True
rig['truefps_original_bones'] = json.dumps(original_names)
rig['truefps_source'] = str(SOURCE)
rig['truefps_export_dir'] = '//Exports'
rig.show_in_front = True
rig.data.display_type = 'OCTAHEDRAL'

def collection(name):
    c = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(c)
    return c

body_col = collection('01 Character - body')
head_col = collection('02 Character - head (external view only)')
rig_col = collection('03 Animation rig')
stage_col = collection('04 Preview stage - never exported')
shape_col = collection('05 Control shapes - hidden')
shape_col.hide_render = True

def move_to(obj, col):
    for old in list(obj.users_collection):
        old.objects.unlink(obj)
    col.objects.link(obj)

move_to(rig, rig_col)
for obj in list(bpy.context.scene.objects):
    if obj.type != 'MESH':
        continue
    move_to(obj, head_col if obj.name in {'Head', 'Hair', 'HeadAccessories', 'GagEyes', 'Tears', 'RiggedTongue'} else body_col)
    obj.hide_select = True
    if obj.name in {'GagEyes', 'Tears', 'RiggedTongue'}:
        obj.hide_set(True)
        obj.hide_render = True

bpy.ops.object.select_all(action='DESELECT')
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode='EDIT')
eb = rig.data.edit_bones
limbs = []
controls = []
for side, suffix in [('Left', 'L'), ('Right', 'R')]:
    for kind, upper, lower, end, parent, pole_name in [
        ('hand', 'arm', 'elbow', 'wrist', f'{side} shoulder', 'elbow'),
        ('foot', 'leg', 'knee', 'ankle', 'Hips', 'knee'),
    ]:
        names = [f'{side} {part}' for part in (upper, lower, end)]
        a, b, c = [eb[n].head.copy() for n in names]
        ctrl_name = f'CTRL_{kind}.{suffix}'
        ctrl = eb.new(ctrl_name)
        ctrl.head = eb[names[2]].head.copy()
        ctrl.tail = eb[names[2]].tail.copy()
        ctrl.roll = eb[names[2]].roll
        ctrl.length = 0.12 if kind == 'hand' else 0.16
        ctrl.parent = eb['Center']
        ctrl.use_deform = False
        controls.append(ctrl_name)
        axis = (c-a).normalized()
        bend = (b-a) - axis * (b-a).dot(axis)
        if bend.length < 0.001:
            bend = Vector((0, 1 if kind == 'hand' else -1, 0))
        bend.normalize()
        pole = eb.new(f'CTRL_{pole_name}.{suffix}')
        pole.head = b + bend * (0.35 if kind == 'hand' else 0.45)
        pole.tail = pole.head + Vector((0, 0, 0.09))
        pole.parent = eb['Center']
        pole.use_deform = False
        controls.append(pole.name)
        m1 = eb.new(f'MCH_{kind}_upper.{suffix}')
        m1.head, m1.tail = a, b
        m1.parent = eb[parent]
        m1.use_deform = False
        m2 = eb.new(f'MCH_{kind}_lower.{suffix}')
        m2.head, m2.tail = b, c
        m2.parent = m1
        m2.use_connect = True
        m2.use_deform = False
        drivers = []
        for source_name, mechanism in zip(names[:2], [m1, m2]):
            d = eb.new('DRV_' + source_name)
            # Setting matrix on a zero-length newly created edit bone loses its
            # orientation. Copy explicit endpoints/roll before assigning parent.
            d.head = eb[source_name].head.copy()
            d.tail = eb[source_name].tail.copy()
            d.roll = eb[source_name].roll
            d.parent = mechanism
            d.use_deform = False
            drivers.append(d.name)
        limbs.append((kind, suffix, names, ctrl_name, pole.name, m1.name, m2.name, drivers, b.copy()))
bpy.ops.object.mode_set(mode='POSE')
for kind, suffix, names, ctrl_name, pole, m1, m2, drivers, elbow in limbs:
    ik = rig.pose.bones[m2].constraints.new('IK')
    ik.name = 'Two bone IK - no stretch'
    ik.target, ik.subtarget = rig, ctrl_name
    ik.pole_target, ik.pole_subtarget = rig, pole
    ik.chain_count = 2
    ik.use_stretch = False
    ik.iterations = 128
    for n in [m1, m2]:
        rig.pose.bones[n].ik_stretch = 0
    # Match the source bend plane instead of guessing a pole angle per limb.
    best = (float('inf'), 0)
    for i in range(72):
        ik.pole_angle = i * math.tau / 72
        bpy.context.view_layer.update()
        error = (rig.pose.bones[m2].head-elbow).length
        if error < best[0]:
            best = (error, ik.pole_angle)
    center = best[1]
    for i in range(-20, 21):
        ik.pole_angle = center + i * math.tau / 14400
        bpy.context.view_layer.update()
        error = (rig.pose.bones[m2].head-elbow).length
        if error < best[0]:
            best = (error, ik.pole_angle)
    ik.pole_angle = best[1]
    for original, driver in zip(names, drivers+[ctrl_name]):
        con = rig.pose.bones[original].constraints.new('COPY_TRANSFORMS')
        con.name = 'Authoring IK - baked on export'
        con.target, con.subtarget = rig, driver
        con.owner_space = con.target_space = 'POSE'
    clamp = rig.pose.bones[names[2]].constraints.new('COPY_LOCATION')
    clamp.name = 'Keep hand or foot attached to solved limb'
    clamp.target, clamp.subtarget = rig, m2
    clamp.head_tail = 1.0
    clamp.owner_space = clamp.target_space = 'POSE'

fk = ['Center', 'Hips', 'Spine', 'Chest', 'Upper Chest', 'Neck', 'HeadBone',
      'Left shoulder', 'Right shoulder', 'Left toe', 'Right toe']
fingers = [n for n in original_names if n.startswith(('Thumb', 'IndexFinger', 'MiddleFinger', 'RingFinger', 'LittleFinger'))]
editable = controls + fk + fingers
rig['truefps_controls'] = json.dumps(editable)
for pb in rig.pose.bones:
    pb.rotation_mode = 'QUATERNION'

for c in list(rig.data.collections):
    rig.data.collections.remove(c)
ik_col = rig.data.collections.new('01 IK - hands feet poles')
fk_col = rig.data.collections.new('02 FK - root torso head toes')
finger_col = rig.data.collections.new('03 Fingers - expand to edit')
deform_col = rig.data.collections.new('04 Original skeleton - do not edit')
mch_col = rig.data.collections.new('05 Mechanism - do not edit')
for b in rig.data.bones:
    target = ik_col if b.name in controls else fk_col if b.name in fk else finger_col if b.name in fingers else mch_col if b.name.startswith(('MCH_', 'DRV_')) else deform_col
    target.assign(b)
    b.hide_select = b.name not in editable
    if b.name in controls:
        b.color.palette = 'THEME04' if b.name.endswith('.L') else 'THEME03'
    elif b.name in fk:
        b.color.palette = 'THEME09'
finger_col.is_visible = False
deform_col.is_visible = False
mch_col.is_visible = False

def shape(name, vertices, edges):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata(vertices, edges, [])
    obj = bpy.data.objects.new(name, mesh)
    shape_col.objects.link(obj)
    obj.hide_render = True
    obj.hide_set(True)
    return obj

cube = shape('WGT_hand', [(x,y,z) for x in [-1,1] for y in [-1,1] for z in [-1,1]],
             [(i,j) for i in range(8) for j in range(i+1,8) if bin(i^j).count('1') == 1])
diamond = shape('WGT_pole', [(1,0,0),(-1,0,0),(0,1,0),(0,-1,0),(0,0,1),(0,0,-1)],
                [(i,j) for i in range(6) for j in range(i+1,6) if i//2 != j//2])
ring = shape('WGT_torso', [(math.cos(i*math.tau/32),0,math.sin(i*math.tau/32)) for i in range(32)],
             [(i,(i+1)%32) for i in range(32)])
for name in controls:
    pb = rig.pose.bones[name]
    pb.custom_shape = diamond if name.startswith(('CTRL_elbow', 'CTRL_knee')) else cube
    pb.use_custom_shape_bone_size = False
    size = 0.035 if pb.custom_shape == diamond else 0.055
    pb.custom_shape_scale_xyz = (size,size,size)
for name in fk:
    if name in {'Left shoulder', 'Right shoulder', 'Left toe', 'Right toe'}:
        continue
    pb = rig.pose.bones[name]
    pb.custom_shape = ring
    pb.use_custom_shape_bone_size = False
    size = 0.21 if name == 'Center' else 0.13 if name == 'Hips' else 0.085
    pb.custom_shape_scale_xyz = (size,size,size)

# A comfortable starting stance; original rest pose remains untouched.
for suffix, sign in [('L', 1), ('R', -1)]:
    pb = rig.pose.bones[f'CTRL_hand.{suffix}']
    mat = Matrix.Rotation(sign*math.radians(76), 4, 'Y') @ original_rest['Left wrist' if suffix=='L' else 'Right wrist']
    mat.translation = Vector((sign*0.20, -0.32, 1.25))
    pb.matrix = mat
bpy.context.view_layer.update()
rig['truefps_default_pose'] = json.dumps({n: {'location':list(rig.pose.bones[n].location),
    'rotation_quaternion':list(rig.pose.bones[n].rotation_quaternion),
    'scale':list(rig.pose.bones[n].scale)} for n in editable})
rig.animation_data_create()
action = bpy.data.actions.new('enf_new_clip')
action.use_fake_user = True
action.use_frame_range = True
action.frame_start, action.frame_end = 1, 31
rig.animation_data.action = action
for name in editable:
    pb = rig.pose.bones[name]
    for attr in ['location', 'rotation_quaternion', 'scale']:
        for frame in (1, 31):
            pb.keyframe_insert(data_path=attr, frame=frame, group=name)

bpy.ops.object.mode_set(mode='OBJECT')
eye = (rig.data.bones['cf_J_Eye_rz_L'].head_local + rig.data.bones['cf_J_Eye_rz_R'].head_local)/2
eye += Vector((0,-0.02,0))
cam_data = bpy.data.cameras.new('Authored head view - HFOV 90')
cam = bpy.data.objects.new('FPS_Head_Camera', cam_data)
stage_col.objects.link(cam)
cam.matrix_world = Matrix.Translation(rig.matrix_world @ eye) @ Vector((0,-1,0)).to_track_quat('-Z','Y').to_matrix().to_4x4()
con = cam.constraints.new('CHILD_OF')
con.target, con.subtarget = rig, 'HeadBone'
con.inverse_matrix = (rig.matrix_world @ rig.pose.bones['HeadBone'].matrix).inverted()
cam_data.sensor_fit = 'HORIZONTAL'
cam_data.sensor_width = 36
cam_data.lens = 18
cam_data.clip_start, cam_data.clip_end = 0.01, 200
cam_data.passepartout_alpha = 1
cam.hide_select = True
bpy.context.scene.camera = cam

def material(name, color):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color,1)
    return m

floor_mat = material('Preview floor', (.13,.17,.21))
target_mat = material('Preview target', (.22,.55,.65))
def box(name, location, scale, mat):
    bpy.ops.mesh.primitive_cube_add(size=1, location=location)
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    obj.data.materials.append(mat)
    move_to(obj, stage_col)
    obj.hide_select = True
    return obj
box('Preview floor - 1 Blender unit = 1 source metre', (0,0,-0.04), (12,12,.06), floor_mat)
box('Preview focus board', (0,-4,1.35), (1.4,.06,1), target_mat)
for x in [-1,1]:
    box('Preview scale pillar', (x,-3,0.75), (.06,.06,1.5), floor_mat)

scene = bpy.context.scene
scene.frame_start, scene.frame_end = 1, 31
scene.render.fps = 30
scene.render.fps_base = 1
scene.render.resolution_x, scene.render.resolution_y = 1920,1080
scene.render.resolution_percentage = 100
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1
scene.tool_settings.use_keyframe_insert_auto = False
scene.tool_settings.transform_pivot_point = 'MEDIAN_POINT'
scene['truefps_note'] = 'Authored head view. Runtime mouse look, IK, head clamps and smoothing are applied in Unity.'
scene.frame_set(1)
bpy.context.view_layer.update()

# Blender's factory Animation workspace already has two views and a Dope Sheet.
workspace = bpy.data.workspaces.get('Animation')
workspace.name = 'ENFILADE - Animate'
window = bpy.context.window
# Assigning window.workspace is deferred in background mode. A screen override
# activates the matching workspace immediately and must remain active at save.
screen = workspace.screens[0]
workspace_context = bpy.context.temp_override(window=window, screen=screen)
workspace_context.__enter__()
views = sorted([a for a in screen.areas if a.type == 'VIEW_3D'], key=lambda a:a.x)
preview, edit = views[0], views[-1]
for area in views:
    sp = area.spaces.active
    sp.shading.type = 'SOLID'
    sp.shading.light = 'STUDIO'
    sp.shading.color_type = 'MATERIAL'
    sp.shading.show_shadows = True
    sp.shading.show_cavity = True
    sp.clip_start = .005
    sp.clip_end = 200
    sp.overlay.show_relationship_lines = False
    sp.show_region_ui = False
    sp.show_region_toolbar = False
edit.spaces.active.region_3d.view_location = Vector((0,0,0.85))
edit.spaces.active.region_3d.view_distance = 2.8
edit.spaces.active.region_3d.view_rotation = (Vector((2.3,-4,2.1))-Vector((0,0,.85))).to_track_quat('Z','Y')
edit.spaces.active.region_3d.view_perspective = 'PERSP'
edit.spaces.active.show_region_ui = True
edit.spaces.active.overlay.show_extras = False

# Isolate the preview viewport so the face stays visible in the exterior view.
bpy.ops.object.select_all(action='DESELECT')
for obj in list(body_col.objects)+list(stage_col.objects)+[rig]:
    obj.hide_select = False
    obj.select_set(True)
with bpy.context.temp_override(window=window, area=preview, region=next(r for r in preview.regions if r.type=='WINDOW')):
    bpy.ops.view3d.localview(frame_selected=False)
preview.spaces.active.camera = cam
preview.spaces.active.use_local_camera = True
preview.spaces.active.region_3d.view_perspective = 'CAMERA'
preview.spaces.active.region_3d.view_camera_zoom = 25
preview.spaces.active.overlay.show_overlays = False
preview.spaces.active.show_gizmo = False
for obj in list(body_col.objects)+list(stage_col.objects):
    obj.hide_select = True

for area in screen.areas:
    if area.type == 'DOPESHEET_EDITOR' and area.height > 80:
        area.spaces.active.mode = 'ACTION'
        area.spaces.active.dopesheet.show_only_selected = False
        area.spaces.active.show_region_ui = False
        with bpy.context.temp_override(window=window, area=area, region=next(r for r in area.regions if r.type=='WINDOW')):
            bpy.ops.action.view_all()
    elif area.type == 'PROPERTIES':
        area.spaces.active.context = 'TOOL'

bpy.ops.object.select_all(action='DESELECT')
rig.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode='POSE')
for bone in rig.data.bones:
    bone.select = False
rig.data.bones['CTRL_hand.L'].select = True
rig.data.bones.active = rig.data.bones['CTRL_hand.L']
scene.keying_sets_all.active = scene.keying_sets_all.get('LocRotScale')
for area in screen.areas:
    if area.type == 'DOPESHEET_EDITOR' and area.height > 80:
        with bpy.context.temp_override(window=window, area=area, region=next(r for r in area.regions if r.type=='WINDOW')):
            bpy.ops.action.view_all()
    elif area.type == 'PROPERTIES':
        area.spaces.active.context = 'DATA'

tools_path = HERE/'truefps_tools.py'
if tools_path.exists():
    text = bpy.data.texts.load(str(tools_path))
    text.name = 'START HERE - True FPS tools.py'
    text.use_module = False
readme = bpy.data.texts.new('READ ME - ENFILADE')
readme.write('Open START_ANIMATING.cmd to load the True FPS sidebar.\n'
    'Left: authored head camera. Right: pose controls. Bottom: Action Editor.\n'
    'N > True FPS: Key controls / New clip / Export active clip.\n'
    'G move, R rotate. Key at each pose with Key controls. Space to play.\n'
    'Full Korean guide: START_HERE.md beside this blend file.\n'
    'Game camera adds mouse look, IK and stabilization. This is the authored pose preview.\n')
for screen2 in bpy.data.screens:
    for area in screen2.areas:
        if area.type == 'TEXT_EDITOR':
            area.spaces.active.text = bpy.data.texts.get('START HERE - True FPS tools.py', readme)

# Exact invariants matter: all export bones keep the original bind pose.
for name in original_names:
    err = max(abs(rig.data.bones[name].matrix_local[r][c]-original_rest[name][r][c]) for r in range(4) for c in range(4))
    assert err < 1e-6, (name, err)
for name in [n for n in original_names if 'DRV_'+n in rig.data.bones]:
    driver_rest = rig.data.bones['DRV_'+name].matrix_local
    assert max(abs(driver_rest[r][c]-original_rest[name][r][c]) for r in range(4) for c in range(4)) < 3e-5, name
upgrade_spec = importlib.util.spec_from_file_location('upgrade_full_rig',HERE/'upgrade_full_rig.py')
upgrade_module = importlib.util.module_from_spec(upgrade_spec)
upgrade_spec.loader.exec_module(upgrade_module)
upgrade_module.upgrade_rig(rig)
bpy.ops.wm.save_as_mainfile(filepath=str(OUTPUT), check_existing=False)
workspace_context.__exit__(None, None, None)
print('TRUEFPS_BUILD_OK', str(OUTPUT))
print('RIG', len(original_names), 'original bones;', len(json.loads(rig['truefps_controls'])), 'editable controls')
for kind,suffix,names,ctrl,pole,m1,m2,drivers,elbow in limbs:
    error = (rig.pose.bones[names[2]].head-rig.pose.bones[ctrl].head).length
    end_error = (rig.pose.bones[m2].tail-rig.pose.bones[ctrl].head).length
    print('IK',kind,suffix,'end_error',end_error,'wrist_or_ankle_error',error)
