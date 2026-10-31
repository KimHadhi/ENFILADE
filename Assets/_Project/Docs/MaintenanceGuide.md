# 설정·유지보수 안내

[프로젝트 안내](../../../README.md) · [전투 제작](CombatFoundationGuide.md) · [이동 안내](MovementGuide.md) · [플레이어 몸](PlayerBodyGuide.md) · [검증 안내](ValidationGuide.md)

## 밸런스 표 (Data Tables)

전투·이동·몸·소리의 숫자는 [Resources/Tables](../Resources/Tables)의 CSV 표에 있다. **표를 저장하면 컴파일 없이 바로 적용되고, Play 중이면 실행 중인 게임에도 즉시 반영된다.** Excel, Google Sheets(파일 → 가져오기/CSV로 다운로드), VS Code 등 CSV를 여는 프로그램이면 무엇이든 된다.

| 표 | 내용 | 행(row) |
| --- | --- | --- |
| [Player.csv](../Resources/Tables/Player.csv) | 자세별 캡슐·눈높이, 이동·속도 쌓기·점프·슬라이드·다이브, 회피·파쿠르·착지·밀치기·사다리·집라인·평균대, 최대 체력, 궁극기 보유량·소비량·충전량, 주먹·패링·가드 브레이크, 처형, 몸 애니메이션·손, 이동 펀치·큰 낙하 화면·평균대 표시, 발소리·동작 소리·숨소리·바람·큰 낙하 소리 | `Posture` `Move` `Ability` `Vitals` `Stats` `Ultimate` `Fists` `Execution` `Body` `Hands` `Punch` `FallScreen` `Sounds` `Footsteps` `MovementAudio` `Breathing` `Wind` `FallAudio` `FixtureAudio` `BalanceGauge` |
| [SoundCues.csv](../Resources/Tables/SoundCues.csv) | 동작마다 나는 소리 묶음(볼트·착지·발소리…)과 적에게 들리는 거리 | 큐 이름 (`Vault`, `Land`, `StepRun` …) |
| [Sounds.csv](../Resources/Tables/Sounds.csv) | 소리 묶음 폴더별 볼륨·피치 | `<묶음>/<폴더>` (`Movement/Bar/Grab` …) |
| [Surfaces.csv](../Resources/Tables/Surfaces.csv) | 표면 종류의 부모와 찾을 키워드 | 표면 이름 (`Concrete` …) |
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
- 메뉴는 코드로 만든 화면이다([UI/Settings](../Scripts/UI/Settings)). 항목은 `SettingsMenuView`, 모양은 `SettingsWidgets`에서 바꾼다. EventSystem이 없는 씬에서는 메뉴를 처음 열 때 하나 만든다.

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
| V 밀치기 | 60 N·s (`Ability.propShoveImpulse`) | 8m/s (`Ability.shovePushSpeed`) | 8m/s | 3m/s |
| 총알 1발 | 소총 3·권총 4 N·s ([Weapons.csv](../Resources/Tables/Weapons.csv) `propImpulse`) | 12m/s | 3·4m/s | 0.15·0.2m/s |
| 슬라이딩·다이브·돌진 밀치기로 부딪힘 | 몸무게 70kg과 함께 밀려 나감 (`Ability.propBodyMass`) | 플레이어 속도 | 플레이어 속도로 밀려 나가고 플레이어는 거의 그대로 | 둘 다 플레이어 속도의 78%로 진행 |

슬라이딩·다이브·돌진 밀치기로 부딪히면 물체와 플레이어가 한 속도(몸무게 ÷ 둘의 질량 합 × 플레이어 속도)로 함께 나아간다. 물체가 튕겨 나가며 더 빨라지지는 않는다. 그 속도가 원래의 30% 아래가 되는 물체(플레이어 몸무게의 약 2.3배, 160kg 이상)는 지금처럼 벽으로 취급해 튕기거나 멈춘다.

움직이는 물체에는 오목한 메시 충돌체를 쓸 수 없어서 볼록 껍질(convex, 최대 255면)로 감싼다. 배·공·상자처럼 거의 볼록한 모양은 이것으로 충분하다. 도넛·의자처럼 크게 오목한 모양은 자식 오브젝트에 Box·Sphere·Capsule 충돌체를 여러 개 나눠 붙이고 `MeshCollider`를 지운다. 움직이지 않는 벽·지형은 `Rigidbody` 없이 convex를 끈 `MeshCollider`를 쓰면 모양 그대로 충돌한다.

물체마다 바꿀 값은 루트 `Rigidbody → Mass` 하나다. 밀기 세기는 모든 물체에 공통이며 위 표의 위치에서 바꾼다. 프리팹이 이미 있으면 도구는 덮어쓰지 않는다.

## 화면 후처리

플레이어 카메라는 URP 후처리를 켠다([PlayerCameraEffects](../Scripts/Player/Camera/PlayerCameraEffects.cs)가 시작할 때 켬). 그래서 파이프라인 볼륨 프로필 [SampleSceneProfile](../Settings/Rendering/SampleSceneProfile.asset)(Neutral 톤매핑, Bloom 0.25, 비네트 0.2)이 항상 적용된다. 화면 색감을 바꾸려면 이 프로필을 고친다. 큰 낙하 효과는 [Resources/Rendering/FallWarp](../Resources/Rendering/FallWarp.asset) 볼륨 프로필을 실행 중에 복사해 우선순위 100의 전역 볼륨으로 쓴다. 이 에셋이 프로젝트에 있어야 빌드에서 렌즈 왜곡·색수차 셰이더가 빠지지 않는다(URP의 쓰지 않는 후처리 변형 제거). 세기는 에셋이 아니라 `FallScreen` 행에서 바꾼다.

## 실행 중 생성되는 분류와 혈흔 풀

Play 중 효과가 필요하면 `EFFECTS → Audio / World / Screen`을 자동으로 만들고 소리·월드 효과·화면 표시를 모은다. 콘솔 소환 적은 `ENEMIES → Console`, 웨이브 적은 `ENEMIES → Waves`에 생성된다. 수동으로 빈 오브젝트를 미리 만들 필요는 없다. 스크립트 변경 후에는 Play를 종료하고 다시 시작해 생성 위치를 확인한다.

`EFFECTS → World → Impact VFX → Blood Decals (Pool)`의 회색 혈흔은 다음 타격에 재사용할 대기 오브젝트다. 풀은 **최대 32개**로 제한되며 무한히 증가하지 않는다. 비활성 혈흔은 표시하지 않고 수명 업데이트도 건너뛴다. 반복 생성·삭제 부하를 줄이기 위해 풀을 유지한다. 화면에 남는 시간은 [GunFeel.asset](../Resources/Combat/GunFeel.asset)의 `Blood Decal Seconds`로 조절한다. 이 시간이 지나면 1.2초 동안 흐려진 뒤 풀로 돌아간다.

적의 분류와 웨이브 집계는 별개다. 웨이브 일시정지·중단은 해당 WaveManager가 추적하는 적에게만 적용하며 콘솔 적은 그대로 유지한다.
