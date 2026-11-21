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

		if (drive.IsSouthEntry)
		{
			EditorGUILayout.HelpBox("직접 주행은 F9 → 1 → 조종 장치를 보고 Space. F5는 Sherman 카메라입니다.", MessageType.Info);
			using (new EditorGUI.DisabledScope(!drive.CanStartMarket || drive.PlayerHealth == null || drive.PlayerHealth.Destroyed))
			using (new EditorGUILayout.HorizontalScope())
			{
				if (GUILayout.Button("Tiger 노출점으로")) drive.MoveSouthTiger(true);
				if (GUILayout.Button("Tiger 건물 뒤로")) drive.MoveSouthTiger(false);
			}
		}

		if (combat == null) return;

		EditorGUILayout.LabelField("교전 상태", combat.State.ToString());
		EditorGUILayout.LabelField("순찰 경유점", combat.PatrolIndex >= 0 ? (combat.PatrolIndex + 1).ToString() : "–");
		EditorGUILayout.LabelField("이동 상태", enemy.MoveStatus.ToString());
		if (enemy.Path != null && enemy.Path.HasPath)
		{
			EditorGUILayout.LabelField("다음 경로 지점", enemy.Path.Point.ToString("F2"));
			EditorGUILayout.LabelField("이동 목적지", enemy.Path.Goal.ToString("F2"));
		}

		if ((drive.IsSouthEntry || drive.IsManeuverTest) && enemy.Sensor != null)
		{
			DrawSight(enemy.Sensor);
			if (combat.LastSeenTime >= 0d)
				EditorGUILayout.LabelField("마지막 목격 위치", combat.LastSeenPosition.ToString("F2"));
		}

		if (drive.IsManeuverTest)
		{
			EditorGUILayout.LabelField("사선 차단 시간", $"{Mathf.Max(enemy.ShotBlockedSeconds, combat.RepositionBlockSeconds):F2}초");
			EditorGUILayout.LabelField("위치 변경 횟수", combat.RepositionCount.ToString());
			EditorGUILayout.LabelField("근거리 이격 시도 / 후진 거리", $"{combat.BackOffCount}회 / {combat.BackOffTravel:F2}m");
			if (combat.BackOffCount > 0)
				EditorGUILayout.LabelField("이격 전후 거리", $"{combat.BackOffStartDistance:F2}m → {combat.BackOffEndDistance:F2}m");
			if (combat.LastBackOffReason.Length > 0)
				EditorGUILayout.HelpBox(combat.LastBackOffReason, MessageType.Info);
			if (combat.LastRepositionFailure.Length > 0)
				EditorGUILayout.HelpBox(combat.LastRepositionFailure, MessageType.Warning);
		}

		EditorGUILayout.LabelField("사격 상태", enemy.AttackStatus.ToString());
		if (enemy.TryGetComponent(out TankGun gun)) EditorGUILayout.LabelField("실제 발사 수", gun.ShotCount.ToString());
		if (drive.PlayerHealth != null) EditorGUILayout.LabelField("Tiger HP", drive.PlayerHealth.CurrentHP.ToString("F1"));

		if (combat.LastStopReason.Length > 0) EditorGUILayout.HelpBox(combat.LastStopReason, MessageType.Info);
		if (enemy.MoveFailure.Length > 0) EditorGUILayout.HelpBox(enemy.MoveFailure, MessageType.Warning);
		if (enemy.AttackBlockReason.Length > 0) EditorGUILayout.HelpBox(enemy.AttackBlockReason, MessageType.Warning);
	}

	private static void DrawSight(TankSensor sensor)
	{
		for (int i = 0; i < sensor.CandidateCount; ++i)
		{
			TankSensor.Reading reading = sensor.ReadCandidate(i);
			if (reading.Target == null || !reading.Target.IsPlayer) continue;

			EditorGUILayout.LabelField("Tiger 시야 판정", reading.State.ToString());
			if (reading.Blocker != null) EditorGUILayout.LabelField("가리는 오브젝트", reading.Blocker.name);
			return;
		}

		EditorGUILayout.LabelField("Tiger 시야 판정", "탐지 후보 없음");
	}

	private void OnSceneGUI()
	{
		DrawPoint("southPlayerStart", "Tiger 출발", Color.cyan);
		DrawPoint("southContact", "노출 / 재등장", Color.yellow);
		DrawPoint("southCover", "건물 뒤 엄폐", new Color(1f, .5f, .2f));
	}

	private void DrawPoint(string field, string label, Color color)
	{
		if (serializedObject.FindProperty(field).objectReferenceValue is not Transform point) return;

		using (new Handles.DrawingScope(color))
		{
			Handles.DrawWireDisc(point.position + Vector3.up * .15f, Vector3.up, 1.75f);
			Handles.Label(point.position + Vector3.up * 2f, label, EditorStyles.whiteBoldLabel);
		}
	}
}
