# 전투 제작 안내

[프로젝트 안내](../../../README.md) · [전투 규칙과 확장 계획](WeaponSystemDesign.md) · [전투 코드 구조](../Scripts/Combat/README.md) · [검증 안내](ValidationGuide.md)

이 문서는 현재 전투 씬을 실행하고, 무기 수치와 표시를 수정하는 작업 순서다. 행동 간 우선순위와 이후 기획은 **전투 규칙과 확장 계획**에서 확인한다.

## 바로 실행하기

1. [CombatFoundation 씬](../Scenes/Development/CombatFoundation.unity)을 열고 Play한다.
2. Game 창을 클릭해 마우스를 잠근다. 잠금을 잡는 첫 클릭은 공격하지 않으므로 버튼을 놓고 다시 누른다.
3. **1**로 소총, **2**로 피스톨을 선택한다. 현재 씬은 슬롯을 해제한 상태로 시작한다.
4. **좌클릭**으로 발사하고 **우클릭 유지**로 정조준(ADS), **R**로 재장전한다.
5. **3 또는 4**의 빈 슬롯을 선택하면 주먹을 사용한다. 선택한 숫자를 다시 누르면 무기와 주먹을 모두 내린다.
6. **Esc**로 일시정지하고 설정 메뉴를 연다(마우스도 풀린다). Esc나 "돌아가기"로 돌아온다. 이동·슬라이딩 조작은 [이동 안내](MovementGuide.md)를 참고한다.

총의 자세는 절차적으로 정하고 [몸의 손](PlayerBodyGuide.md#몸의-손)이 그 총을 잡는다. 지금 씬의 총 모델은 재질을 비워 보이지 않는다. 탄창 교체와 무기별 정식 애니메이션은 후속 작업이다.

## 조작과 화면 확인

| 입력 | 총을 든 상태 | 빈 슬롯을 선택한 상태 |
| --- | --- | --- |
| 좌클릭 | 단발·점사·연사 등 무기 설정에 따라 발사 | 누르면 차징, 놓으면 주먹 공격 |
| 우클릭 | 정조준 유지 | 가드, 새 입력 직후 패링 판정 |
| R | 수동 재장전 | 사용하지 않음 |
| V | 밀치기. 시선 쪽으로 달리며 누르면 돌진 밀치기. 앉아서 누르면 다리 쓸기 | 짧게 눌렀다 놓으면 밀치기, 길게 누르면 한 명에게 가드 브레이크. 시선 쪽으로 달리며 누르면 돌진 밀치기. 앉아서 누르면 다리 쓸기 |
| 1 / 2 / 3 / 4 | 원거리 / 원거리 / 근접 / 특수 슬롯 선택·해제 | 동일 |
| F | 가까운 흐트러진 적에게 처형 표시가 뜨면 처형 | 동일. 슬롯을 해제한 상태에서도 가능 |
| Q / E | 기울이기·마이크로 회피, ADS와 함께 사용 가능 | 기울이기·마이크로 회피 |

- **탄약 HUD**: 장탄수, 탄창 용량, 예비탄을 표시한다. 하단 막대는 평소 탄창 잔량, 장전 중 실제 장전 진행률이다.
- **탄약 경고**: `RELOAD`는 재장전 권장, `NO RESERVE`는 다음 장전용 예비탄 없음, `OUT OF AMMO`는 장탄·예비탄 모두 없음이다.
- **적 표시**: HP와 체간이 줄어들며, 체간이 바닥나면 흐트러짐 표시와 조건에 따른 `[F] 처형하기` 안내가 나타난다.
- **패링**: 공격을 쳐내 적을 밀어내고 체간을 깎는다. 체간이 무너지면 잠깐 느려지며 처형으로 이어진다.
- **처형 칼**(11-13 사용자: "일단 칼은 클래스별로 각자 전용 칼을 쓰게 하고 싶고"): 처형과 등 뒤 암살은 클래스 전용 칼로 한다. 시작하면 F든 주먹 좌클릭이든 몸이 닿을 만큼(`Execution.dashStopDistance` 0.32m) 달려들고(전에는 F는 2.1m에서 그대로 처형했다), 총은 사다리를 탈 때처럼 치운다. 오른손이 허리에서 칼을 뽑아 역수(새끼손가락 쪽으로 칼날)로 쥐고 화면 오른쪽 눈높이로 들었다가, 처형 타격 순간(`Execution.impactTime` 0.35초)에 좀비 목에 내리꽂고, 조금 더 밀어 넣었다가 빼서 칼집에 넣는다(0.92초). 왼손은 그동안 좀비 머리를 잡는다. 손목은 처형하는 PC가 잰 좀비의 목·머리 자리로 당겨져 몸집이 달라도 목에 닿는다. 칼은 [ClassKnives.csv](../Resources/Tables/Melee/ClassKnives.csv)(클래스마다 모델·쥐는 자리·날 방향·소리), 동작은 [KnifeStab.csv](../Resources/Tables/Clips/KnifeStab.csv)(양팔의 길, 초안은 `Tools/Blender/Knives/knife_stab.py`의 키에서 쓴다). 뽑기·박히기·넣기 소리는 SoundCues `Knife/Draw`·`Knife/Stab`·`Knife/Sheath`(투척 나이프의 MW 꺼내기와 Far Cry 4 찌르기 소리)이고 뼈 소리(`Execution/Behind`·`Front`)는 그대로 겹친다. 동료 화면에서도 같다: 처형하는 PC가 단계와 목·머리 자리를 보내고 그 사람의 몸이 같은 동작을 한다.
- **근접 칸의 클래스 칼**(11-13 사용자: "클래스 별로 기본 근접 슬롯에 칼 들게 해 줘"): 근접 칸(무기 슬롯 3번, 맨주먹이던 자리)을 고르면 처형 때 쓰는 그 클래스 칼을 허리에서 뽑아 오른손에 쥔다. 주먹의 모든 구조(약·강 공격, 콤보, 가드, 세키로식 패링, 밀치기, 웅크려 쓸기)는 그대로이고 동작만 칼이다: 약공격은 베기(이어 누르면 되베기와 번갈아), 강공격(충전)·리포스트·박자 연타 마무리는 찌르기, 돌려 치기·추격은 되베기. 충전하는 동안 칼을 찌르기 자세로 당긴다. 모든 공격이 칼 든 오른손으로 나가고 왼손은 가드·쳐내기·팔꿈치만 한다. 쥐는 법과 동작은 칼마다 그 칼이 들어 있던 워크숍 애드온의 1인칭 동작 그대로다(11-13 사용자: "애드온 따오면서 그 모션도 같이 있었을 거 아니야"): 나이트 흑상은 델타포스의 3단 베기(약공격 1·2타, 강공격 3타), 고스트 카람빗·시순트 프라이멀은 L4D2 칼 베기와 밀치기(강공격), 포인트 버터플라이는 CoD 버터플라이 베기·되베기·강공격, 데탕트 가위는 그 애드온의 베기·밀치기이고 꺼내기·가드도 각 애드온 것이다. 손 안의 칼 각도도 애드온에서 잰 값이다(카람빗·프라이멀·버터플라이 역수, 가위·흑상 정수). 애드온에 1인칭 동작이 없는 어썰트 M9 총검·서포트 마체테는 1차 시안(역수 칼은 찌르기가 위에서 내리찍기)을 쓴다. 옮긴 방법과 표는 [AddonPortNotes](AddonPortNotes.md)의 클래스 칼. 피해는 맨주먹(10~28) 대신 칼의 베기~찌르기 값(예: 45~110, 나이트 흑상 60~130·사거리 +0.4m, 서포트 마체테 60~110)이고 머리에는 ×2, 등 뒤(좀비가 보는 쪽으로)에는 ×2(어썰트 ×2.5, 고스트 ×3)가 곱해진다. 나이트 주먹 보너스·주먹 카드는 칼에도 곱해진다. 맞으면 피는 총알처럼 뿜고(헤드샷·처치 맥박도 총처럼), 맞은 자리에 짧고 굵은 흰·붉은 베기 줄기와 불꽃(찌르기는 X자, 머리는 조금 더 오래)이 0.1초 남는다. 소리는 칼 휘두르기·살 베기·벽 소리(SoundCues `Knife/Swing`·`Hit`·`HeavyHit`·`Wall`). 무기 슬롯 3번과 킬 피드에 그 칼의 그림과 이름이 나온다. 동작 클립은 모두 블렌더 근접 칼 작업실(`ArtSource/Knives`, 칼마다 클립이 따로 있고 저장하면 바뀐 클립만 `KnifeMelee*.csv`로 감)에서 다듬고, 칼·클립·수치는 [ClassKnives.csv](../Resources/Tables/Melee/ClassKnives.csv)에 있다. 동료 화면에서도 같은 칼·같은 동작·같은 효과가 나온다(주먹 신호를 받는 그대로).
- **처형·암살 짝 애니메이션**(11-12 사용자: "좀비 calm idle 모션 보면 몇 놈은 누워 있거나 앉아 있거나 일어서 있던데 이거 각각 맞춰서"): 달려들기가 끝나면 좀비가 하는 것에 맞는 짝(서 있음 등 뒤·앞, 앉아 졸기, 먹기, 웅크리기, 엎드림, 누움)을 골라 플레이어와 좀비가 같이 움직이는 클립을 튼다. 처형 타격은 짝의 처치 순간(0.5~0.67초)에 오고, 처형 전체 길이도 짝의 길이(1.2~1.8초)가 된다. 칼은 위의 클래스 칼이 주먹에 쥐고 팔은 짝 클립이 움직이며, 타격 뒤 칼 빼기·넣기와 총 다시 들기는 짝이 끝날 때에 맞춰 늘어난다. 고르는 규칙·자리 맞추기·표·블렌더 작업실은 [Executions.md](Executions.md).
- **리포스트**: 패링에 성공하고 0.5초(실제 시간) 안에 좌클릭하면 가드를 쥐고 있어도 바로 나간다. 패링의 멈춤과 발 묶임이 풀리고, 밀려난 적에게 한 걸음 붙어 튕겨 낸 손의 반대 손으로 스트레이트(최대 충전 주먹의 1.6배 피해, 2배 체간)를 꽂는다. 나이트의 퍼펙트 디펜스 반격이 열렸으면 그 반격이 먼저 나간다. 수치는 [Combos.csv](../Resources/Tables/Melee/Combos.csv)의 `Riposte`.
- **평캔(돌려 치기)**: 주먹 상태에서 V 밀치기, 맞힌 가드 브레이크, 적에게 닿은 돌진 밀치기, 앉아 다리 쓸기를 쓰고 0.6초 안에 좌클릭하면 기다림 없이 돌려 치기(반대편으로 감았다가 손등으로 가로질러 휘두르기, 최대 충전의 1.3배, 세 명까지)가 나가고 짧은 틱 소리가 난다. V는 제 대기를 따로 세어 주먹을 막지 않는다(리그 오브 레전드의 평 Q 평). 수치는 `Combos.csv`의 `Link`.
- **던지고 돌진**: 돌을 던지고(T) 0.9초 안에 조준점의 적(9m 안, 사이에 막힌 것 없이)이 있을 때 좌클릭하면 그 적에게 돌진해 돌려 치기를 꽂는다(세키로의 수리검→추격 베기). 조준점에 아무도 없으면 보통 주먹이고 기회는 남는다. 수치는 `Combos.csv`의 `Chase`.
- **박자 연타**: 주먹이 맞으면 조준점 둘레에 흰 고리가 조여 들어 가운데 얇은 고리와 겹치는 순간이 다음 박자다(앞 주먹이 나간 뒤 0.32초, 단계마다 빨라짐). 박자 0.1초 전부터 0.14초 뒤 사이에 누르면 충전 없이 바로 다음 타가 나가고(단계마다 빨라지고 세짐), 분홍 고리가 터지며 틱 소리가 한 음씩 오른다. 세 번째로 맞춘 누름은 마무리 타: 조준점의 적에게 크게 뻗는 스트레이트(최대 충전의 2.4배, 3배 체간)로 밀쳐 내고 0.6초 경직시키며 시간이 잠깐 멈춘다. 박자를 놓치거나 안 누르면 그냥 끝(그 누름은 보통 주먹). 리포스트·돌려 치기도 맞으면 첫 박자가 열린다(포아너 피스키퍼·센츄리온, 림버스 '찌르고 찌르기'). 수치는 `Combos.csv`의 `Beat`.
- **다리 쓸기**: 앉은 채 V를 누르면(총이든 주먹이든) 한 다리가 바닥을 따라 0.32초에 앞쪽 220°를 돌며 1.9m 안의 적을 모두 넘어뜨린다(1.6초, 거구는 비틀거리며 조금 밀리기만). 장전은 계속되고, 다리가 나가 있는 동안은 밀치기처럼 카운터 판정이 있다. 바닥에 흰 호가 남고 화면이 살짝 내려가며 기운다(친구 화면에서도 다리가 돌고 호가 보인다). 주먹이면 그 뒤 0.6초 안의 좌클릭이 돌려 치기다. 수치는 `Player.csv`의 `Shove.sweep*`.
- **총 휘두르기·발차기 동작**: 총을 든 채 V를 누르면 총 자체가 휘둘린다(11-12 사용자: "밀치기 할 때 총기 휘두르는 거나 발차기 모션이 없어서 … 네가 1차 시안 만들고 내가 그거 후보정"). 긴 총(소총·산탄총·SMG)은 화면 가운데로 20cm 내질러지며 총구가 왼쪽으로 30° 돌고 기울어 총 몸통으로 밀고, 권총은 총구를 들었다가 앞·아래로 내려찍는다. 둘 다 누른 뒤 0.067초에 맞는 자세가 되어 밀치기가 맞히는 0.2초(`Ability.shoveActiveTime`) 안에 들고, 0.4초쯤 쥔 자리로 돌아온다. 총이 쥔 자리에서 얼마나 움직이고 도는지만 담아 조준·흔들림 위에 얹으므로 어느 캐릭터·총이든 같고, 두 손은 총의 접점을 따라온다. 사격·장전·총 넣기는 휘두르기를 0.1초에 끊는다. 장전 중 V의 발차기는 다리 클립 `enf_kick_front`다(0.3초 `Shove.kickHitTime`에 가슴 높이로 발바닥이 닿고 1인칭에서도 발끝이 보인다, `Body.kick`). 친구 화면에서도 같다. 셋 다 1차 시안이라 블렌더 작업실 [ArtSource/Shove](../../../ArtSource/Shove/START_HERE.md)에서 손보고 저장(`Ctrl+S`)하면 바뀐 동작만 게임으로 돌려보낸다. 총 동작은 `Resources/Combat/GunMoves/*.csv`로 가서 Play 중에도 다음 V부터, 발차기는 `Art/Animations/Shove/enf_kick_front.fbx`로 가서 카즈사 아바타로 들어와 다음 Play부터 나온다.
- **회피·배쉬 이펙트**: Alt 스텝 회피는 사용자가 보낸 대시 VFX 영상(유튜브 쇼츠, 11-12) 그대로다(11-12 사용자: "레퍼런스 그대로 (잔상은 본인 캐릭터대로 해) 바람이나 사운드나 회피 효과나 그냥 레퍼런스처럼 멋있고 깔끔하게 하라고"). 0.8.214에서 한 박자로 줄였더니 "너무 슴슴하고 멋없어졌어"라고 해서, 영상에 있는 것을 영상의 때와 굵기대로 다시 다 넣었다: 차고 나가는 순간 발밑의 밝은 흰 폭발과 뒤로 퍼지는 연기, 시작 자리의 잔상(몸을 그 순간 그대로 구워 옷마다 제 텍스처·색으로 비쳐 보이게)과 도착하는 몸 바로 뒤의 잔영, 시작 자리 조금 뒤에서 몸까지 번개처럼 꺾인 가는 흰 선 네 가닥(정강이·무릎·엉덩이·허리), 어깨·머리 높이에서 몸 뒤로 시작 자리 너머까지 올라가며 뻗는 곧은 빛줄기 둘, 착지할 때 지나온 길의 작은 먼지와 몸을 양옆으로 감으며 올라가는 바람 셋. 0.14초 대시 동안 선과 줄기는 몸을 따라가고 착지하면 같이 사라진다. 친구 화면에서도 똑같다(1인칭에서는 내 잔상이 카메라가 빠져나간 뒤 떠오르고 눈높이 줄기·잔영·바람은 뺀다). 1인칭 화면에는 따로 그리지 않고, 옆·앞으로 피해 세계의 선이 화면 밖일 때만 같은 선을 가는 흰 획 네 개로 그린다. 소리는 구매한 BOOM Violent Combat WHOOSH Dash(원래 회피 바람)를 0.25에서 0.6으로, 옷 스침을 0.2에서 0.32로 키웠다. 앉은 채 피하면 몸이 앉은 자세 그대로 미끄러지고 이펙트도 0.6배 낮다. 공격을 실제로 피하면(스텝이든 Q/E든) 선·줄기·바람이 빨강이 되고 친구 화면에서도 빨갛다(EvadedRpc). Q/E는 대시가 없어 흘려보낸 쪽에서 몸까지 짧은 붉은 선이 난다. 그 뒤 느려지는 반격 창의 테두리는 검붉다. 밀치기·발차기·다리 쓸기·돌진 밀치기·가드 브레이크·카운터·박자 마무리 타가 맞으면 맞은 곳에 흰 섬광, 퍼지는 충격 고리, 튀는 줄기, 바닥 먼지가 0.15초쯤 보이고(기술마다 크기가 다르다) 내 화면이 살짝 흔들린다. 친구의 밀치기는 친구 눈앞에 작게 보인다.
- **원호 방어의 패링 자세**(11-12 사용자: "아군을 방어해 주는 건 특유의 패링 자세와 함께 공격을 튕겨내는 화려한 이펙트가 있었으면 좋겠음"): 나이트의 원호가 동료에게 오는 공격을 튕겨 내면, 튕겨 내는 이펙트(CoverDeflect)와 함께 나이트가 0.3초 동안 패링 자세를 잡는다. 주먹을 들고 있으면 패링에 성공했을 때처럼 먼 손이 공격 쪽으로 휙 쳐냈다 돌아오고(가드도 같이 올라간다), 총을 들고 있으면 오른손은 총을 그대로 쥔 채 왼손만 총에서 떨어져 팔뚝과 주먹으로 얼굴 앞을 막았다가 다시 총을 잡는다(던지기·장전 중이면 왼손이 바빠서 안 한다). 보이기만 하는 자세라 가드·패링 판정은 열리지 않고 입력도 그대로다. 친구 화면에도 똑같이 나오고, 1인칭에서도 내 팔이 그렇게 움직인다. 수치는 `Combos.csv`의 `GuardPose`.
- **완벽 방어**: 완벽 회피 후 `[LMB] 반격`이 뜬다. 이때 새 좌클릭은 일반 공격보다 반격을 먼저 실행한다. 패링의 반격은 `Defense.parryOpensCounter`를 켜야 열린다.
- **처치 점수**: 처치하면 크로스헤어 오른쪽 아래에 XP와 처치 사유가 뜬다. 처형으로 처치하면 `처형` 줄에 전술 단검과 베기 자국 아이콘(빨간색), 반격으로 처치하면 `반격` 줄에 되돌아오는 화살표 아이콘(반격 색)이 붙는다. 일반 처치·헤드샷 줄은 기존 해골·폭발 아이콘 그대로다.
- **시험용 적**: 채팅에 `/spawn`, 콘솔 채널에 `spawn`을 입력한다. 소환한 적은 웨이브 진행에 포함되지 않는다. 명령 목록은 `/help`로 확인한다.

### 채팅·목표판과 테스트 명령

**Enter**로 채팅을 열고 메시지나 명령을 입력한 뒤 Enter로 제출한다. 채팅 중 **Tab**은 채널 전환, **Esc**는 닫기, 위·아래 화살표는 입력 기록이다. 채팅이 닫혀 있을 때 Tab은 목표판을 연다. 채팅 중에는 이동·전투 입력을 멈춘다. 채팅 줄은 화면 왼쪽, 아군 카드 위에 줄마다 판을 깔고 아래에서 위로 쌓이며, 닫혀 있으면 가장 새 5줄이 8초 뒤 사라지고 열면 모두 돌아온다(모양과 수치: [설정·유지보수 안내](MaintenanceGuide.md)의 채팅 문단, [Chat.csv](../Resources/Tables/Core/Chat.csv)).

| 명령 | 용도 |
| --- | --- |
| `/help` | 현재 등록된 명령과 인자 확인 |
| `/map 씬이름` | 빌드에 등록된 씬으로 이동. 예: `/map MovementAbilityPlayground` |
| `/spawn`, `/spawn 5` | 생략하면 1마리, 수량을 붙이면 해당 수만큼 테스트 적 소환. 빈 공간이 부족하면 실제 소환 수를 알림. 웨이브 집계 제외 |
| `/poses sight`, `/poses noise`, `/poses shot 2`, `/poses stone`, `/poses` | 조용한 자세(앉기·먹기·웅크리기·엎드리기)마다 좀비를 하나씩 앞에 반원으로 소환하고 2.5초 뒤 깨운다: 눈(플레이어를 보고 있음)·큰 소리 두 번·1 피해 총알(번호를 주면 그 하나만, 아니면 첫 하나가 나머지를 부름)·뒤에 떨어진 돌(가장 가까운 하나가 수색)·깨우지 않음. 모두 제자리에서 일어선 다음에 움직여야 한다. 자세는 호스트 화면만 정해진다. `runner` 같은 종류를 붙일 수 있다 |
| `/dummy`, `/dummy 3`, `/dummy clear` | 네트워크 없이 멀티 구조를 시험할 더미 플레이어 소환·제거 |
| `/ammo` | 보유한 모든 총기의 탄창을 채우고 기본 예비탄까지 보충. 기본보다 많은 예비탄은 유지 |
| `/wave start` | 대기 중인 웨이브 시작 |
| `/wave stop`, `/wave resume` | 웨이브 일시정지 / 재개 |
| `/wave abort`, `/wave status` | 웨이브 종료 / 상태 확인 |
| `/stats`, `/heal`, `/revive` | 플레이어 상태 확인 / HP 회복 / 사망 복구·실드 초기화 |
| `/death`, `/death wipe`, `/death exploder short` | 죽지 않고 데스 스크린만 미리 보기(DeathScreen.csv `previewSeconds`). 좀비 종류·`fall`·`bleed`·`friend`로 이유를, `wipe`로 전멸을, `short`·`full`로 길이를 고른다. 설정이 끔이어도 보인다 ([DeathScreen.md](DeathScreen.md)) |
| `/hurt 20`, `/shield 50`, `/maxhp 200` | 피격·실드·최대 HP와 HUD 확인 |
| `/hit 10 left`, `/hit 10 back` | 방향성 플레이어 피격·혈흔 비네트·쉐이크 시험. front/right/back/left 지원 |
| `/counter 0` ~ `/counter 3` | COUNTER 연출만 그 단계로 미리 보기. 실제로는 연속 카운터마다 한 단계씩 오른다 |
| `/alert danger`, `/alert team`, `/alert self` | 화면 위 알림 띠 미리 보기. danger는 오른쪽, team은 왼쪽 25m에 가상의 자리를 두고 self는 위에서 내려온다 |
| `/chattest`, `/chattest broadcast` | 채팅 줄 모양 미리 보기. 비우거나 `mixed`면 대사·방송(시스템 알림)·팀·전체 줄을 한꺼번에, `dialogue`·`broadcast`·`team`·`all`이면 하나씩 |
| `/ult 100`, `/ultuse` | 궁극기 게이지 충전·자원 소비 테스트. 무기 진화나 스킬은 실행하지 않음 |
| `/ultmax 200`, `/ultcost 70` | 궁극기 최대 보유량을 200%, 1회 소비량을 70%로 각각 지정. 증가량이 아닌 지정한 값으로 설정 |

위 명령은 실행 중인 테스트 상태를 바꾼다. 영구적인 밸런스 설정은 [설정·유지보수 안내](MaintenanceGuide.md)의 표와 Inspector 위치에서 수정한다.

## 무엇을 바꿀지에 따른 수정 위치

숫자는 **[밸런스 표](MaintenanceGuide.md#밸런스-표-data-tables)가 가장 우선**한다. 표는 저장하면 Play 중에도 바로 반영된다. 표에 없는 값은 코드의 기본값보다 씬·프리팹·설정 asset에 저장된 값이 우선하므로, Play를 종료한 뒤 수정한다.

| 바꾸려는 것 | 수정 위치 |
| --- | --- |
| 피해량, 발사 방식, RPM, 탄창·예비탄, 장전 시간, 탄퍼짐, ADS 배율, 몸의 손이 잡는 위치 | [Weapons.csv](../Resources/Tables/Weapons/Weapons.csv)의 해당 무기 행 |
| 조준 반동, 총 모델 추적·high-ready 반응, 무기 사운드·아이콘 | [무기 데이터 폴더](../Data/Combat/Weapons) → 해당 `WeaponData`의 Recoil Feel / Aim Tracking Feel / High-ready 항목 |
| 무기별 총성, 화면 킥, 총구·탄피, 총알 체간 피해 | `WeaponData`의 Feel 및 Feel → Target Reaction 항목 |
| 전체 타격감 켜기·끄기, 히트마커, 점수, 피격·처치 효과 | [GunFeel.asset](../Resources/Combat/GunFeel.asset) (히트마커 '깔끔' 모양의 수치는 [HitMarkers.csv](../Resources/Tables/Hud/HitMarkers.csv), 모양 선택은 설정 → HUD → 히트 마커) |
| 플레이어 피격 아크·혈흔·쉐이크·25% 빈사 박동 | [PlayerDamageFeedback.asset](../Resources/Combat/PlayerDamageFeedback.asset), [피격 효과 안내](PlayerDamageFeedbackGuide.md) |
| 방어·밀치기 소리, 반격, 흐트러짐 표시, 피해 팝업·HP 손실 표시 | [Feedback.asset](../Resources/Combat/Feedback.asset) |
| 시작 무기와 시작 슬롯 | 플레이어의 `PlayerWeaponSlots` → Slot 1~4 / Starting Slot |
| 주먹·가드·패링·V 홀드 | [Player.csv](../Resources/Tables/Player/Player.csv)의 `Fists` 행 |
| 주먹 콤보(리포스트·평캔 돌려 치기·던지고 돌진·박자 연타) | [Combos.csv](../Resources/Tables/Melee/Combos.csv) |
| 밀치기·돌진 밀치기·슬라이딩 수치 | `Player.csv`의 `Ability` 행 |
| 권총 근접 자세(존 윅 자세): 적이 가까이 오거나 총구가 막히면 권총을 가슴 쪽으로 당겨 눕혀 쥐고 그대로 쏘는 거리·각도·빠르기, 켜고 끄기, 뺄 권총 | [CloseHold.csv](../Resources/Tables/Weapons/CloseHold.csv) |
| 쏘아 넘기는 낮은 엄폐물(벽 규칙이 high ready로 들지 않음)인 맵 꾸미기 종류 | [ShootOverCover.csv](../Resources/Tables/Weapons/ShootOverCover.csv)(바꾸면 장을 다시 짓기. 서포트 바리케이드는 코드가 직접 표시) |
| 총 휘두르기·발차기 동작 | 블렌더 작업실 [ArtSource/Shove](../../../ArtSource/Shove/START_HERE.md)(`START_SHOVE_ANIMATION.cmd`, 열면 바로 손볼 수 있게 준비됨)에서 손보고 `Ctrl+S`(바뀐 동작만 보낸다) |
| 적 체력·공격·체간·이동 속도 | [Enemies.csv](../Resources/Tables/Enemies/Enemies.csv) |
| 적의 몸 크기·밀치기 연쇄 | [적 프리팹](../Resources/Enemies)의 `MeleeCombatTarget` |
| 사격·장전·가드 키 | [Player.inputactions](../Input/Player.inputactions)의 Combat 맵 |
| 슬롯 숫자 키 | Player.inputactions의 Combat 맵 `Slot1`~`Slot4`(주 키 숫자열, 보조 키 숫자패드). 플레이어는 설정 → 조작 → 키 설정에서 바꾼다 |
| 탄약 UI의 위치·크기·색 | 씬의 `AmmoHUD` → Screen Margin / Hud Scale / Colors |

슬롯 키는 Input Actions의 `Slot1`~`Slot4` 액션이다. [PlayerWeaponSlots](../Scripts/Weapons/PlayerWeaponSlots.cs)는 `KeyBindings.Pressed`로 읽으므로 에셋의 바인딩과 키 설정 창에서 바꾼 키가 그대로 먹힌다. 사격 모드(B)·총 꾸미기(C)·살펴보기(Y)·라이트(L)도 같다.

## 새 총 데이터 연결하기

1. [무기 데이터 폴더](../Data/Combat/Weapons)에서 기존 asset을 복제하거나 **Create → ENFILADE → Combat → Weapon Data**를 선택한다. `Category`는 **Ranged**로 둔다.
2. [Weapons.csv](../Resources/Tables/Weapons/Weapons.csv)에 asset과 같은 이름의 행을 추가하고 `displayName`, `fireMode`, `damage`, `roundsPerMinute`, `magazineSize`를 적는다.
3. `initialReserveAmmo`, `reloadDuration`, `emptyReloadDuration`을 조절한다. RPM 600은 초당 10발이며 시간 단위는 초다. 몸의 손이 잡을 위치(`rightGrip*`, `leftGrip*`, `holdOffset`)도 모델에 맞춘다.
4. 현재 CombatFoundation에서는 **PlayerWeaponSlots의 Slot 1 또는 Slot 2**에 할당한다. Starting Slot은 0이면 해제, 1~4이면 해당 슬롯으로 시작한다.
5. 모델을 바꾸면 `WeaponController`의 Weapon Root / Muzzle Point를 확인한다. 실제 총구의 로컬 **+Z**가 발사 방향이며 Sight Height는 모델별로 조절한다.
6. 단발·점사·연사, 장전 취소, 슬롯 전환 후 탄약 보존, 벽 앞 사격 차단을 확인한다.

`WeaponController`의 Weapon Data를 직접 할당하는 방식은 슬롯 관리자가 없는 단일 무기 테스트용이다. 슬롯 관리자가 있으면 선택한 슬롯의 데이터와 런타임이 컨트롤러에 연결된다. 새 근접·특수 무기는 Category만 바꿔 완성되지 않으며, 유형별 공격 구현과 모델 연결이 추가로 필요하다.

현재 예제 무기는 다음과 같다. 값은 `Weapons.csv` 기준이다.

| 데이터 | 방식 | HP 피해 | RPM | 탄창 / 예비탄 | 잔탄 있는 장전 / 빈 탄창 장전 |
| --- | --- | ---: | ---: | ---: | ---: |
| [MX16A4](../Data/Combat/Weapons/MX16A4.asset) | 연사 | 20 | 600 | 30 / 90 | 2.667초 / 3.167초 |
| [M1911](../Data/Combat/Weapons/M1911.asset) | 단발 | 35 | 300 | 12 / 48 | 2.5초 / 2.7초 |

손에 드는 총 모델은 KINEMATION의 FPS Animation Pack 총(MX16A4, M1911)이고, 몸이 그 총을 드는 것은 KINEMATION FPS Animation Framework(FPSAF)다. [FPSBody](../Scripts/Weapons/FPSBody/FPSBody.cs)가 몸에 FPSAF 데모의 휴머노이드 플레이어(FPSHumanoidPlayer)와 같은 구성을 올린다. FPSAnimator와 그 부품(FPSAF의 PlayablesController를 고치지 않고 그대로 쓴다), 리그(데모의 리그 순서대로 몸의 뼈를 늘어놓는다, [FPSBodyRig](../Scripts/Weapons/FPSBody/FPSBodyRig.cs)), 총마다 데모의 휴머노이드 라이플·권총 프로필([Data/Combat/FPSBody](../Data/Combat/FPSBody))이다. 몸의 클립은 Animator 그래프의 첫 출력으로 돌고, Animator는 FPSAF가 읽는 파라미터만 가진 빈 컨트롤러(BodyHost)를 받는다. FPSAF가 매 프레임 상체를 잡는다. 총의 자세 클립을 팔에, 시선을 따라 굽는 허리와 머리(Look), 기울이기, 정조준(Ads, 조준경을 눈의 카메라 소켓에 맞춘다), 흔들림(Sway), 반동, 두 손을 총에(IK)다. 정조준이 끝나면 조준선이 눈(카메라)을 정확히 지난다. KINEMATION의 Ads는 프로필의 positionBlend·rotationBlend(권총 프로필 0.21~0.25, 소총 0.38~0.41)만큼 총을 '프로필이 연결될 때 찍어 둔 총과 눈 사이'에 남기는데, 그때 찍히는 것은 앞에 들던 것의 마지막 프레임(그때의 시선 각도까지)이고 Ads는 시선(Look)보다 먼저 돌아서, 총을 꺼낼 때마다 조준선이 눈에서 1~5cm씩 다른 곳에 섰다. 그래서 총마다 만드는 Ads 복사본은 둘을 0으로 둔다(2026-11-12, 아래 조준경 문단). 총 클립은 KINEMATION 팔 뼈대(Generic)용 원본을 KINEMATION Retarget Pro가 스킨마다 몸에 맞는 휴머노이드 클립으로 다시 만든다([GunClipRetarget](../Editor/Guns/GunClipRetarget.cs)). Retarget Pro의 FPS 기능이 두 손을 KINEMATION 팔이 쥐는 그대로 총에 두고, 총은 KINEMATION 카메라가 보는 자리를 몸의 눈 기준으로 옮겨 놓는다. 결과와 그 자세에서 총이 놓인 자리는 [GunBakes](../Resources/Combat/GunBakes.asset)에 모이고, FPSAF의 Pose Sampler가 그 자리에서 총을 시작한다. 편집기가 열릴 때 새 스킨이나 바뀐 클립만 자동으로 굽고, Tools → ENFILADE → Combat → Retarget KINEMATION Gun Clips로 모두 다시 굽는다. 장전 때 카메라는 원본 클립의 카메라 뼈대로 흔들린다([FPSBodyCamera](../Scripts/Weapons/FPSBody/FPSBodyCamera.cs)). 벽에 막힌 총은 FPSAF Collision 레이어의 막힘 자세 그대로이되, 그 레이어의 광선 대신 우리 벽 규칙이 켠다(GunCoverLayer, FPSAF의 광선은 트리거와 제 몸에도 맞는다). 막힌 총은 위로 드는 자세 하나만 쓴다. 아래를 볼 때 바뀌던 두 번째 자세(소총은 왼쪽으로 돌리기, 권총은 아래로 내리기)는 프로필의 GunCoverLayer `useSecondaryPose`로 껐다(2026-11-09 사용자 요청). 무기마다 무엇을 쓰는지는 [FPSBody.asset](../Resources/Combat/FPSBody.asset)에, 조정값은 [FPSBody.csv](../Resources/Tables/Weapons/FPSBody.csv)에 있다. 장전 시간은 KINEMATION 장전 클립의 길이이고, 정조준 시야각 60°는 KINEMATION 총 설정의 `aimFov`다. 정조준에 들어갈 때는 KINEMATION의 조준 모션(`aimingMotion`, 총이 올라오며 출렁인다)을 틀지 않고 내릴 때만 튼다. 4배·8배 조준경에서는 그 출렁임이 들여다보는 순간 그림을 흔들었다(2026-11-10).

FPS Animation Ultimate의 다른 총 13자루도 같은 길로 든다(2026-11-13, 사용자: "KINEMATION에서 무기들 좀 더 임포트하면 안 됨? 다 가져오고 싶은데, 총이 너무 적어 지금."): AKX-200, AS Val, G3, SVD, MGX5, MPS5, PDW-90, Striker-V, Drake-12, X18, DGL-50, Kolibri, Viper .357. [PackGunSetup](../Editor/Guns/PackGunSetup.cs)이 편집기가 스크립트를 읽은 뒤 저절로(Tools → ENFILADE → Combat → Make FPS Animation Pack Guns로 다시) 총마다 WeaponData를 만들고(가장 비슷한 우리 총의 사본이라 그 총의 소리 프로필과 느낌으로 시작하고, 발사음은 팩의 녹음), FPSBody에 CAS 통합 프리팹·MX16A4나 M1911의 FPSAF 에셋(클립에 맞춘 곡선은 뺌)·팩의 자세와 장전 클립을 넣는다. 그다음 TacticalGunSetup이 MX16A4·M1911처럼 Tactical Shooter Pack 부품을 단다(조준경·소음기·손잡이·라이트, 처음에는 KINEMATION이 만든 그대로이고 C 메뉴에서 고른다. 자체 소음기가 있는 AS Val·PDW-90에는 소음기가 없다). 수치는 [Weapons.csv](../Resources/Tables/Weapons/Weapons.csv)(장전 시간은 팩 클립 길이), 가격은 [Shop.csv](../Resources/Tables/Economy/Shop.csv), 클래스 총은 [Classes.csv](../Resources/Tables/Classes/Classes.csv)다. RPG(로켓)와 Ultimate의 Mk14 EBR(Tactical 것이 있음)은 파일만 있다. 무기 아이콘은 `Tools/Blender/weapon_icons.py`를 다시 돌리면 생긴다. PDW-90의 투명 탄창은 팩이 데모 스크립트 Pdw90Animation으로 움직인다(총알 띠 50발이 탄창이 빌수록 밀려가고, 빠진 탄은 장전 때 탄창을 따라가며, 끝 무렵 마지막 탄이 사라지고 급탄판과 스프링이 곡선대로 눌린다). 이 스크립트는 팩의 무기 스크립트와 총 Animator의 그래프를 찾는데 우리 총에는 둘 다 없어서(Animator에 컨트롤러가 없고 부품은 CAS로 돈다, 팩 무기 스크립트는 FPSBody가 뗀다) 매 프레임 오류가 났다(2026-11-13 Editor.log 384번). 이제 FPSBody가 총을 만들 때 그 설정(띠 클립·마스크·뼈·곡선)을 읽고 그 자리에 [PackMagazineRounds](../Scripts/Weapons/FPSBody/PackMagazineRounds.cs)를 단다(0.8.309, 팩 원본은 그대로). 띠 클립을 총의 숨은 복사본에서 마스크의 뼈만 한 번 샘플해 두고, 들고 있는 총의 탄창이 빈 만큼(WeaponRuntime) 그 자세를 재생한다. 다른 팩 총에는 이런 데모 스크립트가 없다(FPSMachineGun·FPSManualWeapon은 팩 데모 프리팹에만 있고 FPSWeapon은 원래 뗀다).

쏠 때마다 손으로 노리쇠를 당기는 Kar98k·L96X와 펌프를 당기는 KXG12도 같은 날 들어왔다(사용자: "KINEMATION에서 무기들 좀 더 임포트하면 안 됨? 다 가져오고 싶은데, 총이 너무 적어 지금."). FPSBodyAssets의 `sourceFire`·`fire`에 KINEMATION의 쏘기 클립(A_FP_Kar98K_Fire 1.5초, A_FP_L96X_Fire 1.67초, A_FP_KXG12_Pump 0.83초)을 두면 GunClipRetarget이 장전 클립처럼 몸에 맞게 굽고(GunBakes의 `fire`), 쏠 때마다 [FPSGun](../Scripts/Weapons/FPSBody/FPSGun.cs)의 `Cycle`이 몸에는 그 클립, 총에는 KINEMATION의 쏘기 부품 클립(노리쇠·펌프), 카메라에는 원본 클립의 카메라 뼈대를 같은 빠르기로 틀고, 총과 두 손은 장전 때처럼 원본 클립이 둔 자리를 따른다(FPSBody.Contacts, `Rig.reloadContacts`). 다음 발은 규칙이 막는다(사용자: "근본적으로 리팩토링을 해서 고치면 되잖아."). 총을 몸에 붙일 때 KINEMATION 원본 클립을 숨은 원본 팔에 틀어 그 동작에서 두 손바닥이 그냥 쥐기 자리(1.5cm 안)로 돌아와 머무는 때를 한 번 재고(FPSBody.MeasureCycle: Kar98k·L96X 1.27초, KXG12 0.5초), [WeaponCycle](../Scripts/Weapons/Core/WeaponCycle.cs)이 그 시간을 들고 [WeaponRuntime](../Scripts/Weapons/Core/WeaponRuntime.cs)이 [Weapons.csv](../Resources/Tables/Weapons/Weapons.csv)의 분당 발사 수와 그 시간 중 느린 쪽으로 쏜다. 같은 클립을 같은 팔에 트니 모든 PC에서 같은 값이고, 클립은 늘 제 빠르기로 돌아 끊기거나 빨라지지 않는다. 손을 떼지 않는 클립(Ultimate의 A_FP_X18_Fire 같은 반동 클립)은 손이 4cm 넘게 떨어지지 않아 막을 때가 0이다. 손으로 정하려면 FPSBodyAssets의 `fireReady`(초). 세 총의 분당 발사 수(Kar98k·L96X 50, KXG12 130)는 동작보다 조금 빠르게 둬서 동작이 정하고, 몸이 아직 재지 않았을 때의 바닥이 된다. 총 꾸미기 메뉴의 분당 발사 수도 동작을 따른다. 쏜 뒤 몸 동작은 총마다 칸으로 켠다(기본 꺼짐): FPSBodyAssets.Gun의 `sourceFire`·`fire`, 만드는 도구에서는 PackGunSetup·TacticalGunSetup Spec의 `fire`. 그래서 다른 팩의 볼트·펌프·레버 총도 클립 이름만 채우면 된다. Ultimate의 다른 총에도 쏘기 몸 클립(A_FP_X18_Fire 등)이 있지만 반동이라 켜지 않았다. 정조준 중에는 클립이 총을 움직이는 몫과 그 카메라 흔들림이 [FPSBody.csv](../Resources/Tables/Weapons/FPSBody.csv) `Rig.aimedCycleMove`(0.25)로 줄어, 조준선이 눈앞에 머문 채 손만 노리쇠나 펌프를 당긴다. 이런 총을 아직 끝까지 조준해 본 적이 없으면 조준경의 눈 조준점을 눈에 둔 자리를 조준 자세로 삼는다(`FPSGun.HoldFromEyes`). 소리는 KINEMATION의 쏘기 부품 클립에 박힌 소리 이벤트 시각에 그 총 설정의 소리를 WeaponFeedback으로 낸다(Kar98k 노리쇠 열기 0초·닫기 0.33초, L96X 노리쇠 녹음, KXG12 펌프). WeaponData의 `cycleSounds`(동작 하나의 통째 녹음)를 채우면 이벤트 대신 그것을 동작이 시작할 때 튼다(TacticalGunSetup은 그 목록을 비우므로 다른 팩의 총은 이쪽). Kar98k와 KXG12는 KINEMATION의 CasFPSManualWeapon처럼 한 발씩 장전한다(시작 → 한 발마다 → 끝, 소리도 각 클립의 녹음, 총 부품 클립은 FPSBodyAssets의 `partsReloadTactical`·`partsReloadEmpty`·`partsReloadShell`·`partsReloadEnd`). Kar98k의 시작은 노리쇠만 열어서 빈 상태에서도 다섯 발을 모두 한 발씩 넣는다(Weapons.csv `emptyStartLoads` false. 다섯 발 클립을 한 번에 밀어 넣는 Reload_Empty는 KINEMATION의 플레이어도 쓰지 않아 파일로 둔다). KXG12의 빈 상태 시작(Pump_Empty)은 배출구로 약실에 한 발을 넣고 튜브를 채운다. L96X는 탄창 장전이고, KINEMATION이 장전 클립마다 녹음한 통째 소리(Reload_Wet_Full·Reload_Dry_Full)를 장전 시작에 튼다(탄창 총에서 WeaponData의 `reloadStartSounds`·`reloadStartEmptySounds`는 그 총의 통째 장전 소리). 고스트 클래스 총에 Kar98k·L96X, 포인트에 KXG12를 더했다.

조준경은 C 메뉴에서 고른 하나만 보이고, 총에 달린 쇠 가늠자는 쇠 가늠자로 조준할 때만 선다([GunSights](../Scripts/Weapons/FPSBody/GunSights.cs)). MX16A4처럼 가늠자·가늠쇠가 몸통 메시에 붙어 있는 총은 [TacticalGunSetup](../Editor/Guns/TacticalGunSetup.cs)이 총을 만들 때 그 조각(조준선에서 3.25cm 안쪽까지 올라오는 메시 조각: 운반 손잡이와 가늠자, 가늠쇠 탑)의 삼각형 번호를 적어 두고, 총이 깨어날 때 그 삼각형을 따로 잘라 내 광학 조준경을 달면 접는다. 광학 조준경은 그 눈 조준점이 쇠 가늠자의 조준점과 만나는 자리, 총몸 위에 붙는다(전에는 운반 손잡이 뒤 개머리 위에 붙어 가늠자와 가늠쇠 너머로 보였다). 4배·8배 렌즈는 따로 카메라로 그린다([GunScope](../Scripts/Weapons/FPSBody/GunScope.cs)). 총이 세상과 같은 Default 레이어라 레이어로 빼면 벽과 바닥까지 빠져 하늘만 보였으므로, 그 카메라가 그리는 동안만 내 몸과 총을 끈다. 그 카메라는 총이 아니라 내 카메라가 보는 쪽을 보므로 총이 흔들려도 확대된 그림은 조준점에 머문다(2026-11-10). 그 그림은 조준경의 확대된 시야로 바로 찍는다. 팩은 조준경 전체 시야(captureFov)로 찍은 그림의 가운데를 렌즈 셰이더가 배율만큼 늘려 보였는데, 4배는 그린 것의 16분의 1, 6배는 36분의 1만 보여 텍셀 하나가 화면 2~5픽셀이 되고 아무것도 부드럽게 하지 않아 조준경 속 모서리와 반짝임이 총이 흔들릴 때마다 계단지며 지글거렸다(2026-11-12 "줌 했을 때 텍스쳐 지글 거리는 거"). 이제 렌즈 셰이더는 늘리지 않고(_Magnification 1), 그림 텍스처는 화면 높이의 0.8배(가장 가까운 2의 거듭제곱, 1080p·1440p 1024, 4K 2048)라 렌즈(조준할 때 화면 높이의 0.3~0.45)의 두 배쯤이며, 그릴 때마다 밉맵을 만들어 트라이리니어로 줄여 보인다([Scopes.csv](../Resources/Tables/Weapons/Scopes.csv)). 배율에 따라 조준선이 커지는 렌즈(_UseReticleZoom)는 셰이더가 늘려야 하므로 예전처럼 넓게 찍는다(지금 총에는 없다). 레드닷과 홀로 조준경은 KINEMATION의 Reflex Scope 셰이더가 점을 판 뒤 깊이(`_ReticleDepth`, 판 짧은 변의 배수)에 그린다. 그래서 눈이 조준선에서 벗어나면 점이 창 안에서 그만큼 비켜 가고, 점이 창 안에 남는 눈의 폭이 정해져 있다. R08·M1911의 미니 레드닷(판 2.2×1.7cm, 눈에서 15cm, 깊이 −7)은 위아래 1.5cm·옆 2cm, 홀로(판 3.4×2.4cm, 깊이 −1.5)는 위아래 5cm·옆 7cm다. 모든 총의 점 판은 제 눈 조준점의 축 위에 있다. 그런데 위 문단의 Ads 블렌드 탓에 조준선이 눈에서 1~5cm 어긋나 있어서, 미니 레드닷은 0.8.51에 창을 뚫은 뒤에도 점이 자주 창 밖으로 나갔다. 예를 들어 MKR9을 들다 R08로 바꾸면 조준선이 눈 위 2.2cm였다(2026-11-12 "R08 리볼버 조준경 줌 했을 때 안 보임. 도트 사이트 고장난 듯"). 홀로는 폭이 넓어 점은 보였지만 화면 가운데에서 비켜 설 수 있었다. 이제 조준선이 눈을 지나므로 모든 조준기의 점과 가늠쇠가 화면 가운데에 선다.

## 다른 씬에 연결하기

게임은 [Scenes/MainMenu](../Scenes/MainMenu.unity)에서 시작한다. 맵 씬에서 Play를 눌러도 메뉴 없이 바로 시험할 수 있다. 새 맵은 빌드 설정에 추가하고 [Maps.csv](../Resources/Tables/Maps/Maps.csv)에 한 줄을 넣으면 메뉴에 나온다.

새 씬에는 플레이어를 놓지 않는다. 시작 위치에 `PlayerSpawnPoint` 컴포넌트를 가진 오브젝트를 두고 `Prefabs/UI/HUD/GameHUD` 프리팹을 넣는다. EventSystem도 필요하다. 전투·체력·궁극기 컴포넌트는 Play 시작 때 `PlayerMovement`가 붙이고 수치는 [밸런스 표](MaintenanceGuide.md#밸런스-표-data-tables)에서 가져오므로, 플레이어 프리팹에 직접 둘 컴포넌트는 이동·시점·입력·무기·슬롯뿐이다.

`WeaponController`의 선택 참조는 비워두면 기존 계층에서 찾는다. Input Source는 같은 오브젝트의 `IWeaponInput`, Aim Origin은 플레이어 카메라, Muzzle Point는 하위의 `MuzzleEffects` 위치를 쓴다. Weapon Root(총 모델)는 직접 할당하며, 장비가 없거나 근접일 때 이 오브젝트를 숨긴다. Hit Layers에는 적과 벽을 모두 포함해야 한다.

## 문제를 찾는 순서

| 증상 | 먼저 확인할 것 |
| --- | --- |
| 공격이 전혀 안 됨 | Console 오류 → 슬롯 선택 → 입력·플레이어 생존 상태 → Game 창 포커스와 마우스 잠금 → 채팅·처형 등 행동 잠금 |
| 총이 안 나감 | 실제 선택 슬롯의 데이터 → 장탄·장전 상태 → 총구 high-ready 여부 → Combat/Fire 입력 |
| 벽 근처에서 총이 올라감 | 정상적인 총구 안전 장치인지 확인. 모델의 총구 위치·+Z 방향과 Hit Layers 검사 |
| 발사했지만 HP가 안 줄어듦 | `Log Shots`의 hit / result 확인. 엄폐, 사거리, 수신자, 회피, 사망 상태 순서로 추적 |
| 재장전이 안 됨 | 탄창이 이미 가득 찼는지, 예비탄이 있는지, 회피·밀치기·슬롯 전환이 장전을 취소했는지 확인 |
| 설정을 바꿨는데 반영되지 않음 | 표에 같은 값이 있으면 표가 이긴다. 표에 없다면 수정한 asset이 실제 슬롯에 연결됐는지, 씬·프리팹에 다른 값이 저장됐는지 확인 |
| 효과만 약하거나 없음 | GunFeel의 Master Switches와 해당 무기의 Feel 설정, Feedback의 음량·참조 확인 |

`Log Shots`와 `Draw Rays`는 `WeaponController`의 진단 옵션이다. 빨간 레이는 고체에 명중했다는 뜻이며 피해 성공을 보장하지 않는다. 피해 결과의 `Evaded`는 회피, `Duplicate`는 같은 공격 ID의 중복 수신이다.

변경 후 확인할 항목은 [검증 안내](ValidationGuide.md)를 따른다. 조작감·모델 자세·화면 가독성은 실제 씬에서 확인한다.
