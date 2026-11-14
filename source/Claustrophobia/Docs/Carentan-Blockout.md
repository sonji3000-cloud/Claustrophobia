# 카랑탕 시가지 블록아웃 계획

《밴드 오브 브라더스》 3화 〈Carentan〉의 시가지 진입 장면에서 착안해, 약 **500m × 500m**의 임시 인게임 맵을 만든다. 남쪽에서 접근하면 T자 교차로 건너편의 카페가 정면에 보이고, 교차로에서 방향을 바꾸면 시장 광장과 주택가 골목으로 이어진다. 광장은 카랑탕의 레퓌블리크 광장을 참고해 배치한다. [3화 〈Carentan〉](https://www.hbomax.com/kn/en/show/6a0a0227-f567-46ba-9a2d-04374c5f6193/s1/e3-carentan/f9641dcf-f772-49a0-b510-8ca0254be598) · [카랑탕 T자 교차로 현장 자료](https://battlefieldtravels.com/battle-of-carentan/) · [코탕탱 관광청의 카랑탕 도심 안내](https://www.ot-baieducotentin.fr/en/jexplore/patrimoine/villes-villages/carentan-les-marais/)

씬, Terrain, NavMesh와 로컬 전차 조작을 `Carentan`에 배치했다. 마지막 Play Mode 주행은 사용자가 확인한다.

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
- Terrain 도로 페인트의 기준 폭은 주도로 **10m**, 우회 골목 직선부 **8m**다. 급한 골목 코너의 페인트는 **14m × 14m**로 넓어지며, 가장자리에서 자갈이 흙과 잔디로 섞인다. NavMesh 베이크에 사용한 기본 도로 범위는 주도로 14m, 골목 직선부 10m다. 실제 전차 통과 폭은 건물 Collider 사이 간격으로 확인한다.
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

- 씬은 `Assets/TankPrototype/Scenes/Carentan.unity`에 저장했다. `Town Geometry` 아래에 Terrain, 세 구역의 건물, 도로 밖 베이크 제외 볼륨을 배치했다.
- `Assets/TankPrototype/Terrain/Carentan/CarentanTerrain.asset`은 가로·세로 500m, 높이 범위 40m인 TerrainData다. 도로와 광장은 Terrain 레이어로 칠해 Terrain을 조각하면 표면을 함께 따라간다. 건물과 전차 출발점은 씬에서 별도로 높이를 맞춘다.
- 주도로와 골목은 `Road.terrainlayer`의 `CarentanGravel.png`를 사용한다. 자갈 타일 크기는 3m × 3m다. 시장 광장과 도로 가장자리의 밝은 흙띠는 `Market` 레이어를 사용한다.
- **건물은 프로젝트에 들어 있는 `Assets/polyperfect/Low Poly War Pack/Prefabs/Buildings`의 기존 프리팹만 사용한다.** 원본 프리팹을 인스턴스로 배치하고 개별 위치와 회전을 조정한다. 파손 주택은 팩의 `building-house-middle-broken` 같은 기존 변형을 사용한다.
- 건물 충돌은 프리팹의 `MeshCollider`부터 사용한다. 통행을 막아야 할 부분의 Collider가 비어 있으면 씬 인스턴스에서 보완한다. 기존 플레이어 전차와 `EnemyTank-Sherman`을 주행 확인에 사용한다.

## 전차 AI용 도로 NavMesh

이 씬은 AI Navigation 2.0.14에서 기존 **`TankFlat`** Agent Type과 `TankPath`를 사용한다. 로컬 Fusion 세션에서 `EnemyTank-Sherman`을 생성한 뒤 `SetMoveMode(NavMesh)`를 호출한다. [기존 전차 AI 가이드](TankAI.md)

### 베이크 설정

1. `Town Geometry`의 `NavMeshSurface`는 `TankFlat` 설정인 반경 3.8m, 높이 3.5m, 최대 경사 16°, 단차 0.1m를 사용한다. 차체와 포신의 코너 회전 여유는 실제 주행으로 확인한다.
2. `Physics Colliders`와 `Current Object Hierarchy`로 Terrain과 건물의 실제 충돌 형상을 수집한다. Terrain 한 장이 접지와 도로 표면을 함께 제공한다.
3. 표면 기본 영역은 `Road`다. `Off-road bake exclusion` 아래의 `NavMeshModifierVolume`과 건물의 `NavMeshModifier`를 `Not Walkable`로 지정해 도로 밖 지면과 건물 표면을 제외한다.
4. 베이크한 데이터는 `Assets/TankPrototype/Terrain/Carentan/NavMesh-TankFlat.asset`에 저장했다. 도로를 옮길 때는 Terrain 페인트와 제외 볼륨을 함께 수정한다. Terrain 높이나 건물 위치를 바꾼 뒤에는 `Town Geometry`의 `NavMeshSurface`에서 다시 베이크하고 저장한다. Terrain 타일을 추가할 때는 베이크 계층과 제외 범위를 함께 확장한다. [Unity `NavMeshSurface` 문서](https://docs.unity3d.com/Packages/com.unity.ai.navigation@2.0/manual/NavMeshSurface.html)

### 기존 전차 AI와 연결

- `TankPath`는 `TankFlat`과 기존 `AllAreas` 마스크를 사용한다. 이 씬에서는 `Road`만 주행 가능한 표면으로 남긴다. 출발점과 목적지의 NavMesh 보정 거리는 기존 0.75m다.
- `TankPath`가 완전한 경로를 계산하면 `TankAI`가 저장된 코너를 순서대로 추종하고 `TankMotor`가 차체를 구동한다. `CarentanDrive`의 순찰은 각 지점에서 `Arrived`가 확인되면 다음 목적지를 전달하고, `Failed`가 되면 정지한다.

### 로컬 조작

- Unity에서 `Carentan`을 열어 Play하면 로컬 Fusion `Single` 세션이 시작된다. 세션과 Tiger가 준비되면 `TankSceneLink`가 기존 `TankInterior`를 Additive로 불러오고 내부 화면을 연다.
- **1 / 2 / 3**으로 조종수·포수·전차장 좌석을 요청하고, 장치를 바라본 상태에서 **Space**로 사용한다. 조종석 레버를 잡은 상태에서 **W/S**와 **I/K**가 왼쪽·오른쪽 레버를 조작하고, **Q/U**가 각각 중립으로 돌린다. **F8 / F9**는 외부·내부 시점 전환이다. [기존 내부 조작 가이드](TankTest-SceneSplit-README.md#8-현재-조작과-기능-확인-순서)
- **7**로 Sherman 순찰을 시작한다. 경로는 **A → B → 광장 뒤편 → C → A**다. **8**은 도로 밖 목적지를 요청하고 결과를 Console에 기록한다. **9**는 순찰을 중지한다.

## 구현 순서와 완료 기준

1. 바닥, 도로, 출발점을 배치하고 A → B → C → A가 이어지는지 확인한다.
2. Low Poly War Pack 프리팹으로 건물 열을 만든다. 운전석에서 진입로 끝의 카페가 보이는지, 광장이 코너를 돈 뒤 열리는지, 가장 좁은 골목에서 회전과 후진이 가능한지 확인한다.
3. 전차용 NavMesh를 베이크한다. Scene 뷰에서 세 구역과 광장 뒤편 연결로가 연속인지, 건물 내부·지붕·도로 밖에 전차 경로가 생기지 않는지 확인한다.
4. Play Mode에서 호스트가 `EnemyTank-Sherman`에 A → B, B → C, C → A 목적지를 지정한다. B → C는 광장 뒤편 골목에 놓인 시험 지점을 거치게 해 우회 연결도 검증한다. 각 경로가 `PathComplete`이며 전차가 건물에 걸리지 않고 실제로 도착해야 한다. 도로에서 떨어진 도달 불가 지점도 지정해 실패 처리를 확인한다.
