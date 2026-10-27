# 검증 안내

[프로젝트 안내](../../../README.md) · [설정·유지보수](MaintenanceGuide.md)

검증은 Unity에서 직접 한다. 자동 복사본 검사 도구(`Validate-Project.ps1`)는 쓰지 않는다.

## 실제 플레이로 확인

[CombatFoundation](../Scenes/Development/CombatFoundation.unity)을 Play하고 바꾼 영역을 직접 조작한다. 자동 검사로 조작감과 화면 구성을 대신할 수 없다.

- **무기**: 슬롯 선택·사격·장전·ADS, 총기 벽 차단, 총구 화염·궤적·탄착·혈흔, 근접 전환 시 총 모델 숨김
- **근접**: 주먹 충전·연타, 벽 적중 소리, 허공 0.2초 후딜, 가드·패링·가드 브레이크, 처형
- **이동**: 평지·경사·낮은 천장·벽 접촉, 적 크기별 슬라이딩·레그돌 복구
- **HUD**: 체력·궁극기·탄약·슬롯·목표판·채팅 입력 가독성
- **밸런스 표**: Play 중 [Resources/Tables](../Resources/Tables)의 값을 저장했을 때 바로 반영되는지, Console에 `[Tables]` 경고가 없는지

## Unity 메뉴 검사 (선택)

빠르게 회귀를 확인하고 싶을 때만 쓴다. **재생을 종료하고 씬을 저장한 뒤** 실행한다. 전체 묶음은 `Tools/Validation/Run Edit Mode Checks`다. 예외나 오류가 생기면 통과가 아니다.

| 영역 | 개별 메뉴 |
| --- | --- |
| 이동 구조 | `Tools/Movement/Run Refactor Checks` |
| 이동 기능 | `Tools/Movement/Run Ability Checks` |
| 회피 반응 | `Tools/Movement/Run Dodge Response Checks` |
| 무기 기본 | `Tools/Combat/Run Foundation Checks` |
| 슬롯·주먹·가드·밀치기·처형 | `Tools/Combat/Run Four Slots and Fists Checks` |
| ADS·예비탄·헤드샷·반동·총구 추적 | `Tools/Combat/Run Ranged Combat Checks` |
| 목표 데이터 | `Tools/HUD/Run Mission Checks` |
| 궁극기 / 불꽃 / 체력 / 채팅 | `Tools/HUD/Run Ultimate HUD Checks` / `Run Ultimate Fire Checks` / `Run Player Health Checks` / `Run Chat Checks` |

**채팅·목표판 입력 충돌 검사는 Play Mode 전용**이다. CombatFoundation을 Play하고 Game 창에 포커스를 준 뒤 `Tools/HUD/Check Mission and Chat Input`을 실행한다.

`Install`, `Connect`, `Export`, `Create`, `Repair` 메뉴는 씬·프리팹·에셋을 생성하거나 저장하는 제작 도구다. 검사 메뉴와 구분한다.

## 문서·메타데이터 확인

에셋을 옮기거나 문서를 고친 뒤에는 프로젝트 루트에서 실행한다. Unity를 띄우지 않고 1초 안에 끝난다.

```powershell
& ./Tools/Test-ProjectStructure.ps1
```

문서 상대 링크, `Assets`의 누락·고아 `.meta`, 중복 GUID를 확인한다.
