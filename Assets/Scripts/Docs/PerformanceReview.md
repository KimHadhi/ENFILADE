# 이동·공격 코드 성능 지적 검토

2026-09-28의 코드와 Unity 공식 API 문서를 확인했다. Profiler로 실행 중 비용을 측정한 결과는 아니며, 이 검토 때문에 공격 판정 구현을 바꾸지는 않았다.

## 배열 할당: 지적이 맞음, 프레임 드랍 원인이라는 단정은 불가

현재 [PlayerCombatActions.HitForward](../Player/Combat/PlayerCombatActions.cs)는 `SphereCastAll`로 결과 배열을 만들고 거리순 정렬한다. 예전 클래스 이름은 MovementCombat이었다. 밀치기 1회보다 **태클 중 매 프레임 반복 호출**되는 부분이 먼저 살펴볼 대상이다.

[MovementTrainingAttacker](../Debug/Movement/MovementTrainingAttacker.cs)는 지금 직접 RaycastAll을 호출하지 않는다. 대신 [AttackTrace.Raycast](../Combat/AttackTrace.cs)가 RaycastAll과 정렬을 실행하므로 할당 문제 자체는 남아 있다. 적이 많다는 사실만으로 GC 스파이크를 확정할 수는 없다. 공격 빈도·전체 할당량·대상 기기에서의 GC.Alloc과 프레임 시간을 확인해야 한다.

미리 만든 결과 배열과 NonAlloc API를 쓰면 물리 질의 결과 배열의 반복 할당을 제거할 수 있다. 하지만 32칸이 가득 찬 경우의 정책도 필요하다. 결과 순서가 보장되지 않고, 버퍼가 가득 차면 가장 가까운 충돌이 누락될 수 있다. 반환된 일부 결과만 정렬해도 누락된 벽을 복구하지 못한다. [Unity RaycastNonAlloc 문서](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics.RaycastNonAlloc.html), [SphereCastNonAlloc 문서](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics.SphereCastNonAlloc.html).

밀치기/태클은 현재 가장 가까운 고체 하나만 처리한다. 따라서 개선 시에는 단일 `SphereCast(out RaycastHit)`가 기존 접촉 동작을 보존하는지부터 검토할 수 있다. 모든 충돌을 모아서 정렬할 필요가 없을 가능성이 크다. 반면 AttackTrace는 공격자 자신의 자식 Collider를 건너뛰므로 같은 방식으로 단순 교체하면 안 된다. 두 경우 모두 벽 차폐와 회피 경로 판정을 유지해야 한다.

## HashSet 재사용: 타당

`contacted.Clear()`는 공격마다 새 HashSet을 만드는 것을 피한다. 단, 초기 삽입이나 용량 확장까지 무조건 무할당이라는 뜻은 아니다. 물리 API 하나를 바꿨다고 전체 공격 처리의 할당이 0이라고 결론 내릴 수 없다.

## EntityReference 도입: 필요성과 큰 성능 개선은 입증되지 않음

`damage as Object` 같은 참조 형변환은 새 객체나 박싱을 만드는 코드가 아니다. 이것을 물리 연산 부하와 묶어 설명하는 것은 부정확하다.

현재 첫 충돌에서 피해·밀치기·파괴 인터페이스를 `GetComponentInParent`로 각각 찾는다. 이 탐색에는 CPU 비용이 있지만, EntityReference를 달면 반드시 크게 빨라진다는 근거는 없다. 현재도 피해 수신자나 Rigidbody를 기준으로 중복 타격을 묶는다.

나중에 적의 몸통/팔다리 Collider를 하나의 전투 개체로 통합할 필요가 생기면 수신자 참조 캐시를 도입할 수 있다. 그때는 성능뿐 아니라 개체 식별, 오브젝트 풀 재사용과 캐시 갱신 규칙을 함께 설계한다. 지금은 프로파일링 없이 새 필수 컴포넌트를 추가할 이유가 부족하다.
