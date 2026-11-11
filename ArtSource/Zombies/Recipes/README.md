# 좀비 클립 레시피

블렌더 좀비 작업실의 **레시피로 저장**이 여기에 `이름.json`을 만든다. **레시피 불러오기**는 여기 있는 것을 다시 연다. Claude도 이 파일을 읽고 고치고 새로 만든다. 그래서 "30프레임쯤 고개를 더 떨궈 줘"처럼 말로 시킬 수 있다. [example_hunch_limp.json](example_hunch_limp.json)은 손으로 쓴 예시다(게임에는 아직 안 넣었다).

| 칸 | 뜻 |
| --- | --- |
| `base` | 바탕 클립 FBX. 프로젝트 폴더 기준 경로 |
| `in_place`, `mirror`, `speed` | 불러오기 옵션: 제자리, 좌우 반전, 빠르기 |
| `vary` | 변형 값: `hunch`, `head_tilt`, `head_droop`(도), `reach`, `dead_arm_l`, `dead_arm_r`, `stiff_leg_l`, `stiff_leg_r`, `twitch`(0~1), `amplify`(-0.6~1), `seed` |
| `touch` | 말로 쓰는 손질 목록. `{"frame": 30, "part": "Head", "move": "left", "degrees": 15, "reach": 6}`. `move`는 `forward`, `back`, `up`, `down`, `left`, `right`(좀비 기준), `twist_left`, `twist_right`. `{"frame": 60, "part": "Shoulder_L", "mirror": true}`는 그 부위의 자세를 반대쪽에 거울처럼 준다 |
| `keys` | 블렌더에서 손으로 고친 손질 키를 그대로 저장한 것. 손대지 않는다 |
| `frames` | 내보낼 프레임 범위 `[처음, 끝]`. 없으면 클립 전체 |
| `slot`, `name` | 보낼 슬롯 폴더와 이름(파일은 `enf_이름.fbx`). 슬롯 이름은 작업실의 "게임에 보내기" 목록과 같다(`Walk`, `Run`, `Attack`, `CalmPose`, `CalmPoseGetUp` …) |

`part` 이름: 머리 `Head`, 목 `Neck`, 가슴 `Spine_03`, 배 `Spine_02`, 허리 `Spine_01`, 골반 `Hips`, 어깨 `Clavicle_L/R`, 위팔 `Shoulder_L/R`, 아래팔 `Elbow_L/R`, 손 `Hand_L/R`, 허벅지 `UpperLeg_L/R`, 정강이 `LowerLeg_L/R`, 발 `Ankle_L/R`.

순서는 불러오기 → 변형 → `keys` → `touch`. 블렌더 창 없이 만들어 게임에 넣는 명령(프로젝트 폴더에서, 같은 이름의 이전 파일은 바뀐다):

```powershell
& 'F:\SteamLibrary\steamapps\common\Blender\blender.exe' --background .\ArtSource\Zombies\ENFILADE_Zombie_Animation.blend --python .\Tools\Blender\Zombies\make_clip.py -- .\ArtSource\Zombies\Recipes\이름.json
```

`--out <폴더>`를 붙이면 게임 대신 그 폴더 아래에 같은 구조로 내보낸다(시험용).
