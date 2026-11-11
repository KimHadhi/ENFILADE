"""ENFILADE True FPS tools, Blender 4.5 LTS.

Run from START_ANIMATING.cmd, or run this text in Blender's Text Editor.
No preference changes or automatic-script permission changes are made.
"""
import bpy
import json
import math
import re
from contextlib import contextmanager
from pathlib import Path
from bpy.app.handlers import persistent
from bpy.props import StringProperty, IntProperty, BoolProperty, EnumProperty
from bpy_extras.io_utils import ExportHelper
from mathutils import Matrix, Quaternion, Vector

bl_info = {'name': 'ENFILADE True FPS', 'author': 'ENFILADE',
           'version': (1, 1, 0), 'blender': (4, 5, 0),
           'location': '3D View > Sidebar > True FPS', 'category': 'Animation'}


def find_rig(context):
    obj = context.active_object
    if obj and obj.type == 'ARMATURE' and obj.get('truefps_rig'):
        return obj
    return next((o for o in context.scene.objects
                 if o.type == 'ARMATURE' and o.get('truefps_rig')), None)


def control_names(rig):
    return [n for n in json.loads(rig['truefps_controls']) if n in rig.pose.bones]


LIMBS = {'arm_L': ('Left arm', 'Left elbow', 'Left wrist', 'CTRL_hand.L', 'CTRL_elbow.L'),
         'arm_R': ('Right arm', 'Right elbow', 'Right wrist', 'CTRL_hand.R', 'CTRL_elbow.R'),
         'leg_L': ('Left leg', 'Left knee', 'Left ankle', 'CTRL_foot.L', 'CTRL_knee.L'),
         'leg_R': ('Right leg', 'Right knee', 'Right ankle', 'CTRL_foot.R', 'CTRL_knee.R')}


def limb_names(rig):
    mapping = rig.get('truefps_limbs')
    return json.loads(mapping) if mapping else LIMBS


def _action_curves(rig):
    data = rig.animation_data
    if not data or not data.action:
        return []
    action = data.action
    if action.is_action_legacy:
        return list(action.fcurves)
    curves = []
    slot = data.action_slot
    if slot:
        for layer in action.layers:
            for strip in layer.strips:
                bag = strip.channelbag(slot)
                if bag:
                    curves.extend(bag.fcurves)
    return curves


def key_ik_modes(rig, frame):
    paths = []
    for limb in LIMBS:
        prop = 'ik_' + limb
        if prop in rig:
            path = '["' + prop + '"]'
            rig.keyframe_insert(data_path=path, frame=frame, group='IK / FK mode')
            paths.append(path)
    for curve in _action_curves(rig):
        if curve.data_path in paths:
            for point in curve.keyframe_points:
                point.interpolation = 'CONSTANT'


def set_default_ik(rig):
    defaults = json.loads(rig.get('truefps_default_ik', '{}'))
    for limb in LIMBS:
        prop = 'ik_' + limb
        if prop in rig:
            rig[prop] = float(defaults.get(prop, defaults.get(limb, 1.0)))
    rig.update_tag(refresh={'OBJECT'})


def _set_pose_matrix(context, rig, name, matrix):
    pb = rig.pose.bones[name]
    kwargs = {}
    if pb.parent:
        kwargs['parent_matrix'] = pb.parent.matrix.copy()
        kwargs['parent_matrix_local'] = pb.parent.bone.matrix_local
    pb.matrix_basis = pb.bone.convert_local_to_pose(
        matrix, pb.bone.matrix_local, invert=True, **kwargs)
    context.view_layer.update()


def switch_limb(context, rig, limb='arm_L', mode='FK', key=True):
    """Match the current pose before switching a single limb's IK/FK mode.

    IK-to-FK reproduces the full solved transforms. FK-to-IK matches the end
    effector and bend plane; deliberately authored axial FK twist is governed
    by the two-bone IK solver after switching.
    """
    mapping = limb_names(rig)
    if limb not in mapping or mode not in {'FK', 'IK'}:
        raise ValueError('Choose one limb and FK or IK.')
    prop = 'ik_' + limb
    if prop not in rig:
        raise ValueError('Open the Full Rig workspace to use IK / FK switching.')
    new_value = 1.0 if mode == 'IK' else 0.0
    if abs(float(rig[prop]) - new_value) < .001:
        return
    names = mapping[limb]
    context.view_layer.update()
    evaluated = rig.evaluated_get(context.evaluated_depsgraph_get())
    matrices = {n: evaluated.pose.bones[n].matrix.copy() for n in names[:3]}
    old_value = float(rig[prop])
    frame = context.scene.frame_current
    path = '["' + prop + '"]'
    if key and frame > context.scene.frame_start and not any(c.data_path == path for c in _action_curves(rig)):
        rig.keyframe_insert(data_path=path, frame=context.scene.frame_start, group='IK / FK mode')
    if mode == 'FK':
        rig[prop] = 0.0
        rig.update_tag(refresh={'OBJECT'})
        context.view_layer.update()
        for name in names[:3]:
            _set_pose_matrix(context, rig, name, matrices[name])
    else:
        _set_pose_matrix(context, rig, names[3], matrices[names[2]])
        a,b,c = [matrices[n].translation.copy() for n in names[:3]]
        axis = c-a
        if axis.length < 1e-6:
            raise ValueError('The limb is folded onto itself; extend it slightly before enabling IK.')
        axis.normalize()
        bend = b-a-axis*(b-a).dot(axis)
        if bend.length < .0001:
            bend = rig.pose.bones[names[4]].head-a
            bend -= axis*bend.dot(axis)
        if bend.length < .0001:
            bend = axis.cross(Vector((0,0,1)))
            if bend.length < .0001:
                bend = axis.cross(Vector((1,0,0)))
        pole_matrix = rig.pose.bones[names[4]].matrix.copy()
        pole_matrix.translation = b + bend.normalized()*max((b-a).length+(c-b).length, .2)
        _set_pose_matrix(context, rig, names[4], pole_matrix)
        rig[prop] = 1.0
        rig.update_tag(refresh={'OBJECT'})
        context.view_layer.update()
        # Imported FBX chains carry bone-roll offsets. Match their actual bend
        # by rotating the pole around the limb axis, rather than assuming that
        # a pole in the geometric bend plane reproduces the source knee/elbow.
        bend_direction=bend.normalized()
        distance=max((b-a).length+(c-b).length,.2)
        best=((rig.pose.bones[names[1]].head-b).length,0.0)
        if best[0] > .0002:
            for i in range(48):
                angle=math.tau*i/48
                pole_matrix.translation=b+Quaternion(axis,angle)@bend_direction*distance
                _set_pose_matrix(context,rig,names[4],pole_matrix)
                error=(rig.pose.bones[names[1]].head-b).length
                if error < best[0]:
                    best=(error,angle)
            center=best[1]
            for i in range(-12,13):
                angle=center+i*math.tau/1152
                pole_matrix.translation=b+Quaternion(axis,angle)@bend_direction*distance
                _set_pose_matrix(context,rig,names[4],pole_matrix)
                error=(rig.pose.bones[names[1]].head-b).length
                if error < best[0]:
                    best=(error,angle)
            pole_matrix.translation=b+Quaternion(axis,best[1])@bend_direction*distance
            _set_pose_matrix(context,rig,names[4],pole_matrix)
    if key:
        for name in names:
            for attr in ('location', 'rotation_quaternion', 'scale'):
                rig.pose.bones[name].keyframe_insert(data_path=attr, frame=frame, group=name)
        rig.keyframe_insert(data_path=path, frame=frame, group='IK / FK mode')
        for curve in _action_curves(rig):
            if curve.data_path == path:
                for point in curve.keyframe_points:
                    point.interpolation = 'CONSTANT'
        rig.animation_data.action.use_fake_user = True
    rig.update_tag(refresh={'OBJECT'})
    context.view_layer.update()


def set_default_pose(rig, names):
    defaults = json.loads(rig.get('truefps_default_pose', '{}'))
    for name in names:
        pb = rig.pose.bones.get(name)
        if pb is None:
            continue
        values = defaults.get(name, {})
        pb.rotation_mode = 'QUATERNION'
        pb.location = values.get('location', (0, 0, 0))
        pb.rotation_quaternion = values.get('rotation_quaternion', (1, 0, 0, 0))
        pb.scale = values.get('scale', (1, 1, 1))


def key_controls(rig, frame):
    for name in control_names(rig):
        pb = rig.pose.bones[name]
        for attr in ('location', 'rotation_quaternion', 'scale'):
            pb.keyframe_insert(data_path=attr, frame=frame, group=name)
    key_ik_modes(rig, frame)
    if rig.animation_data and rig.animation_data.action:
        rig.animation_data.action.use_fake_user = True


def preview_models(context, rig):
    """Find the configured zombie mesh variants in the current view layer."""
    models = json.loads(rig.get('truefps_preview_models', '{}'))
    return {label: context.view_layer.objects[name] for label, name in models.items()
            if name in context.view_layer.objects}


@contextmanager
def _preserve_preview_pose(context, rig):
    """Restore the editing frame and any pending pose edits after sampling."""
    scene = context.scene
    frame, subframe = scene.frame_current, scene.frame_subframe
    object_basis = rig.matrix_basis.copy()
    channels = ('location', 'rotation_quaternion', 'scale')
    bones = [rig.pose.bones[name] for name in control_names(rig)]
    transforms = [(bone, [getattr(bone, channel).copy() for channel in channels])
                  for bone in bones]
    modes = {'ik_' + limb: rig['ik_' + limb] for limb in limb_names(rig) if 'ik_' + limb in rig}
    try:
        yield
    finally:
        scene.frame_set(frame, subframe=subframe)
        rig.matrix_basis = object_basis
        for bone, values in transforms:
            for channel, value in zip(channels, values):
                setattr(bone, channel, value)
        for name, value in modes.items():
            rig[name] = value
        rig.update_tag(refresh={'OBJECT'})
        context.view_layer.update()


def _loop_range(context, rig):
    if not rig or not rig.animation_data or not rig.animation_data.action:
        raise ValueError('Select an active Action first.')
    start, end = context.scene.frame_start, context.scene.frame_end
    if end <= start:
        raise ValueError('End frame must be greater than Start frame.')
    return start, end


def loop_seam(context, rig):
    """Return the largest start/end differences in meters, degrees and scale ratio."""
    start, end = _loop_range(context, rig)
    names = json.loads(rig['truefps_original_bones'])
    samples = []
    with _preserve_preview_pose(context, rig):
        for frame in (start, end):
            context.scene.frame_set(frame)
            context.view_layer.update()
            evaluated = rig.evaluated_get(context.evaluated_depsgraph_get())
            samples.append([(evaluated.matrix_world @ evaluated.pose.bones[name].matrix).decompose()
                            for name in names])
    distance = rotation = scale = 0.0
    for (p0, q0, s0), (p1, q1, s1) in zip(*samples):
        distance = max(distance, (p1 - p0).length)
        delta = q0.rotation_difference(q1)
        angle = 2 * math.atan2(math.hypot(delta.x, delta.y, delta.z), abs(delta.w))
        rotation = max(rotation, math.degrees(angle))
        scale = max(scale, *(abs(a - b) / max(abs(a), abs(b), 1e-8) for a, b in zip(s0, s1)))
    return distance, rotation, scale


def repair_preview_views():
    """Restore local-view membership, which Blender clears on file reload."""
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type != 'VIEW_3D':
                continue
            space = area.spaces.active
            if (space.local_view is None or space.camera is None
                    or space.camera.name != 'FPS_Head_Camera'):
                continue
            members = []
            for obj in bpy.data.objects:
                visible = bool(obj.get('truefps_rig')) or any(
                    c.name in {'01 Character - body',
                               '04 Preview stage - never exported'}
                    for c in obj.users_collection)
                obj.local_view_set(space, visible)
                if visible:
                    members.append(obj)
            # A loaded screen can retain the Local View marker before its
            # membership bit is initialized. Re-enter that UI pane once.
            # In background Blender this getter is always false, including
            # a fresh default cube just placed in Local View by the operator.
            body = bpy.data.objects.get('Body')
            if (not bpy.app.background and body and body in members
                    and not body.local_view_get(space)):
                _initialize_preview_local_view(screen, area, members)
            space.overlay.show_overlays = False
            space.region_3d.view_perspective = 'CAMERA'
    return None


def _initialize_preview_local_view(screen, area, members):
    window = next((w for w in bpy.context.window_manager.windows
                   if w.screen == screen), bpy.context.window)
    if window is None:
        return
    region = next((r for r in area.regions if r.type == 'WINDOW'), None)
    if region is None:
        return
    space = area.spaces.active
    camera = space.camera
    local_camera = space.use_local_camera
    zoom = space.region_3d.view_camera_zoom
    offset = tuple(space.region_3d.view_camera_offset)
    with bpy.context.temp_override(window=window, screen=screen, area=area, region=region):
        view_layer = bpy.context.view_layer
        active = view_layer.objects.active
        mode = active.mode if active else 'OBJECT'
        selected = list(bpy.context.selected_objects)
        select_locks = {obj: obj.hide_select for obj in members}
        try:
            if bpy.context.object and bpy.context.object.mode != 'OBJECT':
                bpy.ops.object.mode_set(mode='OBJECT')
            if space.local_view is not None:
                bpy.ops.view3d.localview(frame_selected=False)
            bpy.ops.object.select_all(action='DESELECT')
            for obj in members:
                if obj.name in view_layer.objects:
                    obj.hide_select = False
                    obj.select_set(True)
            bpy.ops.view3d.localview(frame_selected=False)
        finally:
            bpy.ops.object.select_all(action='DESELECT')
            for obj, locked in select_locks.items():
                obj.hide_select = locked
            for obj in selected:
                if obj.name in view_layer.objects:
                    obj.select_set(True)
            if active is not None and active.name in view_layer.objects:
                view_layer.objects.active = active
                if mode != 'OBJECT':
                    bpy.ops.object.mode_set(mode=mode)
            space.camera = camera
            space.use_local_camera = local_camera
            space.region_3d.view_perspective = 'CAMERA'
            space.region_3d.view_camera_zoom = zoom
            space.region_3d.view_camera_offset = offset


@persistent
def _load_post(_unused):
    repair_preview_views()
    if not bpy.app.background and not bpy.app.timers.is_registered(_startup):
        bpy.app.timers.register(_startup, first_interval=0.2)


def _startup():
    repair_preview_views()
    workspace = bpy.data.workspaces.get('ENFILADE - Animate')
    for window in bpy.context.window_manager.windows:
        if workspace and find_rig(bpy.context):
            window.workspace = workspace
        for area in window.screen.areas:
            if area.type == 'VIEW_3D' and area.spaces.active.region_3d.view_perspective != 'CAMERA':
                area.spaces.active.show_region_ui = True
                try:
                    region = next(r for r in area.regions if r.type == 'UI')
                    region.active_panel_category = 'True FPS'
                except (RuntimeError, TypeError, AttributeError):
                    pass
    return None


def export_active_clip(context, rig, filepath):
    """Bake evaluated original bones to one temporary skeleton and export FBX.

    The authoring rig, actions, rest pose, selection, mode and frame survive
    both successful export and exceptions. No mesh or control bone is exported.
    """
    if not rig or rig.type != 'ARMATURE' or not rig.get('truefps_rig'):
        raise ValueError('Select the ENFILADE authoring rig first.')
    action = rig.animation_data.action if rig.animation_data else None
    if action is None:
        raise ValueError('Create an Action and key at least one pose first.')
    scene = context.scene
    start, end = scene.frame_start, scene.frame_end
    if end <= start:
        raise ValueError('End frame must be greater than Start frame.')
    if not filepath.lower().endswith('.fbx'):
        filepath += '.fbx'
    filepath = str(Path(bpy.path.abspath(filepath)).resolve())
    Path(filepath).parent.mkdir(parents=True, exist_ok=True)
    names = json.loads(rig['truefps_original_bones'])
    if any(n not in rig.data.bones for n in names):
        raise ValueError('The original skeleton is incomplete; use the intact template.')
    name_set = set(names)
    # Match the Synty Avatar's root node and meter-space bone lengths. Blender
    # imports its centimeters as an object scale, which Avatar copy ignores.
    unit_scale = float(rig.get('truefps_export_unit_scale', 1.0))
    old_frame, old_subframe = scene.frame_current, scene.frame_subframe
    old_scene_name, old_rig_name = scene.name, rig.name
    old_active = context.view_layer.objects.active
    old_mode = old_active.mode if old_active else 'OBJECT'
    old_selection = list(context.selected_objects)
    old_basis = {pb.name: pb.matrix_basis.copy() for pb in rig.pose.bones}
    old_object_basis = rig.matrix_basis.copy()
    temp = temp_data = baked_action = None
    try:
        samples = []
        for frame in range(start, end + 1):
            scene.frame_set(frame)
            context.view_layer.update()
            evaluated = rig.evaluated_get(context.evaluated_depsgraph_get())
            samples.append((frame, evaluated.matrix_world.copy(),
                            {n: evaluated.pose.bones[n].matrix.copy() for n in names}))
        if context.object and context.object.mode != 'OBJECT':
            bpy.ops.object.mode_set(mode='OBJECT')
        bpy.ops.object.select_all(action='DESELECT')
        temp = rig.copy()
        temp_data = rig.data.copy()
        temp.data = temp_data
        temp.animation_data_clear()
        temp.parent = None
        temp.hide_viewport = False
        temp.hide_render = False
        temp.hide_select = False
        temp['truefps_rig'] = False
        scene.collection.objects.link(temp)
        temp.hide_set(False)
        for pb in temp.pose.bones:
            for con in list(pb.constraints):
                pb.constraints.remove(con)
        for con in list(temp.constraints):
            temp.constraints.remove(con)
        temp.select_set(True)
        context.view_layer.objects.active = temp
        bpy.ops.object.mode_set(mode='EDIT')
        for bone in list(temp.data.edit_bones):
            if bone.name not in name_set:
                temp.data.edit_bones.remove(bone)
        bpy.ops.object.mode_set(mode='OBJECT')
        if unit_scale != 1.0:
            temp.data.transform(Matrix.Scale(unit_scale, 4))
        for col in temp.data.collections:
            col.is_visible = True
        baked_action = bpy.data.actions.new('__TrueFPS_export_bake')
        temp.animation_data_create()
        temp.animation_data.action = baked_action
        temp.rotation_mode = 'QUATERNION'
        previous = {}
        previous_obj_rotation = None
        for frame, obj_matrix, pose in samples:
            export_frame = frame - start
            scene.frame_set(export_frame)
            location, rotation, scale = obj_matrix.decompose()
            if unit_scale != 1.0:
                scale /= unit_scale
                for matrix in pose.values():
                    matrix.translation *= unit_scale
            if previous_obj_rotation is not None and previous_obj_rotation.dot(rotation) < 0:
                rotation.negate()
            previous_obj_rotation = rotation.copy()
            temp.location, temp.rotation_quaternion, temp.scale = location, rotation, scale
            for attr in ('location', 'rotation_quaternion', 'scale'):
                temp.keyframe_insert(data_path=attr, frame=export_frame)
            for name in names:
                pb = temp.pose.bones[name]
                bone = pb.bone
                kwargs = {}
                if bone.parent:
                    kwargs['parent_matrix'] = pose[bone.parent.name]
                    kwargs['parent_matrix_local'] = bone.parent.matrix_local
                basis = bone.convert_local_to_pose(
                    pose[name], bone.matrix_local, invert=True, **kwargs)
                loc, quat, scl = basis.decompose()
                if name in previous and previous[name].dot(quat) < 0:
                    quat.negate()
                previous[name] = quat.copy()
                pb.rotation_mode = 'QUATERNION'
                pb.location, pb.rotation_quaternion, pb.scale = loc, quat, scl
                for attr in ('location', 'rotation_quaternion', 'scale'):
                    pb.keyframe_insert(data_path=attr, frame=export_frame, group=name)
        # With all-actions/NLA export disabled, Blender names the take after
        # the scene. Keep it equal to the authored action for clips.csv.
        scene.name = action.name
        # FBX's global take preserves absolute frame time. Start at time zero;
        # Blender's importer then maps it back to frame 1 by default.
        scene.frame_start, scene.frame_end = 0, end - start
        rig.name = '__TrueFPS_authoring_in_export'
        temp.name = rig.get('truefps_export_armature_name', old_rig_name)
        # Skeleton-only FBX has no skin bind-pose record. Its default node
        # transforms come from the current pose, so provide the ORIGINAL rest
        # pose at a sentinel frame outside the exported 0..duration take.
        scene.frame_set(-1)
        location, rotation, scale = rig.matrix_world.decompose()
        temp.matrix_world = Matrix.LocRotScale(location, rotation, scale / unit_scale)
        for attr in ('location', 'rotation_quaternion', 'scale'):
            temp.keyframe_insert(data_path=attr, frame=-1)
        for pb in temp.pose.bones:
            pb.location = (0, 0, 0)
            pb.rotation_quaternion = (1, 0, 0, 0)
            pb.scale = (1, 1, 1)
            for attr in ('location', 'rotation_quaternion', 'scale'):
                pb.keyframe_insert(data_path=attr, frame=-1, group=pb.name)
        context.view_layer.update()
        result = bpy.ops.export_scene.fbx(
            filepath=filepath, use_selection=True, object_types={'ARMATURE'},
            apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y',
            use_mesh_modifiers=False, add_leaf_bones=False,
            use_armature_deform_only=False, bake_anim=True,
            bake_anim_use_all_bones=True, bake_anim_use_nla_strips=False,
            bake_anim_use_all_actions=False, bake_anim_force_startend_keying=True,
            bake_anim_step=1.0, bake_anim_simplify_factor=0.0,
            use_custom_props=False)
        if 'FINISHED' not in result:
            raise RuntimeError('Blender did not finish the FBX export.')
        return filepath
    finally:
        if context.object and context.object.mode != 'OBJECT':
            bpy.ops.object.mode_set(mode='OBJECT')
        if temp is not None and temp.name in bpy.data.objects:
            bpy.data.objects.remove(temp, do_unlink=True)
        if temp_data is not None and temp_data.users == 0:
            bpy.data.armatures.remove(temp_data)
        if baked_action is not None and baked_action.users == 0:
            bpy.data.actions.remove(baked_action)
        rig.name = old_rig_name
        scene.name = old_scene_name
        scene.frame_start, scene.frame_end = start, end
        scene.frame_set(old_frame, subframe=old_subframe)
        rig.matrix_basis = old_object_basis
        for name, basis in old_basis.items():
            rig.pose.bones[name].matrix_basis = basis
        bpy.ops.object.select_all(action='DESELECT')
        for obj in old_selection:
            if obj.name in context.view_layer.objects:
                obj.select_set(True)
        if old_active is not None and old_active.name in context.view_layer.objects:
            context.view_layer.objects.active = old_active
            if old_mode != 'OBJECT':
                bpy.ops.object.mode_set(mode=old_mode)
        context.view_layer.update()


class TRUEFPS_OT_key(bpy.types.Operator):
    bl_idname = 'truefps.key_controls'
    bl_label = 'Key All Controls'
    bl_description = 'Save every animation control at the current frame'
    bl_options = {'REGISTER', 'UNDO'}
    def execute(self, context):
        rig = find_rig(context)
        if not rig:
            return {'CANCELLED'}
        key_controls(rig, context.scene.frame_current)
        self.report({'INFO'}, 'All controls keyed')
        return {'FINISHED'}


class TRUEFPS_OT_reset(bpy.types.Operator):
    bl_idname = 'truefps.reset_selected'
    bl_label = 'Reset Selected'
    bl_description = 'Restore selected controls to the template pose; key afterwards to save'
    bl_options = {'REGISTER', 'UNDO'}
    def execute(self, context):
        rig = find_rig(context)
        names = set(control_names(rig)) if rig else set()
        selected = [b.name for b in rig.data.bones if b.select and b.name in names] if rig else []
        if not selected:
            self.report({'WARNING'}, 'Select pose controls first')
            return {'CANCELLED'}
        set_default_pose(rig, selected)
        context.view_layer.update()
        return {'FINISHED'}


class TRUEFPS_OT_new(bpy.types.Operator):
    bl_idname = 'truefps.new_action'
    bl_label = 'New Action'
    bl_options = {'REGISTER', 'UNDO'}
    clip_name: StringProperty(name='Clip name', default='enf_new_clip')
    end_frame: IntProperty(name='End frame', default=31, min=2, max=100000)
    loop: BoolProperty(name='Loop in Unity', default=False)

    def set_zombie_defaults(self, rig):
        if not rig or rig.get('truefps_kind') != 'zombie':
            return
        if not self.properties.is_property_set('clip_name'):
            self.clip_name = 'enf_zombie_new'
        if not self.properties.is_property_set('end_frame'):
            self.end_frame = 91
        if not self.properties.is_property_set('loop'):
            self.loop = True

    def invoke(self, context, event):
        self.set_zombie_defaults(find_rig(context))
        return context.window_manager.invoke_props_dialog(self)
    def execute(self, context):
        rig = find_rig(context)
        if not rig:
            return {'CANCELLED'}
        self.set_zombie_defaults(rig)
        name = self.clip_name.strip()
        if not re.fullmatch(r'[A-Za-z0-9_.-]+', name):
            self.report({'ERROR'}, 'Use letters, numbers, underscore, dot or dash for clip names')
            return {'CANCELLED'}
        rig.animation_data_create()
        if rig.animation_data.action:
            rig.animation_data.action.use_fake_user = True
        action = bpy.data.actions.new(name)
        action.use_fake_user = True
        action['truefps_loop'] = self.loop
        if rig.get('truefps_kind') == 'zombie':
            action.use_frame_range = True
            action.frame_start, action.frame_end = 1, self.end_frame
        rig.animation_data.action = action
        context.scene.frame_start, context.scene.frame_end = 1, self.end_frame
        context.scene.frame_set(1)
        set_default_ik(rig)
        set_default_pose(rig, control_names(rig))
        key_controls(rig, 1)
        context.view_layer.update()
        return {'FINISHED'}


class TRUEFPS_OT_switch_limb(bpy.types.Operator):
    bl_idname = 'truefps.switch_limb'
    bl_label = 'Match and Switch IK / FK'
    bl_description = 'Match this limb to its current pose, switch mode and save keys at this frame'
    bl_options = {'REGISTER', 'UNDO'}
    limb: EnumProperty(items=[(n,n.replace('_',' '),'') for n in LIMBS])
    mode: EnumProperty(items=[('FK','FK','Rotate individual limb bones'),('IK','IK','Move the hand or foot control')])
    def execute(self, context):
        rig = find_rig(context)
        if not rig:
            return {'CANCELLED'}
        try:
            switch_limb(context, rig, self.limb, self.mode, key=True)
        except Exception as error:
            self.report({'ERROR'}, str(error))
            return {'CANCELLED'}
        self.report({'INFO'}, self.limb.replace('_',' ') + ': ' + self.mode + ' (matched and keyed)')
        return {'FINISHED'}


class TRUEFPS_OT_preview_model(bpy.types.Operator):
    bl_idname = 'truefps.preview_model'
    bl_label = 'Preview Zombie Model'
    bl_description = 'Display this zombie mesh on the shared animation rig'
    bl_options = {'REGISTER', 'UNDO'}
    model: StringProperty()

    def execute(self, context):
        rig = find_rig(context)
        if not rig or rig.get('truefps_kind') != 'zombie':
            return {'CANCELLED'}
        models = preview_models(context, rig)
        if self.model not in models:
            self.report({'WARNING'}, 'This preview model is unavailable in the current view layer')
            return {'CANCELLED'}
        chosen = models[self.model]
        for obj in models.values():
            obj.hide_set(obj != chosen, view_layer=context.view_layer)
            obj.hide_render = obj != chosen
        return {'FINISHED'}


class TRUEFPS_OT_copy_loop_end(bpy.types.Operator):
    bl_idname = 'truefps.copy_loop_end'
    bl_label = 'Copy Start Pose to End'
    bl_description = 'Key the starting control pose and limb modes on the end frame'
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        rig = find_rig(context)
        try:
            start, end = _loop_range(context, rig)
            with _preserve_preview_pose(context, rig):
                context.scene.frame_set(start)
                context.view_layer.update()
                key_controls(rig, end)
        except (ValueError, RuntimeError) as error:
            self.report({'WARNING'}, str(error))
            return {'CANCELLED'}
        self.report({'INFO'}, f'Pose from frame {start} keyed at frame {end}')
        return {'FINISHED'}


class TRUEFPS_OT_check_loop(bpy.types.Operator):
    bl_idname = 'truefps.check_loop'
    bl_label = 'Check Loop Seam'
    bl_description = 'Compare the evaluated bone poses on the first and last frames'

    def execute(self, context):
        try:
            distance, rotation, scale = loop_seam(context, find_rig(context))
        except (ValueError, RuntimeError, KeyError) as error:
            self.report({'WARNING'}, str(error))
            return {'CANCELLED'}
        match = distance <= .001 and rotation <= .5 and scale <= .001
        message = 'Loop endpoints match' if match else 'Loop endpoints differ'
        self.report({'INFO'} if match else {'WARNING'},
                    f'{message} ({distance * 1000:.1f} mm, {rotation:.1f}°, {scale * 100:.2f}% scale max)')
        return {'FINISHED'}


class TRUEFPS_OT_export(bpy.types.Operator, ExportHelper):
    bl_idname = 'truefps.export_clip'
    bl_label = 'Export Current Action'
    bl_description = 'Bake this action to the original bones and export one Unity FBX'
    filename_ext = '.fbx'
    filter_glob: StringProperty(default='*.fbx', options={'HIDDEN'})
    loop: BoolProperty(name='Loop in Unity', default=False)
    def invoke(self, context, event):
        rig = find_rig(context)
        action = rig.animation_data.action if rig and rig.animation_data else None
        if action is None:
            self.report({'ERROR'}, 'Create an action first')
            return {'CANCELLED'}
        safe_name = re.sub(r'[^A-Za-z0-9_.-]', '_', action.name)
        directory = Path(bpy.path.abspath(rig.get('truefps_export_dir', '//Exports')))
        self.filepath = str(directory / (safe_name + '.fbx'))
        self.loop = bool(action.get('truefps_loop', False))
        return ExportHelper.invoke(self, context, event)
    def execute(self, context):
        rig = find_rig(context)
        try:
            result = export_active_clip(context, rig, self.filepath)
            action = rig.animation_data.action
            action['truefps_loop'] = self.loop
            csv_path = Path(result).with_suffix('.clips.csv')
            if not csv_path.exists():
                csv_path.write_text(
                    'clip,loop,inPlace,inPlaceY,inPlaceYaw,yawOffset,cycleOffset,from,to\n'
                    f'{action.name},{int(self.loop)},0,0,0,0,0,,\n', encoding='utf-8')
            folder = 'Animations/Zombies' if rig.get('truefps_kind') == 'zombie' else 'Animations/Custom'
            self.report({'INFO'}, f'Exported FBX. Copy it and its clips.csv to Unity {folder}.')
            return {'FINISHED'}
        except Exception as error:
            self.report({'ERROR'}, str(error))
            return {'CANCELLED'}


class TRUEFPS_PT_panel(bpy.types.Panel):
    bl_label = 'ENFILADE Animation'
    bl_idname = 'TRUEFPS_PT_panel'
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = 'True FPS'
    def draw(self, context):
        layout = self.layout
        rig = find_rig(context)
        if not rig:
            layout.label(text='Open the ENFILADE workspace')
            return
        action = rig.animation_data.action if rig.animation_data else None
        is_zombie = rig.get('truefps_kind') == 'zombie'
        if is_zombie:
            models = preview_models(context, rig)
            if models:
                box = layout.box()
                box.label(text='Zombie Preview')
                row = box.row(align=True)
                for label, obj in models.items():
                    op = row.operator('truefps.preview_model', text=label,
                                      depress=not obj.hide_get(view_layer=context.view_layer))
                    op.model = label
        if any('ik_' + limb in rig for limb in LIMBS):
            box = layout.box()
            box.label(text='Limb controls')
            for limb, label in [('arm_L','Left arm'),('arm_R','Right arm'),('leg_L','Left leg'),('leg_R','Right leg')]:
                if 'ik_' + limb not in rig:
                    continue
                current = 'IK' if rig['ik_' + limb] >= .5 else 'FK'
                row = box.row(align=True)
                row.label(text=label + ': ' + current)
                for mode in ('FK','IK'):
                    op = row.operator('truefps.switch_limb', text=mode, depress=current==mode)
                    op.limb, op.mode = limb, mode
            box.label(text='FK: R on arm / thigh / calf bones')
            box.label(text='IK: G on hand / foot handles')
            box.label(text='Fingers: R in either mode')
        layout.label(text=action.name if action else 'No active Action', icon='ACTION')
        layout.operator('truefps.new_action', icon='ADD')
        row = layout.row(align=True)
        row.prop(context.scene, 'frame_start', text='Start')
        row.prop(context.scene, 'frame_end', text='End')
        layout.prop(context.scene, 'frame_current', text='Frame')
        layout.operator('truefps.key_controls', icon='KEY_HLT')
        layout.operator('truefps.reset_selected', icon='LOOP_BACK')
        if is_zombie:
            box = layout.box()
            box.label(text='Loop')
            box.enabled = action is not None and context.scene.frame_end > context.scene.frame_start
            box.operator('truefps.copy_loop_end', icon='DUPLICATE')
            box.operator('truefps.check_loop', icon='CHECKMARK')
        layout.separator()
        layout.operator('truefps.export_clip', icon='EXPORT')
        layout.separator()
        layout.label(text='G: move   R: rotate')
        layout.label(text='Key after every pose change')
        if not is_zombie:
            layout.label(text='Head view = authored motion')
            layout.label(text='Unity adds look / IK / smoothing')


CLASSES = (TRUEFPS_OT_key, TRUEFPS_OT_reset, TRUEFPS_OT_new, TRUEFPS_OT_switch_limb,
           TRUEFPS_OT_preview_model, TRUEFPS_OT_copy_loop_end, TRUEFPS_OT_check_loop,
           TRUEFPS_OT_export, TRUEFPS_PT_panel)


def register():
    # Re-running the embedded text replaces earlier classes and handlers.
    for cls in CLASSES:
        existing = getattr(bpy.types, cls.__name__, None)
        if existing is not None:
            bpy.utils.unregister_class(existing)
        bpy.utils.register_class(cls)
    for handler in list(bpy.app.handlers.load_post):
        if getattr(handler, '__name__', '') == '_load_post' and getattr(handler, '__module__', '') in {__name__, 'truefps_tools'}:
            bpy.app.handlers.load_post.remove(handler)
    bpy.app.handlers.load_post.append(_load_post)
    repair_preview_views()
    if not bpy.app.background and not bpy.app.timers.is_registered(_startup):
        bpy.app.timers.register(_startup, first_interval=0.2)


def unregister():
    if _load_post in bpy.app.handlers.load_post:
        bpy.app.handlers.load_post.remove(_load_post)
    if bpy.app.timers.is_registered(_startup):
        bpy.app.timers.unregister(_startup)
    for cls in reversed(CLASSES):
        existing = getattr(bpy.types, cls.__name__, None)
        if existing is not None:
            bpy.utils.unregister_class(existing)


if __name__ == '__main__':
    register()
