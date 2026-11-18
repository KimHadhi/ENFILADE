# Pickups — 줍기

맵에 놓이는 총·탄약·구급상자·실드·돈.

전체 지도는 [스크립트 길잡이](../README.md), 프로젝트 구조는 [구조 안내](../../../../Docs/Architecture.md)에 있다.

## 무엇을 고치려면 어디를

| 변경할 내용 | 시작할 파일·설정 |
| --- | --- |
| 현장 줍기 (6D: 맵의 줍기 자리마다 판마다 무작위로 총·탄약 더미·구급상자·실드 셀·돈 뭉치, 좀비가 가끔 떨굼. 탄약·돈은 지나가면 줍고 총·구급상자·실드는 바라보고 [F], 모두 먼저 줍는 사람. 탄약은 가진 총마다 탄창 몇 개(탄창 하나로 치는 발 수는 `Weapons.csv` `supplyRounds`, 비우면 탄창 그대로. 그 총이 채워지는 끝까지: PlayerWeaponSlots.ReserveCap, 공용 카드 '탄약 주머니'가 끝을 올리고 '청소부'가 양을 늘린다). 총은 빈 칸 아니면 든 총과 바꾸고 바꾼 총은 바닥에. 바닥의 총은 "그 총"이다(2026-11-18 "필드 무기 교체 및 탄약 시스템 개편"): 어느 총인지와 안에 든 탄, 함께 놓인 탄을 가지고 모든 PC에 알려지며, 주우면 든 탄 그대로 오고 내려놓으면 든 탄 그대로 놓인다. 예비 탄약은 플레이어의 것이라 총을 바꿔도 그 종류의 예비 탄약은 플레이어에게 남고 다시 주우면 그대로다(전에는 바닥의 총이 표의 줄 번호뿐이어서 내려놓으면 탄창과 예비 탄약이 사라지고 주우면 새로 가득 생겼다. 빈 총을 버렸다 주우면 가득 찼고, 모아 둔 탄약은 사라졌다). 맵이 놓거나 좀비가 떨군 총은 탄창 가득에 처음 예비 탄약이 곁에 놓여 있고 처음 줍는 사람이 그것을 한 번 받는다(Pickups.csv `rounds`·`reserve`, 비우면 가득). 이미 든 것과 같은 종류의 총은 줍지 않고 그 총의 탄만 내 예비 탄약에 챙긴다(상한까지. 총은 남은 탄과 함께 그 자리에 남는다. 전에는 이때 가진 총이 전부 가득 찼다). 표에 줄이 없는 총(상점에서 산 총)도 내려놓으면 바닥에 놓인다(전에는 사라졌다). [F] 안내는 총에 든 탄과 곁의 예비 탄약을 보여 준다. 총을 내려놓는 한 줄은 `FieldPickups.Drop(플레이어, 칸)`이고 Tab 메뉴의 드랍이 생기면 이것을 부른다(지금은 콘솔 `/drop`). 색 빛기둥·외곽선, 주우면 고리가 퍼짐. 주운 것은 화면 아래 가운데 목록에 쌓인다(레포데식: 종류 색 칸의 아이콘, 이름, 오른쪽 끝에 얻은 양. 새 것이 맨 아래, 2.4초 뒤 사라짐). 종류마다 줍는 소리가 다르고, 탄약·총은 탄약 표시가, 체력·실드는 체력 띠가 한 번 깜빡이며, 든 총이 살짝 내려갔다 돌아온다) | [FieldPickups](FieldPickups.cs) (방장이 상태를 가지고 나눔, [F] 안내, 주웠을 때의 반응 `Pop`), 목록 [PickupFeedHUD](../UI/Hud/Team/PickupFeedHUD.cs), 깜빡임 [HudBlink](../UI/Hud/HudBlink.cs) (`AmmoHUD.Blink`, `AdaptiveHealthHUD.Blink`), 총 내림 [FPSBody.Reach](../Weapons/FPSBody/FPSBody.Reach.cs), 소리 `KitSound` Pick 계열, 모습 [PickupView](PickupView.cs) (외곽선 셰이더 Resources/Combat/Shaders/PickupOutline), 모델 [PickupProps](PickupProps.cs) (탄약 상자와 돈은 Resources/Combat/PickupLibrary: 돈은 Heist Essentials 팩의 돈다발이고, G로 떨군 돈 뭉치([MoneyDrops](../Economy/MoneyDrops.cs))도 같은 모양), 자리 [PickupSpot](PickupSpot.cs) (회색 맵 빌더 GreyboxBuilder.Pickup), 목록 Tables/Pickups, 규칙 Tables/PickupRules |
