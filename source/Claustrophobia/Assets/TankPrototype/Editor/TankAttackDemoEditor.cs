using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TankAttackDemo))]
public sealed class TankAttackDemoEditor : Editor
{
	public override bool RequiresConstantRepaint() => Application.isPlaying;

	public override void OnInspectorGUI()
	{
		DrawDefaultInspector();
		var demo = (TankAttackDemo)target;
		TankAI ai = demo.Enemy;
		EditorGUILayout.Space();
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
			if (demo.TargetHealth != null)
				EditorGUILayout.FloatField("공격받는 표적 HP", demo.TargetHealth.CurrentHP);
			EditorGUILayout.TextField("파괴 후 중단 검사", demo.DestructionCheck);
		}
	}
}
