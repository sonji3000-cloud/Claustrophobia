탱크 프로토타입 / 팀 프로젝트 이식 안내

1. 준비 환경
검증 기준: Unity 6000.3.10f1, URP 17.3.0, Input System 1.18.0.
팀 프로젝트와 같은 Unity 버전을 우선 사용하세요. 다른 버전은 별도 확인이 필요합니다.
이 코드는 Unity 6 Rigidbody.linearVelocity를 사용합니다.
Package Manager에서 Input System과 Universal RP를 설치/확인하세요.
Project Settings > Player > Other Settings > Active Input Handling을
Input System Package (New) 또는 Both로 설정하세요. 요청되면 Editor를 재시작하세요.
URP가 설정된 3D 프로젝트가 대상입니다. Built-in/HDRP는 재질 변환과 씬 조정이 필요합니다.

2. 가져오기
팀 저장소를 Clone하고 작업 브랜치에서 작업하세요.
Unity에서 Assets > Import Package > Custom Package를 선택하고
TankPrototype.unitypackage의 Assets/TankPrototype 전체를 Import하세요.
기존에 같은 경로나 같은 이름의 TankMotor 등 스크립트가 있다면 중복을 먼저 확인하세요.
패키지는 Assets/TankPrototype에만 파일을 추가합니다.
팀의 Packages, ProjectSettings, 기존 SampleScene은 덮어쓰지 않습니다.
Input System/URP 패키지와 프로젝트 설정은 위 준비 절차로 별도 확인하세요.

3. 바로 실행
Assets/TankPrototype/Scenes/TankTest.unity를 열고 Play를 누르세요.
Game 뷰를 클릭한 후 키보드로 조작하세요.
이 씬에는 탱크 프리팹 인스턴스, 추적 카메라, 바닥, 표적, 얇은 충돌 시험 벽이 있습니다.
초기 자세에서 발사하면 앞쪽 시험 벽에 먼저 맞을 수 있습니다.
표적 체력 시험은 벽을 피해 이동/조준하거나 테스트 씬에서 벽을 비활성화하세요.

4. 조작
W / S: 왼쪽 레버 증가 / 감소
I / K: 오른쪽 레버 증가 / 감소
레버는 키를 떼어도 현재 값을 유지합니다.
Q: 왼쪽 중립, U: 오른쪽 중립, Space: 양쪽 중립(감속 정지)
상단 숫자 1: 조종수, 2: 포수, 3: 전차장 역할 지정(로컬 테스트용).
숫자패드 1: 전진, 2: 우회전 주행, 3: 제자리 우회전
숫자패드 4: 제자리 좌회전, 5: 후진, 6: 후진 회전
좌/우 방향키: 포탑 회전, 위/아래 방향키: 포신 각도
R: 포탄 삽입, T: 폐쇄기 닫기, Enter: 한 발 발사
기본 발사 순서: R -> T -> Enter
발사 후 1.5초 회복이 끝나면 다시 R -> T -> Enter로 발사합니다.
Y: 빈 약실 열기(빈 상태로 T를 눌러 닫았을 때 사용)
F1 / F2 / F3: 현재 역할의 조종수 / 포수 / 전차장 관측 기능. 역할을 바꾸지 않습니다.
F4: 역할과 별개인 테스트용 3인칭(카메라가 연결된 경우).
전차장 시점에서 우클릭을 누른 채 마우스 이동: 관측 방향 조절.
TankCrew의 Role로 테스트 역할을 지정합니다. 두 탱크 프리팹의 기본 역할은 Driver입니다.
Driver: 이동·레버와 F1, Gunner: 조준·장전·사격과 F2, Commander: 장전·약실 조작과 F3·관측 회전.
None: 전차 조작 불가(F4 테스트 시점은 사용 가능).
포수와 전차장 모두 삽입·닫기·빈 약실 열기가 가능합니다. 사격은 포수 전용입니다.
테스트 중 역할 변경은 상단 숫자 1/2/3, TankCrew.SetRole() 또는 Inspector의 Role로 합니다. F1~F3은 역할 지정에 관여하지 않습니다.
HUD 첫 줄에 현재 역할과 역할 변경 숫자를 표시합니다. 숫자 역할 지정은 같은 프레임의 다른 조작보다 먼저 처리합니다.
역할을 바꾸면 기존 조준 입력 표시를 지우고, 권한이 없는 관측 화면은 기본 시점으로 복귀합니다.
레버 설정은 기존처럼 유지합니다. 조종수 역할에서 Space로 중립을 지정한 뒤 다른 역할을 시험하세요.

5. 팀 씬에서 재사용
Prefabs/Tank.prefab: 이동, 조준, 발사, 포탄 참조, 탄도 표시가 연결된 탱크.
Prefabs/TankProjectile.prefab: 중력과 연속 충돌 감지를 사용하는 포탄.
Prefabs/TankTarget.prefab: 체력 100인 표적. 기본 포탄 피해량은 25입니다.
Tank.prefab의 루트는 차체 중심입니다. 평지 Y=0에서는 Y=0.52 정도에 놓으세요.
TankKeyboardDebug의 observationCamera와 targetHealth는 테스트 씬에서만 연결합니다.
프리팹 자체는 특정 씬의 카메라/표적을 참조하지 않습니다.
다른 씬에서는 해당 필드를 Inspector에서 연결하거나 팀의 카메라/입력 시스템을 사용하세요.
탄도 표시기는 탱크 프리팹 내부에 포함되어 있습니다.
팀 입력을 연결할 때 TankInput을 비활성화하고 다음 API를 호출하세요.
입력 경로: 키보드/마우스 -> TankInput -> TankControl -> 이동·조준·무기·카메라 컴포넌트.
TankInput은 TankControl만 참조합니다. 시점 전환과 관측도 TankControl을 거칩니다.
TankControl은 각 요청을 실행하기 전에 TankCrew의 역할 권한을 확인합니다. TankCrew가 없거나 비활성화되면 조작 요청을 거부합니다.
TankControl은 레버 값을 보관하고, 입력 컴포넌트가 꺼져 있어도 발사 후 회복을 매 프레임 처리합니다.
leverChangeRate는 키보드로 레버를 증감하는 속도이므로 TankInput에 남아 있습니다.
TankKeyboardDebug는 진단 HUD와 기존 외부 카메라 추적만 담당합니다.
TankControl.SetLeverInputs(left, right): 각 값 -1~1. LeftLever/RightLever로 유지 값을 조회합니다.
TankControl.ApplyAim(turret, barrel, deltaTime): 조준 입력. 사격과 같은 프레임에는 먼저 호출합니다.
TankControl.RequestInsert(), RequestClose(), RequestOpenEmpty(), RequestFire(): 무기 요청, 성공 여부 반환.
TankControl.SwitchView(station): 0 조종수 / 1 포수 / 2 전차장 / 3 3인칭.
TankControl.Look(delta): 관측 이동량. 감도·각도 제한·회전은 TankCrewCameras가 적용합니다.
TankControl이 활성 상태일 때 요청을 처리합니다. 카메라가 없거나 꺼져 있으면 시점·관측 요청은 무시합니다.
TankCrew는 현재 로컬 테스트 조작자 한 명의 역할을 관리합니다. 세 플레이어의 연결·좌석 점유와 네트워크 처리는 포함하지 않습니다.
기능별 컴포넌트 API를 직접 호출하면 TankControl의 역할 검사를 거치지 않으므로 플레이어 조작은 위 TankControl API를 사용하세요.
기본 이동 모델은 평지용이며 경사/궤도 물리, 네트워크 동기화는 포함하지 않습니다.
기본 레이어 충돌과 중력 (0,-9.81,0), Fixed Timestep 0.02초가 검증 기준입니다.

6. Git에 올릴 것
Import된 Assets/TankPrototype 및 관련 .meta 파일을 함께 커밋하세요.
새로 설치한 패키지/필수 설정 변경이 있다면 해당 변경도 팀 기준에 맞춰 커밋하세요.
Library, Temp, Logs, UserSettings는 올리지 않습니다.
AgentScripts나 Unity Pipeline 도구는 실행에 필요하지 않습니다.

7. 역할
TankMotor: 평지 이동/회전과 가감속.
TankAim: 포탑과 포신 회전.
TankGun: 약실 상태, 발사, 회복 처리.
TankProjectile: 비행, 충돌, 피해와 수명 처리.
TankHealth: 표적 체력과 파괴 상태.
TankBallistics: 탄도 예측, 착탄 표시, 포탄과 공유하는 데이터 타입.
TankInput: 키보드·마우스를 읽어 TankControl에 테스트 조작 요청 전달.
TankControl: 이동·조준·무기·시점·관측 요청의 공통 진입점, 레버 값 유지와 무기 회복 갱신.
TankCrew: 로컬 테스트 역할 지정과 이동·조준·장전·사격·관측 권한 판정.
TankCrewCameras: 요청받은 카메라 선택과 전차장 관측 회전 적용.
TankKeyboardDebug: 기존 외부 카메라 추적과 진단 HUD(입력·전차 조작 없음).
기본 발사에도 공유 타입이 필요하므로 Scripts 폴더는 함께 가져오세요.

8. 입력·역할 통합 플레이 검증 — 2026-10-23
Unity 6000.6.3f1 / Input System 1.20.0 / URP 17.6.0의 실제 Play Mode에서 138개 검사 통과.
가상 키보드·마우스 이벤트를 실제 TankInput 갱신에서 처리해 이동·충돌·조준·공동 장전·사격·명중·HP·역할별 관측을 확인했습니다.
역할 변경 후 기본 Tank의 외부 카메라가 꺼지는 문제를 수정하고 복귀·재진입을 검증했습니다.
상세 결과와 Game 뷰 화면: 프로젝트 루트 TestResults/TankFullPlay-2026-10-23/.
숫자 역할 단축키 추가 후 숫자패드 프리셋을 포함해 152개 Play Mode 검사 통과.
최신 결과·화면·재현용 검사 소스: 프로젝트 루트 TestResults/TankRoleKeys-2026-10-23/.
