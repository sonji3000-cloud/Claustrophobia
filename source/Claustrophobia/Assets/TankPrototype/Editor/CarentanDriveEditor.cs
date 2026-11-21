using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(CarentanDrive))]
public sealed class CarentanDriveEditor : Editor
{
	public override bool RequiresConstantRepaint() => EditorApplication.isPlaying;

	public override void OnInspectorGUI()
	{
		CarentanDrive drive = (CarentanDrive)target;
		if (EditorApplication.isPlaying && drive.IsEntryTest)
		{
			DrawEntryTest(drive);
			return;
		}
		if (EditorApplication.isPlaying && (drive.IsCentralDrive || drive.IsWestGap))
		{
			DrawMapTest(drive);
			return;
		}

		using (new EditorGUI.DisabledScope(EditorApplication.isPlaying))
			DrawDefaultInspector();

		if (!EditorApplication.isPlaying)
		{
			EditorGUILayout.HelpBox("Layout에서 시험 배치를 고른 다음 Play를 누르세요.", MessageType.Info);
			return;
		}

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

	private static void DrawMapTest(CarentanDrive drive)
	{
		TankAI ai = drive.Enemy;
		bool central = drive.IsCentralDrive;
		bool tigerReady = drive.CanStartMarket && drive.PlayerHealth != null && !drive.PlayerHealth.Destroyed;
		bool canStart = central ? drive.CanStartCentralDrive : tigerReady && ai.Combat != null
			&& ai.Combat.State == TankCombat.CombatState.Idle;
		using (new EditorGUI.DisabledScope(!canStart))
			if (GUILayout.Button(central ? "중앙 연결로 주행 시작 (0)" : "틈 교전 시작 (0)")) drive.StartMarketCombat();

		using (new EditorGUI.DisabledScope(ai == null))
		{
			if (GUILayout.Button("시험 정지 (9)")) drive.StopPatrol();
			if (GUILayout.Button("Sherman 따라가기 (F5)")) drive.ObserveEnemy(ai);
		}

		if (central)
		{
			if (ai == null) return;
			EditorGUILayout.LabelField("이동 상태", ai.MoveStatus.ToString());
			if (ai.Path != null && ai.Path.HasPath)
			{
				EditorGUILayout.LabelField("다음 경로 지점", ai.Path.Point.ToString("F2"));
				EditorGUILayout.LabelField("이동 목적지", ai.Path.Goal.ToString("F2"));
			}
			if (ai.MoveFailure.Length > 0) EditorGUILayout.HelpBox(ai.MoveFailure, MessageType.Warning);
			return;
		}

		EditorGUILayout.HelpBox("직접 주행은 F9 → 1 → 조종 장치를 보고 Space.", MessageType.Info);
		using (new EditorGUI.DisabledScope(!tigerReady))
		using (new EditorGUILayout.HorizontalScope())
		{
			if (GUILayout.Button("건물 뒤로")) drive.MoveWestGapTiger(false);
			if (GUILayout.Button("틈에 노출")) drive.MoveWestGapTiger(true);
		}
		if (drive.PlayerHealth != null)
			EditorGUILayout.LabelField("Tiger HP / 피격", $"{drive.PlayerHealth.CurrentHP:F0} / {drive.PlayerHealth.DamageHitCount}회");

		DrawEntryTank("서쪽 Sherman", ai);
	}

	private static void DrawEntryTest(CarentanDrive drive)
	{
		using (new EditorGUI.DisabledScope(!drive.CanStartEntry))
			if (GUILayout.Button("진입 시험 시작 (0)")) drive.StartMarketCombat();

		using (new EditorGUI.DisabledScope(!drive.CanStartMarket))
			if (GUILayout.Button("시험 정지 (9)")) drive.StopPatrol();

		using (new EditorGUI.DisabledScope(drive.Enemy == null))
			if (GUILayout.Button("광장 전체 보기")) drive.ObserveOverview();

		using (new EditorGUILayout.HorizontalScope())
		{
			using (new EditorGUI.DisabledScope(drive.Enemy == null || drive.IsWestOnly))
				if (GUILayout.Button("남쪽 Sherman 따라가기")) drive.ObserveEnemy(drive.Enemy);

			using (new EditorGUI.DisabledScope(drive.WestEnemy == null))
				if (GUILayout.Button("서쪽 Sherman 따라가기")) drive.ObserveEnemy(drive.WestEnemy);
		}

		if (drive.PlayerHealth != null)
			EditorGUILayout.LabelField("Tiger HP / 피격", $"{drive.PlayerHealth.CurrentHP:F0} / {drive.PlayerHealth.DamageHitCount}회");

		if (drive.HasWestOrder)
			EditorGUILayout.LabelField("서쪽 출발까지", $"{drive.WestWait:F1}초");

		if (!drive.IsWestOnly) DrawEntryTank("남쪽 Sherman", drive.Enemy);
		DrawEntryTank("서쪽 Sherman", drive.WestEnemy);
	}

	private static void DrawEntryTank(string label, TankAI ai)
	{
		if (ai == null || ai.Combat == null) return;

		EditorGUILayout.Space();
		EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
		TankCombat combat = ai.Combat;
		EditorGUILayout.LabelField("교전 / 이동", $"{combat.State} / {ai.MoveStatus}");
		if (combat.PatrolIndex >= 0)
			EditorGUILayout.LabelField("순찰 경유점", (combat.PatrolIndex + 1).ToString());
		if (ai.Path != null && ai.Path.HasPath)
			EditorGUILayout.LabelField("이동 목적지", ai.Path.Goal.ToString("F2"));
		if (ai.Sensor != null) DrawSight(ai.Sensor);
		if (ai.TryGetComponent(out TankGun gun))
			EditorGUILayout.LabelField("실제 발사 / 피해를 준 탄", $"{gun.ShotCount} / {gun.DamageHitCount}");
		if (ai.TryGetComponent(out TankHealth health))
			EditorGUILayout.LabelField("Sherman HP", health.CurrentHP.ToString("F0"));

		if (ai.MoveFailure.Length > 0) EditorGUILayout.HelpBox(ai.MoveFailure, MessageType.Warning);
		if (ai.AttackBlockReason.Length > 0) EditorGUILayout.HelpBox(ai.AttackBlockReason, MessageType.Info);
		if (combat.LastStopReason.Length > 0) EditorGUILayout.HelpBox(combat.LastStopReason, MessageType.Info);
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
		var drive = (CarentanDrive)target;
		if (drive.IsEntryTest)
		{
			DrawEntryRoute("southEntrySpawn", "southEntryPatrol", "남쪽", Color.yellow);
			DrawEntryRoute("westEntrySpawn", "westEntryPatrol", "서쪽", Color.cyan);
			DrawPoint("entryTiger", "Tiger", Color.white);
			return;
		}
		if (drive.IsCentralDrive)
		{
			DrawPoint("centralSpawn", "Sherman 출발", Color.cyan);
			DrawPoint("centralGoal", "광장 서쪽 입구", Color.cyan);
			return;
		}
		if (drive.IsWestGap)
		{
			DrawEntryRoute("westGapSpawn", "westGapPatrol", "Sherman", Color.cyan);
			DrawPoint("westGapCover", "Tiger 건물 뒤", Color.white);
			DrawPoint("westGapOpening", "틈에 노출", Color.yellow);
			return;
		}

		DrawPoint("southPlayerStart", "Tiger 출발", Color.cyan);
		DrawPoint("southContact", "노출 / 재등장", Color.yellow);
		DrawPoint("southCover", "건물 뒤 엄폐", new Color(1f, .5f, .2f));
	}

	private void DrawEntryRoute(string spawnField, string routeField, string label, Color color)
	{
		if (serializedObject.FindProperty(spawnField).objectReferenceValue is not Transform spawn) return;

		DrawPoint(spawn, label + " 출발", color);
		var route = serializedObject.FindProperty(routeField);
		Vector3 from = spawn.position;
		using (new Handles.DrawingScope(color))
		{
			for (int i = 0; i < route.arraySize; i++)
			{
				if (route.GetArrayElementAtIndex(i).objectReferenceValue is not Transform point) continue;

				DrawPoint(point, $"{label} {i + 1}", color);
				Handles.DrawDottedLine(from + Vector3.up * .3f, point.position + Vector3.up * .3f, 4f);
				from = point.position;
			}
		}
	}

	private void DrawPoint(string field, string label, Color color)
	{
		if (serializedObject.FindProperty(field).objectReferenceValue is Transform point)
			DrawPoint(point, label, color);
	}

	private static void DrawPoint(Transform point, string label, Color color)
	{

		using (new Handles.DrawingScope(color))
		{
			Handles.DrawWireDisc(point.position + Vector3.up * .15f, Vector3.up, 1.75f);
			Handles.Label(point.position + Vector3.up * 2f, label, EditorStyles.whiteBoldLabel);
		}
	}
}
