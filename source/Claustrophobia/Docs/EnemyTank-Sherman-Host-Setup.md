# Sherman 적 전차: 구성·호스트 실행 연결

2026-11-11, `ksc`의 `bfe477c`를 기반으로 구성했다.

## 프리팹과 원본 에셋

- 프리팹: `Assets/TankPrototype/Prefabs/EnemyTank-Sherman.prefab`
- 원본 모델: `Assets/polyperfect/Low Poly War Pack/- Meshes/- Vehicles/Tank-Sherman.fbx`.
- 몸체·궤도는 원본 모델 에셋을 직접 참조하는 `Model` 인스턴스로 구성한다. 씬에 배치된 오브젝트는 제작 원본이나 참조 대상으로 사용하지 않는다.
- 포탑·포신은 같은 FBX의 원본 메시·재질을 사용하는 별도 조준축으로 구성한다. `Model` 인스턴스에서는 중복 포탑·포신과 기존 Animator를 제외한다. 원본 FBX·재질 파일은 변경하지 않는다.
- 전차 기능에 필요한 컴포넌트와 참조는 프리팹에 저장하며, 기존 적 프리팹 GUID는 유지한다.
- `TankTest`의 기존 Sherman은 은신용 배치다. 해당 씬은 작업 전 상태로 복구했고 적 프리팹을 배치하지 않는다.

| 구성 | 역할 |
| --- | --- |
| `Rigidbody` + BoxCollider 3개 | 차체·궤도 범위, 포탑, 포신의 물리 구성 |
| `NetworkObject`, `NetworkTransform` | 네트워크 등록과 차체 결과 공유 |
| `TankMotor` | 기존 좌우 레버 이동·회전 |
| `TankAim` | 기존 포탑·포신 각도와 네트워크 상태 |
| `TankGun`, `TankProjectile` | 기존 약실·발사·포탄 생성·충돌 |
| `TankHealth` | 기존 HP와 파괴 판정 |
| `TankAI` | 호스트 검사와 전차 기능 호출 |

플레이어용 `TankInput`, `TankControl`, `TankCrewRegistry`, 카메라와 내부 씬 연결은 적 프리팹에 넣지 않는다.

## 호스트 실행

`TankAI`는 네트워크 등록과 실행 중인 서버·상태 권한을 확인한다. 호스트에서만 `FixedUpdateNetwork()`의 AI 처리가 실행된다. 게스트는 AI 입력을 실행하지 않고 복제된 결과를 표시하며 Rigidbody는 kinematic으로 둔다.

현재 단계는 행동을 연결할 기반이다. 초기 명령은 모두 0이며 스스로 목적지를 정하거나 자동 공격하지 않는다.

| 호스트 호출 | 결과 |
| --- | --- |
| `SetDrive(left, right)` | 좌우 레버 명령 저장, 각 값은 -1~1 |
| `SetAim(turret, barrel)` | 좌우·상하 조준 입력 저장, 각 값은 -1~1 |
| `RequestWeapon(action)` | 삽입·닫기·열기·발사를 해당 틱 이후 처리하도록 요청 |
| `StopActions()` | 레버·조준·대기 중 무기 요청과 잔여 주행 정리 |

무기 요청의 `true`는 요청을 접수했다는 뜻이다. 실제 처리 결과는 `TankGun.State`, `ShotCount`, `LastWeaponResult`로 확인한다. 기존 약실 조건을 그대로 사용하며, 조준을 반영한 뒤 `AfterTick`에서 실행한다.

AI 판단은 호스트에서 한 번 수행하고, 결과 공유는 Fusion의 네트워크 상태와 오브젝트 경로를 사용한다. 호스트 검사만으로 새 상태가 자동 동기화되는 것은 아니다. 현재 차체·조준·약실·포탄 경로를 연결했으며, `TankHealth`의 HP와 파괴 외형을 세 화면에 공유하는 작업은 피탄 동기화 담당 작업과 연결해야 한다.

## 다음 작업

이 기반 위에 지정 지점 이동·정지, 지정 표적 조준과 자동 장전·발사를 연결한다. 자동 탐지·내비메시·상세 FSM은 후속 설계 범위다.

구성 수정 기록은 로컬 `Builds/ShermanAssetCorrection/`, 이전 컴파일 기록과 원본 씬 백업은 `Builds/ShermanHostSetup/`에서 확인한다. 컴파일·프리팹 참조 확인과 실제 호스트/게스트 통합 검증의 범위를 구분해서 기록한다.

호스트 실행 코드의 런타임 C# 컴파일은 통과했다. `TankTest`는 작업 전 백업과 바이트 단위로 일치하며 Git diff가 없다. 적 프리팹의 모델 원본 참조·내부 참조·기존 GUID 유지와 씬 의존성 제거를 파일에서 확인했다. 수정된 모델 참조의 Unity 재가져오기, Play Mode 실행과 세 인스턴스 동기화는 아직 검증하지 않았다. 프리팹은 임시 구성 도구 없이 동작하도록 저장했으며, `Assets`에는 임시 도구가 남아 있지 않다.
