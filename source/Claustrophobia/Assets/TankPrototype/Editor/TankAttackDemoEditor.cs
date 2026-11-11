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
		EditorGUILayout.HelpBox("TankAttackTest: 지정 표적 공격\nTankAIIntegrationTest: 이동 → 도착 → 공격\nHP 시험 버튼은 적 전차 자신에게 직접 피해를 적용합니다.", MessageType.Info);
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
