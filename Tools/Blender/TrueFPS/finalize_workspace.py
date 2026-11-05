"""Update the generated Ready template's attachments and embedded tool text."""
from pathlib import Path
import bpy

path = Path(bpy.data.filepath)
if path.name != 'ENFILADE_TrueFPS_Ready.blend':
    raise RuntimeError('Only the generated Ready template may be finalized.')
rig = next(o for o in bpy.context.scene.objects if o.get('truefps_rig'))
if {a.name for a in bpy.data.actions} != {'enf_new_clip'}:
    raise RuntimeError('Authored actions detected; preserve this file and build another template.')
for side, suffix in [('Left', 'L'), ('Right', 'R')]:
    for end, kind in [('wrist', 'hand'), ('ankle', 'foot')]:
        pb = rig.pose.bones[f'{side} {end}']
        name = 'Keep hand or foot attached to solved limb'
        con = pb.constraints.get(name) or pb.constraints.new('COPY_LOCATION')
        con.name = name
        con.target, con.subtarget = rig, f'MCH_{kind}_lower.{suffix}'
        con.head_tail = 1.0
        con.owner_space = con.target_space = 'POSE'
text = bpy.data.texts.get('START HERE - True FPS tools.py')
if text is None:
    text = bpy.data.texts.new('START HERE - True FPS tools.py')
text.from_string((Path(__file__).parent/'truefps_tools.py').read_text(encoding='utf-8'))
text.use_module = False
bpy.context.scene.frame_set(1)
bpy.context.view_layer.update()
bpy.ops.wm.save_as_mainfile(filepath=str(path), check_existing=False)
print('TRUEFPS_FINALIZED',path)
