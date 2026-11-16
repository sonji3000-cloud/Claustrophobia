using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(TankTarget))]
public sealed class TankTargetEditor : Editor
{
	public override bool RequiresConstantRepaint() => Application.isPlaying;

	public override void OnInspectorGUI()
	{
		DrawDefaultInspector();
		var tank = (TankTarget)target;
		EditorGUILayout.Space();
		EditorGUILayout.HelpBox("전차 루트에 한 개만 연결합니다. 후보 등록은 발견 판정이 아닙니다.\n노랑: 시야 확인점 3개 / 빨강: 조준점 / 하늘색: 지면 기준점", MessageType.Info);

		if (!tank.HasFaction || !tank.IsConfigured)
			EditorGUILayout.HelpBox("루트 태그와 같은 루트의 HP, 전차 내부의 확인점 3개·조준점·지면 기준점을 확인하세요.", MessageType.Error);

		using (new EditorGUI.DisabledScope(true))
		{
			EditorGUILayout.TextField("진영", tank.IsPlayer ? "플레이어" : tank.IsEnemy ? "적" : "미지정");
			EditorGUILayout.Toggle("참조 구성 정상", tank.IsConfigured);
			if (Application.isPlaying) EditorGUILayout.Toggle("표적 후보로 사용 가능", tank.CanTarget);
		}

		if (GUILayout.Button("활성 전차 후보 목록 열기")) TankTargetWindow.Open();
	}
}

public sealed class TankTargetWindow : EditorWindow
{
	private Vector2 scroll;

	[MenuItem("Tools/Tank/AI Test/Target Registry")]
	public static void Open() => GetWindow<TankTargetWindow>("전차 후보 목록");

	private void OnEnable()
	{
		minSize = new Vector2(460f, 360f);
	}

	private void OnInspectorUpdate()
	{
		if (Application.isPlaying) Repaint();
	}

	private void OnGUI()
	{
		EditorGUILayout.HelpBox("Play 중 활성 TankTarget을 표시합니다. 같은 태그라도 각 행은 별개의 전차입니다.\n생성 대기·파괴·참조 누락 전차는 등록돼 있어도 사용 가능 여부가 꺼집니다. 시야·발견 판정은 아직 없습니다.", MessageType.Info);
		EditorGUILayout.LabelField("등록된 전차", TankTarget.Candidates.Count.ToString());
		scroll = EditorGUILayout.BeginScrollView(scroll);
		using (new EditorGUI.DisabledScope(true))
		{
			for (int i = 0; i < TankTarget.Candidates.Count; i++)
			{
				TankTarget tank = TankTarget.Candidates[i];
				if (tank == null) continue;

				EditorGUILayout.BeginVertical(EditorStyles.helpBox);
				EditorGUILayout.ObjectField("전차 루트", tank, typeof(TankTarget), true);
				EditorGUILayout.ObjectField("HP", tank.Health, typeof(TankHealth), true);
				EditorGUILayout.TextField("진영", tank.IsPlayer ? TankTarget.PlayerTag : tank.IsEnemy ? TankTarget.EnemyTag : "미지정");
				EditorGUILayout.Toggle("사용 가능", tank.CanTarget);
				EditorGUILayout.EndVertical();
			}
		}
		EditorGUILayout.EndScrollView();
	}
}
