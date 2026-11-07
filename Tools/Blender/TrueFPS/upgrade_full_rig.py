"""Expose all humanoid joints and add matched, keyable FK/IK modes.

Load an existing workspace, then run this script -- NEW_OUTPUT.blend.
Existing actions and the original skeleton are retained; source is not saved.
"""
import bpy
import json
import sys
from pathlib import Path
from mathutils import Vector

HERE = Path(__file__).resolve().parent

def upgrade_rig(rig):
    original = json.loads(rig['truefps_original_bones'])
    rest = {n: rig.data.bones[n].matrix_local.copy() for n in original}
    parent = {n: rig.data.bones[n].parent.name if rig.data.bones[n].parent else None for n in original}
    mapping_path = HERE.parents[2]/'Assets/_Project/Art/Models/Characters/KazusaBand/KazusaBand.kkbp.json'
    human = [entry['bone'] for entry in json.loads(mapping_path.read_text(encoding='utf-8'))['humanoid']]
    ik_controls = [n for n in rig.data.bones.keys() if n.startswith('CTRL_')]
    core = ['Center','Hips','Spine','Chest','Upper Chest','Neck','HeadBone']
    arms = [side+' '+part for side in ['Left','Right'] for part in ['shoulder','arm','elbow','wrist']]
    legs = [side+' '+part for side in ['Left','Right'] for part in ['leg','knee','ankle','toe']]
    fingers = [n for n in human if n.startswith(('Thumb','IndexFinger','MiddleFinger','RingFinger','LittleFinger'))]
    eyes = [n for n in human if n not in core+arms+legs+fingers]
    editable = list(dict.fromkeys(ik_controls+core+arms+legs+fingers+eyes))
    rig['truefps_controls'] = json.dumps(editable)
    rig['truefps_rig_version'] = 2
    rig['truefps_humanoid_controls'] = json.dumps(human)
    rig['truefps_default_ik'] = json.dumps({'ik_'+kind+'_'+s:1.0 for kind in ['arm','leg'] for s in ['L','R']})
    defaults = json.loads(rig.get('truefps_default_pose','{}'))
    for n in editable:
        if n not in defaults:
            defaults[n] = {'location':[0,0,0],'rotation_quaternion':[1,0,0,0],'scale':[1,1,1]}
    rig['truefps_default_pose'] = json.dumps(defaults)
    for col in list(rig.data.collections):
        rig.data.collections.remove(col)
    groups = [('01 IK - hands feet poles',ik_controls,True),
              ('02 Body - root spine neck head',core,True),
              ('03 Arms - shoulder upper forearm wrist',arms,True),
              ('04 Legs - thigh calf ankle toe',legs,True),
              ('05 Fingers - all 30 joints',fingers,True),
              ('06 Eyes',eyes,True),
              ('07 Original auxiliary bones',[],False),
              ('08 IK mechanism',[],False)]
    cols = {}
    for title, names, visible in groups:
        col = rig.data.collections.new(title)
        col.is_visible = visible
        for n in names:
            col.assign(rig.data.bones[n])
        cols[title] = col
    for n in rig.data.bones.keys():
        bone = rig.data.bones[n]
        if n not in editable:
            cols['08 IK mechanism' if n.startswith(('MCH_','DRV_')) else '07 Original auxiliary bones'].assign(bone)
        bone.hide = False
        bone.hide_select = n not in editable
        pb = rig.pose.bones[n]
        if n in editable:
            pb.lock_rotation = (False,False,False)
            pb.lock_rotation_w = False
            pb.rotation_mode = 'QUATERNION'
            pb.lock_scale = (True,True,True)
            if n in arms+legs and n not in ['Left toe','Right toe']:
                pb.lock_location = (True,True,True)
            if n in human:
                bone.color.palette = 'THEME04' if n.endswith('_L') or n.startswith('Left ') else 'THEME03' if n.endswith('_R') or n.startswith('Right ') else 'THEME09'
    for side,suffix in [('Left','L'),('Right','R')]:
        for kind,parts in [('arm',['arm','elbow','wrist']),('leg',['leg','knee','ankle'])]:
            prop = 'ik_'+kind+'_'+suffix
            if prop not in rig:
                rig[prop] = 1.0
            rig.id_properties_ui(prop).update(min=0.0,max=1.0,description='0: rotate FK bones; 1: move IK hand or foot')
            for part in parts:
                for con in rig.pose.bones[side+' '+part].constraints:
                    if con.name not in {'Authoring IK - baked on export','Keep hand or foot attached to solved limb'}:
                        continue
                    curve=con.driver_add('influence')
                    driver=curve.driver
                    driver.type='SCRIPTED'
                    while driver.variables:
                        driver.variables.remove(driver.variables[0])
                    variable=driver.variables.new()
                    variable.name='mode'
                    variable.type='SINGLE_PROP'
                    variable.targets[0].id=rig
                    variable.targets[0].data_path='["'+prop+'"]'
                    driver.expression='mode'
    shapes=bpy.data.collections.get('05 Control shapes - hidden')
    if not shapes:
        shapes=bpy.data.collections.new('05 Control shapes - hidden')
        bpy.context.scene.collection.children.link(shapes)
    shapes.hide_render=True
    ends={}
    for side in ['Left','Right']:
        for a,b in [('shoulder','arm'),('arm','elbow'),('elbow','wrist'),('leg','knee'),('knee','ankle'),('ankle','toe')]:
            ends[side+' '+a]=rig.data.bones[side+' '+b].head_local.copy()
        for name,finger in [(side+' wrist','MiddleFinger1_'+('L' if side=='Left' else 'R'))]:
            ends[name]=rig.data.bones[finger].head_local.copy()
    for suffix in ['L','R']:
        for prefix,indices in [('Thumb',[0,1,2]),('IndexFinger',[1,2,3]),('MiddleFinger',[1,2,3]),('RingFinger',[1,2,3]),('LittleFinger',[1,2,3])]:
            chain=[prefix+str(i)+'_'+suffix for i in indices]
            for a,b in zip(chain,chain[1:]):
                ends[a]=rig.data.bones[b].head_local.copy()
            last,previous=[rig.data.bones[n].head_local for n in chain[-2:]]
            ends[chain[-1]]=previous+(previous-last)*.8
    for n in arms+legs+fingers+eyes:
        bone=rig.data.bones[n]
        start=bone.head_local.copy()
        end=ends.get(n,bone.tail_local.copy())
        axis=end-start
        if axis.length < .001:
            axis=Vector((0,0,.02))
            end=start+axis
        direction=axis.normalized()
        side=direction.cross(Vector((0,0,1)))
        if side.length<.01:
            side=direction.cross(Vector((0,1,0)))
        side.normalize()
        perpendicular=direction.cross(side).normalized()
        width=min(axis.length*.13,.035) if n not in fingers+eyes else max(.002,axis.length*.14)
        middle=start+axis*.22
        points=[start,end,middle+side*width,middle-side*width,middle+perpendicular*width,middle-perpendicular*width]
        inverse=bone.matrix_local.inverted()
        mesh=bpy.data.meshes.new('WGT_anatomical_'+n)
        edges=[(i,j) for i in [0,1] for j in [2,3,4,5]]+[(2,4),(4,3),(3,5),(5,2)]
        mesh.from_pydata([inverse@v for v in points],edges,[])
        obj=bpy.data.objects.new(mesh.name,mesh)
        shapes.objects.link(obj)
        obj.hide_render=True
        obj.hide_set(True)
        pb=rig.pose.bones[n]
        pb.custom_shape=obj
        pb.use_custom_shape_bone_size=False
        pb.custom_shape_scale_xyz=(1,1,1)
        pb.custom_shape_translation=(0,0,0)
        pb.custom_shape_rotation_euler=(0,0,0)
        pb.custom_shape_wire_width=2.0 if n in fingers else 2.5
    # Keep humanoid channels grouped visibly, even when starting from an old action.
    rig.show_in_front=True
    rig.data.display_type='OCTAHEDRAL'
    rig.update_tag(refresh={'OBJECT'})
    bpy.context.view_layer.update()
    # Add missing humanoid channels to the current action without changing any
    # existing artist keys. IK continues to determine the solved pose.
    if rig.animation_data and rig.animation_data.action:
        import importlib.util
        spec=importlib.util.spec_from_file_location('full_rig_tools',HERE/'truefps_tools.py')
        tools=importlib.util.module_from_spec(spec)
        spec.loader.exec_module(tools)
        paths={curve.data_path for curve in tools._action_curves(rig)}
        for n in human:
            pb=rig.pose.bones[n]
            for attr in ['location','rotation_quaternion','scale']:
                if pb.path_from_id(attr) not in paths:
                    for frame in (bpy.context.scene.frame_start,bpy.context.scene.frame_end):
                        pb.keyframe_insert(data_path=attr,frame=frame,group=n)
        tools.key_ik_modes(rig,bpy.context.scene.frame_start)
        tools.key_ik_modes(rig,bpy.context.scene.frame_end)
    for n in original:
        assert (rig.data.bones[n].parent.name if rig.data.bones[n].parent else None) == parent[n]
        assert max(abs(rig.data.bones[n].matrix_local[r][c]-rest[n][r][c]) for r in range(4) for c in range(4)) < 1e-6
    return {'editable':len(editable),'humanoid':len(human),'fingers':len(fingers),'limb_bones':len(arms+legs)}

if __name__=='__main__':
    args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    output=Path(args[0]) if args else Path(bpy.data.filepath).with_name('ENFILADE_TrueFPS_FullRig.blend')
    if output.exists() and '--replace-generated' not in args:
        raise FileExistsError('Pass a new output path; existing files are preserved.')
    rig=next(o for o in bpy.context.scene.objects if o.get('truefps_rig'))
    result=upgrade_rig(rig)
    text=bpy.data.texts.get('START HERE - True FPS tools.py') or bpy.data.texts.new('START HERE - True FPS tools.py')
    text.from_string((HERE/'truefps_tools.py').read_text(encoding='utf-8'))
    text.use_module=False
    # Present the full body and spread-out fingers clearly at the first opening.
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type=='VIEW_3D' and area.spaces.active.region_3d.view_perspective!='CAMERA':
                view=area.spaces.active.region_3d
                view.view_location=Vector((0,0,.85))
                view.view_rotation=(Vector((2.3,-4,2.1))-Vector((0,0,.85))).to_track_quat('Z','Y')
                view.view_distance=2.45
    bpy.ops.wm.save_as_mainfile(filepath=str(output),check_existing=False)
    print('FULL_RIG_CREATED',str(output),json.dumps(result))
