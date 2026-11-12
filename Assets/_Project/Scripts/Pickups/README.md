# Pickups — 줍기

맵에 놓이는 총·탄약·구급상자·실드·돈.

전체 지도는 [스크립트 길잡이](../README.md), 프로젝트 구조는 [구조 안내](../../../../Docs/Architecture.md)에 있다.

## 무엇을 고치려면 어디를

| 변경할 내용 | 시작할 파일·설정 |
| --- | --- |
| 현장 줍기 (6D: 맵의 줍기 자리마다 판마다 무작위로 총·탄약 더미·구급상자·실드 셀·돈 뭉치, 좀비가 가끔 떨굼. 탄약·돈은 지나가면 줍고 총·구급상자·실드는 바라보고 [F], 모두 먼저 줍는 사람. 탄약은 가진 총마다 탄창 몇 개. 총은 빈 칸 아니면 든 총과 바꾸고 바꾼 총은 바닥에. 색 빛기둥·외곽선, 주우면 고리가 퍼짐. 주운 것은 화면 아래 가운데 목록에 쌓인다(레포데식: 종류 색 칸의 아이콘, 이름, 오른쪽 끝에 얻은 양. 새 것이 맨 아래, 2.4초 뒤 사라짐). 종류마다 줍는 소리가 다르고, 탄약·총은 탄약 표시가, 체력·실드는 체력 띠가 한 번 깜빡이며, 든 총이 살짝 내려갔다 돌아온다) | [FieldPickups](FieldPickups.cs) (방장이 상태를 가지고 나눔, [F] 안내, 주웠을 때의 반응 `Pop`), 목록 [PickupFeedHUD](../UI/Hud/Team/PickupFeedHUD.cs), 깜빡임 [HudBlink](../UI/Hud/HudBlink.cs) (`AmmoHUD.Blink`, `AdaptiveHealthHUD.Blink`), 총 내림 [FPSBody.Reach](../Weapons/FPSBody/FPSBody.Reach.cs), 소리 `KitSound` Pick 계열, 모습 [PickupView](PickupView.cs) (외곽선 셰이더 Resources/Combat/Shaders/PickupOutline), 모델 [PickupProps](PickupProps.cs) (탄약 상자와 돈은 Resources/Combat/PickupLibrary: 돈은 Heist Essentials 팩의 돈다발이고, G로 떨군 돈 뭉치([MoneyDrops](../Economy/MoneyDrops.cs))도 같은 모양), 자리 [PickupSpot](PickupSpot.cs) (회색 맵 빌더 GreyboxBuilder.Pickup), 목록 Tables/Pickups, 규칙 Tables/PickupRules |
