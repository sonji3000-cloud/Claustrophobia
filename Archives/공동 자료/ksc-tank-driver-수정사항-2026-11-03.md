# 탱크 내부·외부 조작 연결 및 운전석 모니터 변경 내역

- 작성일: 2026-11-03
- 대상 씬: `Assets/TankPrototype/Scenes/TankTest.unity`
- 작업 브랜치: `ksc`
- 최신 기능 커밋: `acf4a12` — 렌더 텍스쳐로 계속 보여주게 수정
- 문서 작성 시 확인한 병합 커밋: `54f0c3a` — Merge branch 'ksc'

이 문서는 입력·시야·좌석 정리부터 레버 주행 연결, RenderTexture 모니터와 상시 표시까지의 현재 동작을 정리한다. 아래 전체 내용이 마지막 병합에서 모두 새로 추가된 것은 아니다. 마지막 `ksc → main` 병합에서 공통 조상 대비 달라진 파일은 `DriverScreen.cs`, `TankTest.unity`, `DriverScreen.mat`이며, 나머지 항목은 앞선 작업을 포함한 인계 정보다.

## 1. 변경 결과

- F8/F9는 탱크 외부·내부 화면을 전환한다. 화면 전환만으로 좌석·역할·장치 사용을 바꾸지 않는다.
- 상단 숫자 1/2/3은 내부 좌석을 옮기고 해당 역할을 적용한다. 내부·외부에 별도의 시험 역할을 두지 않는다.
- 실제 주행은 조종수 역할의 사용자가 지정된 주행 레버를 사용 중일 때만 허용한다.
- 기존 F1 조종수 카메라를 운전석 모니터 영상용으로 재사용한다.
- 운전석 모니터는 레버 사용 여부와 관계없이 계속 영상을 표시한다. 영상 표시 자체가 주행 권한을 부여하지 않는다.

## 2. 입력·시야·좌석 체계

### 입력의 책임과 순서

`TankInput`에서 좌석 요청, 장치 사용·해제, 주행 입력을 순서대로 처리한다.

```text
좌석 요청 → 선택 대상 갱신 → 장치 사용·해제 → 주행 입력
```

같은 프레임에 좌석을 바꾸거나 장치를 놓은 뒤 이전 사용자의 주행 명령이 다시 전달되는 것을 방지한다. `CrewSeats`는 좌석 승인·적용, `CrewInteract`는 장치 선택·사용 상태를 담당한다.

### 현재 조작법

| 입력 | 동작 |
| --- | --- |
| F8 | 외부 화면으로 전환 |
| F9 | 내부 화면으로 전환 |
| 상단 1 / 2 / 3 | 조종수 / 포수 / 전차장 좌석 요청 및 승인 시 역할 적용 |
| Space | 내부에서 선택 장치 사용 시작, 사용 중이면 사용 종료 |
| Esc | 현재 장치 사용 종료 |
| W / S | 왼쪽 레버 값 증가 / 감소 |
| I / K | 오른쪽 레버 값 증가 / 감소 |
| Q / U | 왼쪽 / 오른쪽 레버 개별 중립 |
| 숫자패드 1~6 | 기존 주행 프리셋. 유효한 조종자만 사용 가능 |
| F2 / F3 / F4 | 외부 모드에서 역할이 허용하는 포수 / 전차장 / 3인칭 화면 |
| F1 | 전체 화면 조종수 카메라 전환 입력에서 제외 |

주행 키는 내부·외부 화면 모두에서 유효한 조종권을 요구한다. 외부 화면에서는 새 내부 장치를 선택하지 않지만, 이미 잡은 레버를 유지하고 Space/Esc로 놓을 수 있다. Space가 양쪽 레버 중립을 동시에 실행하던 입력은 제거했다.

### 화면 전환과 상태 유지

`TankTestMode.SetMode()`는 카메라·오디오·HUD 등 화면 출력을 전환한다. F8/F9 전환만으로 레버 사용을 종료하거나 레버 값을 초기화하지 않는다.

`CrewInteract.ClearSelection()`은 선택 후보와 강조 표시만 지운다. 실제 사용 종료는 `StopUse()`로 구분한다. 좌석 변경이나 사용자·장치 비활성화 등 사용 조건이 깨지는 경우에는 사용을 종료한다.

## 3. 레버 사용과 실제 주행 연결

### 조종권의 기준

`TankControl`이 현재 조종자인 `CrewInteract` 참조를 보관한다. 주행 승인에는 다음 조건이 필요하다.

- 입력한 사용자의 `TankCrew`가 조종수 역할이다.
- 그 사용자가 씬에 연결된 정확한 주행 장치를 사용 중이다.
- 장치의 사용자와 사용자의 활성 장치가 서로 일치한다.
- 다른 조종자가 없고 필요한 컴포넌트가 활성 상태다.

조종수 좌석에 앉는 것만으로 주행하지 않는다. 탱크 오브젝트에 붙은 시험용 Driver 역할만으로 우회하는 경로도 사용하지 않는다.

### 주요 API

| API | 역할 |
| --- | --- |
| `TryBeginDrive(user)` | 지정 레버를 사용 중인 유효한 사용자의 조종권 획득 시도 |
| `CanDrive(user)` | 해당 사용자가 현재 주행 명령을 전달할 수 있는지 조회 |
| `SetLeverInputs(user, left, right)` | 현재 조종자의 좌우 입력만 받아 모터로 전달 |
| `EndDrive(user)` | 해당 조종자의 입력을 0으로 만들고 조종권 해제 |
| `IsDriving` / `DriveChanged` | 주행 사용 상태 조회 및 변경 알림. 모니터는 현재 여기에 의존하지 않음 |

레버 사용 시작·종료 이벤트를 `TankControl`에 연결했다. 역할 변경 후에 종료 이벤트가 와도 조종권 정리가 가능하다. 물리 갱신 전에도 사용자의 유효성을 확인해 무효한 조종자의 입력을 정리한다.

Space/Esc 해제, 좌석 변경, 창 포커스 상실, 관련 컴포넌트 비활성화 시에는 해당 경로에서 사용과 주행 입력을 정리한다. 모터 목표 입력을 0으로 만드는 것이므로 차체 속도를 즉시 0으로 고정하는 기능은 아니다. 기존 `TankMotor`의 감속과 물리 처리를 따른다.

## 4. 외부 전방 영상과 내부 모니터

### 영상 연결 구조

```text
탱크 외부의 기존 Driver Camera
    → DriverView.renderTexture
    → DriverScreen.mat
    → DriverViewScreen의 Display 메시
    → 플레이어가 내부 모니터에서 전방 영상 확인
```

기존 F1 카메라의 위치·방향을 유지하고 출력 대상을 RenderTexture로 바꿨다. 모니터용 카메라를 추가로 생성하지 않았다. 화면용 메시와 컴포넌트 참조는 씬에 저장되어 있다.

### 에셋과 설정

| 대상 | 구성 |
| --- | --- |
| `Assets/TankPrototype/Settings/DriverView.renderTexture` | 1024×576, 16:9, MSAA 1, 밉맵 비활성화 |
| `Assets/TankPrototype/Materials/DriverScreen.mat` | RenderTexture를 표시하는 Unlit 머티리얼 |
| `Assets/TankPrototype/Materials/DriverScreenOff.mat` | 기존 모니터 몸체용 검은 Unlit 머티리얼 |
| `DriverViewScreen`의 `Display` 자식 | 영상을 표시하는 Quad. 별도 충돌체 없음 |
| `DriverScreen` 컴포넌트 | 영상 카메라와 Display Renderer의 활성 상태 담당 |

기존 모니터의 상호작용·충돌체·강조 표시 구성을 보존했다. 영상 카메라에서는 `InteriorDevice` 레이어를 제외한다. 모니터 영상 카메라는 `MainCamera`로 사용하지 않고 AudioListener도 끈다.

`TankCrewCameras`는 RenderTexture 출력 카메라를 전체 화면 선택 대상에서 제외하고, 모니터가 관리하는 카메라 활성 상태를 덮어쓰지 않는다.

### 상시 표시로 바뀐 부분

이전에는 `TankControl`의 주행 상태 변경을 구독하고 레버를 잡았을 때만 영상을 표시했다. 현재 `DriverScreen`은 조종권 참조와 이벤트 구독을 제거했다.

- `OnEnable()`: 영상 카메라·RenderTexture·Display 참조가 유효하면 카메라와 화면을 켠다.
- `OnDisable()`: 영상 카메라와 화면을 끈다.
- 레버 해제, 포수·전차장 좌석 변경, F8/F9 전환, `TankControl` 비활성화에도 영상은 유지한다.

여기서 상시 표시는 Play Mode에서 모니터 오브젝트와 컴포넌트가 활성인 동안을 뜻한다. 편집 모드에서 계속 렌더링하는 기능은 아니다.

영상은 각 실행 환경에서 카메라가 RenderTexture로 렌더링한다. 이번 변경에는 영상 전송용 네트워크 기능이 없다. 상시 표시로 카메라의 렌더링 시간이 늘어나는 만큼 GPU 비용은 발생하며, 실제 FPS·GPU 시간 비교는 아직 측정하지 않았다.

## 5. 주요 관련 파일

경로의 기준은 Unity 프로젝트 루트다.

| 파일 | 현재 책임 / 변경 내용 |
| --- | --- |
| `Assets/TankPrototype/Scripts/TankInput.cs` | 좌석·사용·주행 입력 통합, 사용자 기준 주행 요청, F1 입력 제외 |
| `Assets/TankPrototype/Scripts/CrewSeats.cs` | 좌석 요청·승인, 위치와 역할 적용 |
| `Assets/TankPrototype/Scripts/CrewInteract.cs` | 선택 표시와 사용 종료 분리, 화면 전환 중 사용 유지 |
| `Assets/TankPrototype/Scripts/TankTestMode.cs` | F8/F9 화면 출력 전환 |
| `Assets/TankPrototype/Scripts/TankControl.cs` | 조종권 승인·검사·해제와 모터 명령 전달 |
| `Assets/TankPrototype/Scripts/TankCrewCameras.cs` | 전체 화면 카메라와 RenderTexture 카메라 구분 |
| `Assets/TankPrototype/Scripts/TankKeyboardDebug.cs` | 통합 역할·변경된 키 안내 표시 |
| `Assets/TankPrototype/Scripts/DriverScreen.cs` | 모니터 상시 표시와 비활성화 시 정리 |
| `Assets/TankPrototype/Scenes/TankTest.unity` | 좌석·조종자·주행 장치·영상 카메라·모니터 연결 |

스크립트만 복사하면 모니터 영상 연결이 완성되지 않는다. 씬, RenderTexture, 머티리얼 및 해당 `.meta` 파일을 함께 보존해야 한다. 이 구성은 `TankTest` 씬에 연결되어 있으므로 다른 씬이나 단독 프리팹에 자동 적용된다고 가정하지 않는다.

## 6. 검증 결과와 병합 후 확인

### 구현 당시 확인한 결과

- Unity 컴파일 오류 0개.
- 상시 표시 변경의 Play Mode 검사 18개 통과, 결과 `Passed: true`.
- 기록: `Builds/Validation/always-on-screen/result.json`.
- 레버 사용 전 영상 표시와 RenderTexture 생성 확인.
- 주행 권한 없는 사용자의 주행 입력 차단 확인.
- 레버 사용 후 주행, Space/Esc 해제 후 입력 중립과 영상 유지 확인.
- 좌석 변경, F8/F9 전환, `TankControl` 비활성화 후 영상 유지 확인.
- 모니터 비활성화 시 영상 종료, 재활성화 시 권한 없이 영상 복구 확인.

이 결과는 기존 구현 시점의 로컬 Play Mode 검증이다. 문서 작성 과정에서 테스트를 새로 실행한 것은 아니다. 실제 다중 접속, 네트워크 권한 동기화, 클라이언트별 카메라 구성, 빌드 환경의 렌더링 성능은 별도 검증 대상이다.

### 팀원이 병합 후 확인할 순서

1. `TankTest` 씬을 열고 컴파일 오류와 Inspector의 누락 참조를 확인한다.
2. Play 후 F9로 내부에 들어가 레버를 사용하기 전에도 모니터 영상이 나오는지 확인한다.
3. 조종수 좌석에서 레버를 선택하고 Space를 눌러 사용한 뒤 W/S, I/K로 주행한다.
4. 레버를 잡은 채 F8/F9를 전환해 조종권과 레버 값이 유지되는지 확인한다.
5. Space/Esc로 해제해 입력은 중립이 되고 영상은 유지되는지 확인한다.
6. 포수·전차장으로 좌석을 바꿔 주행 권한은 사라지고 영상은 유지되는지 확인한다.

`main`에 병합해도 개인 작업 브랜치는 자동으로 갱신되지 않는다. 각자 변경을 커밋하고 최신 `main`을 받은 뒤 자기 브랜치에 `main`을 병합한다. 씬 충돌을 해결할 때는 카메라·RenderTexture·Display 참조와 기존 사용자 배치를 함께 확인한다.

## 7. 네트워크 담당자에게 전달할 사항

- 주행 API에는 사용자 `CrewInteract`가 포함되어 있다. 이는 로컬 객체 기준 검사이며 네트워크 송신자 인증이나 서버 권한 검증을 대신하지 않는다.
- 좌석 승인 연동은 `CrewSeats`의 `localSeats`를 끄고 `SeatRequested`를 처리한 뒤 같은 요청 ID로 `ResolveSeat()`를 호출하는 경로를 사용한다.
- 현재 역할은 레버 사용자의 `TankCrew`를 기준으로 판단한다. 전차에 저장한 시험 역할을 임시로 바꿔 요청을 실행하는 방식과 혼용하지 않는다.
- 레버 입력·차체 상태 동기화와 모니터 영상 렌더링을 구분한다. 현재 모니터는 로컬 카메라 영상이며, 이 변경에서 영상 스트리밍은 구현하지 않았다.
- 여러 전차·플레이어가 생성되는 환경에서는 각 로컬 화면에 필요한 영상 카메라와 RenderTexture 연결을 별도로 점검한다. 이번 검증은 현재 `TankTest`의 단일 모니터 구성을 기준으로 한다.
