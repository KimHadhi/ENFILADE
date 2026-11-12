# ENFILADE one-click animation workspaces

The user, 2026-11-13: "야 근데 올인원 원클릭으로 세팅되는 거 없음? 걍 열기만 하면 블렌더 내에서 알아서 애니메이션 불러 올 수 있고
도프 시트는 이미 짜여져 있고 그런 거 ㅇㅇ. 내가 해야 하는 잡일을 최대한 줄여. 나는 딱 오직 인간만이 할 수 있는 것만 해내면 되게끔 ㅇㅇ."

[one_click.py](one_click.py) is what the Blender animation workspaces share so the user only poses and times: each opens
ready from its `.cmd`, and `Ctrl+S` sends what changed to the game. The paired executions workspace did it first and
keeps its own copy ([execution_tools.py](../Executions/execution_tools.py)).

| Workspace | Opens on | Items | Saving sends |
| --- | --- | --- | --- |
| [Shove](../../../ArtSource/Shove/START_HERE.md) (`밀치기` tab) | the last move: a gun move in Object mode with the gun selected, the kick and the mending moves in Pose mode with the right foot or the left hand selected | 5 moves, key poses: editable | `Resources/Combat/GunMoves/enf_bash_*.csv`, `Art/Animations/Shove/enf_kick_front.fbx`, `Resources/Tables/RepairHammer.csv`, `RepairTorch.csv` |
| [Throwables](../../../ArtSource/Grenades/START_HERE.md) (`투척` tab) | the last clip, Pose mode, the left hand selected | 7 clips: the grenade's four and the knife's hold are key poses (editable); the knife's draw and throw are MW2019's motion keyed every frame (touch-up layer) | `Resources/Tables/GrenadeDraw.csv`, `GrenadeHold.csv`, `GrenadeThrow.csv`, `GrenadeTap.csv`, `KnifeDraw.csv`, `KnifeHold.csv`, `KnifeThrow.csv` |
| [Run](../../../ArtSource/Run/START_HERE.md) (`달리기` tab) | the last cycle, Pose mode | 9 cycles retargeted every frame: touch-up layer over the locked draft | `Art/Animations/Run/enf_<clip>.fbx` (+ `.clips.csv`), only once a cycle is changed: an unchanged cycle stays withheld and the game plays the original |
| [Knives](../../../ArtSource/Knives/START_HERE.md) (`칼` tab; the class-knife session's tools, 0.8.320) | the last clip, Pose mode, the right hand selected, auto-key on | 6 melee clips (draw, guard, slash, backslash, stab, reverse stab), key poses: editable | `Resources/Tables/KnifeMeleeDraw.csv`, `KnifeMeleeGuard.csv`, `KnifeMeleeSlash.csv`, `KnifeMeleeBackslash.csv`, `KnifeMeleeStab.csv`, `KnifeMeleeStabReverse.csv` |
| [Victory poses](../../../ArtSource/Poses/START_HERE.md) (`Pose` tab) | the last pose on its struck frame (9), Pose mode, auto-key on, the small view from the front | every `enf_pose_*` action (key poses at 1, 9, 42, 75; the hold follows frame 9) | `Resources/Poses/<name>.fbx` |

Each panel's 게임에 보내기 button does what `Ctrl+S` does (sends what changed and saves the file), and still sends with
the panel's 저장하면 게임에 보내기 switch off. The knife workspace has no switch (saving always sends) and its 지금 게임에
보내기 sends the changed clips without saving.

Not here: the zombie workspace (a clip is loaded, varied and sent to a slot and name the user picks, so a save can't know
where it goes), and the True FPS start file (a template for new clips; its practice actions are not the game's).

## What it does

- **Opening**: a workspace's tools call `register_workspace(name, matches, arrange, send)`. `arrange` runs on a timer
  0.8 s after the tools start (after the True FPS tools' own start-up at 0.2 s) and after every file load: it readies the
  data first (`item_action` on every item), then the screen (`open_workspace`, `main_view`, `show_tab`, `dope_sheet`,
  `select`): the item last worked on, the rig in Pose mode with that item's part selected, every Dope Sheet (not the
  timeline) in Dope Sheet mode showing every animated object's keys with the summary row and the markers, the sidebar
  tab. A window switches its workspace on the next tick, so `arrange` returns False and runs again (up to six times).
- **Items**: `item_action(context, owner, name, frames)` returns the action the user keys into.
  - A draft keyed at key poses becomes an editable copy `<name>` (Replace), the draft kept as `<name>_draft`
    (`revert` copies it back). Its channels are grouped per bone and closed (`group_channels`): a row per bone.
  - A draft keyed on at least 80% of its frames (`DENSE`: retargeted mocap, a ported viewmodel) stays locked on the NLA
    track `1 시안 (잠금)` under `<name>`, the touch-up layer (Combine): one key there bends the clip smoothly, where one key
    on a frame of dense keys would pop that frame.
  - Files built before (every draft locked under an empty touch-up) convert on open. A key-pose item whose touch-up the
    user had keyed is baked (`_bake`): the draft's curves, with every property the touch-up moved keyed every frame as
    the two played together, so nothing is lost.
  - A build that makes a draft again (`--redraft`) is followed by an editable copy nobody touched (`enf_from`, the draft
    fingerprint the copy was made from); a touched copy is kept.
- **Saving sends**: `_save_pre` asks each matching workspace to send; the workspace compares each item's `fingerprint`
  (keys, handles, interpolation, easing, extrapolation, mute, every F-curve modifier setting, plus the item's own numbers:
  marks, end frame) with `enf_sent`, the one it was last sent with, and sends it when they differ or its game file is
  missing (`needs_sending`). An item made ready untouched counts as sent as it is (the builds sent the drafts), or as
  withheld where the game plays something else until the item is first sent (`mark_withheld`). A failed item is reported
  in the status bar and tried again on the next save. A new game file gets a bare `.meta` (GUID only) first
  (`write_meta`); text tables are written in place and retried while Unity holds them (`write_text`).
- Headless saves never send (`SEND_IN_BACKGROUND` is False): the build scripts register the tools and save their
  workspaces. The headless checks set it.

## Checked (Blender 4.5.16, 2026-11-13)

In a scratch copy of the project (the tools, the workspace `.blend`, the game files it writes), never the game's own:

| Check | Shove | Throwables | Run | Knives | Victory poses |
| --- | --- | --- | --- | --- | --- |
| Opening, then an unchanged save: nothing sent, the game files keep their bytes | pass (5 moves made editable) | pass (5 made editable, the knife's draw and throw stay layered) | pass (9 cycles withheld: no files made) | pass (6 clips editable) | pass (5 poses) |
| Fingerprints the same after a save and reload | pass | pass | pass | pass | pass |
| Items changed and saved: only their files rewritten; saving again sends nothing | pass (권총 key moved) | pass (톡 던지기 key moved, 나이프 던지기 touch-up keyed) | pass (걷기 · 앞 trunk raised by the 상체 세우기 job) | pass (베기 key moved) | pass (the V pose's head turned at frame 9) |
| Switch off: a save sends nothing; 게임에 보내기 sends the changed item and saves | pass | pass | pass | — (no switch) | pass |
| Every item sent by the new tools against the old tools' export of the same file | CSV byte for byte; kick FBX keys 0.0 apart | CSV byte for byte | FBX keys at most 6.4e-7 apart (8 cycles) | CSV byte for byte with the committed tables | — |
| An old file's touch-up keyed by hand, made editable (`_bake`) | plays the same (0.0 apart, kick foot 5 cm up) | — | — | — | — |
| The hold follows the strike on leaving frame 9 (75 = 9, 42 keeps its breath) | — | — | — | — | pass |
| Withheld: its file deleted and not sent again until it changes, then sent | — | — | pass | — | pass |

Facts found:

- In `--background`, the windows of a loaded file are there but `window.workspace = ...` never takes (no event loop):
  `arrange` readies the data before the screen, so the data side works headless too.
- The tables the old buttons wrote with `Path.write_text` have CRLF line ends (Windows text mode); `write_text` keeps a
  file's own line ends so an unchanged item writes the same bytes.
- The committed workspaces were in step with the game: the original tools exported every shove move and throwable clip
  again byte for byte equal to the game's files.
