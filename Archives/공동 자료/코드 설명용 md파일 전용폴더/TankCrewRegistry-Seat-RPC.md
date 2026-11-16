# TankCrewRegistry 역할 변경 RPC

Host와 Guest 모두 키보드 상단 숫자열의 `1`로 조종수, `2`로 포수, `3`으로 전차장 좌석을 요청할 수 있다. Host가 최종 좌석을 확정하고, 클라이언트는 복제된 역할 배정에 맞춰 로컬 좌석을 갱신한다.

## 1. 입력에서 Host의 역할 변경까지

1. `TankInput.ReadSeat()`는 상단 숫자키를 `TankCrew.Role`로 변환해 `TankCrewRegistry.RequestSeat()`를 호출한다. Registry가 있는 네트워크 플레이에서는 이 경로를 사용한다. [TankInput.cs](../../../source/Claustrophobia/Assets/TankPrototype/Scripts/TankInput.cs)
2. `RequestSeat()`는 Registry가 Spawn됐고 Runner가 실행 중인지, 목표 좌석이 유효한지, 로컬 플레이어에게 배정된 역할이 있으며 목표와 다른지 확인한다. 그런 다음 현재 역할과 목표 역할을 `RPC_RequestSeat()`로 보낸다.
3. `RPC_RequestSeat()`는 `RpcSources.All`에서 `RpcTargets.StateAuthority`로 전달한다. Host는 `RpcInfo.Source`로 요청자를 식별한다. Host 자신의 요청에도 같은 식별 방식이 적용되도록 `RpcHostMode.SourceIsHostPlayer`를 지정했다. 수신 시 두 역할이 유효하고 발신자의 현재 역할이 요청과 일치할 때만 등록한다.
4. Host는 플레이어마다 마지막으로 수신해 검사를 통과한 대기 요청을 하나 보관한다. `FixedUpdateNetwork()`에서 처리하기 직전에 발신자의 현재 역할을 다시 대조한다. 앞선 요청으로 역할이 바뀌었다면 그 요청은 적용하지 않는다. 접속 종료 시 해당 요청을 제거하고, Registry가 Despawn되면 대기 목록을 비운다. [TankCrewRegistry.cs](../../../source/Claustrophobia/Assets/TankPrototype/Scripts/network/TankCrewRegistry.cs)

`RequestSeat()`의 `true`는 RPC가 로컬에서 호출됐거나 원격으로 전송됐다는 뜻이다. 실제 변경은 복제된 역할 배정으로 확인한다.

## 2. 좌석 배정과 클라이언트 적용

| 입력 또는 상황 | 현재 동작 |
| --- | --- |
| `1/2/3`으로 빈 좌석 선택 | Host가 요청자를 목표 좌석에 배정하고 기존 좌석을 `PlayerRef.None`으로 비운다. |
| `1/2/3`으로 점유 좌석 선택 | Host가 두 사람의 좌석을 즉시 맞바꾼다. |
| `1/2/3`으로 자기 좌석 선택 | 요청을 보내기 전에 차단한다. |
| Host의 `F10` | 조종수와 포수 좌석이 모두 찼을 때 두 좌석을 맞바꾼다. Guest의 `F10` 요청은 거부한다. |

`DriverPlayer`, `GunnerPlayer`, `CommanderPlayer`가 최종 배정의 네트워크 상태다. `OnRolesChanged()`가 호출되면 각 클라이언트는 `Runner.LocalPlayer`와 세 슬롯을 비교해 자신의 역할을 찾고, `CrewSeats.ApplyRole()`로 실제 위치와 로컬 권한을 적용한다. 최초 바인딩에서도 이 경로를 사용한다. Host는 이 배정에 따라 현재 조종수의 레버와 현재 포수의 조준 입력을 읽는다. [TankCrewRegistry.cs](../../../source/Claustrophobia/Assets/TankPrototype/Scripts/network/TankCrewRegistry.cs) · [CrewSeats.cs](../../../source/Claustrophobia/Assets/TankPrototype/Scripts/CrewSeats.cs) · [TankMotor.cs](../../../source/Claustrophobia/Assets/TankPrototype/Scripts/TankMotor.cs) · [TankAim.cs](../../../source/Claustrophobia/Assets/TankPrototype/Scripts/TankAim.cs)

접속·이탈에 따른 초기 배정과 해제는 기존 `SessionJoinInit.OnPlayerJoined()` 및 `OnPlayerLeft()`가 Host에서 Registry로 전달한다. 역할 변경 RPC 작업에서 수정한 소스는 `TankCrewRegistry.cs` 한 개다. [SessionJoinInit.cs](../../../source/Claustrophobia/Assets/TankPrototype/Scripts/network/SessionJoinInit.cs)

## 3. 재현과 기존 검증 결과

1. 같은 Windows 빌드를 두 번 실행해 `MySession`에 접속한다. 두 창의 초기 역할은 Host 조종수, Guest 포수다.
2. Guest 창에 포커스를 두고 상단 `3 → 2 → 1`을 차례로 누른다. 각 변경의 표시가 끝난 뒤 다음 키를 누른다. Guest는 빈 전차장 자리로 갔다가 포수로 돌아오고, 마지막에 Host와 교환해 조종수가 된다.
3. Guest에서 `F10`을 누르면 역할이 유지된다. Host에서 `F10`을 누르면 조종수와 포수가 다시 교환된다. 로그의 `[CREW SEAT]`와 `[CREW LOCAL]`로 Host의 처리 결과와 두 창의 로컬 적용 결과를 확인할 수 있다.

구현 당시 Windows 빌드는 오류 0건으로 완료했다. 검증용 Windows Player 두 개의 실제 Photon 세션에서 빈 좌석 이동, 점유 좌석 교환, 요청에 담긴 잘못된 역할과 범위 밖 좌석 거부, Host의 `F10` 동작과 `StateAuthority` 유지를 확인했다. 자동 검증은 기존 `TankInput.ReadSeat()`에 합성 키를 전달했다. 일반 빌드에서 물리 키보드로 누르는 확인은 수동 검증 항목이다. 로컬 원본 로그는 Unity 프로젝트의 `Builds/CrewRoleRpc/`에 있다.

## 4. 점유 좌석 승인 패널의 설계 범위

점유 좌석의 경우 사용자에게 수락·거절 패널을 띄우고 Host가 승인 뒤 교환하는 흐름을 후속 설계로 정리했다. 현재 구현은 위 표와 같이 점유 좌석도 즉시 교환한다.
