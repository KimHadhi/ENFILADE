# ENFILADE paired executions tools

User entry point: `ArtSource/Executions/START_EXECUTION_ANIMATION.cmd`.
User guide (Korean): `ArtSource/Executions/START_HERE.md`. Game side: `Docs/Features/Executions.md`.

The workspace opens ready to work in, and saving sends the work to the game, so the user only poses and times (the user,
2026-11-13: "내가 해야 하는 잡일을 최대한 줄여. 나는 딱 오직 인간만이 할 수 있는 것만 해내면 되게끔").

- `build_execution_workspace.py` builds `ArtSource/Executions/ENFILADE_Execution_Animation.blend` from the True FPS start
  file: Kazusa's True FPS rig (scaled by `GAME_SCALE` 1.166, so the file is in the game's metres) and the zombie
  workspace's Synty rig (appended from `ArtSource/Zombies/ENFILADE_Zombie_Animation.blend`, which is only read), the
  default class's knife on her right wrist where the game seats it (`execution_knife.py`), the two hold points, and every
  pair's drafts (their knife keys put that knife's tip where they stab). `--export` sends every pair to
  the game, `--only=` limits the pairs, `--force` overwrites an existing workspace (the user's work lives in it: never after
  they edit it). Its own save does not send anything.

  ```powershell
  & 'F:\SteamLibrary\steamapps\common\Blender\blender.exe' --background --factory-startup .\ArtSource\TrueFPS\ENFILADE_TrueFPS_Start.blend --python .\Tools\Blender\Executions\build_execution_workspace.py -- .\ArtSource\Executions\ENFILADE_Execution_Animation.blend --export
  ```

- A pair may begin as a copy of another one played faster (`execution_pairs.py`: `like`, `pace`; `execution_tools.copy_pair`)
  instead of a choreography of its own. A workspace built before such a pair existed gets it from the 처형 tab's
  **＋ … 넣기** button (`enf_exe.add_pair`), in the user's own file, since that file is never built again: both of the
  other pair's actions as they are there, their keys, handles, noise and hold windows that much closer together, the
  kill frame and the length with them, kept as that pair's draft. `front_quick` (the front takedown's own pair, the
  user's to make: 2026-11-18) is the first: `front` at twice the speed, which is what the game played for it. Its row of
  Executions.csv is there already with the cells the export does not write (turn, align, the blends, hitBone, fall);
  saving fills the rest and it becomes a pair. Checked headless in a scratch copy of the project: made, shown,
  restored to its draft, exported, the other pairs' fingerprints unchanged.
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
    bone's frame, head and turn, no scale), and on each `CTRL_hand` a Copy Location (`잡기 · 자리`) and a Copy Rotation
    (`잡기 · 방향`) constraint copying it, never its size; the
    influence is keyed in the pair's action (1 while held). The export reads every run of that influence at 1 as one
    grab: a hand that lets go and takes hold again has a window and an offset per grab, listed with `|` in its
    `l*`/`r*` cells (`TableCells.Parts`), and the game keeps the hand on each (0.9.67; before, only the first grab went).
  - **Saving sends**: a `save_pre` handler remembers the shown pair (Kazusa's rig object, the 처치 marker, the end frame, the
    hold points), then exports every pair whose fingerprint (keys, handles, interpolation, modifiers and those numbers)
    differs from the one it was last sent with: both rigs baked with `truefps_tools.export_active_clip` (Kazusa at her own
    root and size) to `Assets/_Project/Art/Animations/Executions/enf_exe_<pair>_p.fbx` / `_z.fbx` with `.clips.csv` (one
    clip, not looped, root kept; `yawOffset` 180 for Kazusa's clip, as every clip on the player's body whose body
    faces the file's +Z and whose axes Unity bakes, 0 for the zombie's; 0 until 0.9.50 made the player play the pair
    facing away from the zombie), a bare `.meta` (GUID only) before a new file, and the pair's row of
    `Assets/_Project/Data/Resources/Tables/Melee/Executions.csv` written in place (every other column kept), its numbers
    as the scene has them to six decimals (before 0.9.59 places and times went at three decimals and turns at one), and
    the player clip's finger track `Assets/_Project/Data/Resources/Tables/Clips/enf_exe_<pair>_p_fingers.csv`
    (`send_fingers`: `finger_ways.track_rows` over the frames the clip is baked on, both hands by the game's own
    humanoid map of Kazusa, KazusaBand.fbx.meta; the game puts it on over the clip while the pair plays, since a
    humanoid clip drops a finger segment's twist, the outer joints' side swing and the thumb's roll and the body's hand
    passes relax the thumb and clamp the fingers: 0.9.71, the user: "네, 다 고쳐"). A pair whose clips are the game's
    already but whose track is missing or was made from another state of it (`enf_fingers` on the action against its
    fingerprint) gets only its track remade on saving. 게임에서 빼기 deletes a pair's clips and its track and keeps it
    out until the pair changes.
- `execution_knife.py` is the class knife in her right fist (the user, 2026-11-14: "칼 위치를 숫자로 하는 이유가
  있는지, 왜냐하면 블렌더에서 하면 내가 수정할 때 바로 직관적으로 옮길 수 있으니까 편해서"). The tab's 칼 picker lists
  every row of `Tables/Melee/ClassKnives.csv`; the picked row's model (its `Resources` prefab's source FBX, found by the
  prefab's `m_SourcePrefab` GUID, its colour and normal textures hung on from `Textures/` beside it) sits under an empty
  (the holder: the model's origin and turn) on the `Right wrist` bone where `ExecutionKnifeArm.Wear` and `Seat` put it:
  the origin at the wrist plus `heldSide/heldPalm/heldAlong` hand lengths (wrist to the middle knuckle) along the hand's
  axes (x away from the thumb, y out of the palm, z the knuckles' way; the palm from the index and little knuckles, the
  game's right-hand cross taken the other way round in Blender's right-handed axes), the model's blade (Pommel marker
  through the origin) along `blade*`, its edge (Edge marker) along `edge*`, at `scale`. Worked out from the wrist bone's
  rest (the knuckles ride it, so any pose gives the same), never from a hidden object's matrix.
  - **Moved here, written on saving**: 칼 옮기기 selects the holder in Object mode with the Move tool (its scale locked).
    The `save_pre` handler (after the pairs, under the same 저장하면 게임에 보내기 switch) measures the holder back into
    the nine numbers as measured (six decimals, the blade and edge unit and square); a number counts as moved past
    1e-5 (Blender's single-precision noise is about 1e-7, a hand's least move far above). Nothing is rounded to look
    tidy (the user, 2026-11-14: what is made in Blender reaches the game whole; 0.9.56 wrote three decimals and ignored
    moves under 0.002). Of the three groups (held, blade, edge) only those that moved are written, into the
    picked class's row only, in place with every other byte of the file kept (other rows, cells, quoting, BOM, line
    ends). Keys auto-key put on the holder are dropped first. A moved knife picked away from waits in the scene
    (`enf_exe_knife_moved`) and is written on the next save; 칼을 표의 자리로 drops a move.
  - **On open**: a file built before it has its fixed MW throwing knife (`칼 (보기용)`) replaced; the knife is remade when
    its row names another model and put where its row says when its numbers changed outside Blender (a group moved here
    and not yet written stays moved). Nothing changes when they agree, so opening doesn't dirty the file.
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
- Parenting an object to a bone holds it from the bone's tail; the knife's and the hold points' offsets are worked out
  from that.
- The class knives' FBX files name a normal-map texture with no file: Blender's importer prints "Cannot read
  '...\<Knife>\.'" and leaves an empty picture (pink in Material Preview) on the Normal Map node; the knife gets
  `Textures/<material>_normal.png` there (Non-Color) and its `<material>.jpg/png` on Base Color.
- Checked headless on a copy of the user's file (2026-11-14, ClassKnives.csv redirected to a copy): for all eight rows the
  knife sits where Unity's math puts it (ExecutionKnifeArm emulated in Unity's left-handed axes: 0.0001 mm, 0.0000
  degrees), also from the posed hand at frames of two pairs; moved 1 cm and turned 20 degrees, saving changed only that
  row's nine numbers and the table then puts it back where it was moved (0.000 mm); saving again, or after numbers changed
  outside, wrote nothing; a move kept across picking another class was written to its own row only. At six decimals
  (0.9.59) a 0.05 mm, 0.01 degree move is written and comes back within 0.06 micrometres, and a file saved and reopened
  differs from its row by 1e-6 at most (single precision), so opening and saving it again write nothing.
- The finger tracks of the seven pairs in the game were made headless from a scratch copy of the user's file, each pair
  first checked to be the state its clips were sent from (its fingerprint equal to the one marked when sent). Checked as
  the throw clips' tracks were (the shared finger-ways-verify's game working, both hands): the table against the rig,
  1050 segments, 0.00004 degrees at most; put on the game's Kazusa and on the KINEMATION mannequin by the game's
  WearWays done in Unity's axes, 0.00005 and 0.00022 degrees at most, twists within 0.00006 degrees, Kazusa's fingertips
  within 0.001 mm; a 25 degree twist made on the knife hand's index came through with its sign on both.
- A zombie rig appended from the zombie workspace brings that file's action: cleared, as each pair keys its own.
- Blender 4.5's layered actions: `fcurve_ensure_for_datablock` takes no group, so channels are grouped afterwards
  (`channelbag.groups.new`, `fcurve.group = ...`; assigning a curve to the group it is in prints an error, so it is
  checked first).
- A save handler that sends runs on any save, the build's own included: the build drops the handlers before saving.
- A hold copied with one Copy Transforms (0.8.300) also copied the hold point's size (Kazusa's 1.166 in the offset) into
  the wrist: Unity's humanoid import warned "'Left wrist' has scale animation that will be discarded" for every player
  clip. And the bake puts her rig at the origin at her own size while the hold points stayed on the zombie in the
  scene, so held hands were baked up to 0.86 of her hips-to-head length off her body. Now the hands copy place and turn
  only, and the zombie goes into her frame for her bake (its matrix times the inverse of her placement), so the
  clips read back from the FBX match the workspace (0.000 off, wrist scale within 0.00005, the IK's own float noise).
  A file built by 0.8.300 has its one constraint's influence keys moved onto the two on open.
- A hold's influence that eases in from the hand's key before the grab pulls the swing in early (up to 25 cm off the
  first draft's path on the knife hand): it steps (constant) from that key to 1 at the grab, where the hand already is.
- Checked against the first drafts as baked before (every frame keyed): the holds put the hands exactly on their points
  (0.00 cm); the key poses match; between keys the auto-clamped Bezier eases differ by up to 8 cm at the hips and 20 cm at a
  wrist, mostly where a hand lets go. Executions.csv came out the same, value for value.
