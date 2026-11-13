using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TankGroundDriveDemo))]
public sealed class TankGroundDriveEditor : Editor
{
	public override void OnInspectorGUI()
	{
		DrawDefaultInspector();
		var demo = (TankGroundDriveDemo)target;
		EditorGUILayout.HelpBox("Play Mode: select a tank and terrain, enable keyboard control, then focus Game. W/S forward/reverse, A/D turn, Space immediate stop. Release keys for normal braking. Reset places the hull level so the motor must align it.", MessageType.Info);

		using (new EditorGUI.DisabledScope(!Application.isPlaying))
		{
			if (GUILayout.Button("Drive Tiger")) demo.SelectTank(true);
			if (GUILayout.Button("Drive Sherman")) demo.SelectTank(false);
			if (GUILayout.Button(demo.UsesKeyboard ? "Disable keyboard control" : "Enable keyboard control"))
				demo.SetKeyboard(!demo.UsesKeyboard);
			if (GUILayout.Button("Stop both tanks")) demo.StopTanks();
			for (int i = 0; i < demo.CaseCount; i++)
				if (GUILayout.Button("Reset: " + demo.CaseName(i))) demo.Place(i);
			if (GUILayout.Button("Show driving camera")) demo.ShowCamera();
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
