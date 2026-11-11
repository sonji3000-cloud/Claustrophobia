# 지정 지점 이동·정지

`TankAI`가 월드 좌표를 받아 기존 `TankMotor`의 좌우 레버로 이동한다. 호스트에서만 계산하고, 차체 위치·회전 결과는 기존 `NetworkTransform` 경로를 사용한다.

## 호출과 상태

```csharp
bool accepted = enemyAI.MoveTo(new Vector3(10f, 0f, 15f));

// 이동을 취소할 때
enemyAI.StopMove();

// 호스트에서 도착 여부 확인
bool arrived = enemyAI.MoveStatus == TankAI.MoveState.Arrived;
```

- `MoveTo`는 네트워크 등록 이후 호스트에서 호출한다. 반환값은 요청 접수 여부이며 도착 여부가 아니다.
- 목적지는 월드 XZ 좌표로 다룬다. Y는 주행 계산에서 제외한다.
- 이동 중 새 `MoveTo`를 받으면 목적지를 교체한다.
- 비호스트·파괴된 전차·작동할 수 없는 모터·유효하지 않은 좌표의 요청은 거부한다. 거부된 요청은 기존 이동 명령을 바꾸지 않는다.
- `SetDrive`의 직접 레버 명령과 `StopMove`는 목적지 이동을 취소한다.
- 파괴·컴포넌트 비활성화 시 기존 `StopActions` 경로가 이동을 취소한다.

| 상태 | 의미 |
| --- | --- |
| `Idle` | 목적지 이동 대기 또는 취소. 직접 레버 조작 상태도 여기에 포함한다. |
| `Moving` | 목적지를 향해 회전·이동·감속 중 |
| `Arrived` | 허용 거리 안에서 선속도·회전속도가 충분히 낮아져 정지를 확정한 상태 |

`MoveStatus`와 `Destination`은 호스트의 진행 상태다. 이 두 값을 새 네트워크 변수로 복제하지는 않는다. 게스트는 기존 차체 동기화 결과를 표시한다.

## 주행 방식과 설정

전차 정면과 목적지의 각도 차이로 좌우 레버를 계산한다. 방향 차이가 크면 제자리 회전하고, 방향이 맞을수록 전진 비중을 높인다. 목적지에 가까워지면 거리에 따른 속도 제한과 모터 감속 성능을 함께 사용한다. 정지 반경 절반과 한 틱의 이동량을 제동 여유로 남긴다.

반경 안에 들어오면 레버를 중립으로 두어 감속한다. 실제 선속도와 회전속도까지 정지 기준을 만족하면 `Arrived`로 전환하고 잔여 주행을 정리한다. 도착 뒤에는 다음 명령까지 대기한다.

| Inspector 항목 | 기본값 | 의미 |
| --- | --- | --- |
| Stop Radius | 0.5m | 전차 루트 기준 도착 허용 거리 |
| Slow Distance | 3m | 거리에 따라 속도를 낮추는 범위 |
| Turn Angle | 45° | 전진 없이 회전하는 각도 기준 |
| Turn Gain | 2 | 각도 오차에 따른 회전 속도 비율 |
| Turn Tolerance | 1.5° | 미세 조향을 멈추는 각도 범위 |
| Stop Speed | 0.05m/s | 도착 판정 시 선속도 상한 |
| Stop Turn Speed | 0.5°/s | 도착 판정 시 회전속도 상한 |

적 전차 충돌체에는 기존 `TankFlatTestZeroFriction` 물리 재질을 연결했다. 속도를 직접 적용하는 현재 평지용 모터가 지면 마찰과 경합하지 않게 하기 위한 설정이다. 기존 플레이어 전차와 전역 물리 설정은 변경하지 않았다.

장애물 우회·경로 탐색은 아직 없다. 목적지까지 막힘 없이 이동할 수 있는 평지에서 확인한다. 장애물에 막혀 거리가 줄지 않으면 도착으로 처리하지 않고 이동 상태를 유지한다.

## 별도 씬에서 확인하기

`Assets/TankPrototype/Scenes/TankMoveTest.unity`를 연다. `TankTest`의 은신용 Sherman 배치는 변경하지 않았다.

1. Play를 누르면 `TankMoveDemo`가 Fusion Single 세션을 시작하고 적 프리팹 한 대를 스폰한다.
2. 전차는 `Spawn Point`에서 `Destination`으로 이동한다. 기본 목적지는 `(0, 0.03, 8)`이다.
3. 목적지 근처에서 감속하고 정지하면 Console에 `[AI MOVE] Arrived and stopped.`가 표시된다.
4. 다른 지점을 확인하려면 Play 중 `Destination`의 위치를 바꾼다.
5. `Tank Move Demo` 오브젝트를 선택하고 `TankMoveDemo` 컴포넌트의 메뉴에서 **Move To Goal**을 실행한다. 이동 중에도 새 목적지로 변경할 수 있다.
6. 같은 메뉴의 **Stop Movement**로 이동 취소를 확인한다.

`Destination`을 옮기는 것만으로는 이동 명령이 바뀌지 않는다. **Move To Goal**을 실행한 시점의 위치를 목적지로 복사한다.

이 씬은 로컬 Single 모드에서 서버 권한을 확인하는 개발용 씬이다. 세 명의 접속·네트워크 결과 공유 검증과는 구분한다. Build Settings와 운영 세션 코드는 변경하지 않았다.

## 검증 범위

런타임 C# 컴파일을 통과했다. `Builds/TankPointMove/PointMoveChecks.cs`는 실제 `TankAI.cs`와 `TankMotor.cs` 소스를 함께 컴파일해 평면 고정 시간 이동 모형에서 명령과 제어 루프를 검사한다. 모터의 이동·감속 로직을 테스트 코드에 복사하지 않는다.

확인 항목은 앞·좌·우·뒤 목적지 도착, 가까운 지점·현재 위치, 이동 중 목적지 변경, 취소, 잘못된 좌표 거부, 비호스트 차단, 직접 레버 입력으로 전환, 파괴 시 취소, 여러 시간 간격, 서로 다른 AI·물리 갱신 주기, 예열 후 반복 경로의 관리 메모리 할당이다.

이 검사는 Unity의 실제 PhysX 접촉과 Fusion 전송을 실행한 결과가 아니다. 실제 Unity Play Mode의 충돌·차체 표현과 세 클라이언트 동기화는 별도로 확인해야 한다. 검사 결과는 `Builds/TankPointMove/logic-results.txt`에 저장한다.
