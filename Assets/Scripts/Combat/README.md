# 공용 전투

직접 무기를 추가하거나 문제를 찾는 순서는 [전투 제작 안내](../Docs/CombatFoundationGuide.md)를 참고한다. 전체 폴더 지도는 [스크립트 길잡이](../README.md)에 있다.

4슬롯·주먹 전투와 이후 획득/진화/궁극기 설계는 [무기 시스템 기획](../Docs/WeaponSystemDesign.md)을 기준으로 한다.

- `Damage/`: 공격 ID, 피해량, 피해 종류, `IDamageReceiver` 계약.
- `HitDetection/`: 실제로 무엇에 맞았는지 확인하고 피해/회피를 전달.
- `Weapons/Data/`: 여러 무기가 공유할 설정 형식. 실제 무기별 수치는 `Assets/Combat/Weapons`의 asset.
- `Weapons/Core/`: 무기 한 자루의 탄약·장전·발사 간격과 입력 의도. 마우스/카메라를 직접 참조하지 않는다.

플레이어 전용 입력·발사 연결은 `Scripts/Player/Combat`, 표시와 연습 표적은 `Scripts/Debug/Combat`, 편집기 도구는 `Assets/Editor/Combat`에 둔다.
