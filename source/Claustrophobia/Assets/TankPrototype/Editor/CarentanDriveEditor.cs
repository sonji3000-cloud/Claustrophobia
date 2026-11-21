using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CarentanDrive))]
public sealed class CarentanDriveEditor : Editor
{
	public override bool RequiresConstantRepaint() => EditorApplication.isPlaying;

	public override void OnInspectorGUI()
	{
		using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
			DrawDefaultInspector();

		if (!EditorApplication.isPlaying)
		{
			EditorGUILayout.HelpBox("Layout에서 광장 배치를 고른 다음 Play를 누르세요.", MessageType.Info);
			return;
		}

		CarentanDrive drive = (CarentanDrive)target;
		TankAI enemy = drive.Enemy;
		TankCombat combat = enemy != null ? enemy.Combat : null;
		bool canStart = drive.CanStartMarket && combat != null && combat.State == TankCombat.CombatState.Idle;
		using (new EditorGUI.DisabledScope(!canStart))
			if (GUILayout.Button("광장 시험 시작 (0)")) drive.StartMarketCombat();

		using (new EditorGUI.DisabledScope(enemy == null))
			if (GUILayout.Button("시험 정지 (9)")) drive.StopPatrol();

		if (combat == null) return;

		EditorGUILayout.LabelField("교전 상태", combat.State.ToString());
		EditorGUILayout.LabelField("순찰 경유점", combat.PatrolIndex >= 0 ? (combat.PatrolIndex + 1).ToString() : "–");
		EditorGUILayout.LabelField("이동 상태", enemy.MoveStatus.ToString());
		if (enemy.Path != null && enemy.Path.HasPath)
			EditorGUILayout.LabelField("다음 경로 지점", enemy.Path.Point.ToString("F2"));

		EditorGUILayout.LabelField("사격 상태", enemy.AttackStatus.ToString());
		if (enemy.TryGetComponent(out TankGun gun)) EditorGUILayout.LabelField("실제 발사 수", gun.ShotCount.ToString());
		if (drive.PlayerHealth != null) EditorGUILayout.LabelField("Tiger HP", drive.PlayerHealth.CurrentHP.ToString("F1"));

		if (combat.LastStopReason.Length > 0) EditorGUILayout.HelpBox(combat.LastStopReason, MessageType.Info);
		if (enemy.MoveFailure.Length > 0) EditorGUILayout.HelpBox(enemy.MoveFailure, MessageType.Warning);
		if (enemy.AttackBlockReason.Length > 0) EditorGUILayout.HelpBox(enemy.AttackBlockReason, MessageType.Warning);
	}
}
