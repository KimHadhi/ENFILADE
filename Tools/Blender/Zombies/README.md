# ENFILADE zombie animation tools

User entry point: `ArtSource/Zombies/START_ZOMBIE_ANIMATION.cmd`.
User guide (Korean): `ArtSource/Zombies/START_HERE.md`.

`zombie_tools.py` is a Blender 4.5 LTS module for `ArtSource/Zombies/ENFILADE_Zombie_Animation.blend`
(the Synty zombie rig built by `Tools/Blender/TrueFPS/build_zombie_workspace.py`). It imports
`Tools/Blender/TrueFPS/truefps_tools.py` for the rig helpers, preview models, loop tools and the FBX
export (`export_active_clip`), and adds the Zombie sidebar tab:

1. **Load**: any FBX (the Nephilite pack, `Assets/_Project/Art/Animations/Zombies/**`,
   `ArtSource/Zombies/Inbox/**`, or a file picked anywhere) becomes the action of NLA track
   `1 Base (locked)`. A Synty skeleton (all 22 body bones found by name; the pack's fingers are `_L`/`_R`,
   ours are plain and ` 1`) is copied bone for bone in armature space: the pack's bind skeleton matches the
   zombie rig within 2 degrees on 49 of 50 bones with identical lengths (measured 2026-11-11). Any other
   humanoid (Mixamo/Unreal names) is retargeted per bone: its world turn from its own rest, applied after
   aiming the zombie's rest bone along the source rest bone (so A-pose and T-pose rests both work), the
   hips' travel scaled by leg length. A folder's `<folder>.tpose.fbx` stands in for the rest pose of
   files downloaded without skin, as the Unity import rule does. Every limb goes to FK (`ik_*` = 0).
2. **Variations**: the base's own keys are read back, varied (hunch, head, reach, dead arms, stiff legs,
   twitches, amplify) and stored as per-bone deltas in NLA track `2 Variation`, blend Combine.
3. **Touch-ups**: the rig's active action, blend Combine, on top. Blender remaps inserted keys through
   the stack, so pressing `I` keys exactly the pose on screen.
4. **Export**: `export_active_clip` samples the evaluated stack onto an original-bones-only skeleton and
   writes `Assets/_Project/Art/Animations/Zombies/<Slot>/enf_<name>.fbx` and its `.clips.csv`
   (loop by slot, `inPlace` 1). Unity's `ZombieClipFolders` adds it to the slot's `ZombieLooks` list and
   `ModelImportRules` gives `enf_` files the Synty avatar.

Facts the code relies on (checked headless 2026-11-11, Blender 4.5.16):

- Blender's FBX importer sets the scene frame rate to the file's (25 fps when the file names none, as the
  pack's In Place files do) and keys the clip in those frames. `_Imported` restores the workspace rate
  and sampling maps source frames by real time, so a clip keeps its length in seconds.
- NLA Combine multiplies a strip's quaternion on the right of the one below and adds locations, so a
  variation key is `base⁻¹ @ varied`.
- `Action.fcurve_ensure_for_datablock(datablock, data_path, index)` takes no group in 4.5, and the action
  has to be assigned to the rig while it runs.
- Loading a clip, applying variations, keying through the stack, mirroring and the FBX round trip were
  each compared bone by bone against the source: 0.0 degrees apart (0.5 on the head's tip).

Loading `truefps_tools` turns off bytecode writing, because a `__pycache__` file beside it is tracked.
