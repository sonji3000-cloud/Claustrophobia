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
		EditorGUILayout.HelpBox("TankAttackTest 씬에서 Play를 누르면 자동으로 표적을 공격합니다.\n실행 중 아래 버튼으로 공격을 중지하거나 다시 명령할 수 있습니다.", MessageType.Info);
		using (new EditorGUI.DisabledScope(!Application.isPlaying || ai == null))
		{
			if (GUILayout.Button("지정 표적 공격 (Attack Target)")) demo.AttackTarget();
			if (GUILayout.Button("공격 중지 (Stop Attack)")) demo.StopAttack();
		}
		if (!Application.isPlaying || ai == null) return;

		using (new EditorGUI.DisabledScope(true))
		{
			EditorGUILayout.ObjectField("생성된 적 전차", ai, typeof(TankAI), true);
			EditorGUILayout.Toggle("호스트 AI 실행", ai.CanAct);
			EditorGUILayout.EnumPopup("공격 상태", ai.AttackStatus);
			EditorGUILayout.TextField("차단 사유", ai.AttackBlockReason);
			TankGun gun = ai.GetComponent<TankGun>();
			EditorGUILayout.EnumPopup("약실 상태", gun.State);
			EditorGUILayout.IntField("실제 발사 수", gun.ShotCount);
			if (ai.TargetHealth != null)
				EditorGUILayout.FloatField("표적 HP", ai.TargetHealth.CurrentHP);
		}
	}
}
