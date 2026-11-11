"""Build the Synty zombie authoring file and bake its first idle to FBX."""
import importlib.util
import json
import math
import re
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Quaternion, Vector

HERE = Path(__file__).resolve().parent
sys.dont_write_bytecode = True
ROOT = HERE.parents[2]
PACK = ROOT / 'Assets/ThirdParty/Synty/PolygonCityZombies'
SOURCE = PACK / 'Models/Zombies.fbx'
MODELS = {'Hoodie': 'Zombie_Hoodie_Male_01',
          'Roadworker': 'Zombie_Roadworker_Male_01',
          'Police': 'Zombie_Police_Male_01'}
LIMBS = {
    'arm_L': ('Shoulder_L', 'Elbow_L', 'Hand_L', 'CTRL_hand.L', 'CTRL_elbow.L'),
    'arm_R': ('Shoulder_R', 'Elbow_R', 'Hand_R', 'CTRL_hand.R', 'CTRL_elbow.R'),
    'leg_L': ('UpperLeg_L', 'LowerLeg_L', 'Ankle_L', 'CTRL_foot.L', 'CTRL_knee.L'),
    'leg_R': ('UpperLeg_R', 'LowerLeg_R', 'Ankle_R', 'CTRL_foot.R', 'CTRL_knee.R'),
}
spec = importlib.util.spec_from_file_location('truefps_tools', HERE / 'truefps_tools.py')
tools = importlib.util.module_from_spec(spec)
spec.loader.exec_module(tools)


def add_collection(name):
    result = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(result)
    return result


def build_rig():
    bpy.ops.object.select_all(action='SELECT')
    bpy.ops.object.delete(use_global=False)
    bpy.ops.import_scene.fbx(filepath=str(SOURCE), use_anim=False, use_custom_props=False,
                             automatic_bone_orientation=False, ignore_leaf_bones=False)
    context = bpy.context
    rig = next(o for o in context.scene.objects if o.type == 'ARMATURE')
    # FBX has duplicate names for right fingers. Use the same suffix Unity's
    # source Avatar uses so Copy From Other Avatar resolves those joints too.
    for bone in rig.data.bones:
        if re.fullmatch(r'(?:Thumb|IndexFinger|Finger)_\d+\.001', bone.name):
            bone.name = bone.name.removesuffix('.001') + ' 1'
    originals = list(rig.data.bones.keys())
    assert len(originals) == 50
    avatar_names = re.findall(r'^\s*- boneName: (.+)$', SOURCE.with_suffix('.fbx.meta').read_text(), re.MULTILINE)
    assert set(avatar_names) <= set(originals), set(avatar_names) - set(originals)
    rest = {b.name: b.matrix_local.copy() for b in rig.data.bones}
    parents = {b.name: b.parent.name if b.parent else None for b in rig.data.bones}
    rig['truefps_rig'] = True
    rig['truefps_kind'] = 'zombie'
    rig['truefps_original_bones'] = json.dumps(originals)
    rig['truefps_limbs'] = json.dumps(LIMBS)
    rig['truefps_preview_models'] = json.dumps(MODELS)
    rig['truefps_export_dir'] = '//Exports'
    rig['truefps_export_armature_name'] = 'Zombies(Clone)'
    rig['truefps_export_unit_scale'] = float(rig.matrix_world.to_scale().x)
    rig.show_in_front = True
    rig.data.display_type = 'STICK'

    # Only three meshes from the shared source rig are needed for preview.
    image = bpy.data.images.load(str(PACK / 'Textures/Alts/PolygonZombie_01_A.png'), check_existing=True)
    image.pack()
    material = bpy.data.materials.new('Synty zombie preview')
    material.use_nodes = True
    texture = material.node_tree.nodes.new('ShaderNodeTexImage')
    texture.image = image
    texture.interpolation = 'Closest'
    material.node_tree.links.new(texture.outputs['Color'], material.node_tree.nodes.get('Principled BSDF').inputs['Base Color'])
    material.node_tree.nodes.active = texture
    for obj in list(context.scene.objects):
        if obj.type != 'MESH':
            continue
        if obj.name not in MODELS.values():
            bpy.data.objects.remove(obj, do_unlink=True)
            continue
        obj.data.materials.clear()
        obj.data.materials.append(material)
        obj.hide_select = True
        obj.hide_render = obj.name != MODELS['Hoodie']
        obj.hide_set(obj.hide_render)

    bpy.ops.object.select_all(action='DESELECT')
    rig.select_set(True)
    context.view_layer.objects.active = rig
    bpy.ops.object.mode_set(mode='EDIT')
    edit = rig.data.edit_bones
    chains = {}
    controls = []
    for limb, (upper, lower, end, target, pole) in LIMBS.items():
        a, b, c = [edit[n].head.copy() for n in (upper, lower, end)]
        goal = edit.new(target)
        goal.head, goal.tail, goal.roll = c, edit[end].tail.copy(), edit[end].roll
        goal.length = 13
        goal.parent = edit['Root']
        direction = Vector((0, 0, -1 if limb.startswith('arm') else 1))
        pole_bone = edit.new(pole)
        pole_bone.head = b + direction * 38
        pole_bone.tail = pole_bone.head + Vector((0, 8, 0))
        pole_bone.parent = edit['Root']
        mechanisms = [edit.new(f'MCH_{limb}_{i}') for i in (1, 2)]
        first, second = mechanisms
        first.head, first.tail, first.parent = a, b, edit[upper].parent
        second.head, second.tail, second.parent = b, c, first
        second.use_connect = True
        drivers = []
        for name, mechanism in zip((upper, lower), mechanisms):
            driver = edit.new('DRV_' + name)
            driver.head, driver.tail, driver.roll = edit[name].head.copy(), edit[name].tail.copy(), edit[name].roll
            driver.parent = mechanism
            drivers.append(driver.name)
        for bone in (goal, pole_bone, *mechanisms, *(edit[n] for n in drivers)):
            bone.use_deform = False
        chains[limb] = (second.name, drivers, b)
        controls.extend((target, pole))

    bpy.ops.object.mode_set(mode='POSE')
    for bone in rig.pose.bones:
        bone.rotation_mode = 'QUATERNION'
    defaults = {}
    for limb, (upper, lower, end, target, pole) in LIMBS.items():
        mechanism, drivers, rest_bend = chains[limb]
        ik = rig.pose.bones[mechanism].constraints.new('IK')
        ik.target, ik.subtarget = rig, target
        ik.pole_target, ik.pole_subtarget = rig, pole
        ik.chain_count, ik.use_stretch, ik.iterations = 2, False, 128

        def bend_error(angle):
            ik.pole_angle = angle
            context.view_layer.update()
            return (rig.pose.bones[mechanism].head - rest_bend).length

        best = min((i * math.tau / 48 for i in range(48)), key=bend_error)
        ik.pole_angle = min((best + i * math.tau / 1152 for i in range(-12, 13)), key=bend_error)
        prop = 'ik_' + limb
        rig[prop] = defaults[prop] = float(limb.startswith('leg'))

        def constrain(name, kind, subtarget):
            constraint = rig.pose.bones[name].constraints.new(kind)
            constraint.target, constraint.subtarget = rig, subtarget
            constraint.owner_space = constraint.target_space = 'POSE'
            if kind == 'COPY_LOCATION':
                constraint.head_tail = 1.0
            driver = constraint.driver_add('influence').driver
            variable = driver.variables.new()
            variable.name, variable.type = 'mode', 'SINGLE_PROP'
            variable.targets[0].id = rig
            variable.targets[0].data_path = f'["{prop}"]'
            driver.expression = 'mode'

        for name, target_name in zip((upper, lower, end), (*drivers, target)):
            constrain(name, 'COPY_TRANSFORMS', target_name)
        constrain(end, 'COPY_LOCATION', mechanism)

    rig['truefps_controls'] = json.dumps(originals + controls)
    rig['truefps_default_ik'] = json.dumps(defaults)
    for col in list(rig.data.collections):
        rig.data.collections.remove(col)
    groups = {
        'Body': rig.data.collections.new('01 Body and face'),
        'Limbs': rig.data.collections.new('02 Arms and legs'),
        'Fingers': rig.data.collections.new('03 Fingers'),
        'IK': rig.data.collections.new('04 IK handles'),
        'Hidden': rig.data.collections.new('05 IK mechanism'),
    }
    groups['Hidden'].is_visible = False
    limb_names = {n for names in LIMBS.values() for n in names[:3]}
    shapes = add_collection('Control shapes')
    shapes.hide_render = True
    for bone in rig.data.bones:
        name = bone.name
        if name in controls:
            group = 'IK'
        elif name not in originals:
            group = 'Hidden'
        elif name.startswith(('Thumb_', 'IndexFinger_', 'Finger_')):
            group = 'Fingers'
        elif name in limb_names or name.startswith(('Clavicle_', 'Ball_', 'Toes_')):
            group = 'Limbs'
        else:
            group = 'Body'
        groups[group].assign(bone)
        bone.hide_select = group == 'Hidden'
        if group == 'Hidden':
            continue
        bone.color.palette = 'THEME04' if name.endswith(('_L', '.L')) else 'THEME03' if name.endswith(('_R', '.R', ' 1')) else 'THEME09'
        # Draw the real joint segment rather than the arbitrary FBX bone tail.
        if name == 'Root':
            vertices = [(22 * math.cos(i * math.tau / 32), 0, 22 * math.sin(i * math.tau / 32)) for i in range(32)]
            edges = [(i, (i+1) % 32) for i in range(32)]
        else:
            children = [b for b in bone.children if b.name in originals]
            start = bone.head_local
            if group == 'IK':
                span = bone.tail_local - start
            elif children:
                child = next((b for b in children if b.name.startswith(('Spine_', 'Neck', 'Head', 'IndexFinger'))), children[0])
                span = child.head_local - start
            elif group == 'Fingers' and bone.parent:
                span = (start - bone.parent.head_local) * .65
            else:
                span = (bone.tail_local - start).normalized() * 4
            if span.length < .01:
                span = Vector((0, 3, 0))
            axis = span.normalized()
            side = axis.cross(Vector((0, 0, 1)))
            if side.length < .01:
                side = axis.cross(Vector((0, 1, 0)))
            side.normalize()
            up = axis.cross(side)
            width = 4.5 if group == 'IK' else min(3.2, span.length * .15)
            center = start + span * (.5 if group == 'IK' else .25)
            points = [start, start + span, center + side*width, center - side*width, center + up*width, center - up*width]
            inverse = bone.matrix_local.inverted()
            vertices = [inverse @ p for p in points]
            edges = [(i,j) for i in (0,1) for j in (2,3,4,5)] + [(2,4),(4,3),(3,5),(5,2)]
        mesh = bpy.data.meshes.new('Shape ' + name)
        mesh.from_pydata(vertices, edges, [])
        obj = bpy.data.objects.new(mesh.name, mesh)
        shapes.objects.link(obj)
        obj.hide_set(True)
        pb = rig.pose.bones[name]
        pb.custom_shape = obj
        pb.use_custom_shape_bone_size = False
        pb.custom_shape_wire_width = 2.1
    for name in originals:
        b = rig.data.bones[name]
        assert (b.parent.name if b.parent else None) == parents[name]
        assert max(abs(b.matrix_local[i][j] - rest[name][i][j]) for i in range(4) for j in range(4)) < .0002
    context.view_layer.update()
    return rig


def author_idle(rig):
    context = bpy.context
    scene = context.scene
    scene.frame_start, scene.frame_end = 1, 91
    scene.render.fps, scene.render.fps_base = 30, 1
    scene.unit_settings.system = 'METRIC'
    scene.tool_settings.use_keyframe_insert_auto = False
    bones = rig.pose.bones

    def rotate(name, axis, degrees):
        matrix = bones[name].matrix.copy()
        rotation = Matrix.Rotation(math.radians(degrees), 3, axis)
        turned = (rotation @ matrix.to_3x3()).to_4x4()
        turned.translation = matrix.translation
        tools._set_pose_matrix(context, rig, name, turned)

    def aim(name, child, direction):
        bone = bones[name]
        local = bone.bone.matrix_local.to_3x3().inverted() @ (bones[child].bone.head_local - bone.bone.head_local)
        matrix = bone.matrix.copy()
        delta = (matrix.to_3x3() @ local).rotation_difference(Vector(direction))
        turned = (delta.to_matrix() @ matrix.to_3x3()).to_4x4()
        turned.translation = matrix.translation
        tools._set_pose_matrix(context, rig, name, turned)

    hips = bones['Hips'].matrix.copy()
    hips.translation += Vector((1.8, -3.6, .7))
    tools._set_pose_matrix(context, rig, 'Hips', hips)
    for name, amount in [('Spine_01', 7), ('Spine_02', 6), ('Spine_03', 3), ('Neck', 5), ('Head', 7)]:
        rotate(name, 'X', amount)
    rotate('Spine_03', 'Z', 4)
    rotate('Head', 'Y', -10)
    rotate('Head', 'Z', -6)
    for suffix, sign in [('L', 1), ('R', -1)]:
        aim(f'Shoulder_{suffix}', f'Elbow_{suffix}', (sign*.17, -1, -.08 if sign == 1 else .06))
        aim(f'Elbow_{suffix}', f'Hand_{suffix}', (sign*.05, -1, .27 if sign == 1 else .38))
        end = 'IndexFinger_01' if sign == 1 else 'IndexFinger_01 1'
        aim(f'Hand_{suffix}', end, bones[f'Hand_{suffix}'].head - bones[f'Elbow_{suffix}'].head)
        for prefix in ['IndexFinger', 'Finger']:
            for joint, degrees in enumerate((14, 23, 12), 1):
                pb = bones[f'{prefix}_{joint:02d}' + (' 1' if sign == -1 else '')]
                local = pb.bone.matrix_local.to_3x3().inverted() @ Vector((0, 0, -sign))
                pb.rotation_quaternion = Quaternion(local, math.radians(degrees))
    context.view_layer.update()
    names = tools.control_names(rig)
    rig['truefps_default_pose'] = json.dumps({
        name: {attr: list(getattr(bones[name], attr)) for attr in ('location','rotation_quaternion','scale')}
        for name in names})
    action = bpy.data.actions.new('enf_zombie_calm_idle')
    action.use_fake_user = True
    action.use_frame_range = True
    action.frame_start, action.frame_end = 1, 91
    action['truefps_loop'] = True
    rig.animation_data_create().action = action
    for frame, shift, drop, chest, yaw, head_roll in [
        (1, 0, 0, 0, 0, 0), (20, 2.1, .25, 3.8, -2, 1),
        (38, .5, .55, 1.1, 1, -1), (43, -.2, .6, -.6, 7, -5),
        (60, -2.3, -.3, -3.7, 3, -2), (77, -.8, -.4, -1.4, -1, .8),
        (91, 0, 0, 0, 0, 0),
    ]:
        scene.frame_set(frame)
        tools.set_default_pose(rig, names)
        tools.set_default_ik(rig)
        context.view_layer.update()
        position = bones['Hips'].matrix.copy()
        position.translation += Vector((shift, -drop, 0))
        tools._set_pose_matrix(context, rig, 'Hips', position)
        rotate('Spine_02', 'Z', chest)
        rotate('Spine_03', 'Y', chest * .8)
        rotate('Head', 'Y', yaw)
        rotate('Head', 'Z', head_roll)
        rotate('Elbow_R', 'X', chest * .7)
        tools.key_controls(rig, frame)
    for name, frame in [('Rest', 1), ('Weight shift', 20), ('Head twitch', 43), ('Return', 77), ('Loop', 91)]:
        action.pose_markers.new(name).frame = frame
    scene.frame_set(1)
    return action


def setup_workspace(rig):
    context = bpy.context
    scene = context.scene
    camera = bpy.data.objects.new('Silhouette review', bpy.data.cameras.new('Silhouette review'))
    scene.collection.objects.link(camera)
    camera.location = (2.5, -4.9, 2.0)
    camera.rotation_euler = (Vector((0, 0, .83)) - camera.location).to_track_quat('-Z','Y').to_euler()
    camera.data.lens = 60
    camera.hide_select = True
    scene.camera = camera
    floor_mesh = bpy.data.meshes.new('Studio floor')
    floor_mesh.from_pydata([(-200,-200,-.012),(200,-200,-.012),(200,200,-.012),(-200,200,-.012)], [], [(0,1,2,3)])
    floor = bpy.data.objects.new('Studio floor', floor_mesh)
    scene.collection.objects.link(floor)
    floor.hide_select = True
    floor_material = bpy.data.materials.new('Studio slate')
    floor_material.diffuse_color = (.065,.10,.13,1)
    floor_mesh.materials.append(floor_material)
    scene.render.engine = 'BLENDER_WORKBENCH'
    scene.render.resolution_x, scene.render.resolution_y = 960, 720
    scene.render.resolution_percentage = 100
    scene.display.shading.color_type = 'TEXTURE'
    scene.display.shading.show_cavity = True
    scene.display.shading.show_shadows = True
    scene.display.shading.show_specular_highlight = False
    scene.view_settings.view_transform = 'Standard'
    workspace = bpy.data.workspaces['Animation']
    workspace.name = 'ENFILADE - Animate'
    screen = workspace.screens[0]
    views = sorted((a for a in screen.areas if a.type == 'VIEW_3D'), key=lambda a:a.x)
    for area in views:
        space = area.spaces.active
        space.shading.type = 'SOLID'
        space.shading.color_type = 'TEXTURE'
        space.shading.show_cavity = True
        space.overlay.show_extras = False
        space.overlay.show_relationship_lines = False
        space.show_region_toolbar = False
    left, right = (a.spaces.active for a in views)
    left.camera = camera
    left.use_local_camera = True
    left.region_3d.view_perspective = 'CAMERA'
    left.region_3d.view_camera_zoom = 20
    left.overlay.show_overlays = False
    left.show_gizmo = False
    right.region_3d.view_location = Vector((0,0,.84))
    right.region_3d.view_distance = 3.05
    right.region_3d.view_rotation = Vector((1.5,-4,1.2)).to_track_quat('Z','Y')
    right.show_region_ui = True
    for area in screen.areas:
        if area.type == 'DOPESHEET_EDITOR' and area.height > 80:
            area.spaces.active.mode = 'ACTION'
            area.spaces.active.show_pose_markers = True
            area.spaces.active.show_region_ui = False
        elif area.type == 'PROPERTIES':
            area.spaces.active.context = 'DATA'
    for bone in rig.data.bones:
        bone.select = False
    rig.data.bones['Hips'].select = True
    rig.data.bones.active = rig.data.bones['Hips']
    scene.keying_sets_all.active = scene.keying_sets_all.get('LocRotScale')
    text = bpy.data.texts.load(str(HERE/'truefps_tools.py'), internal=True)
    text.name = 'START HERE - True FPS tools.py'
    for screen_data in bpy.data.screens:
        for area in screen_data.areas:
            if area.type == 'TEXT_EDITOR':
                area.spaces.active.text = text
    bpy.data.orphans_purge(do_local_ids=True, do_linked_ids=False, do_recursive=True)
    return screen


def main():
    output, export = (Path(p).resolve() for p in sys.argv[sys.argv.index('--')+1:])
    for path in (output, export, export.with_suffix('.clips.csv')):
        if path.exists():
            raise FileExistsError(path)
        path.parent.mkdir(parents=True, exist_ok=True)
    rig = build_rig()
    action = author_idle(rig)
    screen = setup_workspace(rig)
    tools.export_active_clip(bpy.context, rig, str(export))
    export.with_suffix('.clips.csv').write_text(
        'clip,loop,inPlace,inPlaceY,inPlaceYaw,yawOffset,cycleOffset,from,to\n'
        f'{action.name},1,0,0,0,0,0,,\n', encoding='utf-8')
    with bpy.context.temp_override(window=bpy.context.window, screen=screen):
        for area in screen.areas:
            if area.type == 'DOPESHEET_EDITOR' and area.spaces.active.mode == 'ACTION':
                with bpy.context.temp_override(area=area, region=next(r for r in area.regions if r.type == 'WINDOW')):
                    bpy.ops.action.view_all()
        bpy.ops.wm.save_as_mainfile(filepath=str(output), check_existing=False)
    print('ZOMBIE_WORKSPACE_READY', output, export)


if __name__ == '__main__':
    main()
