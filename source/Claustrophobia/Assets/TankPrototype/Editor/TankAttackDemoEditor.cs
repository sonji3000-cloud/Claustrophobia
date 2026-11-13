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
			EditorGUILayout.HelpBox("경로 시험: 구역 버튼을 누르면 해당 출발점에서 새 적을 생성합니다.\nPlay 시작 시 Test Index의 구역을 시험합니다 (기본 02). Scene 뷰의 Gizmos로 경로를 확인합니다.\n04는 이동 실패가 정상입니다. 05의 장애물 활성화 후 정체 복구는 다음 단계입니다.", MessageType.Info);
			using (new EditorGUI.DisabledScope(!Application.isPlaying || ai == null))
			{
				if (GUILayout.Button("01 직선 이동")) demo.RunPathTest(0);
				if (GUILayout.Button("02 ㄱ자 통로")) demo.RunPathTest(1);
				if (GUILayout.Button("03 벽 우회")) demo.RunPathTest(2);
				if (GUILayout.Button("04 이동 불가")) demo.RunPathTest(3);
				if (GUILayout.Button("05 직선 통로 (정체 시험 준비)")) demo.RunPathTest(4);
			}
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
