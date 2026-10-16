탱크 프로토타입 / 팀 프로젝트 이식 관련 안내


1. 바로 실행
Assets/TankPrototype/Scenes/TankTest.unity를 열고 Play를 누르세요.
Game 뷰를 클릭한 후 키보드로 조작하세요.
이 씬에는 탱크 프리팹 인스턴스, 추적 카메라, 바닥, 표적, 얇은 충돌 시험 벽이 있습니다.
초기 자세에서 발사하면 앞쪽 시험 벽에 먼저 맞을 수 있습니다.
표적 체력 시험은 벽을 피해 이동/조준하거나 테스트 씬에서 벽을 비활성화하세요.

2. 조작
W / S: 왼쪽 레버 증가 / 감소
I / K: 오른쪽 레버 증가 / 감소
레버는 키를 떼어도 현재 값을 유지합니다.
Q: 왼쪽 중립, U: 오른쪽 중립, Space: 양쪽 중립(감속 정지)
1: 전진, 2: 우회전 주행, 3: 제자리 우회전
4: 제자리 좌회전, 5: 후진, 6: 후진 회전
좌/우 방향키: 포탑 회전, 위/아래 방향키: 포신 각도
R: 포탄 삽입, T: 폐쇄기 닫기, Enter: 한 발 발사
기본 발사 순서: R -> T -> Enter
발사 후 1.5초 회복이 끝나면 다시 R -> T -> Enter로 발사합니다.
Y: 빈 약실 열기(빈 상태로 T를 눌러 닫았을 때 사용)

3. 팀 씬에서 재사용
Prefabs/Tank.prefab: 이동, 조준, 발사, 포탄 참조, 탄도 표시가 연결된 탱크.
Prefabs/TankProjectile.prefab: 중력과 연속 충돌 감지를 사용하는 포탄.
Prefabs/TankTarget.prefab: 체력 100인 표적. 기본 포탄 피해량은 25입니다.
Tank.prefab의 루트는 차체 중심입니다. 평지 Y=0에서는 Y=0.52 정도에 놓으세요.
TankKeyboardDebug의 observationCamera와 targetHealth는 테스트 씬에서만 연결합니다.
프리팹 자체는 특정 씬의 카메라/표적을 참조하지 않습니다.
다른 씬에서는 해당 필드를 Inspector에서 연결하거나 팀의 카메라/입력 시스템을 사용하세요.
탄도 표시기는 탱크 프리팹 내부에 포함되어 있습니다.
팀 입력을 연결할 때 TankKeyboardDebug를 비활성화하고 다음 API를 호출하세요.
TankMotor.SetLeverInputs(left, right): 각 값 -1~1.
TankAim.ApplyInput(turret, gun, deltaTime): 조준 입력.
TankGun.TickRecovery(): 매 프레임 호출해 회복 완료 처리.
TankGun.RequestInsert(), RequestClose(), RequestFire(): 장전/폐쇄/발사 요청.
기본 이동 모델은 평지용이며 경사/궤도 물리, 네트워크 동기화는 포함하지 않습니다.
기본 레이어 충돌과 중력 (0,-9.81,0), Fixed Timestep 0.02초가 검증 기준입니다.

4. 역할
TankMotor: 평지 이동/회전과 가감속.
TankAim: 포탑과 포신 회전.
TankGun: 약실 상태, 발사, 회복 처리.
TankProjectile: 비행, 충돌, 피해와 수명 처리.
TankHealth: 표적 체력과 파괴 상태.
TankBallistics: 탄도 예측, 착탄 표시, 포탄과 공유하는 데이터 타입.
TankKeyboardDebug: 테스트용 입력(이건 나중에 역할 나눠서 할 예정), 카메라 추적, 진단 HUD.

