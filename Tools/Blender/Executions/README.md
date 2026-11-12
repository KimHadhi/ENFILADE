# ENFILADE paired executions tools

User entry point: `ArtSource/Executions/START_EXECUTION_ANIMATION.cmd`.
User guide (Korean): `ArtSource/Executions/START_HERE.md`. Game side: `Assets/_Project/Docs/Executions.md`.

- `build_execution_workspace.py` builds `ArtSource/Executions/ENFILADE_Execution_Animation.blend` from the True FPS start
  file: Kazusa's True FPS rig (scaled by `GAME_SCALE` 1.166, so the file is in the game's metres) and the zombie
  workspace's Synty rig (appended from `ArtSource/Zombies/ENFILADE_Zombie_Animation.blend`, which is only read), the game's
  knife on her right wrist for seeing only, and every pair's drafts. `--export` sends every pair to the game, `--only=`
  limits the pairs, `--force` overwrites an existing workspace (the user's touch-ups live in it: never after they edit it).

  ```powershell
  & 'F:\SteamLibrary\steamapps\common\Blender\blender.exe' --background --factory-startup .\ArtSource\TrueFPS\ENFILADE_TrueFPS_Start.blend --python .\Tools\Blender\Executions\build_execution_workspace.py -- .\ArtSource\Executions\ENFILADE_Execution_Animation.blend --export
  ```

- `execution_tools.py` is the 처형 tab: pick a pair (both rigs' layers: NLA track `1 시안 (잠금)` locked under the touch-up
  action, Combine), nudge buttons, the 처치 marker, re-fit Kazusa's draft to the zombie as it plays now, export.
  Export bakes both rigs with `truefps_tools.export_active_clip` (Kazusa at her own root and size: her rig object is set to
  identity for the bake, the pair's offset goes to the table instead) to
  `Assets/_Project/Art/Animations/Executions/enf_exe_<pair>_p.fbx` / `_z.fbx` with `.clips.csv` (one clip, not looped,
  root kept), writes a bare `.meta` (GUID only) before a new file, and writes the pair's row of
  `Assets/_Project/Resources/Tables/Executions.csv` (kind, clips, length, kill, right/forward/yaw of the player's root, the
  zombie's start hips and head yaw, each hand's hold window and wrist offset) in place, keeping every other column.
- `execution_drafts.py` makes drafts: `ZombieDraft` (a start pose sampled from a pack clip with `zombie_tools.sample_clip`,
  key poses as body-axis turns of bone subtrees plus a hips move, eased, shudders on top; dense FK keys) and `PlayerDraft`
  (Kazusa's hips, spine, neck and head in FK, both arms and legs on the rig's IK controls, finger curls; hands as `Palm`,
  `Knife` (tip, blade, edge; the knife seat of the table's Knife row) or `Hold` (rides a zombie bone from the grab); dense
  keys).
- `execution_pairs.py` holds every pair's choreography in game metres and 30 fps frames (frame 1 = 0 s).

Facts found while building (Blender 4.5.16, 2026-11-13):

- The True FPS rig's leg IK pole angles (0 and 180 degrees) bend the knees sideways once the hips drop. The build tries
  four quarter turns per limb on a bent pose and keeps the one that bends toward its pole (-90 for both legs; the arms keep
  theirs). Blender clamps a pole angle past ±180 rather than wrapping it. Only this file changes; the other workspaces keep
  the rig as it was.
- Parenting an object to a bone holds it from the bone's tail; the preview knife's offset is worked out from the bone's
  rest matrix and length so it does not depend on the pose evaluated at that moment.
- A zombie rig appended from the zombie workspace brings that file's action: cleared, as each pair keys its own.
