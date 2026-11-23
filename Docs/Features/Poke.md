# 가리키기와 찌르기 (F 꾹)

2026-11-12 사용자: "F가 핑이잖아? 핑 찍을 때 남는 손으로 손가락질 하는 모션 있고 F 꾹 누르고 있으면 GTA5에서 무슨 키 누르고 있으면 계속 손가락질할 수 있잖아? 그런 것처럼 F 꾹 누르고 있으면 손가락질 계속 하고 있을 수 있고 그걸로 친구 건드리면 이 첨부한 파일처럼 스킨 터치 꾹 돼서 볼 누르고 뱃살 누르고 그런 기능 있었으면 좋겠어. 재밌을 거 같아."

첨부한 VRChat 패키지(DocMe Skin Touch)는 느낌만 참고했다. 그 패키지는 VRChat의 Material Contacts와 Poiyomi 모듈이라 이 게임에서 돌지 않고, 라이선스가 코드를 옮겨 쓰는 것을 막는다. 여기의 코드와 셰이더는 모두 처음부터 새로 쓴 것이다.

## 조작

- **F를 짧게**: 예전처럼 조준점의 적을 핑하고, 왼손이 0.6초(`Poke.csv` `Ping.point`) 그 적을 가리킨다(0.8.217부터, 꾹 누를 때와 같은 손 모양, 적이 움직이면 따라감). 조준(줌) 중이면 왼손만 0.35초(`Ping.pointAimed`) 짧게 가리키고 총은 오른손에 남아 조준이 그대로다(0.8.266). 장전·꺼내기·투척 중이라 손이 바쁘면 손짓 없이 표시와 소리만 난다(예전에 대신 나오던 총 끄덕임은 조준을 흔들어 없앴다). 화면을 돌려도 손은 핑한 적에 남지만, 어깨가 팔을 돌려 줄 수 있는 데까지만이다(0.9.30, 사용자: "핑할 때 손 고정되는 건 좋은데 확 돌리면 팔이 너무 꺾이는데 일정 각도 이상 화면(고개) 돌려서 꺾으면 빠르게 핑하던 손가락 끝내고 돌아오게 가능?"). 적이 몸 앞을 가로질러 45°(`Point.turnAcross`, 화면을 왼쪽으로 돌릴 때)나 바깥쪽으로 100°(`Point.turnOutward`, 오른쪽으로 돌릴 때)보다 더 돌아가면 그 자리에서 핑의 손가락질이 끝나고, 손은 가리키던 쪽에서 그대로 0.1초(`Point.turnOut`)에 내려온다. 친구 화면에서도 같은 적을 가리키고 같은 때 내려온다(포즈 표시와 핑이 함께 보내는 적 위의 자리).
- **F를 꾹**(0.25초, `Poke.csv` `Point.holdAfter`): 누르고 있는 동안 왼손이 바라보는 곳을 가리킨다. 총은 오른손에 그대로 있고, 맨손이어도 왼손이다(콜아웃 몸짓과 같은 손). 검지는 펴고 나머지는 주먹, 엄지는 그 위로 접는다. 손가락은 총처럼 조준선이 실제로 닿는 곳을 가리킨다(0.8.234): 시선 레이가 처음 닿는 벽·물건·좀비(`Point.aimRange` 60m 안), 가까운 벽이면 손끝이 그 앞 1.5cm에서 멈춰 벽을 뚫지 않고, 벽 너머의 친구는 가리키지도 찌르지도 않는다. 친구는 이동용 캡슐이 아니라 몸 표면으로 닿는다.
- **상호작용이 먼저다**: F 누름이 줍기·부활·사다리·문·상점·수리 같은 상호작용 위에서 시작됐으면 가리키지 않는다. 가리키는 중에 그런 것이 조준점에 오면 끝난다.
- **손이 필요하면 끝난다**: 조준(ADS)·장전·꺼내기·살펴보기·사격·주먹·가드·차지, 그리고 G 투척(투척이 왼손을 먼저 가진다, 0.8.208). 끝나면 F를 다시 눌러야 다시 가리킨다.
- 서 있거나 앉아 있을 때만(걷기 포함). 파쿠르·사다리·쓰러짐·잡힘 중에는 안 된다.

## 찌르기

손이 닿는 거리(어깨에서 검지 끝까지 팔 길이의 97%, `Point.reach`)의 친구를 가리키면 손끝이 그 몸에 닿으러 간다. 몸 중 가장 바깥(옷·머리카락·피부)에 닿는다.

| 부위 | 어디 | 느낌(Poke.csv) |
| --- | --- | --- |
| 볼 | 눈 아래, 코 옆에서 얼굴 옆까지 | 말랑, 1.2cm, 1초에 5번 출렁 |
| 배 | 엉덩이 뼈 조금 위에서 가슴 뼈보다 꽤 아래까지, 몸 앞쪽 | 가장 푹신, 1.8cm, 출렁임 많음 |
| 팔 | 위팔(어깨 쪽 30% 빼고)과 아래팔(손목 앞까지) | 단단, 0.8cm |
| 어깨 | 쇄골 뼈, 위팔의 어깨 쪽 30% | 더 단단, 0.6cm |
| 정수리 | 두 눈보다 눈 사이 거리만큼 위 | 머리카락 위로 살짝 |

- 손끝이 꾹 들어가면(`press`, `pressTime`) 그 자리가 오목하게 들어간다. 손끝을 떼면 스프링처럼 되돌아오며 살짝 부풀었다가(`hz`, `bounce`, `bulge`) 가라앉는다.
- 손끝을 문지르듯 옮기면 자국도 따라간다. 한 번에 6cm(`Touch.slide`) 넘게 옮기면 앞 자국은 되돌아오고 새로 누른다.
- 누를 때 '뽁', 뗄 때 가볍게 '퐁' 소리가 난다. 볼은 높게, 배는 낮게(`pitch`). 소리 파일을 `Assets/_Project/Art/Resources/Audio/Poke/Press`·`Lift`에 넣으면 그 소리를 쓴다.
- **눌리지 않는 곳**: 가슴, 엉덩이, 사타구니는 어느 부위도 아니라 절대 눌리지 않는다(총관리자의 결정. 장난스럽고 건전하게. 바꾸고 싶으면 말해 주면 된다). 이마·코·입·턱·귀·뒤통수·눈·목·손·다리·등도 부위가 아니다. 이런 곳과 찌르기를 끈 친구에게는 손끝이 1.5cm 앞에서 멈춘다(`Point.hover`).
- 가슴·엉덩이·치마처럼 가슴 뼈·윗가슴 뼈·엉덩이 뼈에 달린 휴머노이드가 아닌 뼈(흔들림 뼈)는 모두 '꺼짐'으로 본다. 배의 윗선은 가슴 뼈보다 몸통 길이의 10% 아래라서 배를 눌러도 가슴까지 번지지 않는다.

## 설정

설정 → 조작 → **친구가 내 캐릭터 찌르기**(기본 켬, `GameSettings.letOthersPoke`, `SettingsDefaults.csv` 열 `letOthersPoke`). 끄면 모두의 화면에서 친구 손끝이 내 캐릭터 앞에서 멈춘다.

## 손 모양 그리기 (블렌더)

가리키는 손의 모양은 블렌더에서 그린다(0.8.217, 사용자: "블렌더로 만들어 줘. 왜냐하면 적 핑 찍을 때 가리키는 애니메이션이 있었으면 하니까"). 꾹 누른 손가락질과 F를 짧게 눌러 적을 핑할 때의 손가락질이 같은 손을 쓴다. 손 모양은 [PointHand.csv](../../Assets/_Project/Data/Resources/Tables/Clips/PointHand.csv)에 들어가고, [Tools/Blender/point_hand.py](../../Tools/Blender/point_hand.py)가 그 표를 블렌더로 불러오고 다시 저장한다.

1. 블렌더(4.x)에서 캐릭터 하나를 가져온다. 예: File > Import > FBX로 `Assets/ThirdParty/BOOTH/Chocolate rice/0.MAYO/FBX/FBX_MAYO.fbx`. 프로젝트에 있는 휴머노이드면 다 된다. 뼈 이름은 그 FBX의 `.meta`(Unity 휴머노이드 매핑)에서 읽고, 없으면 흔한 이름(Hand.L, IndexProximal_L, LeftHandIndex1…)으로 찾는다.
2. Scripting 탭 > Text > Open으로 `Tools/Blender/point_hand.py`를 열고 Run Script. 3D 뷰 사이드바(N)에 **ENFILADE** 탭이 생긴다.
3. 아마추어를 고르고 **게임 손 불러오기**를 누른다. 왼손 손가락이 지금 게임의 모양이 된다(처음 표는 0.8.213의 각도 그대로).
4. 포즈 모드에서 왼손 손가락 15마디(엄지·검지·중지·약지·새끼, 셋씩)를 원하는 모양으로 돌린다. 손목과 팔은 상관없다. 게임이 손을 돌려서 검지가 가리키는 곳을 향하게 한다.
5. **게임에 저장**을 누르면 PointHand.csv가 써진다. Unity가 켜져 있으면 Unity 창을 누르는 순간 실행 중인 게임에 들어간다(콘솔에 `[Tables] PointHand.csv ...`).
6. 게임에서는 개발자 콘솔(` 키)의 `point`로 F 없이 손가락질을 세워 두고 F5로 보면 편하다. 다시 `point`로 푼다.

저장되는 것은 손가락 마디 15개마다 손 자기 틀(x 손목→가운데 손가락 뿌리, y 손바닥 쪽, z = x × y)에서 가리키는 쪽과 제 길이를 축으로 더 돈 각도(손톱 방향)다(`Hand` 줄의 `index1X`…`thumb3Twist`, 투척·수리·칼과 같은 칸, 0.9.75, 사용자 11-14: "네, 다 고쳐"). 게임은 몸이 잰 손에 입히고(`PlayerBodyHands.WearFingerWays`), 블렌더 불러오기도 같은 계산(`finger_ways.wear`)이라 블렌더 모양이 비틀림까지 어느 휴머노이드에나 그대로 나온다(모드 캐릭터도). 예전에는 마디 방향만 저장해서 비틀림이 빠졌고, 끝 마디는 캐릭터마다 어림한 축으로 놓여 카즈사에서 손끝이 6~9°(엄지 끝 32°) 그림과 달랐다. 마디 방향만 있던 예전 표(Index1~Thumb3)도 그대로 읽는다. 검지 끝 마디는 늘 가운데 마디에서 곧게 이어진다(0.8.266, 사용자: "손가락질 왜 손가락 가장 끝 마디가 굽어진 거임?"): 그림에서 끝 마디가 굽어 있어도 게임이 곧게 펴고, 블렌더 도구도 불러올 때와 저장할 때 곧게 둔다. PointHand.csv의 마디 줄을 지우면 아래 `Point.*` 각도로 만든다.

블렌더에서 그리지 않는 것은 Poke.csv 숫자다. 게임을 켠 채 저장하면 바로 바뀐다.

| 숫자 | 바꾸는 것 |
| --- | --- |
| `Point.palmIn` | 손바닥 방향: 0 아래, 1 몸 안쪽(엄지가 위) |
| `Point.wristBend`, `Point.wristSide` | 손목이 쉴 때의 꺾임(°): 손바닥 쪽(+)·손등 쪽(-), 엄지 쪽(+)·새끼 쪽(-). 손목은 이 자리에서 `Point.wristLimit` 안으로만 꺾인다 |
| `Point.elbowDown`·`elbowOut`·`elbowBack` | 팔꿈치가 향하는 쪽: 아래, 시선의 왼쪽, 뒤 |
| `Point.extend` | 그냥 가리킬 때 팔을 뻗는 정도(어깨~검지 끝 길이의 몫, .96). 팔은 어깨에서 조준점 쪽으로 뻗어서 걷기·상체 움직임을 그대로 탄다(0.8.253. 0.8.232~0.8.234는 손을 화면 한 자리에 붙였다). 팔꿈치가 많이 굽을수록 아래팔이 조준점에서 비껴 손목이 더 꺾여야 해서 0.8.266에 .92에서 .96으로 폈다 |
| `Point.wristLimit` | 손목이 곧은 자리(모델을 만든 그대로의 손목, 스킨의 바인드 포즈)에서 꺾일 수 있는 최대 각도(°, 20). 위·아래를 봐도 이 안이다: 높낮이는 어깨와 팔꿈치가 맡는다(0.8.266) |
| `Point.forearmTwist` | 손을 돌린 만큼 아래팔도 제 길이 축으로 같이 돌리는 몫(0~1, 1). 1이면 손목은 비틀리지 않는다(0.8.266) |
| `Point.swayHz`·`Point.swayBounce` | 팔이 겨누는 쪽을 따라가는 스프링: 빠르기(Hz)와 감쇠비. 빨리 돌면 팔이 살짝 늦게 따라오다 넘쳤다가 멈춘다 |
| `Point.turnAcross`·`Point.turnOutward` | 어깨가 팔을 돌려 줄 수 있는 데(°): 몸 앞을 가로질러(오른쪽) 45, 바깥쪽(왼쪽) 100. 위·아래는 시선에서 약 70°. 가슴(두 어깨를 잇는 선)이 향한 쪽에서 잰다. 핑한 적이 이 밖으로 나가면 핑의 손가락질이 끝나고, 꾹 누른 손가락질도 빨리 돌 때 스프링에 밀려 이 밖으로 가지 않는다(0.9.30) |
| `Point.turnOut` | 그렇게 끝난 핑의 손가락질이 돌아가는 시간(초, .1). 가리키던 쪽에서 그대로 내려오고 조준점 쪽으로 휘지 않는다. 손이 총으로 돌아가는 0.15초보다 짧아야 더 빨라진다 |
| `Point.touchIn` | 친구 몸에 손끝을 대러 가거나 떼고 돌아오는 시간(초) |
| `Ping.point` | F를 짧게 눌러 핑할 때 가리키는 시간(초). 0이면 손짓 없음 |
| `Ping.pointAimed` | 조준(줌) 중에 핑할 때 왼손만 짧게 가리키는 시간(초, .35). 0이면 조준 중엔 손짓 없음 |
| `Point.index*`·`curl*`·`fist`·`thumb*` | PointHand.csv가 없을 때만 쓰는 손가락 각도 |

## 표정 연결 (face-moods)

찔림은 각 PC에서 이벤트로 나온다. 표정 파일은 건드리지 않았다.

```csharp
// 손끝이 들어가기 시작할 때 true, 뗄 때 false. 모든 PC에서(찔린 사람 자신의 PC 포함).
SkinPoke.Poked += (PlayerMovement poked, PlayerMovement poker, PokeZone zone, bool pressing) => { ... };
// 지금 눌리고 있는지(누르는 동안 표정을 유지하려면)
bool now = SkinPoke.IsPoked(poked, out PokeZone zone);
// PokeZone: None, Cheek, Belly, Arm, Shoulder, HeadTop
```

## 어떻게 되어 있나

### 손 (PlayerPing → PlayerPoint → PointArm)

- `PlayerPing`이 F를 길게 누른 것을 `PlayerPoint.Hold`로 알린다.
- F를 짧게 눌러 적을 핑하면 `PlayerPing`이 `PlayerPoint.Flash`로 `Ping.point`초 동안 가리키게 한다. 어느 PC에서나 핑한 자리(적이 움직이면 따라감)를 가리킨다: 핑을 알리는 RPC(`PlayerNetworkCombat.PingRpc`)가 적 위의 자리를 적 자신의 좌표로 함께 보내고(`PlayerPoint.Pinged`), 포즈의 `PingPoint` 비트가 '지금 가리키기는 핑의 것'이라고 알린다(0.9.30. 전에는 친구 PC의 복사본이 그 사람의 시선을 가리켰다). 조준(줌) 중이면 `Ping.pointAimed`초만 왼손으로 짧게 가리키고(`PlayerPoint.CanPoint(…, whileAiming: true)`), 총은 오른손이 조준 그대로 든다. 손이 바쁘면(장전·꺼내기·투척) 손짓은 없다. 예전에 대신 나오던 총 끄덕임(`FPSBody.Point`)은 0.8.266에 뺐다. 사격·주먹이 나오면 그 순간 손을 거둔다.
- 손 모양은 블렌더로 그린 `PointHand.csv`(`PokeTables.HandWays`, 예전 표는 `HandPose`): 그린 검지(마디 방향의 평균)가 가리키는 쪽을 향하게 손 전체를 돌리고(`IndexAlong`), 손가락은 `WearFingerWays`로 입힌다. 표가 없으면 `Point.*` 각도로 만든다.
- 왼손은 받치는 손 빌려주기로 총에서 뗀다(0.8.205의 공용 입구, 0.8.208부터). 가리키기가 [손의 주인](Hands.md)에게서 왼손을 받으면(길게 누른 F는 `point.hold`, 핑의 손짓은 `point.ping`. 2026-11-18부터) 주인이 `PlayerBodyHands.LendOffHand`를 불러 왼손의 총 접점·손잡이·총 손가락이 0.15초에 풀린다. 가리키는 자세는 `OffHandLent`(지금 풀린 정도)보다 앞서지 않게 섞여서 손이 총에서 떨어지는 속도와 같고, 끝나면 주인이 돌려준다.
- G 투척·주사·수리 도구·막기 자세가 왼손을 쓰는 동안과, 조준·장전·꺼내기·살펴보기·총으로 밀치기 중에는 가리키지 않는다. 누가 먼저인지는 손의 주인의 표가 정한다(`Tables/Player/HandUses.csv`의 `rank`: `point.hold`는 10, 핑의 손짓 `point.ping`은 35라 조준 중에도 된다). 내 PC에서는 가리키기가 끝나고(F를 다시), 다른 PC의 복사본도 그동안 자세를 놓는다.
- `PlayerPoint`(플레이어)는 가리키는 중인지와 찌르기를 허락하는지를 들고 있다. 다른 PC에는 `PlayerPose.Marks`의 비트(`Pointing`, `NoPoke`, 0.9.30부터 `PingPoint`, `PointAway`)로 간다(`MovementMirror`). 꾹 누른 가리키기의 방향은 포즈가 이미 보내는 시선 그대로라 가리키기만을 위한 전송은 없다. 핑의 가리키기는 핑 RPC에 실린 적 위의 자리를 가리킨다.
- 어깨가 돌려 줄 수 있는 데(`PointArm.ShoulderReach`, 0.9.30): 가슴이 향한 쪽(두 어깨 위치로 재서 어느 리그든 같다)을 가운데에서 팔 쪽(왼쪽)으로 (`turnOutward`−`turnAcross`)/2만큼 돌린 축 둘레의 원뿔, 반각은 둘의 평균이다. 그래서 몸 앞을 가로질러 `turnAcross`, 바깥쪽 `turnOutward`, 위·아래 약 70°. 좌우는 가슴에서 잰다: 서 있으면 가슴은 시선과 같은 쪽이지만(돌 때 엉덩이가 늦게 따라오지 않는다) 옆걸음질 땐 엉덩이가 걸음 쪽으로 돌고 가슴은 그 40%만큼 남아서, 어깨가 매달린 가슴이 기준이다. 위·아래는 시선의 높이에서 잰다: 팔을 어깨에서 올리고 내리는 것은 70° 넘게 괜찮고 척추는 시선의 45%(아래)·20%(위)만 굽어서, 가슴으로 재면 꾹 누른 손가락질이 위를 볼 때 잘린다.
  - 45°는 마요 기준이다: 어깨 관절이 몸 가운데에서 7.5cm라 37°에서 팔꿈치가 몸 가운데를 넘고 45°에서 아래팔이 가슴에 닿기 시작하며(60°면 4~5cm 파고든다), 1인칭에서 손끝이 화면(가로 90°) 오른쪽 끝에 온다. 100°는 옆으로 곧게 뻗은 팔(90°)에서 조금 더, 그 너머는 팔꿈치와 손이 등 뒤로 넘어간다(95°부터).
  - 핑의 가리키기는 그 자리가 이 원뿔 밖으로 나가면 내 PC에서 `PlayerPoint.TurnAway`로 끝난다. 팔은 그때 시선에서 갖던 방향을 그대로 들고(시선을 따라 같이 돌며) `Point.turnOut`에 내려오고, 포즈의 `PointAway` 비트가 친구 PC의 복사본도 같은 때 같은 식으로 내려오게 한다(`Point.turnOut`보다 0.2초 더 켜 둬서 네트워크가 늦어도 빠지지 않는다).
  - 스프링으로 따라가는 팔의 방향도 늘 이 안에 둔다(`HoldWithin`). 꾹 누른 손가락질은 시선을 따라가니 원뿔 가운데에 있지만, 빨리 휙 돌면 스프링이 늦게 따라와 가슴을 가로지를 수 있었다. 가장자리에 닿은 만큼의 출렁임은 버려서 튕기지 않는다.
- 내 다리([ArmOffLegs](../../Assets/_Project/Scripts/Player/Poke/ArmOffLegs.cs), 0.9.362, 사용자 11-23: "앉은 채로 내 다리쪽으로 가면 내 팔과 손이 다리를 뚫는데"): 위팔·아래팔·손과 손가락을 통째로 허벅지·정강이 밖에 둔다. 어깨·팔꿈치·손목·손끝을 팔 길이로 이은 점으로 보고, 자세로 끌리면서 다리 밖으로 밀려난다. 밀린 만큼은 다음 프레임으로 이어지고 0.12초에 풀려서, 팔이 다리 위에 얹히고 미끄러져 넘어가며 튀지 않는다. 다리와 팔의 굵기는 그 몸의 메시에서 옷까지 재고(`PokeBody.Thickness`, 모든 PC에서 같다) 재기 전에는 `ClothBody.csv`의 비율을 쓴다. 다리는 몸의 것 하나([OwnLegs](../../Assets/_Project/Scripts/Player/Body/OwnLegs.cs), 0.9.363)를 허리 숙임 한도(SpineRoom: 내려다볼 때 머리·눈·어깨가 무릎에 들어가지 않게)와 같이 쓴다. 물리(리지드바디·관절)로 하지 않는 까닭: 팔은 클립 뒤에 매 프레임 자세를 다시 놓으므로 물리 몸은 그 자세에 휘둘리고, PC마다 다르게 서고, 멈춘 자리에서도 떨린다. 앉은 다리로 시선을 마구 돌려 보면 막지 않은 팔은 11cm까지 뚫고 이 방식은 0cm, 한 프레임에 움직이는 거리는 자세와 같다. 0.9.360은 손가락이 향한 줄 하나만 재서 아래팔이 옆 허벅지를 뚫고 다리 가장자리에서 팔이 튀었다.
- `PointArm`(몸 모델, 가리키는 몸에만 붙음)은 FPSAF·손·총·콜아웃 몸짓 다음(`GunFramework + 6`), 옷감 전에 왼팔을 고친다.
  1. 손목에서 검지 뿌리로 가는 선을 가리키는 쪽으로, 손바닥은 아래이면서 몸 안쪽으로(`Point.palmIn`) 돌린다.
  2. 손가락 마디마다 다음 마디가 있는 쪽을 월드 방향으로 겨눈다(`FromToRotation`).
  3. `TwoBoneIK`로 팔꿈치를 아래·바깥으로 굽혀 손목을 놓는다.
  4. 그다음 손을 그 팔의 아래팔 쪽으로, 손목이 곧은 자리에서 `Point.wristLimit`(20°)을 넘지 않을 만큼만 돌리고 팔을 다시 맞추기를 세 번 한다. 마지막은 늘 손목 쪽이라 어느 높낮이에서도 손목이 한도를 넘지 않는다(0.8.266. 0.8.253은 마지막 팔 맞춤 전에 한도를 재서 위·아래를 크게 보면 넘었다). 손목이 곧은 자리는 그 캐릭터의 스킨 바인드 포즈(메시를 입힌 자세)에서 읽어서 뼈 축이 달라도 같다.
  5. 아래팔을 제 길이 축으로 손과 같이 돌린다(`Point.forearmTwist`). 팔뚝이 총 잡던 대로 돌아 있는 위에 손만 돌리던 탓에 위·아래를 볼 때 손목이 비틀렸다(0.8.266).

  팔의 방향은 스프링으로 조준점 쪽을 따라간다(`Point.swayHz`·`swayBounce`).

  모든 휴머노이드에서 실행 중에 맞춘다(굽기 없음). 1인칭 카메라는 그 전에 놓였으니 시야는 팔을 따라 흔들리지 않는다.
- 모든 PC에서 같은 코드가 같은 시선과 같은 플래그로 돈다. 내 1인칭 몸에도, 남이 보는 내 몸에도 같은 손가락질과 같은 찌르기가 보인다(각 PC의 시선이 조금 늦게 오는 만큼만 다르다).

### 몸 표면 (PokeBody)

친구의 손끝이 처음 가까이 올 때 그 몸의 모든 메시(피부·머리카락·옷·액세서리)를 읽는다. 지금 걸린 블렌드셰이프(체형 옵션·표정)를 더하고, 정점마다 가장 많이 따르는 뼈의 공간에 놓은 뒤, 6mm(`Touch.grid`) 칸마다 점 하나로 줄인다. 한 프레임에 2만 정점(`Touch.bakeVertices`)씩 읽어 끊김이 없다. 걸린 블렌드셰이프는 무게가 큰 12개(10 이상)만 더한다(셰이프 하나가 메시 전체 복사라서. 이치고는 키가 438개).

시선 선은 뼈의 경계 구부터 걸러서 가장 가까운 점(선에서 7mm 안, `Touch.width`)을 찾는다. 그래서 배 위의 셔츠, 볼 위의 옆머리처럼 가장 바깥에 닿는다. 점은 매 프레임 뼈를 따라가므로 Magica가 흔드는 살(`FleshSpring`)도 그려진 자리에서 만난다. FleshSpring은 고치지 않았다.

부위는 휴머노이드 뼈로 정한다.
- 얼굴: 그 플레이어의 시선(머리가 따라감) 기준으로 두 눈 사이에서 눈 사이 거리 단위로 잰다. 눈 뼈가 없는 모델은 몸통 길이로 짐작한다.
- 배: 엉덩이~목 길이의 비율로 정한다.
- 팔: 마디 위의 비율로 정한다.

모드 캐릭터도 그대로 된다. 메시는 Read/Write가 켜져 있어야 하고(BOOTH 가져오기는 켬), 꺼진 메시는 건너뛴다.

### 눌림 (SkinPoke + SkinDent.hlsl)

`SkinPoke`는 손끝 슬롯 넷(방의 네 명)을 가진다. 누르는 동안 슬롯은 손끝을 따라간다. 떼면 누른 뼈에 붙은 채 피부 가운데를 감쇠 스프링으로 되돌리고, 멈추면 비운다. 매 프레임 옷감 다음(`Cloth + 5`)에 셰이더 전역 값을 쓴다.

| 전역 값 | 내용 |
| --- | --- |
| `_EnfDentCount` | 쓰는 슬롯 수 |
| `_EnfDentTip` | 손끝 자리, 오목한 곡률 |
| `_EnfDentPush` | 누르는 방향, 최대 깊이 |
| `_EnfDentShape` | 가장자리 부드러움, 퍼지는 반경, 되돌아올 때 부풂과 그 폭 |

슬롯의 비트(1 << 슬롯)는 눌리는 몸의 모든 렌더러의 셰이더 사용자 값(Unity 6.3 `Renderer.SetShaderUserValue`)에 켠다. 그래서 그 몸과 그 옷만 눌리고, 찌르는 손가락이나 옆 사람은 안 눌린다. 사용자 값의 비트 0~3은 SkinPoke 것이다. 다른 곳에서 사용자 값을 쓰려면 이 비트를 남겨야 한다.

`Assets/_Project/Art/Shaders/SkinDent.hlsl`은 lilToon 정점 단계에서 돈다.
- 손끝에 매달린 그릇 모양(손끝 선에서 r만큼 떨어진 곳의 깊이 -r²/2R)보다 앞에 있는 피부를 그 그릇까지 밀어 넣는다. CPU가 표면을 몰라도 손끝이 들어간 만큼 정확히 오목해진다.
- 가장자리는 부드럽게(soft max) 둥글린다. 손끝 근처는 깊이 그대로, 반경의 바깥 절반에서 사라진다.
- 손끝 바로 앞뒤 몇 cm만 움직인다. 얼굴 앞의 팔이나 머리 뒤쪽은 안 움직인다.
- 법선을 오목한 벽을 따라 기울여 툰 그림자가 진다.
- 모든 패스(색·외곽선·그림자·깊이·모션)에 같이 적용된다.

### lilToon에 넣은 줄 (업데이트하면 다시 넣기)

lilToon 2.4.1의 `Packages/jp.lilxyzw.liltoon/Shader/Includes`에 있는 두 파일에 `ENFILADE` 표시 블록을 넣었다. 하나는 모든 패스가 쓰는 `lil_common_vert.hlsl`이고, 하나는 이치고 머리카락이 얼굴에 드리우는 가짜 그림자 `lil_pass_forward_fakeshadow.hlsl`이다. lilToon을 업데이트하면 lilToon 원래 파일로 돌아가서 이 줄이 빠진다. 그러면 에디터가 스크립트를 읽을 때 콘솔에 `[SkinDent]` 경고를 낸다(`SkinDentHookCheck`). 손끝과 소리는 되지만 피부가 안 눌린다. 아래 곳들을 다시 넣는다.

`lil_common_vert.hlsl`:

1. `#define LIL_CUSTOM_VERT_COPY` 블록 바로 다음:

```hlsl
// ENFILADE (not lilToon): a friend's fingertip dents the skin (SkinDent.hlsl, SkinPoke.cs). Put back after updating
// lilToon, with the call in lilCustomVertexOS below (Docs/Features/Poke.md).
#include "Assets/_Project/Art/Shaders/SkinDent.hlsl"
```

2. `void lilCustomVertexOS(inout appdata input, inout float2 uvMain, inout float4 positionOS)`의 `LIL_CUSTOM_VERTEX_OS` 블록 다음, 닫는 `}` 앞:

```hlsl
    // ENFILADE (not lilToon): the skin dent, see the include above.
    #if defined(LIL_APP_NORMAL)
        EnfiladeSkinDent(positionOS, input.normalOS, true);
    #else
        float3 enfiladeNoNormal = 0.0;
        EnfiladeSkinDent(positionOS, enfiladeNoNormal, false);
    #endif
```

`lil_pass_forward_fakeshadow.hlsl`:

1. `#include "lil_common_appdata.hlsl"` 바로 다음에 위 1번과 같은 `#include` 줄(주석 포함).
2. `vert`의 `LIL_VERTEX_POSITION_INPUTS(input.positionOS, vertexInput);` 바로 앞:

```hlsl
    // ENFILADE (not lilToon): the skin dent, see the include above.
    float3 enfiladeNoNormal = 0.0;
    EnfiladeSkinDent(input.positionOS, enfiladeNoNormal, false);
```

lilToon 머티리얼을 쓰는 캐릭터(마요·이치고·에네 옷·카즈사, 모드의 lilToon 아바타)는 눌린다. 실(Poiyomi와 전용 셰이더)은 눌림 없이 손끝·소리·표정 이벤트만 있다.

## 가볍게

- 아무도 누르지 않으면 셰이더는 슬롯 수 하나만 보고 끝난다. 눌리는 몸이 아니면 렌더러 값 하나만 보고 끝난다.
- CPU는 누군가 가리키는 동안, 그 시선 근처(1.3m + 몸 크기)에 있는 몸만 일한다.
- 몸 표면은 몸마다 한 번 읽는다. 옷을 바꾸면 다음 찌르기 때 다시 읽는다.
- 소리는 누를 때와 뗄 때 한 번씩이다.

## 숫자 (Poke.csv)

- `Point.*`: 가리키기.
  - `holdAfter` 누르고 있어야 하는 시간.
  - `in`·`out` 팔이 오가는 시간.
  - `turnAcross`·`turnOutward` 어깨가 팔을 돌려 줄 수 있는 데, `turnOut` 핑의 손가락질이 그 밖에서 끝날 때 돌아가는 시간.
  - `follow` 손끝 따라가기.
  - `range`·`reach`·`extend` 거리.
  - `hover` 못 찌르는 곳 앞에서 멈추는 거리.
  - `palmIn` 손바닥 방향, `wristBend`·`wristSide`·`wristLimit` 손목, `forearmTwist` 아래팔 돌리기.
  - `index1~2`·`curl1~3`·`curlSpread`·`fist`·`thumb*` 손가락(PointHand.csv가 없을 때만), `elbowDown`·`elbowOut`·`elbowBack` 팔꿈치(위 **손 모양 그리기**).
- `Ping.point`: F를 짧게 눌러 핑할 때 왼손이 가리키는 시간. `Ping.pointAimed`: 조준(줌) 중일 때.
- `Touch.*`: 몸 표면 찾기. `width`, `grid`, `bakeVertices`, `lose`(시선이 잠깐 빗나가도 유지), `slide`.
- `Sound.*`: 소리. `volume`, `lift`, `near`, `far`.
- `Zone.*`: 부위 경계. 볼은 눈 사이 거리 단위, 배는 몸통 길이 비율, 어깨·손목은 팔 마디 비율.
- `Cheek`·`Belly`·`Arm`·`Shoulder`·`HeadTop`의 `.*`: 부위마다 눌리는 느낌.
  - `press`·`pressTime`: 얼마나, 얼마 만에.
  - `deepest`: 최대 깊이.
  - `bowl`: 오목한 곡률. 클수록 말랑하고 넓다.
  - `soft`: 가장자리.
  - `reach`: 퍼지는 반경.
  - `hz`·`bounce`·`bulge`: 되돌아오기.
  - `pitch`: 소리.

## 아직 안 된 것

- DocMe에 있는 '손이 다가오면 피부가 어두워지는 그림자'는 없다. lilToon 프래그먼트까지 고쳐야 해서 뺐다. 툰 그림자는 오목한 법선으로만 생긴다.
- 실 캐릭터는 눌림이 없다(위).
- 한 손(왼손)만 가리킨다.
