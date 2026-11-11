# 3인 공동 전차 제어 구조 및 네트워크 연동 인계

- 작성일: 2026-10-23
- 대상: `Assets/TankPrototype/`의 현재 전차 제어 코드
- 목적: 기존 프로토타입을 역할별 공동 제어 구조로 정리한 과정과 네트워크 담당자의 연결 지점을 설명한다.

현재까지 구현한 것은 **기존 전차 기능을 재사용하면서 입력·역할별 권한·실제 행동의 책임을 분리한 구조**다. 역할별 조작은 로컬 플레이로 검증했다. 플레이어 식별과 좌석 배정, 세 명의 동시 접속 및 상태 동기화는 네트워크 연동 단계에서 연결해야 한다.

## 1. 처음 코드는 어떤 구조였는가

처음부터 전차의 실제 기능은 어느 정도 분리되어 있었다.

| 클래스 | 기존에 담당하던 기능 |
|---|---|
| `TankMotor` | 좌우 레버 값으로 전차 이동·회전 |
| `TankAim` | 포탑 좌우 회전, 포신 상하 조준 |
| `TankGun` | 장전·약실 상태·사격·사격 후 복구 |
| `TankProjectile` | 실제 포탄 비행·충돌·피해 전달 |
| `TankBallistics` | 예상 탄착점 계산과 표시 |
| `TankHealth` | 피해 적용·HP·파괴 처리 |

이 기능들을 키보드로 실행하는 부분은 주로 `TankKeyboardDebug`에 들어 있었다. 이후 추가된 카메라는 `TankCrewCameras`에서 F1~F4와 마우스 입력을 별도로 읽었다.

```text
키보드
 └─ TankKeyboardDebug
     ├─ TankMotor
     ├─ TankAim
     └─ TankGun

F1~F4 / 마우스
 └─ TankCrewCameras
     └─ 카메라 선택·회전
```

한 명이 모든 입력을 사용하는 테스트에는 충분했지만, 조종수·포수·전차장이 같은 전차를 조작하려면 다음 구분이 필요했다.

- 누가 어떤 입력을 했는가.
- 그 역할이 해당 행동을 실행할 수 있는가.
- 허용된 행동을 어느 기능에 전달할 것인가.
- 실제 전차 상태를 어떻게 변경할 것인가.

그래서 기존 이동·전투 로직은 유지하고, 그 앞에 입력과 권한을 처리하는 구조를 추가했다.

## 2. 현재 전체 구조

```text
키보드·마우스
       │
       ▼
   TankInput
   입력을 행동 요청으로 변환
       │
       ▼
   TankControl ───────► TankCrew
   행동의 공통 진입점     현재 역할·행동 권한 확인
       │
       ├─ TankMotor
       │    └─ 전차 Rigidbody 이동·회전
       │
       ├─ TankAim
       │    └─ 포탑·포신 각도 변경
       │
       ├─ TankGun
       │    ├─ 장전·약실 상태 변경
       │    └─ TankProjectile 생성
       │          └─ 실제 충돌 시 TankHealth에 피해 전달
       │
       └─ TankCrewCameras
            └─ 관측 시점 선택·전차장 관측 회전
```

전차의 이동 상태, 포탑 각도, 약실 상태는 **전차 하나가 소유하는 공통 상태**다. 조종수용 전차와 포수용 전차를 따로 만드는 구조가 아니다.

현재는 하나의 `TankInput`이 입력을 읽고, `TankControl`이 현재 역할에 따라 요청을 허용하거나 차단한다.

## 3. 새로 분리한 클래스의 책임

### 3.1. TankInput: 입력 장치를 해석하는 곳

소스: [TankInput.cs](../Assets/TankPrototype/Scripts/TankInput.cs)

키보드·마우스를 읽어 `TankControl`의 메서드를 호출한다. 예를 들어 오른쪽 방향키를 누르면 포탑을 직접 회전시키지 않고, 오른쪽 조준 입력을 `TankControl.ApplyAim()`에 전달한다.

현재 포함된 입력은 다음과 같다.

- 좌우 레버 조작과 프리셋.
- 포탑·포신 조준.
- 장전·약실 조작·사격.
- F1~F4 관측 시점 요청.
- 우클릭을 누른 상태의 마우스 관측 입력.
- 상단 숫자 1·2·3을 사용하는 테스트 역할 변경.

`TankInput`은 `TankMotor`, `TankGun`, 카메라 등을 직접 참조하지 않고 `TankControl`을 참조한다. 이후 VR 입력이나 네트워크 입력을 붙일 때도 입력 장치 해석과 실제 전차 동작을 구분할 수 있도록 한 것이다.

### 3.2. TankControl: 전차 행동의 공통 진입점

소스: [TankControl.cs](../Assets/TankPrototype/Scripts/TankControl.cs)

전차 조작 요청을 받아 다음 순서로 처리한다.

1. `TankControl`과 필요한 컴포넌트가 사용 가능한지 확인한다.
2. `TankCrew`에서 해당 행동의 권한을 확인한다.
3. 허용된 요청을 실제 기능 컴포넌트에 전달한다.

이동 계산은 `TankMotor`, 조준 계산은 `TankAim`, 약실 상태 전이는 `TankGun`에 남겨 두었다.

카메라 역시 전차의 관측 기능으로 취급해 `TankControl`을 통해 요청한다. 카메라 선택과 회전 계산 자체는 `TankCrewCameras`가 담당한다.

추가로 `TankControl`은 다음을 담당한다.

- 현재 좌우 레버 설정값 보관.
- 역할 변경에 따른 입력 표시와 관측 권한 정리.
- 입력 유무와 관계없이 `TankGun.TickRecovery()`를 호출해 사격 후 복구 진행.

### 3.3. TankCrew: 역할과 허용 행동을 정의하는 곳

소스: [TankCrew.cs](../Assets/TankPrototype/Scripts/TankCrew.cs)

현재 역할은 다음 네 가지다.

```text
None / Driver / Gunner / Commander
```

이 역할로 `CanDrive`, `CanAim`, `CanFire`, `CanLoad`, `CanLook`, `CanView()`를 판정한다.

현재 `TankCrew`에 저장되는 것은 **로컬 테스트 조작자의 역할 하나**다. 플레이어 목록이나 좌석 점유 정보는 아직 들어 있지 않다.

### 3.4. 기존 클래스에서 빠진 책임

`TankKeyboardDebug`에서는 입력 처리를 제거했다. 현재는 역할·속도·무기 상태 등의 디버그 표시와 외부 카메라 추적을 담당한다.

`TankCrewCameras`에서도 키보드·마우스를 직접 읽는 부분을 제거했다. 현재는 전달받은 요청으로 카메라를 선택하고 회전한다.

이렇게 해서 같은 입력을 여러 스크립트가 중복 실행하지 않도록 정리했다.

## 4. 현재 역할별 권한과 호출 API

| 행동 | TankControl 진입 메서드 | 허용 역할 | 현재 테스트 입력 |
|---|---|---|---|
| 좌우 레버 설정 | `SetLeverInputs(left, right)` | 조종수 | W/S/Q, I/K/U, Space, 숫자패드 |
| 포탑·포신 조준 | `ApplyAim(turret, barrel, dt)` | 포수 | 방향키 |
| 포탄 삽입 | `RequestInsert()` | 포수·전차장 | R |
| 약실 닫기 | `RequestClose()` | 포수·전차장 | T |
| 빈 약실 열기 | `RequestOpenEmpty()` | 포수·전차장 | Y |
| 사격 | `RequestFire()` | 포수 | Enter |
| 조종수 관측 | `SwitchView(0)` | 조종수 | F1 |
| 포수 관측 | `SwitchView(1)` | 포수 | F2 |
| 전차장 관측 | `SwitchView(2)` | 전차장 | F3 |
| 테스트용 3인칭 시점 | `SwitchView(3)` | 역할 공통 | F4 |
| 전차장 관측 회전 | `Look(mouseDelta)` | 전차장 | 전차장 관측 중 우클릭+마우스 |

**F1~F3은 역할을 변경하지 않는다.** 현재 역할에 맞는 관측 기능을 요청하는 키다. F4는 활성 상태인 `TankCrew`에서 역할 공통으로 허용하는 테스트 기능이며, 실제 전환에는 해당 카메라 참조가 필요하다.

역할 변경은 별도의 테스트 경로다.

```text
상단 숫자 1 → Driver
상단 숫자 2 → Gunner
상단 숫자 3 → Commander

TankInput.ReadRole()
 → TankControl.SetTestRole()
 → TankCrew.SetRole()
```

숫자패드 1~6은 이동 프리셋이다. 따라서 숫자패드를 누를 때 전차가 움직이는 것은 현재 키 배치에 맞는 동작이다.

| 숫자패드 | 왼쪽 레버 | 오른쪽 레버 |
|---|---:|---:|
| 1 | 1 | 1 |
| 2 | 1 | 0.3 |
| 3 | 1 | -1 |
| 4 | -1 | 1 |
| 5 | -1 | -1 |
| 6 | -1 | -0.3 |

역할과 관측 시점의 숫자 값은 다르다.

```text
Role:        None=0, Driver=1, Gunner=2, Commander=3
CrewStation: Driver=0, Gunner=1, Commander=2, ThirdPerson=3
```

네트워크 메시지나 상태 변환에서 두 값을 그대로 캐스팅하면 안 된다.

무기 요청 메서드는 성공 여부를 `bool`로 반환한다. 현재 `false`만으로는 권한 거부, 컴포넌트 누락, 약실 상태 거부를 구분할 수 없다. 이동·조준·관측 메서드는 `void`이며, 허용되지 않는 요청은 실행하지 않고 반환한다.

## 5. 실제 입력이 행동으로 이어지는 과정

### 5.1. 조종수 이동

조종수가 왼쪽 레버를 올리는 경우의 흐름이다.

```text
W 입력
 → TankInput이 왼쪽 레버 설정값 증가
 → TankControl.SetLeverInputs(left, right)
 → TankCrew.CanDrive 확인
 → TankMotor.SetLeverInputs(left, right)
 → 다음 FixedUpdate에서 Rigidbody에 이동·회전 적용
```

좌우 레버는 `-1~1` 범위이며, 기본 이동 명령은 다음과 같이 계산한다.

```text
전진 명령 = (왼쪽 + 오른쪽) / 2
회전 명령 = (왼쪽 - 오른쪽) / 2
```

양쪽 레버가 같은 방향이면 전진·후진하고, 반대 방향이면 제자리 회전한다. 실제 속도는 가속·감속 처리를 거쳐 변경된다.

레버는 키를 놓아도 설정값이 유지된다. W/S는 왼쪽 레버를 증감하고 Q는 왼쪽 중립, I/K는 오른쪽 레버를 증감하고 U는 오른쪽 중립이다. Space는 양쪽을 중립으로 만든다.

**역할을 바꿔도 기존 레버 설정은 유지된다.** 포수로 바꾼 뒤 전차가 계속 움직이더라도 포수에게 새 이동 명령 권한이 생긴 것은 아니다. 조종수가 마지막으로 설정한 레버 값으로 `TankMotor`가 계속 동작하는 것이다.

향후 연결 종료나 좌석 이탈 때 레버를 유지할지 중립으로 만들지는 이 특성을 바탕으로 연결해야 한다.

### 5.2. 포수 조준과 사격

```text
방향키
 → TankInput
 → TankControl.ApplyAim()
 → TankCrew.CanAim 확인
 → TankAim.ApplyInput()
 → 포탑·포신 각도 변경
```

`TankAim`은 입력 축에 회전 속도와 `dt`를 곱해 각도를 누적한다. 포탑은 좌우 회전하고, 포신은 설정된 상하 각도 범위 안에서 움직인다.

사격은 별도로 처리한다.

```text
Enter
 → TankControl.RequestFire()
 → TankCrew.CanFire 확인
 → TankGun.RequestFire()
 → 약실·발사 조건 확인
 → 현재 포구 위치와 방향으로 포탄 생성
```

`TankInput.Update()` 안에서는 **조준을 먼저 적용한 뒤 사격을 요청**한다. 같은 프레임에 조준과 사격을 입력하면 그 프레임에 변경된 포구 방향으로 발사하기 위한 순서다.

현재 `TankInput.Update()` 내부 호출 순서는 역할 변경 → 조준 → 무기 → 레버 → 관측이다. 마우스 관측 입력은 `LateUpdate()`에서 읽는다. 이는 `TankInput` 내부의 순서이며, 서로 다른 컴포넌트의 `Update()` 실행 순서를 보장한다는 뜻은 아니다.

포탄이 생성되면 실제 충돌을 통해 `TankHealth`에 피해가 전달된다. 예상 탄착점 계산은 사격 보조·검증 기능이며, 피해 판정은 실제 포탄 충돌 경로에서 발생한다.

### 5.3. 전차장 관측

```text
F3
 → TankControl.SwitchView(2)
 → TankCrew.CanView(2) 확인
 → TankCrewCameras에서 전차장 카메라 선택

우클릭+마우스
 → TankControl.Look(delta)
 → TankCrew.CanLook 확인
 → TankCrewCameras에서 전차장 관측 방향 회전
```

`TankCrewCameras.Look()`에서도 현재 시점이 전차장 관측인지 확인한다. 전차장 역할이어도 다른 시점에서는 이 입력으로 관측 카메라가 회전하지 않는다.

마우스 입력은 픽셀 이동량에 감도를 곱하는 방식으로, 조준 축과 달리 `dt`를 추가로 곱하지 않는다.

역할 변경으로 기존 관측 권한을 잃으면 `TankControl`이 해당 관측을 종료하고 3인칭 또는 외부 관측 카메라로 복귀시킨다. 새 역할의 관측은 해당 F키로 요청한다.

## 6. 포수·전차장 공동 장전

포수와 전차장은 **같은 `TankGun`의 약실 상태**를 조작한다.

예를 들어 전차장이 포탄을 넣고, 포수가 약실을 닫은 뒤 사격할 수 있다. 어느 역할이 앞 단계를 실행했는지에 따라 별도의 약실이 생기지 않는다.

현재 상태 전이는 다음과 같다.

```text
EmptyOpen: 비어 있고 열린 상태
 ├─ 포탄 삽입 → LoadedOpen
 │                └─ 약실 닫기 → Ready
 │                                  └─ 사격 → Recovering
 │                                               └─ 복구 완료 → EmptyOpen
 │
 └─ 빈 약실 닫기 → EmptyClosed
                       └─ 빈 약실 열기 → EmptyOpen
```

여기에는 두 단계의 검사가 있다.

- `TankControl`: 요청한 역할이 이 행동을 할 수 있는가.
- `TankGun`: 현재 약실 상태에서 이 행동을 할 수 있는가.

따라서 전차장은 장전 권한이 있어도 사격할 수 없으며, 포수는 사격 권한이 있어도 `Ready` 상태가 아니면 발사할 수 없다.

현재 장전 요청은 즉시 상태를 바꾸는 방식이다. 포탄을 손으로 전달하거나, 두 사람이 일정 시간 장전 장치를 점유하는 기능은 아직 포함하지 않는다.

두 장전 요청이 연속으로 들어오면 먼저 처리된 요청이 상태를 바꾸고, 이후 요청은 바뀐 상태를 기준으로 검사한다. 다만 네트워크 메시지의 순서 결정이나 재전송 중복 제거는 아직 구현되어 있지 않다.

로컬 `TankInput`에서는 한 프레임에 무기 요청 하나를 처리하며, 우선순위는 사격 → 삽입 → 닫기 → 빈 약실 열기다. 이 입력 우선순위가 여러 네트워크 요청의 처리 순서까지 보장하는 것은 아니다.

## 7. 입력과 독립적으로 유지되는 전차 상태

현재 구조에서는 입력이 들어오는 순간과 전차 상태가 지속되는 시간을 구분한다.

- 이동은 마지막 레버 설정을 받아 `TankMotor.FixedUpdate()`에서 계속 계산한다.
- 조준은 입력이 있을 때 각도를 변경하고, 이후에도 변경된 각도를 유지한다.
- 약실 상태는 `TankGun`에 유지된다.
- 발사된 포탄은 입력이나 역할 변경과 관계없이 비행·충돌 처리를 이어간다.
- 사격 후 복구는 `TankControl.Update()`에서 진행하므로 `TankInput`만 비활성화해도 계속 진행된다. 이때 `TankControl`은 활성 상태여야 한다.

역할 변경 시에는 조준 입력 표시를 0으로 정리하지만, 실제 포탑·포신 각도를 초기화하지 않는다. 약실이나 이미 발사한 포탄도 초기화하지 않는다.

표현 기능도 실제 결과를 기준으로 연결했다.

- `TankTrackAnimation`: 실제 차체 속도·회전 속도로 궤도 애니메이션 계산.
- `TankGunRecoil`: 성공한 발사의 `ShotCount`와 발사 시각으로 반동 재생.
- `TankKeyboardDebug`: 현재 상태를 읽어 화면에 표시.

거부된 사격 요청은 성공한 발사 횟수를 늘리지 않고, 반동도 발생시키지 않는다.

## 8. 네트워크 담당자가 연결할 때 가장 중요한 부분

### 8.1. 현재 권한 검사와 요청자 신원 검사의 차이

**현재 `TankControl`의 권한 검사는 요청자의 신원을 검사하는 구조까지 구현된 것은 아니다.**

현재 메서드는 다음처럼 호출된다.

```text
SetLeverInputs(left, right)
ApplyAim(turret, barrel, dt)
RequestFire()
```

인자에 플레이어 ID나 요청자 정보가 없다. 내부에서는 해당 전차의 `TankCrew.CurrentRole` 하나를 확인한다.

따라서 네트워크에서 세 플레이어의 요청을 그대로 이 메서드에 전달하면 요청자별 권한 검증이 완성되는 것은 아니다.

연동 시에는 다음 관계가 필요하다.

```text
접속한 플레이어의 요청
 → 요청자 신원 확인
 → 해당 전차의 탑승 여부·배정된 역할 확인
 → 그 역할이 요청 행동을 할 수 있는지 확인
 → 전차 하나의 행동 실행
 → 확정된 결과를 각 클라이언트에 전달
```

현재 구현된 역할별 허용 규칙은 재사용할 수 있다. 다만 **검증된 요청자의 역할을 기준으로 검사하도록 `TankControl`과 권한 조회 경계를 확장해야 한다.**

요청이 올 때마다 공유 `TankCrew.CurrentRole`을 임시로 바꾸는 방식은 역할 상태와 관측 정리까지 함께 변경하므로 적절하지 않다.

### 8.2. 추가 연결이 필요한 항목

- 플레이어와 전차·좌석·역할의 대응 관계.
- 좌석 점유 및 역할 교대 승인.
- 요청 순서와 중복 처리.
- 권한 있는 실행 주체에서의 전차 물리·사격·피해 처리.
- 원격 객체에 확정 상태를 적용하는 수신 경로.

현재 여러 상태는 조회 전용 프로퍼티로 공개되어 있으며, 네트워크 스냅샷을 받아 상태를 복원하는 API는 아직 없다.

`TankMotor`, `TankAim`, `TankGun`의 실제 기능 메서드 자체에는 플레이어 역할 검사가 없다. 네트워크 요청이 이러한 하위 컴포넌트로 직접 들어갈 경우에도 검증된 권한 경로를 반드시 거쳐야 한다.

## 9. 공통 전차 상태와 각 플레이어 화면의 구분

다음은 현재 구조를 기준으로 한 네트워크 연결 방향이다. 이미 구현된 동기화 기능을 뜻하지 않는다.

### 9.1. 공유할 전차 상태와 결과

| 대상 | 공유할 상태·결과 |
|---|---|
| 차체 | 위치·회전·속도, 필요에 따른 좌우 레버 설정 |
| 포탑·포신 | 현재 조준 각도 |
| 무기 | 약실 상태, 복구 완료 시각, 확정된 발사 정보 |
| 포탄 | 생성·비행·충돌·종료 결과 |
| 피격 대상 | HP·파괴 결과 |
| 궤도·반동 | 실제 이동 상태와 확정 발사 정보를 이용한 표현 |

레버는 설정값이 유지되는 입력이고, 조준은 시간에 따라 각도를 누적하는 입력이며, 장전·사격은 한 번씩 처리하는 요청이다. 이 차이를 유지해서 연결해야 한다.

특히 조준에 사용하는 시간 간격은 네트워크 실행 기준에 맞춰야 한다. 현재 로컬 코드가 받는 `dt`를 클라이언트가 보낸 값 그대로 신뢰하는 것으로 연결해서는 안 된다.

포탄 생성과 피해 적용도 확정된 실행 경로에서 한 번 처리해야 한다. 원격 클라이언트의 발사 연출을 위해 기존 `RequestFire()`를 다시 호출하면 별도의 포탄과 약실 변경까지 발생할 수 있다.

현재 `ShotId` 역시 해당 `TankGun`의 발사 번호이므로, 여러 전차를 구분하는 네트워크 식별자는 별도로 필요하다.

네트워크 입력 경로를 추가할 때는 동일 객체의 로컬 `TankInput`과 수신 경로가 같은 행동을 중복 실행하지 않도록 실행 대상을 구분해야 한다. `TankInput`을 비활성화하는 경우에도 전차 기능과 복구를 담당하는 컴포넌트의 실행 여부는 별도로 관리해야 한다.

### 9.2. 각 플레이어의 카메라

카메라는 각 플레이어의 화면에서 처리한다.

카메라 요청을 `TankControl`로 통일한 설계는 유지할 수 있다. 다만 조종수가 F1을 눌렀을 때 세 사람의 화면이 모두 조종수 시점으로 바뀌면 안 된다.

현재 `TankCrewCameras`는 한 인스턴스에서 선택한 카메라와 `AudioListener`를 활성화한다. 네트워크에서는 로컬 플레이어의 역할과 화면에 해당하는 인스턴스만 동작하도록 연결해야 한다.

```text
조종수 클라이언트 → 조종수 관측 화면
포수 클라이언트   → 포수 관측 화면
전차장 클라이언트 → 전차장 관측 화면

세 화면이 바라보고 조작하는 전차 상태는 공통
```

상단 숫자 역할 변경과 F4는 현재 테스트 편의 기능이다. 실제 플레이어의 역할 배정은 네트워크의 좌석·역할 관리 결과와 연결해야 한다.

## 10. 현재 검증 범위

기존 Play Mode 검증 기록은 **152개 통과, 실패 0개, 런타임 오류 0개**다.

키보드·마우스 이벤트를 `TankInput`으로 전달해 이동·충돌·조준·공동 장전·사격·명중·HP·역할별 차단·관측·역할 변경을 확인했다.

- [검증 README](../TestResults/TankRoleKeys-2026-10-23/README.txt)
- [검증 결과 JSON](../TestResults/TankRoleKeys-2026-10-23/results.json)

이 검증은 현재의 로컬 역할 전환과 전차 기능을 대상으로 한다. 세 플레이어의 동시 접속, 요청자별 권한 검증, 지연·재전송·상태 동기화는 네트워크 연동 후 검증할 범위다.

## 11. 관련 파일

| 구분 | 파일 |
|---|---|
| 입력 | [TankInput.cs](../Assets/TankPrototype/Scripts/TankInput.cs) |
| 공통 행동 진입점 | [TankControl.cs](../Assets/TankPrototype/Scripts/TankControl.cs) |
| 역할·권한 | [TankCrew.cs](../Assets/TankPrototype/Scripts/TankCrew.cs) |
| 관측 기능 | [TankCrewCameras.cs](../Assets/TankPrototype/Scripts/TankCrewCameras.cs) |
| 이동 | [TankMotor.cs](../Assets/TankPrototype/Scripts/TankMotor.cs) |
| 조준 | [TankAim.cs](../Assets/TankPrototype/Scripts/TankAim.cs) |
| 무기·약실 | [TankGun.cs](../Assets/TankPrototype/Scripts/TankGun.cs) |
| 포탄 | [TankProjectile.cs](../Assets/TankPrototype/Scripts/TankProjectile.cs) |
| 탄도 예측 | [TankBallistics.cs](../Assets/TankPrototype/Scripts/TankBallistics.cs) |
| HP·파괴 | [TankHealth.cs](../Assets/TankPrototype/Scripts/TankHealth.cs) |
| 궤도 표현 | [TankTrackAnimation.cs](../Assets/TankPrototype/Scripts/TankTrackAnimation.cs) |
| 반동 표현 | [TankGunRecoil.cs](../Assets/TankPrototype/Scripts/TankGunRecoil.cs) |
| 디버그 표시 | [TankKeyboardDebug.cs](../Assets/TankPrototype/Scripts/TankKeyboardDebug.cs) |
| 기본 전차 프리팹 | [Tank.prefab](../Assets/TankPrototype/Prefabs/Tank.prefab) |
| Tiger 전차 프리팹 | [TigerTank.prefab](../Assets/TankPrototype/Prefabs/TigerTank.prefab) |
| 테스트 씬 | [TankTest.unity](../Assets/TankPrototype/Scenes/TankTest.unity) |
