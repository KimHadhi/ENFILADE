# 손가락을 게임에 그대로 보내기 (finger_ways.py)

사용자, 2026-11-14: "제기랄 똑똑하고 유동적이고 robust하면서 근본적으로 문제를 고치는 게 우리 철학이건만!! … 다신 이런 일이
재발하지 않도록 뼈에 새겨라. 이 문제를. 지금 당장. 고쳐라."

0.9.61 전까지 블렌더 손 작업실(투척, 수리, 칼)은 손가락을 손가락마다 굽은 정도 숫자(`curl`, `thumb`) 하나로만 게임에 보냈다.
그래서 블렌더에서 잡은 손가락 마디 모양은 게임에 가지 않았다. **손이 나오는 표를 쓰는 블렌더 도구는 모두
[finger_ways.py](finger_ways.py)로 손가락을 잰다.** 같은 방식을 따로 만들지 않는다.

## 표에 가는 것

손가락 마디 15개(검지·중지·약지·새끼·엄지 × 뿌리·가운데·끝)마다 네 칸이다.

| 칸 | 뜻 |
| --- | --- |
| `<접두어><손가락><마디>X`, `Y`, `Z` | 그 마디가 가리키는 쪽. 손 자기 틀에서 잰다: x 손이 뻗은 쪽(손목 → 가운데 손가락 뿌리), y 손바닥 쪽, z = x × y(Unity의 외적: 왼손은 엄지 쪽, 오른손은 엄지 반대쪽). 총의 원본 손가락(`GunArms.Digits`)과 같은 틀이다 |
| `<접두어><손가락><마디>Twist` | 리그의 기본 자세에서 그 쪽을 가리키게 하는 가장 작은 회전보다 제 길이를 축으로 더 돈 각도(°, Unity의 방향) |

- 손가락 이름: `index`, `middle`, `ring`, `little`, `thumb`. 마디: `1`(뿌리) `2` `3`(끝). 예: `index1X` … `thumb3Twist`, 한 손 60칸.
- 접두어: 한 손뿐인 표는 없음(`""`). 두 손을 한 표에 담으면 손마다 다른 접두어(예: 칼 표의 `r`, `l`).
- 마디의 세 채널(굽힘, 벌림, 제 길이 축 비틀림)이 모두 간다. 뼈의 꼬리는 보지 않는다(카즈사 손가락 뼈 꼬리는 손가락과 95° 엇나감).

## 게임에서

- `FingerWays`([FingerWays.cs](../../../Assets/_Project/Scripts/Player/Body/Hands/FingerWays.cs))가 한 손의 마디 15개를 담는다:
  `ways.Read(row, prefix)`로 줄을 읽고, `FingerWays.Blend(a, b, t, into)`로 키 사이를 섞는다.
  - 새 칸이 없는 옛 표나 모드 표는 `Has`가 false다. 그때는 부르는 쪽이 예전처럼 굽은 정도로 쥔다.
- 팔을 코드로 잡는 쪽(손 다음 순서, `FrameOrder.GunFramework + n`)은 이렇게 손에 입힌다:
  `hands.WearFingerWays(animator, isLeft, ways, weight)`([PlayerBodyHands.Ways](../../../Assets/_Project/Scripts/Player/Body/Hands/PlayerBodyHands.Ways.cs)).
  - 그 몸의 손이 기본 자세에서 잰 `HandGrip`으로 간다. 총 클립 손가락과 같은 계산이다(`HandGrip.WearWays`).
  - 마디마다 가장 적게 돌려 그 쪽을 가리키게 한 뒤, 비틀림만큼 더 돌린다. 그래서 어느 휴머노이드(모드 캐릭터 포함)든 뼈 축과 상관없이 같은 모양이 나온다.
  - false가 오면(몸이 아직 묶이지 않음, 손이 없음, 표에 마디가 없음) 예전 방식으로 쥔다.
- 클립 표(`GrenadeArmPath`)는 `GrenadeArmPath.SampleFingers(table, time, ways)`로 읽는다(투척 `GrenadeArm`, 수리 `RepairArm`).

## 블렌더에서

작업실 도구는 다른 공용 모듈처럼 경로로 한 번만 불러온다:

```python
spec = importlib.util.spec_from_file_location('enf_finger_ways', root / 'Tools/Blender/HandPaths/finger_ways.py')
```

| 함수 | 하는 일 |
| --- | --- |
| `columns(prefix='')` | 그 손의 60칸 이름 |
| `descriptions(hand='')` | 설명 줄의 칸들 (`hand`: 두 손 표면 '왼손 '·'오른손 ') |
| `measure(rig, wrist, chains, right=False)` | 지금 포즈의 마디 15개 `(x, y, z, twist)`. `chains`: 손가락 → 뼈 셋(뿌리부터, 엄지는 손바닥 안 뿌리부터) |
| `cells(ways)` | `measure`의 값을 표 칸 글자로 |
| `closing(rig, wrist, chains, right=False)` | 손가락마다 굽은 정도(요약 칸 `curl`·`thumb`용) |
| `close(rig, wrist, chains, curl, thumb, right=False, update=...)` | 예전 게임이 굽은 정도로 쥐던 모양(`ArmBones.Close`)을 리그에 그대로 만든다. 손가락 키가 없던 시안에 처음 쥐기를 넣을 때 쓴다 |

- 리그의 armature 공간에서 잰다. 오브젝트의 위치, 회전, 크기, 거울은 결과에 들어가지 않는다.
- 쓰는 곳: 투척 작업실 [grenade_tools.py](../Grenades/grenade_tools.py) `clip_rows`, 밀치기 작업실 수리 동작 [shove_tools.py](../Shove/shove_tools.py) `arm_rows`,
  칼 작업실 [knife_tools.py](../Knives/knife_tools.py) `clip_rows`(오른손, `right=True`: KnifeMelee*.csv, 0.9.63; 칼 작업실 클립 29개를 카즈사·마네킹 오른손에 입혀 다시 재 보니 방향 최대 0.0002°, 비틀림 최대 0.0001°).
  - 손가락 키가 없던 수리 시안은 열 때 예전 게임과 같은 쥐기를 받는다(`draft_grips`).

## 확인 (2026-11-14, Blender 4.5.16, 스크래치 사본)

투척 7개·수리 2개 클립의 표본 45프레임을 확인했다. 마디로는 675개다.

- 방법: 게임 계산(ArmIK 손 틀, HandGrip의 기본 자세·축, WearWays)을 Unity의 왼손 좌표 그대로 옮겨 두 몸에 입혔다. 그 뒤 같은 `measure`로 다시 쟀다.
  - 몸: 카즈사 실제 모델(`KazusaBand.fbx`)과 KINEMATION 마네킹.
- 방향 차이: 최대 0.0001°.
- 비틀림 차이: 최대 0.0005°.
- 카즈사 손끝 자리 차이: 0.01 mm 미만.
- 표의 숫자는 반올림하지 않고 소수 여섯째 자리까지 쓴다(`cells`; 넷째 자리면 방향이 최대 0.03° 어긋났다).
- 일부러 25° 비튼 마디(검지 가운데, 엄지 뿌리)도 두 몸에서 부호까지 그대로 나왔다.
