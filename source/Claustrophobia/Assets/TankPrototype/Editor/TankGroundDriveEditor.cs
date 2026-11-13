using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TankGroundDriveDemo))]
public sealed class TankGroundDriveEditor : Editor
{
	public override void OnInspectorGUI()
	{
		DrawDefaultInspector();
		var demo = (TankGroundDriveDemo)target;
		EditorGUILayout.HelpBox("Play 후 Game 화면의 패널에서 전차·지형을 선택하세요. 키보드 제어는 자동으로 켜집니다. WASD/방향키, Space 정지, Tab 전차 변경, R 재배치. 화면의 운전 버튼과 2초 전진 시험도 사용할 수 있습니다.", MessageType.Info);

		using (new EditorGUI.DisabledScope(!Application.isPlaying))
		{
			if (GUILayout.Button("Game 화면으로 이동")) EditorApplication.ExecuteMenuItem("Window/General/Game");
			if (GUILayout.Button("전차 정지")) demo.StopTanks();
		}

		TankMotor motor = demo.ActiveMotor;
		if (motor == null) return;
		EditorGUILayout.Space();
		EditorGUILayout.LabelField("Selected tank", motor.name);
		EditorGUILayout.LabelField("Can drive", motor.CanDrive ? "YES" : "NO");
		EditorGUILayout.LabelField("Forward / turn", motor.AppliedForwardSpeed.ToString("F2") + " m/s / "
			+ motor.AppliedTurnSpeed.ToString("F2") + " deg/s");
		if (motor.Ground != null)
			EditorGUILayout.LabelField("Ground slope", motor.Ground.SlopeAngle.ToString("F2") + " deg");
	}

	public override bool RequiresConstantRepaint() => Application.isPlaying;
}
