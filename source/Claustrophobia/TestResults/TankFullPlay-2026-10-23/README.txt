전차 입력·행동·역할 통합 Play Mode 검증 — 2026-10-23

이 기록은 상단 숫자 역할 단축키 추가 전의 검증이다. 최신 키 배치와 재현 소스는 ../TankRoleKeys-2026-10-23/에 있다.

최종 결과
138개 검사 통과, 실패 0개, 실행 중 수집한 Error/Exception/Assert 0개.
Unity 6000.6.3f1 / Input System 1.20.0 / URP 17.6.0.
results.json이 최종 결과이며 initial-results.json과 camera-fixed-results.json은 수정 전 확인 기록이다.

실행 방법과 범위
저장된 Assets/TankPrototype/Scenes/TankTest.unity를 Play Mode에서 추가 로드했다.
Input System 가상 키보드·마우스 이벤트를 실제 TankInput.Update/LateUpdate에서 처리했다.
TankControl이나 TankMotor 호출로 키보드 조작을 대신하지 않았으며, 역할 지정과 검사 환경 준비에는 API를 사용했다.
Game 뷰 포커스를 확인하고 우클릭 마우스 경로도 실제 LateUpdate에서 실행했다.
결과 화면 9장은 실행 중 Game 뷰를 캡처한 것이다.

검사한 기능
- W/S/I/K, Q/U/Space, 숫자 1~6, 반대 키 상쇄, 값 유지·상한, 중복 갱신 여부.
- 실제 전후 이동·양방향 제자리 선회·정지와 궤도 애니메이션.
- 전차 전체 충돌체를 기준으로 간격을 둔 벽 접근·정지·침투 제한.
- 방향키 조준, 상하 각도 제한, 반대 키 상쇄와 조종수의 조준 차단.
- R/T/Y/Enter, 빈 약실·장전·폐쇄·사격·자동 회복, Enter 유지 시 단발, 반동.
- 포수와 전차장의 공동 장전, 포수 전용 사격, 무권한 요청의 무기 상태 변경 차단.
- 실제 포탄 명중으로 HP 100→75→50→25→0, 파괴 1회와 파괴 후 추가 피해 거절.
- 예상 착탄 표시와 실제 충돌 결과의 일치 판정.
- 레버 설정을 유지한 채 포수로 역할을 바꿔 이동·선회 중 조준·사격, 새 표적 HP 100→75.
- 역할별 F1/F2/F3 허용·차단, 관측 키 사용 전후 역할 유지, F4, 카메라·AudioListener 단일 활성화.
- 우클릭 없는 마우스 차단, 우클릭 관측 회전 1회 적용, 상하 제한, 다른 역할의 회전 차단.
- 역할 변경·TankCrew 비활성화·누락 시 처리, 카메라 참조 누락 시 이동 유지.
- 저장된 기본 Tank 인스턴스의 외부 카메라 복귀와 관측 재진입.

발견하고 수정한 문제
기본 Tank의 외부 Main Camera가 씬에서 처음부터 비활성화되어 있었다.
기존 ExitObservation()은 원래 비활성 상태를 복원해 관측 권한을 잃은 뒤 카메라와 AudioListener가 모두 꺼졌다.
TankCrewCameras.ExitObservation()의 외부 시점 복귀에서 카메라·AudioListener를 명시적으로 켜도록 수정했다.
컴포넌트를 비활성화할 때 원래 상태를 복원하는 OnDisable 동작은 유지했다.
수정 후 실제 저장된 씬 인스턴스에서 외부 화면 복귀와 재진입을 통과했다.

검사 환경 보정
초기 벽 간격 검사는 차체 BoxCollider만 사용해 포신 등 자식 충돌체와 겹쳤다.
최종 실행은 attachedRigidbody가 동일한 모든 활성 비Trigger 충돌체의 전방 범위를 계산하고,
이동 전에 각 충돌체와 벽이 겹치지 않는지를 별도로 검사했다.
벽과 표적 위치는 Play Mode에서 검사 조건을 만들기 위해 조정했으며 씬·프리팹에 저장하지 않았다.

정리와 재현
검사 씬을 언로드하고 기존 씬 루트 활성 상태, 입력 기기, Time.timeScale을 복원했다.
Unity는 검증 전과 같은 편집 모드로 종료했다.
TankFullPlayVerification.cs는 재현용 검사 소스이며 Assets 밖에 보관하므로 게임 컴파일에 포함되지 않는다.
재실행하려면 이 소스를 Assets/TutorialInfo/Editor/에 복사해 컴파일한 뒤 Play Mode에서
Tools > Tank Verification > Run Full Play Checks 메뉴를 실행한다.
재실행하면 이 폴더의 results.json과 화면 파일이 갱신되므로 필요한 기존 결과는 먼저 보존한다.
검사 후 복사한 임시 스크립트와 해당 .meta를 제거한다.
