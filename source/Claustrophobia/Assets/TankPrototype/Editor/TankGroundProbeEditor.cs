using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TankGroundProbe))]
public sealed class TankGroundProbeEditor : Editor
{
	public override void OnInspectorGUI()
	{
		DrawDefaultInspector();
		var probe = (TankGroundProbe)target;
		if (!Application.isPlaying) probe.Sample();

		EditorGUILayout.Space();
		EditorGUILayout.LabelField("Ground reading", EditorStyles.boldLabel);
		EditorGUILayout.LabelField("Grounded", probe.IsGrounded ? "YES" : "NO");
		EditorGUILayout.LabelField("Contacts / hits", probe.ContactCount + " / " + probe.HitCount + " (5 points)");
		EditorGUILayout.LabelField("Supported slope", probe.SlopeAngle.ToString("F2") + " deg");
		using (new EditorGUI.DisabledScope(true))
			EditorGUILayout.Vector3Field("Ground normal", probe.GroundNormal);

		if (!probe.IsGrounded)
			EditorGUILayout.HelpBox("No supported plane. The normal is the last grounded reading (up before first contact).", MessageType.Info);
		if (probe.BufferFull)
			EditorGUILayout.HelpBox("A ray filled the hit buffer. That point is rejected; check overlapping colliders.", MessageType.Warning);
		EditorGUILayout.HelpBox("Gizmos: green = contact, yellow = ground too far, red = no hit. Cyan = supported normal.", MessageType.None);
	}

	public override bool RequiresConstantRepaint() => Application.isPlaying;
}

[CustomEditor(typeof(TankGroundDemo))]
public sealed class TankGroundDemoEditor : Editor
{
	public override void OnInspectorGUI()
	{
		DrawDefaultInspector();
		var demo = (TankGroundDemo)target;
		EditorGUILayout.HelpBox("Parked sensor samples only. Both samples use the original tank meshes and hull sizes, with kinematic bodies and no motor or AI. Slope driving is the next step.", MessageType.Info);
		for (int i = 0; i < demo.CaseCount; i++)
		{
			if (!GUILayout.Button(demo.CaseName(i))) continue;

			Undo.RecordObjects(new Object[] { demo.Tiger.transform, demo.Sherman.transform }, "Place ground samples");
			demo.Place(i);
			SceneView.RepaintAll();
		}
		if (GUILayout.Button("View samples in Scene"))
		{
			Selection.activeGameObject = demo.Tiger.gameObject;
			SceneView.lastActiveSceneView?.Frame(new Bounds(
				(demo.Tiger.transform.position + demo.Sherman.transform.position) * 0.5f, Vector3.one * 18f), false);
		}
		if (GUILayout.Button("Show samples in Game camera"))
		{
			var camera = serializedObject.FindProperty("testCamera").objectReferenceValue as Camera;
			if (camera == null) return;
			Undo.RecordObjects(new Object[] { camera, camera.transform }, "View ground samples");
			demo.ShowSamples();
		}
	}
}
