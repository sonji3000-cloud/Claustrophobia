F7 뒤쪽 추적 시야 수정 — 2026-10-30

대상: Assets/Scenes/tankroom.unity의 기존 CrewPlayer.
F7로 플레이어 뒤쪽 시야와 1인칭 시야 전환.
Game 화면 클릭 후 마우스로 기존 1인칭 시선을 움직이면 파란 시야선과 중앙 판정 범위가 회전.
뒤쪽 카메라는 좌석/몸 방향을 기준으로 따라가며, 독립 마우스 회전/휠 줌은 제거.
F7에서도 1/2/3 좌석 이동과 Space 장치 조작 가능.
F6는 기존 디버그 표시 전환. F7 복귀 시 바라보던 방향 유지.

변경: CrewDebugView / CrewLook / CrewInteract / CrewInteractHud / CrewSeats.
시점 입력 가능 조건과 Camera 화면 출력 조건 분리. 뒤쪽 시야에서는 플레이어 몸 표시.
기존 씬/프리팹/모델/재질/GUID/장치 배치는 이번에 변경하지 않음.
큐브 이름·계층·Layer·추가 장치 등록은 사용자 확인 후 진행할 후속 작업으로 보류.
네트워크 소스 변경 없음.

검증: Unity 6000.6.3f1 컴파일 및 Play Mode 12개 검사 통과(results.json).
가상 마우스의 delta를 실제 ReadLook 처리기에 전달해 yaw/pitch 회전 확인.
카메라가 몸 뒤에 남고, 판정선은 플레이어 눈 방향을 따르는지 확인.
Space·좌석 키 처리, 좌석 이동 추적, 1인칭 복귀 방향, 카메라/AudioListener 각 1개 확인.
rear-forward.png / rear-look-right.png를 열어 같은 뒤쪽 시야에서 판정 범위 회전을 비교.
검사 중 OS 포커스/커서/마우스를 가져오지 않음. 실제 Game 클릭과 마우스 캡처 경로는 수동 확인 필요.
임시 검사 도구는 Assets에서 제거. 사본/작업 전 백업은 Builds/F7Follow-2026-10-30에 보관.
이전 CrewInteraction 검증 기록의 '관찰 중 입력 중지' 동작은 이번 변경으로 대체됨.
