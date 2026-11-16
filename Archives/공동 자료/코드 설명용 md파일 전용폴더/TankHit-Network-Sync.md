# 적·아군 전차 피격 동기화

아군 Tiger와 적 Sherman은 같은 `TankHealth`로 포탄 피해를 처리한다. Fusion Host Mode에서 Host가 실제 충돌로 피해를 확정하고, Guest는 대상 전차의 HP와 피격 횟수를 받는다. 공격 방향에 관계없이 맞은 전차가 자기 체력 상태를 소유한다.

## 1. 충돌에서 피해 확정까지

1. Host에서 생성·초기화된 `TankProjectile`이 물리 충돌을 감지한다. `OnCollisionEnter()`는 발사한 전차와의 충돌을 제외하고 `Finish()`를 호출한다. `HasEnded`가 이미 처리된 포탄의 중복 처리를 막는다.
2. `Finish()`는 충돌한 Collider의 부모 계층에서 `TankHealth`를 찾아 `ApplyDamage(Damage, ShotId, …)`를 호출한다. Tiger가 Sherman을 맞히거나 Sherman이 Tiger를 맞힐 때 같은 경로를 거친다. 네트워크 발사체는 Host가 제거한다. [TankProjectile.cs](../../../source/Claustrophobia/Assets/TankPrototype/Scripts/TankProjectile.cs)
3. 네트워크 전차의 `TankHealth.ApplyDamage()`는 Spawn 완료, 유효한 `NetworkObject`, 실행 중인 서버 Runner, `StateAuthority`를 확인한다. 비활성 대상, 이미 파괴된 대상, 0 이하 또는 유한하지 않은 피해는 거부한다. 유효한 명중은 HP를 최대 0까지 줄이고 피격 횟수를 한 번 늘린다. `NetworkObject`가 없는 로컬 시험 표적은 로컬 HP와 피격 횟수를 사용한다. [TankHealth.cs](../../../source/Claustrophobia/Assets/TankPrototype/Scripts/TankHealth.cs)

## 2. Fusion 상태와 화면 갱신

| `TankHealth`의 네트워크 필드 | 역할 |
| --- | --- |
| `NetworkHP` | 현재 HP. `CurrentHP`의 원본이며 `Destroyed`는 HP가 0 이하인지로 판정한다. |
| `NetworkMaxHP` | Host가 Spawn 시 확정한 최대 HP. `Capacity`로 읽는다. |
| `NetworkHits` | 적용된 피격 횟수. `DamageHitCount`로 읽는다. |

Host는 `Spawned()`에서 HP를 최대치로, 피격 횟수를 0으로 초기화한다. HP가 처음 0에 도달하면 `Died`를 호출한다. Sherman의 `TankAI`는 이 이벤트를 받아 이동과 공격을 중단한다. [TankHealth.cs](../../../source/Claustrophobia/Assets/TankPrototype/Scripts/TankHealth.cs) · [TankAI.cs](../../../source/Claustrophobia/Assets/TankPrototype/Scripts/TankAI.cs)

각 클라이언트는 `NetworkHP`의 `OnChangedRender` 콜백에서 파괴 색상을 적용한다. 최초 Spawn에서도 같은 외형 갱신을 실행하므로 이미 파괴된 Sherman에 늦게 접속한 Guest도 파괴 외형을 본다. 아군 내부 HUD가 쓰는 `TankStatus`는 `TankHealth`에서 HP·최대 HP·피격 횟수를 읽으며, 레버 위치는 `TankStatus`가 복제한다. 발사 번호, 마지막 피해량, 마지막 피격 로그 문자열은 로컬 진단값이다. [TankHealth.cs](../../../source/Claustrophobia/Assets/TankPrototype/Scripts/TankHealth.cs) · [TankStatus.cs](../../../source/Claustrophobia/Assets/TankPrototype/Scripts/TankStatus.cs)

## 3. 씬과 프리팹 연결

- `TankTest`의 Tiger와 Sherman 프리팹, 네트워크 시험 표적 프리팹은 `TankHealth`를 각 `NetworkObject`의 `NetworkedBehaviours` 목록에 등록한다. [TankTest.unity](../../../source/Claustrophobia/Assets/TankPrototype/Scenes/TankTest.unity) · [EnemyTank-Sherman.prefab](../../../source/Claustrophobia/Assets/TankPrototype/Prefabs/EnemyTank-Sherman.prefab) · [TankAINetworkTestTarget.prefab](../../../source/Claustrophobia/Assets/TankPrototype/Prefabs/TankAINetworkTestTarget.prefab)
- `TankAINetworkTest`의 **아군 Tiger 공격** 버튼은 Tiger 프리팹의 `Aim Point`에 연결된다. **지정 표적 공격** 버튼은 별도 고정 시험 표적을 사용한다. 시험 패널에서 클라이언트별로 Sherman, Tiger, 고정 표적의 HP와 피격 횟수를 읽을 수 있다. [TankAINetworkTest.cs](../../../source/Claustrophobia/Assets/TankPrototype/Scripts/TankAINetworkTest.cs) · [TigerTank.prefab](../../../source/Claustrophobia/Assets/TankPrototype/Prefabs/TigerTank.prefab)

## 4. 재현과 기존 검증 결과

1. 같은 빌드로 `TankTest`에 Host와 Guest를 접속시킨다.
2. Host의 **적 AI 네트워크 시험 → 적 생성 / 초기화 → 아군 Tiger 공격**을 실행한다. 첫 명중 후 **전체 정지**를 누르고 두 화면에서 Tiger의 HP와 피격 횟수를 비교한다.
3. Tiger의 포수로 전환해 Sherman을 맞힌다. 명중마다 Sherman의 HP가 한 번 감소하고, HP가 0이면 양쪽 화면에서 파괴 색상이 적용되는지 확인한다.
4. Sherman이 파괴된 뒤 다른 Guest를 접속시켜 처음 보이는 HP와 외형을 확인한다.

구현 당시 Windows 빌드는 오류 0건으로 완료했다. 실제 Photon 세션에서 Sherman → Tiger 한 발로 HP `100 → 75`, Tiger → Sherman 네 발로 HP `100 → 75 → 50 → 25 → 0`을 확인했다. Guest의 HP와 피격 횟수, 늦게 접속한 Guest의 Sherman 파괴 외형도 확인했다. Tiger의 조준과 발사는 검증 스크립트로 수행했다. 로컬 원본 로그는 Unity 프로젝트의 `Builds/TankHitSync/`에 있다.
