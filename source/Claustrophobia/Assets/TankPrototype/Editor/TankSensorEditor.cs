using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TankSensor))]
public sealed class TankSensorEditor : Editor
{
	public override bool RequiresConstantRepaint() => Application.isPlaying;

	public override void OnInspectorGUI()
	{
		DrawDefaultInspector();
		if (Application.isPlaying) DrawStatus((TankSensor)target);
	}

	public static void DrawStatus(TankSensor sensor)
	{
		if (sensor == null) return;

		EditorGUILayout.Space();
		EditorGUILayout.LabelField("호스트 관측", EditorStyles.boldLabel);
		using (new EditorGUI.DisabledScope(true))
		{
			EditorGUILayout.Toggle("관측 가능", sensor.CanObserve);
			EditorGUILayout.ObjectField("선택 표적", sensor.SelectedTarget, typeof(TankTarget), true);
			EditorGUILayout.Toggle("현재 보임", sensor.IsVisible);
			EditorGUILayout.IntField("누적 검사 횟수", sensor.ScanCount);
			EditorGUILayout.IntField("최근 Ray 수", sensor.RayCount);
			EditorGUILayout.Toggle("검사 버퍼 초과", sensor.QueryOverflow);
		}
		for (int i = 0; i < sensor.CandidateCount; i++)
		{
			TankSensor.Reading reading = sensor.ReadCandidate(i);
			EditorGUILayout.BeginVertical(EditorStyles.helpBox);
			using (new EditorGUI.DisabledScope(true))
			{
				EditorGUILayout.ObjectField("후보", reading.Target, typeof(TankTarget), true);
				EditorGUILayout.TextField("판정", StateText(reading.State));
				EditorGUILayout.FloatField("수평 거리", reading.Distance);
				EditorGUILayout.FloatField("연속 확인 시간", reading.ConfirmSeconds);
				int count = ((reading.VisibleMask & 1) != 0 ? 1 : 0)
					+ ((reading.VisibleMask & 2) != 0 ? 1 : 0) + ((reading.VisibleMask & 4) != 0 ? 1 : 0);
				EditorGUILayout.TextField("노출 확인점", count + "/3 (앞·뒤·포탑 비트: " + reading.VisibleMask + ")");
				EditorGUILayout.ObjectField("가림 충돌체", reading.Blocker, typeof(Collider), true);
			}
			EditorGUILayout.EndVertical();
		}
	}

	private static string StateText(TankSensor.SightState state)
	{
		switch (state)
		{
			case TankSensor.SightState.Visible: return "발견 확정";
			case TankSensor.SightState.Confirming: return "발견 확인 중";
			case TankSensor.SightState.OutOfRange: return "거리 밖";
			case TankSensor.SightState.OutOfView: return "시야각 밖";
			case TankSensor.SightState.Occluded: return "가려짐";
			case TankSensor.SightState.Self: return "자기 자신 제외";
			case TankSensor.SightState.SameTeam: return "같은 진영 제외";
			case TankSensor.SightState.OtherSession: return "다른 세션 제외";
			case TankSensor.SightState.LocalDisabled: return "로컬 시험 대상 허용 꺼짐";
			case TankSensor.SightState.QueryFull: return "검사 버퍼 초과";
			default: return "비활성·파괴·생성 대기·참조 무효";
		}
	}
}

[CustomEditor(typeof(TankSensorTest))]
public sealed class TankSensorTestEditor : Editor
{
	public override bool RequiresConstantRepaint() => Application.isPlaying;

	public override void OnInspectorGUI()
	{
		DrawDefaultInspector();
		var test = (TankSensorTest)target;
		EditorGUILayout.HelpBox("Play 후 탐지 시험 시작을 누르세요. 기존 로컬 호스트를 재사용하며 적은 정지합니다.\n목격 기억·자동 공격은 다음 단계입니다. 벽에 가려지면 선택이 해제됩니다.", MessageType.Info);
		using (new EditorGUI.DisabledScope(!Application.isPlaying))
		{
			if (GUILayout.Button(test.IsWaiting ? "로컬 호스트 준비 중…" : "탐지 시험 시작 / 초기화")) test.StartTest();
		}
		using (new EditorGUI.DisabledScope(!test.IsRunning))
		{
			Button(test, "01 정면 (확인 시간 초기화)", TankSensorTest.TestCase.Front);
			Button(test, "02 후방", TankSensorTest.TestCase.Rear);
			Button(test, "03 거리 밖 60m", TankSensorTest.TestCase.Far);
			Button(test, "04 높은 벽 - 완전 가림", TankSensorTest.TestCase.Wall);
			Button(test, "05 낮은 벽 - 포탑 노출", TankSensorTest.TestCase.Partial);
			Button(test, "06 두 표적 - A가 가까움", TankSensorTest.TestCase.Two);
			Button(test, "07 B를 더 가까이 (A 유지)", TankSensorTest.TestCase.NearerB);
			Button(test, "08 A를 후방으로 (B 전환)", TankSensorTest.TestCase.SwitchToB);
			Button(test, "09 발견 후 45m (거리 유지)", TankSensorTest.TestCase.KeepRange);
			Button(test, "10 발견 후 65도 (각도 유지)", TankSensorTest.TestCase.KeepAngle);
			Button(test, "11 각도 밖 85도", TankSensorTest.TestCase.BeyondAngle);
			if (GUILayout.Button("시험 종료 / 기존 배치 복원")) test.EndTest();
		}
		if (!test.IsRunning) return;

		EditorGUILayout.LabelField("선택한 시험", test.Case.ToString());
		TankSensorEditor.DrawStatus(test.Sensor);
	}

	private static void Button(TankSensorTest test, string label, TankSensorTest.TestCase value)
	{
		if (GUILayout.Button(label)) test.SetCase(value);
	}
}
