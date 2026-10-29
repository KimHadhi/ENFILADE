# 설정·유지보수 안내

[프로젝트 안내](../../../README.md) · [전투 제작](CombatFoundationGuide.md) · [검증 안내](ValidationGuide.md)

## 밸런스 표 (Data Tables)

전투 밸런스 숫자는 [Resources/Tables](../Resources/Tables)의 CSV 표에 있다. **표를 저장하면 컴파일 없이 바로 적용되고, Play 중이면 실행 중인 게임에도 즉시 반영된다.** Excel, Google Sheets(파일 → 가져오기/CSV로 다운로드), VS Code 등 CSV를 여는 프로그램이면 무엇이든 된다.

| 표 | 내용 | 행(row) |
| --- | --- | --- |
| [Player.csv](../Resources/Tables/Player.csv) | 자세별 캡슐·눈높이, 이동·점프·슬라이드·다이브, 회피·파쿠르·낙하·밀치기, 최대 체력, 궁극기 보유량·소비량·충전량, 주먹·패링·가드 브레이크, 처형, 몸 애니메이션, 발소리·동작 소리·숨소리 | `Posture` `Move` `Ability` `Vitals` `Stats` `Ultimate` `Fists` `Execution` `Body` `Hands` `Sounds` `Footsteps` `MovementAudio` `Breathing` |
| [SoundCues.csv](../Resources/Tables/SoundCues.csv) | 동작마다 나는 소리 묶음(볼트·착지·발소리…)과 적에게 들리는 거리 | 큐 이름 (`Vault`, `Land`, `StepRun` …) |
| [Weapons.csv](../Resources/Tables/Weapons.csv) | 총 피해·RPM·장탄·예비탄·장전·탄퍼짐·헤드샷·ADS, 몸의 손이 잡는 위치 | `WeaponData` 에셋 이름 (`FoundationRifle` …) |
| [Enemies.csv](../Resources/Tables/Enemies.csv) | 적 체력·공격·자세(체간)·이동 속도 | 적 프리팹 이름 (`Enemy_Test`) |
| [Waves.csv](../Resources/Tables/Waves.csv) | 웨이브 적 수·증가량·대기·등장 간격 | `WaveManager`의 `Table Id` (기본 `Default`) |

- **열 이름 = 인스펙터 필드 이름**이다(인스펙터 표시 이름이 `Attack Range`면 열은 `attackRange`). 표에 열을 추가하면 그 필드도 표가 관리한다. 적 표의 `speed`처럼 `NavMeshAgent`의 공개 속성도 쓸 수 있다.
- 표에 있는 칸이 인스펙터 값보다 우선한다. **빈칸이거나 열이 없으면 인스펙터 값**을 그대로 쓴다. 표에 행이 없는 오브젝트(테스트 표적 등)는 영향을 받지 않는다.
- `#`로 시작하는 행·열은 설명용이라 무시된다. `Player.csv`는 `key,value,note` 형식이고 key는 `행.필드`다.
- 목록과 벡터는 `;`로 구분한다: 패링 단계 `0.35;0.2;0.1;0.05`, 주먹 피해 `10;28`(최소;최대 충전). 소수점은 `.`을 쓴다.
- Excel에서는 **CSV UTF-8**로 저장해야 한글 설명이 깨지지 않는다.
- 열 이름 오타나 읽을 수 없는 값은 Console에 `[Tables]` 경고로 한 번만 표시된다. Play 중 변경이 바로 안 보이면 `Tools → Data Tables → Reload Tables`를 누르거나 Preferences → Asset Pipeline → Auto Refresh가 켜져 있는지 확인한다.
- 플레이 중 최대 체력·자세를 바꾸면 살아 있는 적과 플레이어는 현재 비율을 유지한다.

새 무기는 `WeaponData` 에셋을 만들고 `Weapons.csv`에 같은 이름의 행을 추가한다. 새 적 프리팹도 같은 방식이며, 이름과 다른 행을 쓰려면 `EnemyHealth → Table Id`를 적는다.

## 그 밖의 수치를 바꾸고 저장하기

표에 없는 값은 **Play를 종료한 뒤 실제 사용 중인 씬·프리팹·데이터를 수정한다.** C# 필드에 적힌 초기값은 새 컴포넌트의 시작값이다. 이미 저장된 씬·프리팹의 수치를 덮어쓰지 않는다.

| 바꿀 내용 | 실제 편집 위치 | 적용 범위 |
| --- | --- | --- |
| 바닥으로 인정할 레이어 | 씬의 `PlayerMovement → Ground Layers`, `Ability Settings → Interaction Layers` | 해당 플레이어 |
| 무기 반동·조준 추적·화면 흔들림·사운드·아이콘 | [Data/Combat/Weapons](../Data/Combat/Weapons)의 해당 `WeaponData` | 그 데이터를 사용하는 모든 무기 |
| 시작 무기·선택 슬롯 | 플레이어의 `PlayerWeaponSlots` | 해당 플레이어 |
| 적 크기·밀치기 연쇄 | [적 프리팹](../Prefabs/Enemies)의 `MeleeCombatTarget` | 프리팹 인스턴스. 씬의 Override 확인 |
| 총성·히트마커·피격·처치·점수 효과 | [GunFeel.asset](../Resources/Combat/GunFeel.asset), 무기별 `Feel` | 공통 효과 / 개별 무기 |
| 주먹·벽 타격음·방어·반격·처형·피해 팝업 | [Feedback.asset](../Resources/Combat/Feedback.asset) | 공통 전투 피드백 |
| 총구 화염·궤적·탄착·혈흔 프리팹 | Player의 `Gun/…/MuzzlePoint → MuzzleEffects`, `Weapon Impact Effects` | 해당 플레이어 |
| 이동·공격·UI 입력 | [Player.inputactions](../Input/Player.inputactions) | 입력 액션 사용 기능 |
| 슬롯 1~4 입력 | [PlayerWeaponSlots](../Scripts/Player/Combat/Weapons/PlayerWeaponSlots.cs)의 직접 키보드 처리 | 숫자열·숫자패드 슬롯 선택 |

1. 사용할 씬을 열고 실제 연결된 컴포넌트·데이터를 선택한다.
2. 값 하나 또는 관련된 작은 묶음을 수정한다. 씬 인스턴스의 Override와 프리팹 원본을 구분한다.
3. 씬과 프로젝트 에셋을 저장한다. 데이터 에셋은 Play 중에도 변경이 남을 수 있으므로 Play를 종료한 상태에서 편집한다.
4. 다시 Play해 효과를 확인하고 [해당 검사](ValidationGuide.md)를 실행한다.

예제 무기의 비교표는 [전투 제작 안내](CombatFoundationGuide.md)에 있다. 장탄·예비탄의 현재 값은 `WeaponRuntime`이 관리하며 공유 데이터에 저장하지 않는다.

플레이어의 `PlayerDamageReceiver`, `PlayerStats`, `UltimateCharge`, `PlayerMeleeCombat`, `PlayerExecution`은 씬에 두지 않고 `PlayerMovement`·`PlayerWeaponSlots`가 Play 시작 때 붙인다. 이 컴포넌트의 수치는 `Player.csv`에서 바꾼다. HUD 프리팹의 `PlayerStats`/`UltimateCharge`는 편집 화면 미리보기용이다.

궁극기 스탯의 기본값은 **최대 보유량 120%, 1회 소비량 100%**다. 120%에서 사용하면 20%가 남고, 소비량을 70%로 바꾸면 50%가 남는다. 현재 충전량은 `UltimateCharge`, 최대 보유량과 소비량은 `PlayerStats`가 관리한다. 최대 보유량 변경은 현재 충전량을 유지하며, 최대치가 줄어들 때만 초과분을 잘라낸다. 소비량이 최대 보유량보다 크면 사용할 수 없다.

`UltimateHUD`의 전체 바 길이는 최대 보유량에 비례한다. 기본 120%에서 배치한 폭을 유지하며 `Maximum Bar Width`(기본 480 Canvas 단위)를 넘으면 폭을 고정하고 충전량·사용 기준점을 비율로 표시한다. `UltReadyThresholdIcon`은 전체 바의 **소비량 ÷ 최대 보유량** 지점을 가리킨다. 소비량만 바꾸면 전체 바 길이는 유지된다. `/ultmax 200`은 최대 보유량을 200%로, `/ultcost 70`은 1회 소비량을 70%로 지정한다. `/ultmax 20`은 20%를 더하는 것이 아니라 최대 보유량을 20%로 설정한다. 이 명령은 Play 중 테스트 상태만 바꾼다.

## 폴더 이름·저장 위치를 바꿨을 때

1. Unity와 코드 편집기를 닫고 폴더를 이동하거나 이름을 바꾼다.
2. Unity Hub에서 새 폴더를 다시 추가한다. 프로젝트는 루트의 `Assets`, `Packages`, `ProjectSettings`를 기준으로 연다.
3. 지정 버전으로 열고 임포트가 끝날 때까지 기다린다. 검사 스크립트는 자신의 위치에서 루트를 찾으므로 절대 경로를 고칠 필요가 없다.
4. 이전 이름의 `.slnx`, `.sln`, `.csproj`는 Unity가 재생성하는 파일이다. External Tools의 프로젝트 파일 재생성을 사용한다.
5. 캐시 문제가 지속되면 Unity를 종료한 상태에서 `Library`를 재생성할 수 있다. 첫 임포트가 다시 필요하다. `Assets`, `Packages`, `ProjectSettings`와 `.meta`는 보존한다.

폴더명, Player Settings의 Product Name, 기존 셰이더·편집기 세션 키는 서로 다른 값이다. 이번 정리에서 문서·무기 생성 메뉴는 ENFILADE로 맞췄다. 기존 `Hidden/Morphang/...` 셰이더 식별자와 세션 키는 연결된 코드와 자료가 사용하므로 유지한다. 출시용 제품명·플랫폼 식별자는 Player Settings에서 별도로 결정한다.

## 에셋과 파일 정리

- 에셋 이동은 Unity Project 창에서 한다. 파일 도구로 옮긴다면 원래 `.meta`를 함께 옮겨 GUID를 보존한다. 씬·프리팹 연결은 GUID에 의존한다.
- `Resources/Combat/Feedback.asset`과 `GunFeel.asset`의 로드 키는 각각 `Combat/Feedback`, `Combat/GunFeel`이다. 경로를 바꿀 때는 코드의 키도 함께 바꾼다.
- 외부 폰트·모델은 씬·재질·프리팹뿐 아니라 문자열 로드에서도 사용한다. 참조와 라이선스를 확인한 뒤 정리한다.
- `Tools` 메뉴에는 `Data Tables`(표 폴더 열기·다시 읽기), `Models`(임포트 규칙 재적용·물리 프랍 만들기), `TextMeshPro`(Dynamic 폰트 정리·저장)만 둔다. HUD는 씬과 프리팹에서 직접 편집하며, 숨겨진 목표판은 `MissionHUD` Inspector의 편집 미리보기 버튼으로 펼친다.
- 공유할 소스는 `Assets`와 `.meta`, `Packages`, `ProjectSettings`, `Tools`, 문서다. `Library`, `Temp`, `.vs`, 생성 솔루션·업그레이드 로그는 재생성 가능한 로컬 파일이다.

문서 링크·메타데이터를 확인하려면 프로젝트 루트에서 `& ./Tools/Test-ProjectStructure.ps1`을 실행한다. 게임 기능 변경은 실제 씬 조작으로 확인한다.

## 플레이어 설정 (Esc 메뉴)

**Esc**를 누르면 게임이 멈추고 설정 메뉴가 열린다. Esc를 다시 누르거나 "돌아가기"를 누르면 이어서 하고, 이때 설정이 저장된다. 채팅을 닫는 Esc로는 메뉴가 열리지 않는다.

| 분류 | 항목 |
| --- | --- |
| 소리 | 전체, 전투, 이동, 목소리, UI 볼륨 |
| 조작 | 마우스 감도(×0.2~3, `PlayerLook`의 기본 감도에 곱함), 상하 반전, 앉기·기울이기를 "누르는 동안" 또는 "눌러서 전환" |
| 화면 | 시야각(16:9 화면 기준 가로 70~120°, 기본 90°. 세로 약 59°로 이전 기본 60°와 거의 같다. 더 넓은 화면은 옆이 더 보인다. 달릴 때의 시야 넓힘과 ADS 줌은 이 값에서 계산한다), 화면 흔들림: 걸음 흔들림, 착지, 충격, 볼트 펀치, 이동·다이브 기울임에 곱한다. Q/E 기울이기와 총 반동은 영향을 받지 않는다 |

- **저장 위치**: 사용자별 `Application.persistentDataPath/settings.json`이다. Windows에서는 보통 `%USERPROFILE%\AppData\LocalLow\<회사>\<제품>\settings.json`이다. 파일을 지우면 기본값으로 돌아간다. 밸런스 숫자는 CSV 표, 플레이어 취향은 이 파일에 둔다.
- **앉기 전환**: 한 번 누르면 앉고, 다시 누르면 선다. Shift나 점프를 누르거나, 다이브·포복·파쿠르로 앉기·슬라이드 상태를 벗어나면 자동으로 풀린다. 키를 놓은 것과 같다. 그래서 전환 모드의 점프는 서서 하는 점프이고, 몸을 접는 앉아 점프는 Ctrl을 누른 채 점프할 때만 나온다. 점프 중에 누르면 공중에서 몸을 접고, 앉은 채로 착지한다. 달리며 누르면 슬라이드하고, 끝나면 앉은 채로 남는다. 전환 모드에서 슬라이드 감속은 S 키로 한다.
- **기울이기 전환**: Q나 E를 한 번 누르면 그쪽으로 기울고, 같은 키를 다시 누르면 돌아온다. 반대 키를 누르면 반대로 기운다. Shift를 누르거나 서기·앉기 외의 상태가 되면 풀린다.
- **일시정지**: 싱글 플레이 기준이다. 멀티 플레이로 바꿀 때는 메뉴만 열고 게임은 멈추지 않게 한다(`SettingsMenu.Open`의 `GamePause.Set(true)`). [GamePause](../Scripts/Settings/GamePause.cs)만 게임 속도를 0으로 만든다. 멈출 때의 속도(반격 슬로 포함)를 그대로 되돌리므로, `CombatTimeEffects`의 슬로는 메뉴를 닫은 뒤 이어진다. UI 채널 소리만 일시정지 중에도 재생된다.
- 메뉴는 코드로 만든 화면이다([UI/Settings](../Scripts/UI/Settings)). 항목은 `SettingsMenuView`, 모양은 `SettingsWidgets`에서 바꾼다. EventSystem이 없는 씬(이동 시험장)에서는 메뉴를 처음 열 때 하나 만든다.

## 사운드를 추가하거나 교체하기

기준 폴더는 [Art/Audio/Combat](../Art/Audio/Combat)이다. 클립을 폴더에 넣은 뒤 [Feedback.asset](../Resources/Combat/Feedback.asset)의 해당 배열에도 연결한다. 폴더 안의 모든 파일이 자동으로 재생 목록에 들어가는 구조는 아니다.

| 폴더 | 재생 상황 | Feedback 설정 |
| --- | --- | --- |
| `Player/Attack/Punch/Swing` | 주먹을 휘두를 때 | `Punch Swing Sounds` |
| `Player/Attack/Punch/Hit` | 내 주먹이 적에게 피해를 줬을 때 | `Punch Hit Sounds` |
| `Player/Attack/Punch/Wall` | 주먹이 벽·지형에 닿았을 때 | `Punch Wall Sounds`, `Punch Wall Volume` |
| `Player/Damage/Hit` | 플레이어가 낙하 이외의 피해를 받았을 때 | `Player Damage Sounds` |
| `Shove` | 밀치기의 적·월드 접촉, 낙하 피해 레이어 | `Shove Enemy Sounds`, `Shove World Sound` |
| `Hitfullness` | 총기 명중·처치·근접 처치·낙하 등에 겹치는 타격 레이어 | `Impact Bullet Sounds` 등 각 레이어 |
| `Defense/Block`, `Defense/Parry` | 가드·패링 성공 | `Block Sounds`, `Parry Sounds` |
| `Defense/ReservedStrongParries` | 향후 강공격용 보관 클립 | 현재 일반 패링에 연결하지 않음 |

기존 클립을 이동하거나 이름을 바꿀 때는 `.meta`도 함께 이동해 GUID를 유지한다. 새 클립은 Unity 임포트가 끝난 뒤 배열에 추가하며, `Missing` 항목이나 남은 옛 경로의 `.meta`가 없는지 확인한다. 각 배열은 직전에 재생한 클립을 피해서 고르므로 원하는 변형들을 같은 배열에 넣으면 된다.

### 이동·목소리 소리 (폴더가 곧 목록)

이동과 목소리 소리는 배열에 연결하지 않는다. **폴더에 넣으면 자동으로 들어간다.** 폴더 구조가 소리 이름이고, 파일을 추가·이동·삭제하면 편집기가 `Resources/Audio/<묶음>` 소리 묶음(SoundBank)을 다시 만든다([SoundBankBuilder](../Editor/Import/SoundBankBuilder.cs)).

| 폴더 | 묶음 | 내용 |
| --- | --- | --- |
| [Art/Audio/Movement](../Art/Audio/Movement) | `Movement` | `<행동>/<표면>/`: `Run`·`Walk`·`Sneak`·`Release`(발 떼기)·`Land`·`Slide`·`SlideLoop`·`WallStep`·`HandSoft`·`HandHard`·`HandRelease`·`Ringout` 아래에 `Concrete`·`Metal`·`Duct`·`Wood`·`Glass`… 표면이 없는 소리는 `<행동>/` 또는 `<행동>/<종류>/`: `Cloth/VaultSwish`, `Roll/Start`, `Ladder/Step`, `Bar/Grab`, `Fall/Heartbeat`, `LandHard`, `Push`, `Prone/Down`, `Lean`… |
| [Art/Audio/Voice/Hiro](../Art/Audio/Voice/Hiro) | `Voice` | 숨소리 `Breath/SoftShortIn`…, 힘쓰는 소리 `StrainSoft/Medium/Hard`, 충격음 `ImpactSoft/Impact/ImpactHard`. 캐릭터 목소리를 바꾸면 이 폴더 내용만 갈아 끼운다 |
| [Art/Audio/Library](../Art/Audio/Library) | 없음 | 아직 쓰지 않는 보관 소리(Beatrun 나머지, FEAR 손전등·문, 투척 칼, 잠입). 어디에서도 참조하지 않으므로 빌드 크기에 들어가지 않는다. 쓰려면 위 폴더로 옮긴다 |

- **볼륨·피치**: [Sounds.csv](../Resources/Tables/Sounds.csv)의 `<묶음>/<폴더>` 행(예: `Movement/Walk/Concrete`). Beatrun 원본 값으로 채워져 있다. 행이 없으면 볼륨 1, 피치 1이다.
- **표면**: 물체에 [SurfaceType](../Scripts/World/SurfaceType.cs) 컴포넌트를 붙여 표면 이름을 적으면 그 값을 쓴다. 없으면 [Surfaces.csv](../Resources/Tables/Surfaces.csv)의 키워드를 물리 재질·렌더 재질·오브젝트 이름에서 찾는다(가장 긴 키워드가 이김). 아무것도 맞지 않으면 `Concrete`다.
- **표면이 없는 소리**: 표마다 `parent`가 있어서 소리가 없는 표면은 부모로 넘어간다. `Duct` → `Metal` → `Concrete` 순서다. 예를 들어 `Grass`는 착지 소리만 따로 있고, 발소리는 `Dirt` → `Gravel` 소리를 쓴다.
- 출처: Beatrun(미러스 엣지 원본), Dynamic Landing(`LandHard`·`LandHeavy`·`Roll`·`Push`), FEAR-like SFX(`Land/Dirt·Grass·Stone·Tile·Carpet`, `Slide/Dirt·Sand`, `Prone`, `Ledge/Reach`, `Lean`, `Cloth/SprintRustle`). 공개 빌드 전에는 교체가 필요할 수 있다.
- **언제 무슨 소리가 나는지**: 코드는 큐 이름만 부르고, 그 큐가 무엇을 재생할지는 [SoundCues.csv](../Resources/Tables/SoundCues.csv)가 정한다. `sounds` 칸은 `;`로 나눈 `묶음/키` 목록이다. `{surface}`로 끝나면 발밑·장애물·렛지·벽의 표면 소리, `*0.5`는 볼륨, `+0.1`은 0.1초 뒤다(예: `Movement/HandHard/{surface}; Voice/StrainSoft +0.1`). `Vault/Gantry`처럼 표면 이름을 붙인 행이 있으면 그 표면에서는 그 행을 쓴다. `noise`는 적에게 들리는 거리(m)이고 [Noise](../Scripts/World/Noise.cs) 이벤트로 나간다(아직 듣는 적은 없다).
- **발소리**는 타이머가 아니라 몸의 발이 땅에 닿는 순간에 난다([PlayerFootsteps](../Scripts/Player/Audio/PlayerFootsteps.cs), GMod Anim-Based Footsteps 방식). 발마다 직전 접지 높이를 추적하며 새로 들어 올린 발만 다음 걸음으로 센다. 양발이 함께 올라가는 골반 움직임은 제외하고, 낮은 프레임 속도로 발의 최저점을 건너뛰어도 하강 후 반등을 인식한다. 가만히 서 있으면 양발의 기준을 각각 다시 잰다. 시험장은 [MovementAbilityPlayground](../Scenes/Development/MovementAbilityPlayground.unity) 시작 위치 뒤의 `Surface pads`(표면별 판 16개)이고, 디버그 HUD의 `Surface:`가 마지막 걸음의 표면이다.
- **이동 소리의 비중**: 일반 이동 속도 5.2m/s는 몸의 달리기 클립과 `StepRun`을 쓴다. 반복되는 옷 소리는 이 큐에서만 `*0.22`, 발 떼기 마찰음은 `StepRelease`에서 `*0.3`으로 줄여 발 디딤이 먼저 들리게 한다. 볼트·구르기·착지의 옷 소리는 각 동작 큐에서 따로 조절한다. 몸 소리가 설정된 동시 재생 수를 넘으면 가장 먼저 끝날 소리를 멈추고 새 소리로 교체한다.
- **넘기 뒤 발소리 복귀**: 볼트의 남은 애니메이션을 재생하는 동안만 발 접지를 잠시 읽지 않는다. 마무리가 끝나거나 다른 자세로 중단되면 `playingOut`을 해제해 보행 접지와 발소리가 다시 이어진다.

**볼륨 채널**: 모든 소리는 `전투`·`이동`·`목소리`·`UI` 채널 중 하나에 속하고, Esc 메뉴의 볼륨을 따른다. 새 `AudioSource`를 만들면 [GameAudio](../Scripts/Audio/GameAudio.cs)`.Bind(source, AudioChannel.…)`로 한 번 연결한다. 소리마다 크기를 바꿀 때는 `source.volume`을 쓰지 않고 `PlayOneShot(clip, 볼륨)`의 볼륨 값을 쓴다. 커졌다 작아지는 반복 소리(슬라이드 긁는 소리)는 `GameAudio.SetGain`으로 크기를 정한다. 월드 한 지점에서 한 번 나는 소리는 `AudioSource.PlayClipAtPoint` 대신 `GameAudio.PlayAt`을 쓴다. 전체 볼륨은 `AudioListener.volume`이다.

주먹은 적이나 벽에 닿으면 후딜을 즉시 해제하며, 허공 후딜 기본값은 **0.2초**다. 벽 소리는 한 공격당 한 번만 재생한다. 주먹·공통 근접 스윕·밀치기·반격은 사거리 안에서 벽의 가림을 무시한다. 총기 명중은 벽에 막히며, 실제 몸 이동·V 홀드 접근·처형의 충돌과 가림 검사는 각각 유지한다. 자세한 규칙은 [전투 규칙](WeaponSystemDesign.md)을 따른다.

## 3D 모델 추가하기

모델 하나당 폴더 하나를 [Art/Models](../Art/Models) 아래 분류 폴더에 만든다. 이름에는 공백 대신 PascalCase를 쓴다.

```text
Art/Models/
  Characters/  Weapons/  Props/  Environment/
    Props/TetoPear/
      TetoPear.fbx
      Textures/tetopear.png      ← 머티리얼 이름(tetopear)과 같은 이름
```

폴더에 넣기만 하면 [ModelImportRules](../Editor/Import/ModelImportRules.cs)가 처음 임포트할 때 다음을 맞춘다.

| 항목 | 처리 |
| --- | --- |
| 루트 | 씬에 놓은 오브젝트의 루트는 회전 0·크기 1. Blender의 -90°·×100은 안쪽 자식에 남는다 |
| 축·불필요 요소 | 축 변환을 메시에 굽고, 카메라·조명은 가져오지 않는다 |
| `Props`·`Environment` | 리그·애니메이션을 끈다. `Characters`·`Weapons`는 기본값(Generic)을 유지한다 |
| 머티리얼 | URP Lit으로 만들고, 모델 옆이나 `Textures/`에서 이름이 맞는 텍스처를 자동 연결한다 |
| 텍스처 | 최대 2048. `_Normal`·`_N`은 노멀맵, `_Mask`·`_Metallic`·`_Roughness`·`_AO`·`_ORM`·`_Height`는 sRGB 끔 |

텍스처 이름 규칙: 머티리얼 이름 또는 모델 이름 뒤에 아무것도 없거나 `_BaseColor`·`_Albedo`·`_Diffuse`·`_Color`이면 기본 색, `_Normal`이면 노멀맵이다. 대소문자·공백·밑줄은 구분하지 않는다. 폴더에 텍스처가 하나뿐이면 이름과 상관없이 기본 색으로 쓴다. 텍스처를 나중에 넣어도 모델이 다시 임포트되어 연결된다.

- 규칙은 **처음 임포트할 때만** 적용되므로 이후 Inspector에서 바꾼 설정은 유지된다. 규칙을 다시 적용하려면 모델이나 폴더를 선택하고 `Tools → Models → Reapply Import Rules`를 누른다.
- 머티리얼은 모델 안에 포함되어 자동으로 갱신된다. 매끄러움 등을 직접 조정하려면 모델 Inspector → Materials → **Extract Materials**로 꺼낸다. 꺼낸 뒤에는 텍스처가 자동으로 바뀌지 않는다.
- 크기는 `Model → Scale Factor`로 조정한다. 플레이어 키는 약 1.75m다.
- 게임에 쓰는 오브젝트(충돌체·스크립트 포함)는 모델을 씬에 놓고 [Prefabs](../Prefabs) 아래 프리팹으로 만든다. 굴러다니는 물체는 아래 물리 프랍 도구를 쓴다.
- 원본 `.blend`는 `Assets` 밖에 둔다. `Assets` 안에 두면 Unity가 Blender를 실행해 변환하려고 한다. `.blend1`은 Blender 백업이므로 넣지 않는다. `.fbx`·`.png`·`.blend`는 Git LFS로 저장된다.
- Blender에서 내보낼 때 Apply Scalings `FBX All`, Forward `-Z`, Up `Y`, Apply Transform을 켜면 안쪽 자식의 회전·크기도 깔끔해진다.

### 충돌과 물리 (물리 프랍)

모델을 Project 창에서 선택하고 우클릭 → `ENFILADE → Create Physics Prop Prefab`(또는 `Tools → Models → Create Physics Prop Prefab`)을 누르면 `Prefabs/<분류>/<이름>.prefab`이 만들어진다. 씬에는 이 프리팹을 놓는다.

| 구성 | 내용 |
| --- | --- |
| 루트 | `Rigidbody`(질량 1kg, 보간, 빠른 충돌 감지) + [PhysicsProp](../Scripts/World/PhysicsProp.cs) |
| 자식 | 모델 그대로. 메시마다 **convex `MeshCollider`** |
| 반응 | **질량이 결과를 정한다.** 모든 밀기는 운동량이라 같은 힘이면 가벼울수록 멀리 간다. 벽 타기·매달리기·넘기 대상에서는 빠진다 |

| 밀기 | 세기 (설정 위치) | 최고 속도 | 1kg 물체 | 20kg 물체 |
| --- | --- | --- | --- | --- |
| 걷기 | 300N으로 기댐 (`Player.csv`의 `Ability.propPushForce`) | 걷는 속도 | 바로 걷는 속도 | 서서히 밀림 |
| V 밀치기 / 태클 | 60 N·s / 120 N·s (`Prop Shove Impulse`) | 8m/s (`Shove Push Speed`) | 8m/s | 3m/s |
| 총알 1발 | 소총 3·권총 4 N·s ([Weapons.csv](../Resources/Tables/Weapons.csv) `propImpulse`) | 12m/s | 3·4m/s | 0.15·0.2m/s |
| 슬라이딩·다이브·태클로 부딪힘 | 몸무게 70kg과 함께 밀려 나감 (`Prop Body Mass`) | 플레이어 속도 | 플레이어 속도로 밀려 나가고 플레이어는 거의 그대로 | 둘 다 플레이어 속도의 78%로 진행 |

슬라이딩·다이브·태클로 부딪히면 물체와 플레이어가 한 속도(몸무게 ÷ 둘의 질량 합 × 플레이어 속도)로 함께 나아간다. 물체가 튕겨 나가며 더 빨라지지는 않는다. 그 속도가 원래의 30% 아래가 되는 물체(플레이어 몸무게의 약 2.3배, 160kg 이상)는 지금처럼 벽으로 취급해 튕기거나 멈춘다.

움직이는 물체에는 오목한 메시 충돌체를 쓸 수 없어서 볼록 껍질(convex, 최대 255면)로 감싼다. 배·공·상자처럼 거의 볼록한 모양은 이것으로 충분하다. 도넛·의자처럼 크게 오목한 모양은 자식 오브젝트에 Box·Sphere·Capsule 충돌체를 여러 개 나눠 붙이고 `MeshCollider`를 지운다. 움직이지 않는 벽·지형은 `Rigidbody` 없이 convex를 끈 `MeshCollider`를 쓰면 모양 그대로 충돌한다.

물체마다 바꿀 값은 루트 `Rigidbody → Mass` 하나다. 밀기 세기는 모든 물체에 공통이며 위 표의 위치에서 바꾼다. 프리팹이 이미 있으면 도구는 덮어쓰지 않는다.

## 플레이어 몸 (진짜 1인칭)

플레이어는 보이는 몸을 가진다. 몸은 이동 상태에 맞는 애니메이션을 재생하고, 카메라는 항상 몸의 눈에 붙어 있다(Beatrun Eye View처럼, 아래 **눈 시점**). 회전·기울기·충격 흔들림은 코드가 더한다. 내려다보면 허리·목이 같이 굽어 몸통·다리가 보인다. 머리·머리카락은 Esc 설정 **내 머리카락 보기**를 켜면 보이고, 끄면 그림자만 남는다. **F5**는 뒤에서 몸을 보는 확인용 시점이다.

| 구성 | 위치 |
| --- | --- |
| 캐릭터 | [Art/Models/Characters/Hiro](../Art/Models/Characters/Hiro) (휴머노이드). 서 있는 눈높이가 카메라(1.6m)에 맞게 자동으로 크기가 정해진다 |
| 애니메이션 | [Art/Animations/Mixamo](../Art/Animations/Mixamo)의 26개 클립(서기·걷기·달리기 8방향, 넘기, 난간, 낙하·착지·구르기, 일어나기). [Art/Animations/Beatrun](../Art/Animations/Beatrun)의 173개 + 웅크려 옆걸음 2개(슬라이딩·다이브·웅크리기·벽·봉 등, 소총·권총 버전 포함). 휴머노이드라 어떤 캐릭터에도 옮겨 붙는다 |
| 연결 | `Resources/Characters/PlayerBody` 프로필(모델 + 클립 목록). 임포트할 때 자동으로 만들어지고 클립 목록도 자동 갱신된다 |
| 상태별 클립·연출 수치 | [Player.csv](../Resources/Tables/Player.csv)의 `Body` 행 (예: `Body.slide,slideloop`) |
| 자세별 캡슐·눈높이 | `Player.csv`의 `Posture` 행. 웅크림 0.8m, 슬라이딩 0.62m, 엎드림 0.37m, 누움 0.35m. 눈이 캡슐 꼭대기를 넘지 않게 맞췄다 |
| 동작 코드 | [PlayerBody](../Scripts/Player/Body/PlayerBody.cs). Play 시작 때 `PlayerMovement`가 붙인다 |

- **캐릭터 바꾸기**: 새 모델을 `Art/Models/Characters/<이름>/`에 넣고, `PlayerBody` 프로필의 `Model`을 바꾼다. 1인칭에서 숨길 부분은 프로필의 `First Person Shadow Only`에 렌더러 이름으로 적는다(기본 `Head`, `Hair`).
- **애니메이션 추가**: 휴머노이드 FBX를 `Art/Animations/<묶음>/`에 넣고 옆에 `<파일이름>.clips.csv`를 둔다. 클립 하나짜리 파일이 많은 폴더(Mixamo)는 폴더 이름의 표 하나(`Mixamo/Mixamo.clips.csv`)에 모은다. Mixamo는 모든 클립 이름이 `mixamo.com`이라 파일 이름이 클립 이름이 된다. 스킨 없이 받은 파일에는 T자 자세가 없어서, 파일마다 첫 프레임을 기본 자세로 삼은 아바타가 만들어지고 클립이 그만큼 틀어져 리타깃된다(Mixamo는 발이 반 바퀴 뒤집혀 나왔다). 그래서 같은 캐릭터를 T자 자세·스킨 포함으로 받아 폴더 이름으로 `<폴더>.tpose.fbx`(`Mixamo/Mixamo.tpose.fbx`)로 두면, 그 폴더의 다른 파일이 모두 그 아바타를 복사해 쓴다(Copy From Other Avatar). 넣으면 자동으로 다시 임포트된다. 열: `loop`가 1이면 반복, `inPlace`가 1이면 클립 안의 앞뒤·옆 이동을 버리고 제자리에서 재생, `inPlaceY`가 1이면 오르내림도 버린다(넘기·올라가기처럼 플레이어 경로가 몸을 옮기는 클립). 기본은 원래 위치 그대로다. `cycleOffset`은 반복 클립이 시작하는 지점(0~1)으로, 섞이는 이동 클립들이 같은 순간에 왼발을 딛게 맞춘다. `from`·`to`는 첫 프레임부터 센 프레임 번호로 그 구간만 쓴다(착지는 발이 닿는 프레임부터). `inPlaceYaw`가 1이면 클립 안의 회전을 버린다(몸이 직접 도는 제자리 돌기). `yawOffset`은 클립의 앞 방향 보정 각도(도, 기본 0)다. Source 클립을 그대로 변환하면 옆을 향하므로 **90**, 아래처럼 캐릭터 뼈대에 구워 넣은 클립(지금의 Beatrun)은 모델과 같이 Unity -Z를 향하므로 **180**이다(변환 스크립트가 알맞은 값을 써 준다). 현재 Hiro 아바타에서는 Mixamo 기본값 0이 카메라 반대인 -Z를 향하므로 `Mixamo.clips.csv`의 `yawOffset`도 **180**으로 둔다. 이미 +Z를 향하는 Beatrun의 기존 **180**은 유지한다. 구르기·좌우 회피처럼 애니메이션에 들어 있는 회전은 유지한다. 수정 후 반영되지 않으면 해당 FBX를 우클릭해 **Reimport**한다.
- **Source(GMod·L4D2) 모델 변환**: [Tools/Blender](../../../Tools/Blender)의 스크립트를 SourceIO 애드온이 있는 Blender로 실행한다. 모델은 `source_model_to_fbx.py`, 애니메이션은 `source_anims_to_fbx.py`. 애니메이션은 마지막 인자로 플레이어 모델의 MDL을 주면 그 모델의 뼈대(뼈 길이·기본자세)에 직접 구워 낸다. 서로 다른 뼈대 사이의 휴머노이드 리타깃이 없어져 발이 꺾이거나 몸이 뒤로 빠지지 않는다. 예: `blender -b --factory-startup --python source_anims_to_fbx.py -- new_climbanim.mdl BeatrunBody.fbx BeatrunBody * - 180 survivor_gambler.mdl`. Hiro의 뼈대 MDL은 [Tools/Blender/Source/Hiro](../../../Tools/Blender/Source/Hiro)에 있다. 맨 끝에 접미사(`_tp`)를 주면 클립 이름 뒤에 붙는다. 같은 이름의 클립 묶음을 나란히 두고 비교할 때 쓴다. 몸은 같은 이름이 여러 개면 경로 순서상 첫 번째를 쓴다.
  - **블렌드 시퀀스**: 3인칭 세트의 이동 클립(`runfwd`, `walkfwd`, `crouchfwd`, 회피 등)은 이동 방향 3×3 블렌드이고, 가운데 칸이 서 있기다. 칸마다 몸 전체가 이동 방향으로 돈다. 변환 스크립트는 클립 이름에 맞는 칸을 고른다. 앞 이동은 앞 칸, `runbwd`·`walkback`·`crouchbwd`는 뒤 칸, `dodgejumpleft/right`는 옆 칸이고, 나머지 블렌드(매달리기·흔들기)는 모든 값이 0인 칸이다. 다른 칸을 따로 굽으려면 이름을 `새이름=시퀀스@옆:앞`으로 준다. 웅크려 옆걸음(`BeatrunCrouch.fbx`)은 `crouchleft=crouchfwd@-1:0,crouchright=crouchfwd@1:0`으로 구웠다. `sprintfwd` 원본은 2초에 같은 보행 주기가 네 번 들어 있다. 다른 이동 클립과 한 주기로 섞도록 `BeatrunBody.clips.csv`에서 `from=0`, `to=15`, `cycleOffset=0`으로 잘라 0.5초 한 주기만 쓴다(30fps). 전체를 다시 구우면 표가 새로 써지므로 이 세 값을 복원한다.
  - **Spine4**: Source 뼈대에는 척추가 4개(`Spine`, `Spine1`, `Spine2`, `Spine4`)지만 Unity 휴머노이드에는 3자리뿐이다. 그래서 변환할 때 `Spine4`의 회전을 `Spine2`에 합쳐 굽는다. 임포트 때 "in between humanoid transforms … discarded" 경고가 나면 이 처리가 빠진 것이다.
  - **손가락**: Hiro의 손가락 뼈 축은 Beatrun 뼈대와 최대 167° 달라서, 회전을 그대로 옮기면 손가락이 옆·뒤로 꺾인다. 변환할 때 클립 손가락이 자기 기본 자세에서 움직인 만큼을 Hiro 손가락 축으로 옮겨서 Hiro 기본 자세에 더한다. 클립이 기본 자세면 Hiro도 정확히 기본 자세다.
  - 모든 뼈를 매 프레임 키로 굽는다(173개, 약 170MB). 움직이지 않는 뼈를 빼거나 키를 줄이면 일정한 자세를 유지하는 뼈가 기본 자세로 튀거나 중간에 꺾여서 쓰지 않는다. 한 프레임짜리 포즈와 레이어 클립은 Unity에서 빈 클립이 되므로 넣지 않는다.
- **들어 있는 애니메이션 묶음**: [Beatrun](../Art/Animations/Beatrun)은 Beatrun 3인칭 여성 세트(`tp_climbanim_f.mdl`)를 Hiro 뼈대에 구운 173개 클립이다. 벽 달리기 4개는 쓰지 않아 뺐다. 소총·권총 버전은 `_ar2`·`_pistol`이 붙는다(누워 미끄러지기, 다이브, 머리부터 미끄러지기, 공중 코일 등). [Mixamo](../Art/Animations/Mixamo)는 mixamo.com에서 X Bot 기준으로 받은 클립(스킨 없음, 30fps)이다. 리타깃 기준 자세는 같은 X Bot을 T자 자세·스킨 포함·FBX Binary로 받은 `Mixamo.tpose.fbx`다(클립 파일과 같은 FBX 2019 형식, 같은 `mixamorig:` 뼈 66개). 걷기·달리기·옆걸음·뒷걸음은 제자리 버전이다. 클립마다 실제 속도·길이·발 딛는 시점은 `Mixamo.clips.csv`의 메모에 있다. 웅크리기는 Beatrun을 쓴다. Mixamo 웅크려 걷기는 눈높이가 약 1.2m라 웅크린 카메라(0.8m)와 맞지 않는다. `(아직 안 씀)` 표시 클립(제자리 돌기, 달리며 구르기, 일어나기 3종)은 4단계·10단계에서 쓴다. `Player.csv`의 `Body` 행에서 클립 이름을 바꾸면 Play 중에도 몸이 다시 만들어진다.
- **8방향 이동**([PlayerBody.Locomotion](../Scripts/Player/Body/PlayerBody.Locomotion.cs)): 걷기·달리기·웅크려 걷기는 앞·오른쪽·뒤·왼쪽 클립을 이동 방향으로 섞는다(대각선은 양옆 두 개). 몸은 항상 카메라 쪽을 본다. 모든 반복 클립은 한 보행 주기를 공유해서 섞여도 발이 엇갈리지 않는다. 전력질주 클립은 `SprintProgress`가 올라간 상태에서 앞으로 이동할 때만 쓴다. 일반 이동·뒷걸음·옆걸음에는 전력질주 클립을 섞지 않는다.
- **이동 재생 속도**: `Body.runClipSpeed=5.2`, `Body.sprintClipSpeed=8.4`, `Body.crouchClipSpeed=3`은 각각 `Move.walkSpeed`, `Move.sprintSpeed`, `Move.crouchSpeed`에 맞춘 기준값(m/s)이다. 빠른 기본 이동 때문에 평소에도 질주 자세가 섞이거나 웅크리기가 1.5배로 재생되지 않게 한다. 이동 애니메이션의 재생 배율은 0.65~1.1로 제한한다. 이동 속도를 바꾸면 이 기준값도 함께 조정한다.
- **파쿠르 시간**:
  - 넘기: 도움닫기 속도를 그대로 유지하며 지나간다. 걷기 속도보다 느려지지는 않는다. 넘기 클립은 자기 속도(`Body.vaultClipSpeed`)로 재생된다. 몸이 먼저 넘어가면 클립은 발이 닿는 지점(`vaultLandShare`)까지 이어서 재생된 뒤 달리기로 넘어간다.
  - 올라서기: 매달린 난간에서 W·Space로만 된다. 장애물 앞에서는 넘기만 하고, 넘을 수 없으면 점프해서 난간을 잡는다. `Ability.mantleFromGround`를 true로 하면 예전처럼 땅·공중에서 바로 올라선다. 올라서기는 묵직하게 `Ability.mantleDuration` + 높이 1m마다 `mantleDurationPerMeter`가 걸리고, 클립이 그 진행도에 맞춰 재생된다.
  - 넘기는 진행의 75%, 올라서기는 80%에서 손을 놓고 이동으로 돌아간다.
  - 구르기는 `Ability.rollDuration` 동안 구르기 클립 전체가 재생된다.
- **난간 잡기**([PlayerBody.Grip](../Scripts/Player/Body/PlayerBody.Grip.cs)): 매달려 있는 동안 몸은 어디를 보든 벽을 향한다. 클립이 자세를 잡은 뒤에는 손목이 난간 윗면(`Body.ledgeGrip`)에 오도록 몸 전체를 옮긴다. 손은 그 자리에 있고 몸이 그 아래에 매달리므로 캐릭터나 클립이 바뀌어도 손이 허공을 잡지 않는다. 좌우 이동 클립은 실제 이동 속도에 맞춰 빨라진다(`shimmyClipSpeed`).
- **상황별 클립**([PlayerBody.Variants](../Scripts/Player/Body/PlayerBody.Variants.cs)): 같은 상태라도 상황에 맞는 클립을 고른다. 모두 `Body` 행에서 이름과 기준값을 바꾼다.
  - 점프: 도움닫기 속도에 따라 제자리(`jumpStill`), 걷기(`jumpSlow`), 달리기(`jump`).
  - 공중에서 Ctrl: 무릎을 접는 `airTuck`(Beatrun 코일).
  - 착지: 낙하 속도가 `severeLandingSpeed`(16m/s, 낙하 피해가 시작되는 속도)를 넘으면 손과 무릎을 짚는 `landSevere`. 약 3m(`hardLandingSpeed` 12m/s)를 넘으면 무릎을 깊게 굽히는 `land`, 달리는 중이면 `landMoving`, 웅크린 채면 서지 않는 `landCrouch`. 그보다 낮을 때 공중에서 무릎을 접었으면 다리를 펴는 `coilLand`/`coilLandCrouch`, 아니면 착지 동작 없이 바로 이어진다.
  - 슬라이드 끝: 웅크림·엎드림으로 이어지는 끝 동작.
  - 넘기: 장애물 높이에 따라 한 손 `vault`, 1m 넘으면 두 손 `vaultHigh`. `vaultChainTime`(1.5초) 안에 다시 넘으면 직전과 다른 쪽을 쓴다. 같은 클립은 자기 자신과 섞일 수 없어서 첫 프레임으로 튀기 때문이다.
  - 올라서기: 매달린 데서 올라가면 높이와 관계없이 `ledgeClimbUp`. `mantleFromGround`가 켜져 있을 때 땅에서 올라서면 높이에 따라 `stepUp`, `mantleLow`, `mantleMid`, `mantle`.
  - 난간: 잡는 순간(`ledgeCatch`)과 좌우 이동(`ledgeShimmyLeft/Right`).
  - 벽: 벽 오르기 시작(`wallClimbStart`), 튀는 방향별 벽 차기(`wallKick`/`wallKickRight`).
  - 총: `armedClips`를 켜면 소총·권총을 들었을 때 해당 클립의 총 든 버전(`rifleSuffix`·`pistolSuffix`)을 쓴다. 이 클립들은 Source가 손을 총에 고정해 주는 것을 전제로 만들었다. 지금은 손을 몸의 손 IK가 총에 놓으므로 켜 볼 수 있지만, 아직 확인하지 않아 꺼 두었다.
- **재질(lilToon)**: Hiro는 [lilToon](https://github.com/lilxyzw/lilToon) 2.4.1(`Packages/jp.lilxyzw.liltoon`, MIT) 셰이더를 쓴다. 재질은 [Hiro/Materials](../Art/Models/Characters/Hiro/Materials)에 따로 있어 Inspector의 lilToon 화면에서 그림자 색·림라이트·광택을 바로 조정한다. 새 캐릭터는 모델 Inspector → Materials → **Extract Materials** 후 셰이더를 `lilToon`으로 바꾼다.
- **머리 맞추기**: 자세마다 클립 속 머리 위치가 다르므로(누워 미끄러지면 머리가 발 뒤쪽에 있다) 몸은 머리가 카메라 기준점 바로 뒤에 오도록 천천히 따라 움직인다(`Body.headFollowTime`). 매달릴 때는 손이 몸을 지지하고, 일반 슬라이딩 중에는 진행 방향 기준 위치를 유지한다.
- **눈 시점과 시선**([PlayerBody.Look](../Scripts/Player/Body/PlayerBody.Look.cs)):
  - 카메라는 몸의 눈에서 `eyeViewAhead`만큼 앞에 놓인다. 걸음의 흔들림, 착지·구르기의 꺼짐은 애니메이션에서 온다. 조준은 마우스 그대로이고, Q/E 기울이기의 충돌 검사된 이동량을 더한다. 정조준하면 `eyeViewTime` 동안 고정 조준 위치로 옮겨 가서 조준이 흔들리지 않는다.
  - 눈 위치는 아바타에 연결된 LeftEye·RightEye의 중점이다. 눈 사이 간격은 캐릭터 크기에 비례해 검사하므로 Hiro처럼 눈이 넓게 배치된 캐릭터도 실제 눈 뼈(`pd.013`·`pd.014`)를 쓴다. 모델 기본 자세의 앞 방향은 양팔 뼈로 구한다. Hiro의 기본 자세가 모델 -Z를 향해도 눈과 `eyeViewAhead`·`eyeAdjust`를 해부학적 앞 방향으로 계산해 머리 로컬 좌표에 저장한다. 눈 뼈가 없거나 적합하지 않으면 같은 앞 방향과 카메라 높이로 추정한다. 위치를 조정하려면 Play 중 `Body.eyeAdjust`(위;앞)를 고친다.
  - 벽 근처에서는 먼저 원하는 상체 자세의 머리·눈 여유 공간을 검사한다([PlayerBody.Clearance](../Scripts/Player/Body/PlayerBody.Clearance.cs)). 허리를 굽히면 얼굴이 벽에 닿는 경우, 닿지 않는 허리 굽힘 범위를 찾아 남은 시선 각도를 목·머리에 나눠 준다. 그 뒤에도 남는 충돌은 플레이어 캡슐 안의 일정한 기준점에서 눈까지 카메라 부피를 쓸어 검사한다([CameraClearance](../Scripts/Player/Camera/CameraClearance.cs)). 이 부피는 시야각·화면 비율·근거리 평면의 모서리까지 고려한다. 머리가 벽과 겹치는 순간마다 검사 시작점을 바꾸거나 뒤로 밀리는 양을 단순 보간하지 않는다.
  - 몸은 머리가 카메라 기준점 바로 뒤에 오도록 계속 따라오므로(`headFollowTime`), 카메라는 플레이어 위치에서 옆으로 멀리 벗어나지 않는다.
  - 위아래를 보면 허리(`spineBendDown`·`spineBendUp` 비율)·목·머리가 나눠 굽고, 머리는 카메라가 보는 곳을 본다.
  - 골반이 시선과 다른 쪽을 보면 가슴·목·머리가 시선 쪽으로 비틀린다. 제자리에서 다리를 디딘 채일 때와 전력질주로 골반이 돌 때도 적용한다.
  - 일반 슬라이딩 중 하체는 플레이어 캡슐에 대한 위치와 월드 진행 방향(`SlideDirection`)을 유지한다. 시선만 돌려서는 다리 위치·방향이 달라지지 않으며, 실제 이동·조향을 따라간다. 허리·가슴·목·머리만 시선을 부드럽게 따라간다. 좌우는 `Body.slideTurnLimit`(기본 각 75°), 위아래는 `Body.slidePitchLimits`(기본 위 40°·아래 55°)로 제한하며, 상체와 카메라가 같은 범위를 쓴다. 미끄러지는 방향이 시선의 반대쪽(벽에 튕겨 뒤로 밀림, 뒤로 슬라이딩 시작)이면 시선과 다리는 그대로이고 몸이 뒤로 미끄러진다. 옆으로 튕겨 기존 시선이 새 범위를 벗어나면 그 범위 안으로 서서히 돌아온다.
  - 카메라 효과는 흔들림·기울이기와 기준 위치를 계산하고, 몸 코드가 그다음에 눈 시점의 최종 위치를 한 번 정한다(실행 순서 250). 기울이기 이동량을 직접 전달하므로 지난 프레임의 눈 시점이 다시 이동량으로 더해지지 않는다. 무기는 이후에 처리되어 총과 총알이 확정된 시점을 기준으로 동작한다.
  - Esc로 일시정지하면 카메라 효과와 몸 자세 갱신을 함께 멈추어 마지막 눈 시점을 유지한다. 메뉴를 여는 순간 고정 카메라 위치로 바뀌거나, 재개하면서 눈에서 다시 멀어지지 않는다.
- **제자리 돌기**: 서 있을 때 시선만 돌리면 다리는 그 자리에 있고 상체만 비튼다. `turnStartAngle`(60°)을 넘으면 `turnLeft`/`turnRight` 클립으로 발을 옮기며 돈다(`turnSpeed`). 돌기 클립은 `inPlaceYaw`로 회전을 빼고 발걸음만 쓴다. 이 임계각에 따른 다리 돌기는 슬라이딩 중에는 실행하지 않는다.
- **내 머리카락 보기**(Esc 설정, 기본 켬): 켜면 머리·머리카락(`First Person Shadow Only`에 적힌 렌더러)을 그린다. 카메라가 그 안에 있는 부분은 근거리 클리핑(`Body.headNearClip`, 0.05m)으로 잘린다.
- 누운 자세·누워 미끄러지기는 Beatrun의 `meslide` 계열을 쓴다. 엎드림은 전용 애니메이션이 없어 머리부터 슬라이딩 자세를 임시로 쓴다.
- **몸의 손**([PlayerBodyHands](../Scripts/Player/Body/PlayerBodyHands.cs), [ArmIK](../Scripts/Player/Body/ArmIK.cs), `Player.csv`의 `Hands` 행): 클립이 팔 자세를 잡은 뒤, 팔을 굽혀 손목을 목표에 놓는다. 팔꿈치는 자연스러운 쪽으로 향하고, 손이 닿지 않으면 어깨가 그쪽으로 돈다(`shoulderReach`). 손 방향은 캐릭터의 손가락 뼈로 잰 "손가락 방향·손바닥 방향"으로 정하므로 캐릭터를 바꿔도 맞는다.
  - 총: 무기 모델 기준의 손목 위치·손가락 방향·손바닥 방향을 [Weapons.csv](../Resources/Tables/Weapons.csv)의 `rightGrip*`(방아쇠 손)·`leftGrip*`(받치는 손)에서 읽는다. 총 자세(지향·정조준·벽에 막혀 든 자세·반동·장전)는 지금처럼 카메라 기준 코드가 정하고 손이 따라간다. 지향 자세는 `holdOffset`만큼 몸 쪽으로 당겨서 팔이 닿게 했다(Hiro의 팔 길이는 약 0.51m). 벽에 막혀 총을 든 자세(하이레디)는 시선의 좌우만 따른다. 그래서 내려다보면 고개만 숙이고 총과 상체는 든 채로 있다.
  - 손은 무기 코드 다음(실행 순서 320)에 놓이므로 총보다 늦게 따라오지 않는다. 넘기·올라서기·매달리기·벽 동작·구르기·봉 동작 중에는 손이 총을 놓는다.
  - 주먹: [FistsView](../Scripts/Player/Combat/FistsView.cs)가 카메라 아래에 막기·충전·주먹 위치만 정하고, 몸의 손이 그 자리로 간다. 예전 상자 장갑은 없앴다.
  - 난간: 매달리면 두 손이 몸 정면의 난간 모서리 양쪽(`ledgeHandSpacing`)을 잡는다.
  - 넘기·올라서기: 손이 장애물 윗면 아래로 들어가면 윗면 위(`palmHeight`)로 올린다.
  - F5 뒤에서 보기에서는 총·주먹 위치를 눈 위치 기준으로 옮겨서 손이 뒤쪽 카메라로 뻗지 않는다.
  - 지금 장면의 총(`SK_AR_01`)은 재질을 비워 둬서 보이지 않는다. 보이게 하려면 그 오브젝트의 Inspector에서 재질 오버라이드를 Revert한다. 그 상태에서 손 위치를 맞추면 된다. 권총 모델은 아직 없어서 권총도 같은 총 위치를 쓴다.
- **발 IK**([PlayerBody.Feet](../Scripts/Player/Body/PlayerBody.Feet.cs), GMod IK Foot 애드온 방식): 서 있거나 웅크려 땅에 있을 때(착지 동작 포함, 넘기 마무리 동작 제외) 발마다 뒤꿈치·발목·발끝 아래로 땅을 찾아(`Body.footReach`) 가장 높은 곳에 선다. 그래서 발 일부가 계단 속으로 들어가지 않는다. 한 발의 땅이 캡슐 바닥보다 낮으면(내려가는 계단·경사) 그 발이 닿도록 골반을 내리고(최대 `maxHipDrop`), 높은 쪽 발은 무릎을 굽혀 땅에 맞춘다. 무릎은 클립의 무릎 축으로만 굽고, 다리가 제 길이 축으로 돌지 않아서 발목이 꼬이지 않는다(GMod IK Foot도 허벅지·종아리를 앞뒤로만 굽힌다). 거의 편 다리는 무릎 축이 분명하지 않아서 골반 좌우 방향을 축으로 앞으로 굽는다. 발은 그 아래 땅 높이만큼만 옮기므로 클립의 걸음과 발 들기는 그대로 남는다. 땅을 디딘 발은 경사에 맞춰 기운다(`maxFootTilt`). 골반이 내려가면 눈과 카메라도 같이 내려간다.
  - 땅과 골반 높이는 월드 기준으로 기억한다. 그래서 캡슐이 계단을 한 칸씩 툭 오르내려도 발이 계단을 뚫지 않는다. 높아진 땅에는 바로 올라서고, 낮아진 땅과 골반은 `footSettleTime`에 걸쳐 내려간다.
  - 실행 중 Scene 뷰에서 플레이어를 선택하면 발마다 찾은 땅(초록 선)과 발목 목표(노란 구)가 보인다.
  - 팔·발 IK는 같은 두 뼈 계산([TwoBoneIK](../Scripts/Player/Body/TwoBoneIK.cs))을 쓴다.
  - **발목 제한**([AnkleLimit](../Scripts/Player/Body/AnkleLimit.cs)): 모든 상태에서 발이 정강이 축으로 돌아간 각도(`Body.maxAnkleTurn`)와 옆으로 꺾인 각도(`Body.maxAnkleRoll`)를 Hiro 기본 자세 기준으로 자른다. 발끝을 위아래로 젖히는 것은 자르지 않는다. 리타깃이 잘못된 클립이나 섞인 클립, 다리 IK 때문에 발목이 꼬여도 실제 발목이 가능한 범위 안에 머문다.

## 실행 중 생성되는 분류와 혈흔 풀

Play 중 효과가 필요하면 `EFFECTS → Audio / World / Screen`을 자동으로 만들고 소리·월드 효과·화면 표시를 모은다. 콘솔 소환 적은 `ENEMIES → Console`, 웨이브 적은 `ENEMIES → Waves`에 생성된다. 수동으로 빈 오브젝트를 미리 만들 필요는 없다. 스크립트 변경 후에는 Play를 종료하고 다시 시작해 생성 위치를 확인한다.

`EFFECTS → World → Impact VFX → Blood Decals (Pool)`의 회색 혈흔은 다음 타격에 재사용할 대기 오브젝트다. 풀은 **최대 32개**로 제한되며 무한히 증가하지 않는다. 비활성 혈흔은 표시하지 않고 수명 업데이트도 건너뛴다. 반복 생성·삭제 부하를 줄이기 위해 풀을 유지한다. 화면에 남는 시간은 [GunFeel.asset](../Resources/Combat/GunFeel.asset)의 `Blood Decal Seconds`로 조절한다. 이 시간이 지나면 1.2초 동안 흐려진 뒤 풀로 돌아간다.

적의 분류와 웨이브 집계는 별개다. 웨이브 일시정지·중단은 해당 WaveManager가 추적하는 적에게만 적용하며 콘솔 적은 그대로 유지한다.
