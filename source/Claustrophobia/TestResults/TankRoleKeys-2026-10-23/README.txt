숫자 역할 단축키 추가 후 Play Mode 검증 — 2026-10-23

Unity 6000.6.3f1, 최종 152개 통과 / 실패 0개 / 런타임 오류 0개.
results.json은 실제 Play Mode에서 가상 키보드·마우스 이벤트를 TankInput으로 전달한 결과다.
Game 뷰 화면 10장과 재현용 TankFullPlayVerification.cs를 함께 보관한다.

추가한 기능
- 상단 숫자 1: Driver / 2: Gunner / 3: Commander.
- TankInput.ReadRole() → TankControl.SetTestRole() → TankCrew.SetRole().
- 역할 지정은 같은 프레임의 조준·무기·이동·관측 입력보다 먼저 적용한다.
- 기존 레버 프리셋 1~6은 숫자패드 1~6으로 이전했다.
- F1~F3은 현재 역할의 관측 기능을 유지하며 역할을 바꾸지 않는다.
- HUD 첫 줄에 현재 역할과 해당 숫자를 표시한다.

검증
이전 전체 검증 138개를 새 숫자패드 키 배치로 다시 실행했다.
역할 지정·레버 프리셋 충돌 방지·같은 프레임의 역할 변경과 조작·HUD 표시·관측 키 분리·비활성 TankCrew의 역할 지정 차단 등 14개를 추가했다.
실제 이동·벽 충돌·조준·공동 장전·사격·명중·HP·관측·마우스 회전·기본 탱크 카메라 복귀가 모두 통과했다.

재현
TankFullPlayVerification.cs를 임시로 Assets/TutorialInfo/Editor/에 복사해 컴파일한 뒤,
Play Mode에서 Tools > Tank Verification > Run Full Play Checks를 실행한다.
결과 파일을 덮어쓰므로 필요한 기존 결과는 먼저 보존한다.
검사 후 임시 스크립트와 .meta를 제거한다. 이번 실행 후에도 임시 스크립트를 제거하고 편집 모드로 복귀했다.
