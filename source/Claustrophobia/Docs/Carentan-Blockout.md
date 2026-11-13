# 카랑탕 시가지 블록아웃 계획

《밴드 오브 브라더스》 3화 〈Carentan〉의 시가지 진입 장면에서 착안해, 약 **500m × 500m**의 임시 인게임 맵을 만든다. 남쪽에서 접근하면 T자 교차로 건너편의 카페가 정면에 보이고, 교차로에서 방향을 바꾸면 시장 광장과 주택가 골목으로 이어진다. 광장은 카랑탕의 레퓌블리크 광장을 참고해 배치한다. [3화 〈Carentan〉](https://www.hbomax.com/kn/en/show/6a0a0227-f567-46ba-9a2d-04374c5f6193/s1/e3-carentan/f9641dcf-f772-49a0-b510-8ca0254be598) · [카랑탕 T자 교차로 현장 자료](https://battlefieldtravels.com/battle-of-carentan/) · [코탕탱 관광청의 카랑탕 도심 안내](https://www.ot-baieducotentin.fr/en/jexplore/patrimoine/villes-villages/carentan-les-marais/)

이 문서는 구현 계획이다. 씬 배치, NavMesh 베이크, 전차 AI 연결은 구현 단계에서 진행한다.

## 세 구역과 주행 동선

```text
                           북쪽 (+Z)

              ┌───────── 골목 ─────────┐
              │                        │
     ┌────────┘                        │
     │ C 주택가 골목              B 시장 광장
     │                                 │
     │         카페 정면               │
     └───────── A T자 교차로 ──────────┘
                      │
                   전차 출발
```

| 구역 | 배치 | 주행 경험 |
| --- | --- | --- |
| **A · 진입로와 T자 교차로** | 남쪽 진입로 끝의 맞은편에 `building-cafe_T`를 두고 정면을 출발점 쪽으로 향하게 한다. 교차로 양옆에는 건물을 도로 가까이에 배치한다. | 진입 중 카페를 기준으로 방향을 잡고, 교차로에 가까워지면 양옆 거리의 시야가 열린다. |
| **B · 시장 광장** | 교차로에서 동쪽으로 우회전한 뒤 북쪽으로 꺾으면 약 80m × 60m 광장이 열린다. `building-medium`과 `building-house-big`을 가장자리에 두고 중앙은 비운다. | 골목을 벗어나면서 시야가 넓어지고, 광장에서 전차가 방향을 바꿀 수 있다. |
| **C · 주택가 골목** | 교차로에서 서쪽으로 좌회전하면 `building-house-middle`, `building-house-big`과 파손 변형이 이어진다. 건물 열을 엇갈리게 두고, 꺾이는 골목을 시장 뒤편에 연결한다. | 코너에서 시야가 짧아진다. 시장까지 이어지는 골목을 이용해 마을 안에서 한 바퀴 돌 수 있다. |

### 첫 배치 치수

- 지면은 평지로 잡고 맵 중심을 원점에 둔다. Unity의 `+Z`를 북쪽, `+X`를 동쪽으로 사용한다. X와 Z의 범위는 각각 `-250m`에서 `+250m`다.
- 도로 유효 폭은 주도로 **14m**, 우회 골목 **10m**부터 시작한다. 건물 돌출부와 실제 Collider를 기준으로 폭을 재고, 좁은 코너는 전차 주행 결과로 조정한다.
- 아래 좌표는 기존 평면 배치를 옮기기 위한 첫 배치 기준점이다. 단위는 미터이며 좌표 순서는 `(X, Z)`다.

| 기준점 | 시작 좌표 |
| --- | --- |
| 남쪽 전차 출발점 | `(-85, -195)`, 북향 |
| A 교차로 중심 | `(-85, -50)` |
| B 광장 진입 코너 | `(65, -50)` |
| B 광장 중심 | `(55, 40)` |
| C 골목 입구와 굴곡 | `(-165, -50) → (-165, 55) → (-60, 55) → (-60, 125) → (20, 125)` |
| C에서 B로 연결되는 광장 뒤편 | `(20, 70)` |

## Unity 씬과 건물 프리팹

- 신규 테스트 씬 경로는 `Assets/TankPrototype/Scenes/CarentanBlockout.unity`로 제안한다. 바닥, 도로, 세 구역, 카페, 출발점, 경계, 내비게이션 오브젝트를 씬에 직접 배치하고 참조를 저장한다.
- **건물은 프로젝트에 들어 있는 `Assets/polyperfect/Low Poly War Pack/Prefabs/Buildings`의 기존 프리팹만 사용한다.** 원본 프리팹을 인스턴스로 배치하고 개별 위치와 회전을 조정한다. 파손 주택은 팩의 `building-house-middle-broken` 같은 기존 변형을 사용한다.
- 건물 충돌은 프리팹의 `MeshCollider`부터 사용한다. 통행을 막아야 할 부분의 Collider가 비어 있으면 씬 인스턴스에서 보완한다. 기존 플레이어 전차와 `EnemyTank-Sherman`을 주행 확인에 사용한다.

## 전차 AI용 도로 NavMesh

프로젝트의 AI Navigation 패키지는 `com.unity.ai.navigation` **2.0.14**로 설치돼 있다. 맵 구현에 **전차용 NavMesh 설정과 베이크, 기존 AI의 경로 연결**을 포함한다. 현재 `TankAI.MoveTo`는 월드 XZ의 한 지점을 향해 조향하므로, 이 씬에서 도로를 따라 우회하려면 NavMesh 경로의 코너를 주행에 연결해야 한다. [기존 전차 AI 가이드](TankAI.md)

### 베이크 설정

1. 씬에 전차용 `NavMeshSurface`를 배치하고 전차용 `Agent Type`을 지정한다. `Radius`는 `EnemyTank-Sherman` 차체 충돌 폭의 절반에 측면 여유를 더해 시작한다. 높이와 단차 허용치를 설정하고, 차체 길이와 포신이 코너를 돌 때 차지하는 공간은 Play Mode에서 검증한다.
2. 주도로, 교차로, 우회 골목, 시장 광장의 주행 가능 바닥을 베이크 입력으로 수집한다. 도로와 광장의 경계는 실제 도로 Collider에 맞춘다. `Use Geometry`는 `Physics Colliders`로 두고, 수집 범위를 이 맵의 주행 바닥과 정적 장애물로 제한한다.
3. 도로·광장에는 `Road` NavMesh Area를 사용한다. 500m 전체를 덮는 바닥과 도로 밖 지면은 전차용 베이크 입력에서 제외한다. 건물 Collider는 장애물로 반영하고, 건물 표면은 `NavMeshModifier`의 `Not Walkable`로 지정해 지붕·건물 안쪽에 전차 경로가 생기지 않게 한다.
4. 에디터에서 베이크한 `NavMeshData`를 씬과 함께 저장한다. 도로 폭, 건물 위치, 충돌체가 달라지면 다시 베이크한다. [Unity `NavMeshSurface` 문서](https://docs.unity3d.com/Packages/com.unity.ai.navigation@2.0/manual/NavMeshSurface.html)

### 기존 전차 AI와 연결

- 호스트가 새 목적지를 받을 때, 전차용 Agent Type과 `Road` 영역을 지정한 `NavMeshQueryFilter`로 경로를 구한다. 출발점과 목적지는 짧은 허용 거리 안에서 도로 NavMesh에 맞추고, 원래 위치와 보정점 사이가 벽으로 막혀 있으면 해당 보정을 거부한다.
- `NavMesh.CalculatePath`의 반환값과 `PathComplete`를 함께 확인한다. 도달 가능한 경로의 코너를 순서대로 따라가고, 현재 경로가 끊기면 이동을 멈춘다. 도달 불가 요청은 실패로 처리한다. [Unity `NavMesh.CalculatePath` 문서](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AI.NavMesh.CalculatePath.html)
- NavMesh는 경로 계산에 사용하고, 실제 조향과 가감속은 기존 `TankAI`와 `TankMotor`를 통해 동적 Rigidbody에 적용한다. 경로는 목적지가 바뀌거나 재탐색이 필요할 때 계산하고, 주행 중에는 저장된 코너를 사용한다.

## 구현 순서와 완료 기준

1. 바닥, 도로, 출발점을 배치하고 A → B → C → A가 이어지는지 확인한다.
2. Low Poly War Pack 프리팹으로 건물 열을 만든다. 운전석에서 진입로 끝의 카페가 보이는지, 광장이 코너를 돈 뒤 열리는지, 가장 좁은 골목에서 회전과 후진이 가능한지 확인한다.
3. 전차용 NavMesh를 베이크한다. Scene 뷰에서 세 구역과 광장 뒤편 연결로가 연속인지, 건물 내부·지붕·도로 밖에 전차 경로가 생기지 않는지 확인한다.
4. Play Mode에서 호스트가 `EnemyTank-Sherman`에 A → B, B → C, C → A 목적지를 지정한다. B → C는 광장 뒤편 골목에 놓인 시험 지점을 거치게 해 우회 연결도 검증한다. 각 경로가 `PathComplete`이며 전차가 건물에 걸리지 않고 실제로 도착해야 한다. 도로에서 떨어진 도달 불가 지점도 지정해 실패 처리를 확인한다.
