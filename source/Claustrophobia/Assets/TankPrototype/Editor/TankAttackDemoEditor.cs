using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TankAttackDemo))]
public sealed class TankAttackDemoEditor : Editor
{
	private bool showLegacy;

	public override bool RequiresConstantRepaint() => Application.isPlaying;

	public override void OnInspectorGUI()
	{
		var demo = (TankAttackDemo)target;
		if (demo.HasSearchTests)
		{
			DrawSearchTests(demo);
			if (TankCombatChecks.Current?.IsRunning == true) return;
			showLegacy = EditorGUILayout.Foldout(showLegacy, "기존 이동·공격 시험 및 상세 설정", true);
			if (!showLegacy) return;
		}
		DrawDefaultInspector();
		TankAI ai = demo.Enemy;
		EditorGUILayout.Space();
		if (demo.HasCombatTest)
		{
			using (new EditorGUI.DisabledScope(!demo.CanRunTests))
			{
				if (GUILayout.Button("원거리 발견 → 접근 후 사격")) demo.RunCombatTest(TankAttackDemo.CombatCase.Far);
				if (GUILayout.Button("근거리 발견 → 정지 사격")) demo.RunCombatTest(TankAttackDemo.CombatCase.Near);
				if (demo.HasCombatFailureTests)
				{
					if (GUILayout.Button("표적 가림 → 사격 중단")) demo.RunCombatTest(TankAttackDemo.CombatCase.Occluded);
					if (GUILayout.Button("접근 경로 없음 → 정지")) demo.RunCombatTest(TankAttackDemo.CombatCase.NoPath);
				}
			}
		}
		EditorGUILayout.HelpBox("HP 시험 버튼은 적 전차 자신에게 직접 피해를 적용합니다.\n이동 후 공격 버튼은 도착한 뒤 지정 표적을 공격합니다.", MessageType.Info);
		if (demo.HasPathTests)
		{
			EditorGUILayout.HelpBox("구역 버튼은 적을 새로 생성합니다. 재명령 버튼은 현재 적을 유지합니다.\n04는 경로 없음으로 실패, 05 정체 시험은 제한된 재탐색 후 실패가 정상입니다.\n대기 중 장애물을 끄면 이동을 재개할 수 있습니다.", MessageType.Info);
			using (new EditorGUI.DisabledScope(!demo.CanRunTests))
			{
				using (new EditorGUI.DisabledScope(demo.TargetHealth == null || demo.TargetHealth.Destroyed))
				{
					if (GUILayout.Button("01 이동 → 정지 → 공격 통합 시험")) demo.RunIntegrationTest();
				}
				if (GUILayout.Button("01 직선 이동")) demo.RunPathTest(0);
				if (GUILayout.Button("02 ㄱ자 통로")) demo.RunPathTest(1);
				if (GUILayout.Button("03 벽 우회")) demo.RunPathTest(2);
				if (GUILayout.Button("04 이동 불가")) demo.RunPathTest(3);
				if (GUILayout.Button("05 직선 통로 (정체 시험 준비)")) demo.RunPathTest(4);
				if (demo.HasBlocker && GUILayout.Button("05 정체 유발 (장애물 포함)")) demo.RunStallTest();
				if (demo.HasBlocker && GUILayout.Button(demo.BlockerEnabled ? "장애물 끄기" : "장애물 켜기")) demo.SetBlocker(!demo.BlockerEnabled);
				using (new EditorGUI.DisabledScope(ai == null))
				{
					if (GUILayout.Button("현재 목적지로 재명령")) demo.MoveToGoal();
					if (GUILayout.Button("출발 지점으로 재명령")) demo.ReturnToStart();
				}
			}
			if (Application.isPlaying && demo.TargetHealth != null && demo.TargetHealth.Destroyed)
				EditorGUILayout.HelpBox("표적이 파괴됐습니다. 통합 공격 시험을 다시 하려면 Play를 종료하고 다시 시작하세요.", MessageType.Info);
		}
		using (new EditorGUI.DisabledScope(!Application.isPlaying || ai == null))
		{
			if (GUILayout.Button("지정 표적 공격 (Attack Target)")) demo.AttackTarget();
			if (GUILayout.Button("공격 중지 (Stop Attack)")) demo.StopAttack();
			if (demo.HasMovePoint && GUILayout.Button("이동 후 공격 (Move Then Attack)")) demo.MoveThenAttack();
			if (GUILayout.Button("전체 중지 (Stop All Actions)")) demo.StopAllActions();
		}
		using (new EditorGUI.DisabledScope(!Application.isPlaying || demo.EnemyHealth == null || demo.EnemyHealth.Destroyed))
		{
			if (GUILayout.Button("적 전차 HP -25 (시험용)")) demo.DamageEnemy();
			if (GUILayout.Button("적 전차 파괴 (시험용)")) demo.DestroyEnemy();
		}
		if (!Application.isPlaying || ai == null) return;

		using (new EditorGUI.DisabledScope(true))
		{
			EditorGUILayout.ObjectField("생성된 적 전차", ai, typeof(TankAI), true);
			EditorGUILayout.Toggle("호스트 AI 실행", ai.CanAct);
			TankCombat combat = ai.Combat;
			if (combat != null && demo.HasCombatTest)
			{
				EditorGUILayout.EnumPopup("자율 교전 상태", combat.State);
				if (!string.IsNullOrEmpty(combat.LastStopReason))
					EditorGUILayout.TextField("자율 교전 종료 사유", combat.LastStopReason);
				if (combat.PatrolIndex >= 0)
					EditorGUILayout.IntField("순찰 경유점", combat.PatrolIndex + 1);
				if (combat.State == TankCombat.CombatState.Approach)
					EditorGUILayout.Vector3Field("접근 목적지", ai.Destination);
				if (combat.Target != null)
				{
					EditorGUILayout.ObjectField("교전 표적", combat.Target, typeof(TankTarget), true);
					EditorGUILayout.FloatField("발견 거리 (m)", combat.ContactDistance);
					EditorGUILayout.Vector3Field("교전 시작 위치", combat.EngageOrigin);
					EditorGUILayout.Vector3Field("마지막 목격 위치", combat.LastSeenPosition);
				}
			}
			EditorGUILayout.EnumPopup("이동 상태", ai.MoveStatus);
			EditorGUILayout.EnumPopup("이동 방식", ai.Movement);
			EditorGUILayout.TextField("시험 구역", demo.TestName);
			EditorGUILayout.TextField("이동 실패 사유", ai.MoveFailure);
			EditorGUILayout.Toggle("재탐색 대기", ai.IsRetrying);
			EditorGUILayout.IntField("재시도 횟수", ai.RetryCount);
			EditorGUILayout.IntField("재시도 한도", ai.RetryLimit);
			EditorGUILayout.FloatField("진전 없는 시간", ai.StallSeconds);
			EditorGUILayout.TextField("최근 재시도 원인", ai.RetryReason);
			if (ai.Path != null && ai.Path.HasPath)
			{
				EditorGUILayout.IntField("목표 지점 (시작 포함)", ai.Path.CornerIndex + 1);
				EditorGUILayout.IntField("전체 지점 수", ai.Path.CornerCount);
				EditorGUILayout.Vector3Field("현재 이동 지점", ai.Path.Point);
			}
			EditorGUILayout.EnumPopup("공격 상태", ai.AttackStatus);
			EditorGUILayout.TextField("차단 사유", ai.AttackBlockReason);
			TankGun gun = ai.GetComponent<TankGun>();
			EditorGUILayout.EnumPopup("약실 상태", gun.State);
			EditorGUILayout.IntField("실제 발사 수", gun.ShotCount);
			if (demo.EnemyHealth != null)
			{
				EditorGUILayout.FloatField("적 전차 자신의 HP", demo.EnemyHealth.CurrentHP);
				EditorGUILayout.IntField("적 전차 파괴 횟수", demo.EnemyHealth.DestructionCount);
			}
			TankHealth targetHealth = demo.DisplayTargetHealth;
			if (targetHealth != null)
				EditorGUILayout.FloatField("공격받는 표적 HP", targetHealth.CurrentHP);
			EditorGUILayout.TextField("파괴 후 중단 검사", demo.DestructionCheck);
		}
	}

	private static void DrawSearchTests(TankAttackDemo demo)
	{
		TankCombatChecks check = TankCombatChecks.Current;
		if (check != null && !check.IsFor(demo)) check = null;
		EditorGUILayout.Space();
		EditorGUILayout.LabelField("자율 교전 · 수색 · 순찰 복귀", EditorStyles.boldLabel);
		EditorGUILayout.HelpBox(
			"Play 후 아래 버튼을 누르세요. 버튼이 AI 모드를 켜고 시험을 시작합니다. Game 화면에서 전차를 보고, 아래에서 판정과 상태 전이를 확인하세요.",
			MessageType.Info);
		using (new EditorGUI.DisabledScope(!Application.isPlaying || TankCombatChecks.Current?.IsRunning == true))
		{
			if (GUILayout.Button($"전체 자동 검사 ({TankCombatChecks.SuiteCount}개)")) TankCombatChecks.Start(demo, TankCombatChecks.Scenario.All);
			if (GUILayout.Button("01 짧은 가림")) TankCombatChecks.Start(demo, TankCombatChecks.Scenario.BriefCover);
			if (GUILayout.Button("02 수색 중 재발견")) TankCombatChecks.Start(demo, TankCombatChecks.Scenario.Reacquire);
			if (GUILayout.Button("03 끝까지 못 찾음 → 순찰 복귀")) TankCombatChecks.Start(demo, TankCombatChecks.Scenario.Return);
		}
		using (new EditorGUI.DisabledScope(!Application.isPlaying || (check?.IsRunning != true && demo.Enemy == null)))
		{
			if (GUILayout.Button("전체 중지"))
			{
				if (check?.IsRunning == true) check.Cancel();
				else demo.StopAllActions();
			}
		}
		if (check == null) return;

		EditorGUILayout.Space();
		EditorGUILayout.LabelField("검사 상태", check.Status, EditorStyles.boldLabel);
		if (check.IsRunning) DrawLiveSearch(demo);
		foreach (string result in check.Results)
			EditorGUILayout.HelpBox(result, result.StartsWith("PASS", System.StringComparison.Ordinal) ? MessageType.Info : MessageType.Error);
		if (check.Timeline.Count > 0)
		{
			EditorGUILayout.LabelField(check.ActiveCase + " · 상태 전이", EditorStyles.boldLabel);
			foreach (string entry in check.Timeline) EditorGUILayout.LabelField(entry, EditorStyles.wordWrappedLabel);
		}
		if (!check.IsRunning)
		{
			EditorGUILayout.LabelField(check.ReportPath, EditorStyles.wordWrappedLabel);
			if (System.IO.File.Exists(check.ReportPath) && GUILayout.Button("결과 파일 보기"))
				EditorUtility.RevealInFinder(System.IO.Path.GetFullPath(check.ReportPath));
		}
	}

	private static void DrawLiveSearch(TankAttackDemo demo)
	{
		TankAI ai = demo.Enemy;
		TankCombat combat = ai != null ? ai.Combat : null;
		if (combat == null) return;
		string waypoint = combat.PatrolIndex >= 0 ? (combat.PatrolIndex + 1).ToString() : "—";
		EditorGUILayout.LabelField("현재 상태", TankCombatChecks.Label(combat.State));
		EditorGUILayout.LabelField("경유점 / 가림 벽", $"{waypoint}번 / {(demo.IsCombatCoverOn ? "켜짐" : "꺼짐")}");
		if (ai.TryGetComponent(out TankGun gun)) EditorGUILayout.LabelField("실제 발사 수", $"{gun.ShotCount}발");
		using (new EditorGUI.DisabledScope(true))
		{
			if (combat.LastSeenTime >= 0d) EditorGUILayout.Vector3Field("마지막 목격 위치", combat.LastSeenPosition);
			if (ai.MoveStatus == TankAI.MoveState.Moving) EditorGUILayout.Vector3Field("이동 목적지", ai.Destination);
		}
	}
}
