# 전차장 장전과 포탄 네트워크 동기화

`TankTest`의 Fusion Host Mode에서 포수와 전차장은 각자 탄약고에서 포탄을 집고, 같은 약실을 열고 닫고 장전할 수 있다. 발사 권한은 포수에게 있다. Host는 운반과 장전에 같은 `TankRound` 객체를 사용해 모든 클라이언트에서 포탄의 위치와 상태가 일치하도록 한다.

## 1. 씬 연결과 권한

`TankSceneLink.Connect()`는 `TankInterior`가 준비되면 `TankBreech.Bind(gun, next.Interaction)`을 호출한다. 이 연결을 통해 외부 씬의 `TankGun`은 내부 씬의 탄약고 목록, 운반 기준점 `RoundHold`, 장전 위치 `RoundSeat`를 사용할 수 있다. 전차장 오른쪽 탄약고와 약실은 내부 씬에 이미 배치되어 있으며, 두 장치의 `CrewDevice`는 포수와 전차장의 사용을 허용한다. [TankSceneLink.cs](../../../source/Claustrophobia/Assets/TankPrototype/Scripts/TankSceneLink.cs) · [TankInterior.unity](../../../source/Claustrophobia/Assets/TankPrototype/Scenes/TankInterior.unity)

- **클라이언트**는 `CrewDevice.CanUse()`로 선택한 장치를 사용할 수 있는지 확인한다. 약실은 `TankBreech.TryUse()`에서도 `TankGun.CanLoad()`를 검사한다.
- **Host**는 `RPC_Request()`의 `RpcInfo.Source`로 요청자를 식별한다. `AfterTick()`에서 `TankCrewRegistry`로 조회한 현재 역할이 요청에 담긴 역할과 일치하는지 확인하고, 포수와 전차장의 탄약·약실 요청 및 포수의 발사 요청을 처리한다. [TankBreech.cs](../../../source/Claustrophobia/Assets/TankPrototype/Scripts/TankBreech.cs) · [TankGun.cs](../../../source/Claustrophobia/Assets/TankPrototype/Scripts/TankGun.cs)

## 2. 탄약고에서 포탄을 집고 운반하기

1. 플레이어가 탄약고를 바라보고 `Space`를 누르면 `CrewInteract.TryUse()`가 `TankAmmoRack.TryTake()`를 호출한다. 네트워크 플레이에서는 `CrewCarry.RequestTake()`를 거쳐 선택한 탄약고의 장치 인덱스를 Host에 보낸다.
2. Host의 `TankGun.TakeRound()`는 해당 역할의 손이 비어 있는지, 탄약고를 사용할 수 있는지, 운반 자세 입력이 유효한지 확인한다. 승인하면 `Runner.Spawn()`으로 `TankRound`를 한 개 생성하고 요청자를 그 포탄의 `InputAuthority`로 지정한다. Host가 `StateAuthority`를 가지며, 포탄 참조는 역할에 따라 `GunnerRound` 또는 `CommanderRound`에 저장한다.
3. `SessionJoinInit.OnInput()`이 `TankControl.SubmitNetworkInput()`을 호출하면 `CrewCarry.SamplePose()`가 로컬 `RoundHold`의 월드 위치와 회전을 읽는다. `RoundPosition`, `RoundRotation`, `RoundRole`을 기존 `TankCrewNetworkInput`에 채워 한 번의 `input.Set()`으로 제출한다.
4. Host의 `TankGun.FixedUpdateNetwork()`는 각 운반자의 `PlayerRef`로 입력을 읽는다. 제출된 역할, 좌석 기준점에서 2 Unity 단위 이내인 위치, 단위 길이에 가까운 회전을 확인한 뒤 포탄의 위치와 회전을 갱신한다. 각 클라이언트는 `NetworkTransform`을 통해 그 변화를 받는다.

`RoundHold`는 각 클라이언트의 로컬 플레이어 시점에 붙어 있다. 네트워크 포탄은 루트 객체로 유지하고 `SyncParent`를 끈 상태에서 월드 위치와 회전을 공유한다. 운반자 자신의 `CrewCarry.Held`도 복제된 역할별 포탄 참조에서 구한다. [CrewCarry.cs](../../../source/Claustrophobia/Assets/TankPrototype/Scripts/CrewCarry.cs) · [TankCrewNetworkInput.cs](../../../source/Claustrophobia/Assets/TankPrototype/Scripts/network/TankCrewNetworkInput.cs) · [TankRound.prefab](../../../source/Claustrophobia/Assets/TankPrototype/Prefabs/TankRound.prefab)

## 3. 장전, 약실 개폐, 반환

| 조작 | Host의 처리 | 클라이언트에서 보이는 결과 |
| --- | --- | --- |
| 열린 빈 약실에 장전 | 요청자와 운반 중인 포탄의 `NetworkId`를 대조한다. 역할별 운반 참조를 비우고 그 객체를 `ChamberRound`에 저장한 뒤, `NetworkTransform.Teleport()`로 `RoundSeat`에 배치한다. | 손에서 약실로 같은 포탄이 이동한다. 약실 상태는 `LoadedOpen`이다. |
| 장전된 약실 닫기 | 약실을 `Ready`로 바꾸고 포탄의 네트워크 프로퍼티 `Visible`을 끈다. | 포탄 객체를 유지하면서 메시만 가린다. |
| 장전된 약실 다시 열기 | 약실을 `LoadedOpen`으로 바꾸고 `Visible`을 켠다. | 같은 포탄이 다시 보인다. |
| 운반 중 `Esc` | 요청자와 포탄 ID를 대조하고 역할별 운반 참조를 비운 뒤 `Runner.Despawn()`한다. | 손에 든 포탄이 사라진다. 약실에 장전된 포탄은 그 자리에 남는다. |
| 포수가 발사 | 발사체를 생성하고 약실이 `Recovering`으로 전이할 때 `ChamberRound`를 비우고 장전 포탄을 `Runner.Despawn()`한다. | 장전 포탄이 제거되고 비행하는 `TankProjectile`이 나타난다. |

`TankGun`의 `NetworkState`는 약실의 상태를 공유한다. `TankBreech`는 상태 변경 이벤트로 약실 블록의 위치와 회전을 갱신하고, `TankRound`는 `Visible` 변경을 받아 메시 표시를 갱신한다. 좌석 배정이 바뀌거나 운반자가 접속을 종료하면 Host는 해당 역할과 포탄의 `InputAuthority`가 더 이상 일치하지 않는 운반 포탄을 정리한다. [TankGun.cs](../../../source/Claustrophobia/Assets/TankPrototype/Scripts/TankGun.cs) · [TankRound.cs](../../../source/Claustrophobia/Assets/TankPrototype/Scripts/TankRound.cs)

## 4. 재현과 검증

1. 같은 빌드를 세 개 실행해 Host, 포수 Guest, 전차장 Guest 순으로 접속한다.
2. 전차장 창에서 `F9`로 내부 모드에 들어간다. 오른쪽 탄약고를 바라보고 `Space`로 포탄을 집은 뒤 시선을 움직인다. 다른 창에서 손 위치를 따라 움직이는 포탄을 확인한다.
3. 빈 약실이 닫혀 있으면 약실을 바라보고 `Space`로 연다. 열린 약실을 다시 바라보고 `Space`로 장전한다. 빈손으로 약실을 닫았다 열면서 장전된 포탄이 가려졌다 다시 보이는지 확인한다.
4. 장전된 상태에서 여분의 포탄을 집고 `Esc`로 반환한다. 이어서 약실을 닫고 포수로 발사해 장전된 포탄도 제거되는지 확인한다.

구현 당시 Unity Play Mode에서 전차장 좌석의 탄약고·약실 선택과 장전을 확인했다. Windows Player 세 개를 연결한 자동 검증에서는 세 인스턴스에서 같은 포탄 ID를 확인했고, 운반 자세 복제, 장전, 개폐, 여분 포탄 반환, 발사 후 제거가 통과했다. 검증 스크립트를 제외한 최종 Windows 빌드도 오류 0건으로 완료했다.
