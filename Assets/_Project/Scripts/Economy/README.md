# Economy — 돈과 상점

돈, 돈 떨구기, 휴게소 상점, 잠입 보상.

전체 지도는 [스크립트 길잡이](../README.md), 프로젝트 구조는 [구조 안내](../../../../Docs/Architecture.md)에 있다.

## 무엇을 고치려면 어디를

| 변경할 내용 | 시작할 파일·설정 |
| --- | --- |
| 돈 (6A: 처치 점수가 돈이 된다. 점수 1점 = `earnShare`(인원별 1인 0.4 · 2인 0.7 · 3인 1 · 4인 1.3: 적 수는 인원과 상관없이 같아 여럿이면 처치가 나뉘므로, 11-11 돈이 넘친다는 말에 줄였다. 들어올 때의 인원으로 바꾼다), 숫자 + 지폐 아이콘(기호·경험치 없음), G로 친구에게 돈 떨구기, 제목 화면으로 가면 0부터. 사망세 카드는 처치 돈 +%(CombatFeedHUD.Kill의 '사망세' 줄)) | [Wallet](Wallet.cs), [MoneyDrops](MoneyDrops.cs), 표시 [CombatFeedHUD](../UI/Hud/KillFeed/CombatFeedHUD.cs)·[ScoreboardHUD](../UI/Hud/Team/ScoreboardHUD.cs), 결과 [CampaignRun](../Modes/Campaign/CampaignRun.cs) Results, 표: [Money.csv](../../Resources/Tables/Economy/Money.csv) |
| 휴게소 상점 (6B: 계산대에서 [F], 총·탄약·구급상자·실드, 내 돈으로 사기. 사면 계산대 소리가 나고 돈 칸이 번쩍이며 숫자가 줄고, 낸 돈이 칸 아래로 떨어지고, 카드에 "구매 완료", 킬 피드에 한 줄. 못 사면 거절음. 탄약은 가진 총의 탄약이 모자랄 때만 팔리고 가득하면 "탄약 가득". 11-13: 첫 화면은 위에 보급 큰 칸(탄약·실드·체력, 클래스 투척물·데탕트 주사기는 그 클래스에게만) 아래에 총 분류 판 6개, 판을 누르면 그 분류의 총만 싼 것부터(뒤로 판·Esc·Backspace로 돌아옴, 넘치면 스크롤). 번호 키로 고르기, 가진 총은 흰 꼬리표·어두운 카드, 돈이 모자란 총은 빨간 값과 모자란 돈. 분류는 Shop.csv `category`, 빈 칸(모드 총)은 총의 숫자로 저절로) | [ShopMenu](ShopMenu.cs), 소리는 [KitSound](../UI/Kit/KitSound.cs), 계산대 자리는 [GreyboxBuilder](../../Editor/Maps/GreyboxBuilder.cs) Shop, 표: [Shop.csv](../../Resources/Tables/Economy/Shop.csv) |
