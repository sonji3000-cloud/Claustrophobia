# 지정 표적 조준·공격

이동부터 HP·자기 파괴 중단까지 이어서 확인하려면 [HP·파괴 중단·통합 확인](TankAI-HPIntegration.md)의 `TankAIIntegrationTest`를 사용한다.

`TankAI.Attack(Transform)`으로 지정한 표적을 제자리에서 조준하고 반복 공격한다. 판단과 명령 실행은 기존 `IsHost` 검사에 따라 호스트에서만 수행한다. 표적 자동 탐색, 추격, 이동 표적의 선행 조준은 이번 작업에 포함하지 않는다.

## 바로 테스트하기

1. 먼저 Play를 종료한다. Unity Project 창에서 `Assets/TankPrototype/Scenes/TankAttackTest.unity`를 더블클릭한다. Hierarchy 상단의 씬 이름이 **TankAttackTest**인지 확인한 뒤 Play를 누른다. `TankMoveTest`는 이동 전용 씬이다.
2. 자동으로 Fusion Single 세션이 시작되고 `EnemyTank-Sherman` 한 대가 생성된다. 포탑과 포신이 오른쪽 앞의 `Attack Target`을 향해 돌아간 뒤 발사한다.
3. Hierarchy에서 **Tank Attack Demo**를 선택한다. Inspector의 **Tank Attack Demo (Script)** 아래에 공격 상태, 약실 상태, 실제 발사 수, 표적 HP가 표시된다. Enemy 프리팹 자산을 선택하는 것이 아니다.
4. 표적 HP가 **100 → 75 → 50 → 25 → 0**으로 줄어드는지 확인한다. 기본 피해는 25이며, 약 10초 정도 관찰하면 된다. HP가 0이 되면 표적이 어두워지고 `TargetDown` 상태에서 추가 공격을 멈춰야 한다.
5. Console에서도 `Tank fire: Fired #…`, `HP … -> …`, `[AI ATTACK] TargetDown`을 확인할 수 있다. 성공 기준은 발사 로그만 생기는 것이 아니라 실제 충돌 후 HP가 줄어드는 것이다.

중간에 멈추려면 같은 Inspector의 **공격 중지 (Stop Attack)** 버튼을 누른다. **지정 표적 공격 (Attack Target)** 버튼으로 살아 있는 표적을 다시 공격한다. 이미 발사된 포탄은 계속 날아가므로 중지 직후 한 번 더 명중할 수 있다. 파괴된 표적은 Play를 종료했다가 다시 시작하면 초기화된다.

Demo와 NetworkRunner는 서로 다른 루트 오브젝트로 배치했다. Fusion 실행 시 `Demo Network Runner`는 `DontDestroyOnLoad`로 이동할 수 있지만, 조작용 `Tank Attack Demo`는 원래 씬에 남는다. 이전 구성으로 이미 실행 중이면 `DontDestroyOnLoad`를 펼쳐 Demo를 찾거나, Play를 종료하고 수정된 씬을 다시 열어야 한다.

추가로 다음을 확인할 수 있다.

| 시험 | 조작 | 기대 결과 |
| --- | --- | --- |
| 표적 위치 변경 | Play 중 `Attack Target` 루트를 옮긴다. `Aim Point`는 표적 포탑 안에 둔다. | 현재 위치를 다시 조준한다. 표적 이동 속도를 예상하는 선행 조준은 하지 않는다. |
| 가림 | 적과 표적 사이에 Collider가 있는 큰 Cube를 둔다. 예: 위치 `(3, 2, 7)`, 크기 `(4, 4, 1)` | 포가 장전되어 있어도 `Blocked / Shot path obstructed`로 발사를 보류한다. 벽을 치우면 다시 공격한다. |
| 사거리 초과 | 표적을 `(0, 0, 100)`으로 옮긴다. | `Blocked / Target outside ballistic range`로 발사를 보류한다. |
| 표적 비활성화 | `Attack Target` 오브젝트를 비활성화한다. | 명령을 취소하고 `Idle / Target unavailable`로 바뀐다. 다시 켠 뒤 공격 버튼으로 재지정한다. |

이 씬의 `Attack Target`은 기존 `TankTarget` 프리팹의 구성요소를 복사한 테스트 표적이다. 자식 `Aim Point`를 포탑 중심 `(0, 1.3, -0.85)`에 두었다. 표적 루트의 바닥 위치 대신 피격 가능한 부분을 겨냥하기 위한 점이다.

## 호스트에서 호출하기

```csharp
// 표적 TankHealth 오브젝트 자신 또는 그 아래의 조준점.
bool accepted = enemyAI.Attack(targetAimPoint);

enemyAI.StopAttack();
bool destroyed = enemyAI.AttackStatus == TankAI.AttackState.TargetDown;
```

- 네트워크 Spawn 완료 후 호스트에서 호출한다. 반환값은 명령 접수 여부이며 명중 여부가 아니다.
- 조준점은 활성 상태이며, 자신 또는 부모에 살아 있는 `TankHealth`가 있어야 한다. 자기 자신은 표적으로 삼을 수 없다.
- 새 `Attack`은 이전 이동을 멈추고 표적을 교체한다. 표적의 현재 Transform 위치를 매 틱 읽는다.
- 유효한 `MoveTo`, `SetDrive`, `SetAim`, `RequestWeapon` 명령은 자동 공격을 취소한다. 잘못된 요청은 기존 명령을 유지한다.
- `StopMove`는 이동만, `StopAttack`은 자동 공격만, `StopActions`는 둘 다 취소한다.
- 공격 취소 시 대기 중인 AI 무기 요청을 지운다. 이미 장전한 포탄이나 이미 발사된 포탄은 제거하지 않는다.
- 적 전차 사망·비활성화, 표적 파괴·소실 시 후속 공격을 중단한다. 발사 요청 소비 시점에도 표적의 생존 여부를 다시 검사한다.

## 동작과 상태

| AttackStatus | 의미 |
| --- | --- |
| `Idle` | 공격 명령 없음 또는 취소됨 |
| `Aiming` | 포탑·포신을 조준 중 |
| `Reloading` | 약실 열기·삽입·닫기 또는 발사 후 회복 중 |
| `Attacking` | 조준·탄도 검사를 통과해 발사 요청을 전달함 |
| `Blocked` | 사거리, 포신 각도, 장애물, 무기 설정 등으로 공격 보류. 사유는 `AttackBlockReason`에서 확인 |
| `TargetDown` | 지정 표적이 파괴되어 공격 종료 |

각도 조정과 장전은 함께 진행하므로, 조준 도중에도 `Reloading`이 표시될 수 있다. `Attacking`은 무기 요청을 전달한 상태이며, 실제 발사 여부는 `TankGun.ShotCount`, 실제 명중 여부는 `TankHealth.CurrentHP`로 확인한다.

포탄 속도·중력·물리 시간 간격·수명을 사용해 낮은 탄도의 발사 방향을 계산한다. 기존 `TankBallisticStep`의 중력을 먼저 적용하는 방식에 맞췄다. 차체 방향을 기준으로 포탑 좌우 각도와 포신 상하 각도를 구하고, 기존 `TankAim`의 회전 속도와 각도 제한을 지킨다. 기본 조준 허용 오차는 0.5도다.

장전은 기존 `TankGun`의 `OpenEmpty → Insert → Close → Fire → Recovering` 경로를 사용한다. 초기 약실이 열려 있으면 `Insert`부터 시작한다. 실제 포탄 생성과 피해는 기존 `TankGun`·`TankProjectile`·`TankHealth` 경로가 담당한다. AI가 직접 HP를 깎지 않는다.

발사 전 기존 `TankBallistics.Predict`로 포탄 구체의 경로를 검사한다. 자신의 Collider를 제외한 첫 충돌 대상이 지정 표적에 속할 때만 발사한다. 기본 검사 주기는 0.1초이며, 적 프리팹의 자동 화면 갱신은 꺼 두어 게스트에서 AI용 탄도 예측을 반복하지 않는다.

`AttackStatus`와 표적 참조는 호스트의 내부 상태다. 차체는 기존 `NetworkTransform`, 조준 각도와 약실·발사 수는 기존 Networked 값, 포탄은 기존 `Runner.Spawn`을 사용한다. 이번 변경에서 새로운 RPC나 HP 동기화는 추가하지 않았다. `TankHealth`의 HP는 현재 로컬 값이므로 세 클라이언트의 HP 표시 동기화는 별도 연결이 필요하다.

## 검증 범위

런타임과 Editor 어셈블리 C# 컴파일을 통과했다. 기존 경고 2건 외에 새 컴파일 오류는 없다. `Builds/TankTargetAttack/AttackChecks.cs`는 실제 `TankAI.cs`, `TankAim.cs`, `TankMotor.cs`, `TankAimSolver.cs`를 엔진 대체 객체와 함께 실행한다. 탄도 계산, 회전·장전 요청, 벽에 막힌 예측 결과, 취소·사망·호스트 제한 및 기존 이동 회귀를 확인한다.

이 검사는 네이티브 PhysX 충돌, Unity 씬 로딩, Fusion 통신을 실행한 검사가 아니다. Unity Play Mode의 실제 명중과 세 클라이언트 동시 접속 검증은 별도로 남아 있다. 테스트 결과는 `Builds/TankTargetAttack/logic-results.txt`에 저장한다.

기존 `TankTest` 씬과 원본 Sherman 모델은 수정하지 않았다. `TankMoveTest`도 조작용 Demo가 씬에 남도록 Runner를 별도 루트로 분리했다. 공격 테스트는 `TankAttackTest`에서만 자동 실행된다.
