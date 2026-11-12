# ENFILADE paired executions tools

User entry point: `ArtSource/Executions/START_EXECUTION_ANIMATION.cmd`.
User guide (Korean): `ArtSource/Executions/START_HERE.md`. Game side: `Assets/_Project/Docs/Executions.md`.

The workspace opens ready to work in, and saving sends the work to the game, so the user only poses and times (the user,
2026-11-13: "내가 해야 하는 잡일을 최대한 줄여. 나는 딱 오직 인간만이 할 수 있는 것만 해내면 되게끔").

- `build_execution_workspace.py` builds `ArtSource/Executions/ENFILADE_Execution_Animation.blend` from the True FPS start
  file: Kazusa's True FPS rig (scaled by `GAME_SCALE` 1.166, so the file is in the game's metres) and the zombie
  workspace's Synty rig (appended from `ArtSource/Zombies/ENFILADE_Zombie_Animation.blend`, which is only read), the game's
  knife on her right wrist for seeing only, the two hold points, and every pair's drafts. `--export` sends every pair to
  the game, `--only=` limits the pairs, `--force` overwrites an existing workspace (the user's work lives in it: never after
  they edit it). Its own save does not send anything.

  ```powershell
  & 'F:\SteamLibrary\steamapps\common\Blender\blender.exe' --background --factory-startup .\ArtSource\TrueFPS\ENFILADE_TrueFPS_Start.blend --python .\Tools\Blender\Executions\build_execution_workspace.py -- .\ArtSource\Executions\ENFILADE_Execution_Animation.blend --export
  ```

- `execution_tools.py` is the 처형 tab and the workspace's behaviour:
  - **On open** (a timer after the True FPS tools' own, and after every file load): the `ENFILADE - Animate` workspace, the
    last pair shown, both rigs in Pose mode, the Dope Sheet in Dope Sheet mode with both rigs (not only the selected) and
    the summary row, the 처형 tab, the large 3D view at three quarters on the pair and the start file's small first-person
    preview turned into a side view (its camera dropped, the zombie, knife and hold points added to its local view).
  - **One action per rig per pair** (`enf_exe_<pair>_p`, `_z`), keyed by hand directly (auto-key on); the first draft is
    kept as `<name>_draft` for 이 짝을 시안으로 되돌리기. A file built by 0.8.294-0.8.297 (draft locked on NLA track
    `1 시안 (잠금)` under an empty touch-up action) is read as its draft.
  - **Holds**: an empty per hand (`잡는 자리 · 왼손`, `잡는 자리 · 오른손(칼)`) parented to the zombie bone the hand holds in
    the shown pair, at the grab's offset (kept on the pair's action as `enf_hold_L/R`: the bone and the offset from the
    bone's frame, head and turn, no scale), and a Copy Transforms constraint `잡기` on each `CTRL_hand` copying it; the
    influence is keyed in the pair's action (1 while held). The export reads each hold's window from that influence.
  - **Saving sends**: a `save_pre` handler remembers the shown pair (Kazusa's rig object, the 처치 marker, the end frame, the
    hold points), then exports every pair whose fingerprint (keys, handles, interpolation, modifiers and those numbers)
    differs from the one it was last sent with: both rigs baked with `truefps_tools.export_active_clip` (Kazusa at her own
    root and size) to `Assets/_Project/Art/Animations/Executions/enf_exe_<pair>_p.fbx` / `_z.fbx` with `.clips.csv` (one
    clip, not looped, root kept), a bare `.meta` (GUID only) before a new file, and the pair's row of
    `Assets/_Project/Resources/Tables/Executions.csv` written in place (every other column kept). 게임에서 빼기 deletes a
    pair's clips and keeps it out until the pair changes.
- `execution_drafts.py` makes drafts as key poses: `ZombieDraft` (a start pose sampled from a pack clip with
  `zombie_tools.sample_clip`, key poses as body-axis turns of bone subtrees plus a hips move, every bone keyed at the
  zombie's key frames, shudders as Noise modifiers on the shaken bones' rotation channels, faded over their frames) and
  `PlayerDraft` (Kazusa's hips, spine, neck and head in FK, both arms and legs on the rig's IK controls, finger curls; hands
  as `Palm`, `Knife` or `Hold`; each part keyed at the frames the choreography sets it, a pole also where its limb's root
  moves). Each key eases into the next as the choreography's key there says (`smooth` Bezier, `snap` cubic out, `in`/`out`
  quadratic, `linear`), handles auto-clamped; quaternions keyed on one side of the sphere. Channels are grouped per bone,
  closed, so the Dope Sheet shows a row per bone.
- `execution_pairs.py` holds every pair's choreography in game metres and 30 fps frames (frame 1 = 0 s).

Facts found while building (Blender 4.5.16, 2026-11-13):

- The True FPS rig's leg IK pole angles (0 and 180 degrees) bend the knees sideways once the hips drop. The build tries
  four quarter turns per limb on a bent pose and keeps the one that bends toward its pole (-90 for both legs; the arms keep
  theirs). Blender clamps a pole angle past ±180 rather than wrapping it. Only this file changes; the other workspaces keep
  the rig as it was.
- Parenting an object to a bone holds it from the bone's tail; the preview knife's and the hold points' offsets are
  worked out from that.
- A zombie rig appended from the zombie workspace brings that file's action: cleared, as each pair keys its own.
- Blender 4.5's layered actions: `fcurve_ensure_for_datablock` takes no group, so channels are grouped afterwards
  (`channelbag.groups.new`, `fcurve.group = ...`; assigning a curve to the group it is in prints an error, so it is
  checked first).
- A save handler that sends runs on any save, the build's own included: the build drops the handlers before saving.
- A hold's influence that eases in from the hand's key before the grab pulls the swing in early (up to 25 cm off the
  first draft's path on the knife hand): it steps (constant) from that key to 1 at the grab, where the hand already is.
- Checked against the first drafts as baked before (every frame keyed): the holds put the hands exactly on their points
  (0.00 cm); the key poses match; between keys the auto-clamped Bezier eases differ by up to 8 cm at the hips and 20 cm at a
  wrist, mostly where a hand lets go. Executions.csv came out the same, value for value.
