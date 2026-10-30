TankTest 내부 참조 복구 — 2026-10-31

대상: Assets/TankPrototype/Scenes/TankTest.unity

복구
- LowerRoom/CrewPlayer의 CrewSeats에 Driver/Gunner/Commander 앵커 3개 연결.
- UpperRoom/LowerRoom의 CrewDevice 20개를 CrewInteract 목록에 연결. 빈 참조 없음.
- CrewDebugView의 InteriorObserver, AudioListener, 시야선, 중앙 원/유지 원 연결.
- Assets/Scenes/tankroom.unity의 기존 InteractionHUD를 편집 단계에 복사.
  UI 구성과 20개 진단 행/후보 표시는 보존하고 대상 플레이어 참조만 재연결.
- 복구 연결은 TankTest 씬에 직렬화한 프리팹 인스턴스 재정의로 저장.
- LowerRoom/UpperRoom/CrewPlayer 원본 프리팹, 런타임 코드, 네트워크 코드는 변경하지 않음.
- 기존 내부 오브젝트 배치 변경 0개. 기존 씬 ID 누락 0개(references.json).

검증
- 내부 단독 Play Mode 검사 181개 통과(results.json).
- 가상 1/2/3 입력으로 각 앵커 이동·역할 적용 확인.
- 허용된 좌석/장치 조합 33개에서 도달 가능 표면·선택·테두리·HUD 문구·Space 시작/종료 확인.
- F7 카메라 전환 API, 플레이어 뒤쪽 배치, 시야/중앙/유지 원 표시, 1인칭 복귀 확인.
- 내부 단독 검사 중 출력 카메라/AudioListener 각 1개 확인.
- 검사 중에만 외부 TankInput/TankCrewCameras/TankKeyboardDebug와 외부 카메라를 격리.
- 검사 중 NetworkAsset을 비활성화하여 네트워크 세션 자동 시작을 막음.
- 검사 후 저장된 TankTest를 다시 열어 외부 입력/카메라/NetworkAsset을 원래 설정으로 복원.
  외부 TankInput 활성 및 NetworkAsset 활성, 복구된 앵커/관찰 카메라 유지 재확인.
- 물리 키보드/마우스 포커스 경로는 조작하지 않음. 입력은 가상 키보드로 실제 처리기에 전달.

남은 통합 단계
- 이번 작업은 참조 복구만 수행. F8 입력 모드 전환은 아직 없음.
- 원래 외부 입력/카메라를 복원했으므로 1/2/3 및 Space 동시 입력,
  외부/내부 카메라 동시 출력 문제는 통합 씬에 여전히 남아 있음.
- 내부 기능의 tankroom 기준 단독 동작을 검증한 것이며,
  외부 시스템과 동시에 실행한 전체 흐름이 정리됐다는 의미는 아님.
- 다음 단계에서 내부/외부 입력 허용 및 카메라 출력 전환을 연결해야 함.

임시 도구는 Assets에서 제거. 백업/도구 사본은 Builds/RestoreInterior-2026-10-31 아래 보관.
