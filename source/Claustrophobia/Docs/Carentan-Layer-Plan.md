# Carentan 오브젝트 레이어와 충돌 규칙

2026년 11월 22일 기준. Carentan의 지형은 `Ground`, 건물은 `Building`, 상자·드럼통·전신주·낮은 돌담은 `StreetProp`으로 분리했다. `StreetProp`은 프리팹별로 밀림·파손·전도를 처리한다.

## 적용 전 배치

적용 전 Carentan 씬은 프리팹 인스턴스와 비활성 오브젝트를 포함해 GameObject 380개가 모두 `Default`(0)에 있었다.

| 범위 | GameObject 수 | 현재 레이어 |
| --- | ---: | --- |
| Terrain | 1 | `Default` |
| 건물 프리팹 루트 | 133 | `Default` |
| 전신주 프리팹 루트 | 27 | `Default` |
| 기타 소품 프리팹 루트 | 25 | `Default` |
| NavMesh 제외 볼륨 | 69 | `Default` |
| `Town Geometry` 안의 그룹 등 | 15 | `Default` |
| `Town Geometry` 밖의 오브젝트 | 110 | `Default` |
| 합계 | 380 | |

`Town Geometry` 밖에는 Tiger 전차, 맵 경계, 카메라, 스폰 지점 등이 있다. 프로젝트에 이미 등록된 사용자 레이어는 `Soldier`(7), `InteriorDevice`(8), `TankInterior`(9)다. 전차 내부 씬과 관련 프리팹은 기존 내부 레이어를 사용한다. [프로젝트 레이어 설정](../ProjectSettings/TagManager.asset) · [Carentan 씬](../Assets/TankPrototype/Scenes/Carentan.unity)

## 레이어와 필터

| 레이어 | 적용 대상 | 접지 검사 | 전차 물리 충돌 | AI 시야 |
| --- | --- | --- | --- | --- |
| `Ground` (10) | Terrain과 주행 바닥으로 사용할 메시 | 포함 | 유지 | 지형에 의한 시야 차단 유지 |
| `Building` (11) | 건물 인스턴스와 해당 자식 콜라이더 | 제외 | 유지 | 건물에 의한 시야 차단 유지 |
| `StreetProp` (12) | 상자, 드럼통, 전신주, 낮은 담벽, 나무 | 제외 | 전차와 충돌하며, 충돌 후 반응은 프리팹별로 설정 | 기존 동작 유지 |
| `Default` (0) | 빈 그룹, 스폰 및 경로 표식, 나머지 기본 오브젝트 | Carentan에서 제외 | 기존 설정 유지 | 기존 설정 유지 |

Carentan의 `Meadow`, `Road`, `Market`은 같은 Terrain에 칠하는 **Terrain Layer**다. 지면 콜라이더의 GameObject Layer는 `Ground` 하나로 둔다. NavMesh의 `Road`, `Not Walkable`은 **NavMesh Area**이며, 길을 베이크하는 기존 구역 설정을 계속 사용한다.

## StreetProp 충돌 규칙

상자, 드럼통, 전신주, 낮은 담벽, 나무는 모두 `StreetProp`에 둔다. 전차와의 충돌은 공통으로 허용하고, 밀림·파손·전도의 차이는 각 프리팹의 콜라이더, Rigidbody, 상태 전환으로 정한다.

| 프롭 | 평소 상태 | 전차와 부딪혔을 때 |
| --- | --- | --- |
| 나무 상자 (`crate-box-wooden`, `crate-wooden_long_T`) | 가벼운 동적 물체 | 가볍게 닿으면 밀리고, 강한 충격을 받으면 부서져 파편이 흩어진다. |
| 드럼통 (`oil-drum`, `oil-drum-empty`) | 굴러갈 수 있는 동적 물체 | 가볍게 닿으면 굴러가거나 넘어지고, 강한 충격을 받으면 찌그러진 상태로 바뀐다. 파손 외형은 `oil-drum-demaged`를 쓴다. |
| 전신주 (`pole-electric`) | 지면에 고정 | 처음 닿으면 저항한다. 충격이 크거나 전차가 계속 밀면 고정이 풀리고, 전신주 전체가 동적 Rigidbody로 전환돼 넘어지거나 밀린다. |
| 낮은 돌담 (`fence-stone`) | 구간 단위로 지면에 고정 | 충격이 크거나 전차가 계속 밀면 맞닿은 구간이 무너지고 잔해가 밀려난다. 파손 외형은 `fence-demaged-stone`을 우선 사용한다. |
| 나무 (`tree-lime`, `tree-lime_dark`) | 지면에 고정 | 첫 전차 충돌로 통과·제거 타이머를 시작한다. |

전신주와 담벽은 충돌 직후 속도가 떨어져도 전차가 계속 밀면 넘어가거나 무너지도록 한다. 전환은 프롭당 한 번만 일어나며, 원래 자리의 고정 콜라이더를 해제하고 넘어지는 모델이나 잔해의 콜라이더로 이어받는다. 이때 전차가 기존 자리의 보이지 않는 충돌체에 막히거나 잔해에 계속 걸리지 않도록 한다. 여기서 래그돌화는 전신주나 담벽 조각이 고정 상태에서 물리에 따라 움직이는 상태로 바뀌는 동작을 뜻한다.

충격 기준, 지속해서 미는 시간, 질량은 프리팹별 조정값으로 둔다. 나중에 조정할 때는 가벼운 접촉, 강한 충돌, 저속으로 계속 미는 상황을 각각 확인한다.

나무·돌담·전신주는 첫 플레이어 또는 AI 전차 충돌에서 타이머를 한 번 시작한다. 충돌 기준 1초 뒤에는 모든 Carentan 전차와의 물리 충돌만 해제한다. 지면과 다른 소품에 대한 충돌은 유지한다. 최초 충돌 5초 뒤에는 해당 프롭을 제거한다. 사이에 전신주가 넘어지거나 돌담이 무너져 콜라이더가 교체되면 같은 전차 충돌 규칙을 적용한다. 상자·드럼통과 건물에는 이 타이머가 없다. [프롭 충돌 타이머](../Assets/TankPrototype/Scripts/StreetPropLifetime.cs)

## Carentan에 반영한 설정

1. **접지 검사.** Carentan의 Tiger는 씬에서 `Ground` 마스크를 지정하고, Sherman에게는 `CarentanDrive`가 생성 직후 같은 마스크를 지정한다. 공유 전차 프리팹은 `Default` 마스크를 유지하므로 기존 테스트 씬의 바닥을 계속 감지한다. [접지 검사 코드](../Assets/TankPrototype/Scripts/TankGroundProbe.cs#L11) · [Carentan 주행 설정](../Assets/TankPrototype/Scripts/CarentanDrive.cs)
2. **물리와 시야.** 기존 충돌 매트릭스에서 전차의 `Default`가 새 환경 레이어와 충돌한다. 카메라와 AI 시야 마스크는 새 레이어를 포함한다. 건물 133채는 기존 콜라이더를 사용한다. 밀림·파손·전도는 상자 12개, 드럼통 7개, 전신주 27개, 낮은 돌담 53구간에 연결했다. 전차 충돌 후 타이머는 전신주, 돌담, 나무 9그루에 연결했다. 현재 프롭 물리는 Carentan의 로컬 단일 세션을 기준으로 한다. [물리 설정](../ProjectSettings/DynamicsManager.asset) · [프롭 동작](../Assets/TankPrototype/Scripts/StreetProp.cs)
3. **NavMesh.** `Town Geometry`의 `NavMeshSurface`는 지형과 건물의 콜라이더를 수집하고 `StreetProp`은 고정 베이크에서 제외한다. 프롭이 밀려난 뒤 원래 자리에 고정 경로 차단이 남지 않도록 기존 `TankFlat` 에셋을 재베이크했다. [Carentan 블록아웃과 NavMesh 설정](Carentan-Blockout.md#전차-ai용-도로-navmesh)

Play Mode에서 확인할 항목은 전차 접지, 건물 충돌, 약한 충돌 시 상자와 드럼통의 밀림, 강한 충돌 시 파손, 전신주와 담벽을 저속으로 계속 밀었을 때의 통과, 주요 도로의 AI 주행이다.
