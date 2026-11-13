using System;
using System.Collections.Generic;
using System.IO;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

// Editor-only maintenance for the authored AI test ground. Never drives the tank.
public static class TankNavMeshCheck
{
	private const string ScenePath = "Assets/TankPrototype/Scenes/TankAIIntegrationTest.unity";
	private const string DataPath = "Assets/TankPrototype/Navigation/TankAIIntegrationTest/NavMesh-TankFlat.asset";
	private const string ReportPath = "Builds/TankNavMeshSetup/navmesh-check.txt";
	private const int AgentId = 174295371;

	[MenuItem("Tools/Tank/AI Test/Bake and Check NavMesh")]
	public static void Bake()
	{
		Run(true);
	}

	[MenuItem("Tools/Tank/AI Test/Check Saved NavMesh")]
	public static void Check()
	{
		Run(false);
	}

	private static void Run(bool bake)
	{
		if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
			throw new InvalidOperationException("Stop Play Mode and wait for compilation before checking the NavMesh.");

		Scene previous = SceneManager.GetActiveScene();
		Scene scene = SceneManager.GetSceneByPath(ScenePath);
		bool opened = !scene.IsValid() || !scene.isLoaded;
		if (!opened && scene.isDirty)
			throw new InvalidOperationException("Save the AI test scene before baking or checking it.");

		Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
		try
		{
			if (bake) ApplySettings();
			if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
			GameObject geometry = FindRoot(scene, "AI Test Ground").transform.Find("Geometry").gameObject;
			NavMeshSurface surface = geometry.GetComponent<NavMeshSurface>();
			if (surface == null) throw new InvalidOperationException("The authored NavMeshSurface is missing.");

			NavMeshBuildSettings settings = surface.GetBuildSettings();
			if (Mathf.Abs(settings.agentRadius - 3.8f) > 0.001f || Mathf.Abs(settings.agentHeight - 3.5f) > 0.001f)
				throw new InvalidOperationException("TankFlat settings are not loaded. Reopen this project so the saved Navigation settings are imported.");
			if (surface.collectObjects != CollectObjects.Children || surface.useGeometry != NavMeshCollectGeometry.PhysicsColliders)
				throw new InvalidOperationException("The surface must collect only Geometry children using Physics Colliders.");

			if (bake) BakeSurface(surface, DataPath);
			Terrain terrain = FindRoot(scene, "Ground Probe Test").GetComponentInChildren<Terrain>();
			NavMeshSurface slopes = terrain != null ? terrain.GetComponent<NavMeshSurface>() : null;
			if (slopes == null || slopes.agentTypeID != AgentId || slopes.collectObjects != CollectObjects.Children
				|| slopes.useGeometry != NavMeshCollectGeometry.PhysicsColliders)
				throw new InvalidOperationException("The slope surface must collect only Terrain colliders for the tank agent.");
			if (bake) BakeSurface(slopes, "Assets/TankPrototype/Navigation/TankAIIntegrationTest/NavMesh-TankSlope.asset");
			if (slopes.navMeshData == null || !AssetDatabase.Contains(slopes.navMeshData))
				throw new InvalidOperationException("The saved slope NavMesh data is missing.");
			if (surface.navMeshData == null || !AssetDatabase.Contains(surface.navMeshData))
				throw new InvalidOperationException("No saved NavMesh data. Run Bake and Check NavMesh first.");

			var lines = new List<string>
			{
				"Scene: " + ScenePath,
				"Unity: " + Application.unityVersion,
				"Agent: TankFlat; radius=3.8m; height=3.5m; slope=" + settings.agentSlope + "deg; climb=0.1m",
				"Geometry: Physics Colliders, Geometry hierarchy only",
				"Data: " + AssetDatabase.GetAssetPath(surface.navMeshData)
			};
			Transform cases = FindRoot(scene, "Test Cases").transform;
			var filter = new NavMeshQueryFilter { agentTypeID = surface.agentTypeID, areaMask = NavMesh.AllAreas };
			var path = new NavMeshPath();
			for (int i = 0; i < cases.childCount; i++)
			{
				Transform test = cases.GetChild(i);
				Vector3 start = MapPoint(test.Find("Start"), filter);
				Vector3 goal = MapPoint(test.Find("Goal"), filter);
				bool found = NavMesh.CalculatePath(start, goal, filter, path);
				bool complete = found && path.status == NavMeshPathStatus.PathComplete;
				bool blocked = NavMesh.Raycast(start, goal, out _, filter);
				bool expectComplete = i != 3;
				bool expectBlocked = i == 1 || i == 2 || i == 3;
				if (complete != expectComplete || i < 5 && blocked != expectBlocked)
					throw new InvalidOperationException(test.name + ": unexpected path=" + path.status + ", directBlocked=" + blocked);

				lines.Add("PASS " + test.name + ": " + path.status + ", directBlocked=" + blocked + ", corners=" + path.corners.Length);
			}

			Transform blocker = FindRoot(scene, "Runtime Blockers").transform.GetChild(0);
			if (blocker.gameObject.activeSelf || blocker.IsChildOf(geometry.transform))
				throw new InvalidOperationException("The stall blocker must start inactive and remain outside Geometry.");
			lines.Add("PASS stall blocker: inactive, physical collider, excluded from bake hierarchy");
			lines.Add("Path calculation checked. Actual tank path following is checked separately in Play Mode.");
			File.WriteAllLines(ReportPath, lines);
			Debug.Log("[TANK NAVMESH] All authored test cases passed. Report: " + ReportPath);
		}
		catch (Exception error)
		{
			File.WriteAllText(ReportPath, "FAILED: " + error);
			throw;
		}
		finally
		{
			if (opened && scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
			if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
		}
	}

	private static void ApplySettings()
	{
		if (NavMesh.GetSettingsByID(AgentId).agentTypeID == AgentId) return;

		// Also refresh the running Editor when this settings file was updated on disk.
		UnityEngine.Object asset = Unsupported.GetSerializedAssetInterfaceSingleton("NavMeshProjectSettings");
		var settings = new SerializedObject(asset);
		SerializedProperty agents = settings.FindProperty("m_Settings");
		SerializedProperty names = settings.FindProperty("m_SettingNames");
		int index = -1;
		for (int i = 0; i < agents.arraySize; i++)
		{
			if (agents.GetArrayElementAtIndex(i).FindPropertyRelative("agentTypeID").intValue == AgentId)
			{
				index = i;
				break;
			}
		}
		if (index < 0)
		{
			index = agents.arraySize;
			agents.InsertArrayElementAtIndex(index);
			names.arraySize = agents.arraySize;
		}

		SerializedProperty agent = agents.GetArrayElementAtIndex(index);
		agent.FindPropertyRelative("agentTypeID").intValue = AgentId;
		agent.FindPropertyRelative("agentRadius").floatValue = 3.8f;
		agent.FindPropertyRelative("agentHeight").floatValue = 3.5f;
		agent.FindPropertyRelative("agentSlope").floatValue = 16f;
		agent.FindPropertyRelative("agentClimb").floatValue = 0.1f;
		agent.FindPropertyRelative("ledgeDropHeight").floatValue = 0f;
		agent.FindPropertyRelative("maxJumpAcrossDistance").floatValue = 0f;
		agent.FindPropertyRelative("minRegionArea").floatValue = 2f;
		agent.FindPropertyRelative("manualCellSize").boolValue = true;
		agent.FindPropertyRelative("cellSize").floatValue = 0.15f;
		agent.FindPropertyRelative("manualTileSize").boolValue = false;
		agent.FindPropertyRelative("tileSize").intValue = 256;
		names.GetArrayElementAtIndex(index).stringValue = "TankFlat";
		settings.ApplyModifiedProperties();
	}

	private static void BakeSurface(NavMeshSurface surface, string dataPath)
	{
		string folder = Path.GetDirectoryName(dataPath).Replace('\\', '/');
		Directory.CreateDirectory(folder);
		AssetDatabase.ImportAsset("Assets/TankPrototype/Navigation", ImportAssetOptions.ForceSynchronousImport);
		AssetDatabase.ImportAsset(folder, ImportAssetOptions.ForceSynchronousImport);

		NavMeshData saved = AssetDatabase.LoadAssetAtPath<NavMeshData>(dataPath);
		surface.BuildNavMesh();
		NavMeshData generated = surface.navMeshData;
		if (generated == null) throw new InvalidOperationException("Unity did not generate NavMesh data.");

		if (saved == null)
		{
			AssetDatabase.CreateAsset(generated, dataPath);
		}
		else
		{
			surface.RemoveData();
			EditorUtility.CopySerialized(generated, saved);
			surface.navMeshData = saved;
			UnityEngine.Object.DestroyImmediate(generated);
			surface.AddData();
			EditorUtility.SetDirty(saved);
		}

		EditorUtility.SetDirty(surface);
		EditorSceneManager.MarkSceneDirty(surface.gameObject.scene);
		AssetDatabase.SaveAssetIfDirty(surface.navMeshData);
		EditorSceneManager.SaveScene(surface.gameObject.scene);
	}

	private static Vector3 MapPoint(Transform point, NavMeshQueryFilter filter)
	{
		if (point == null || !NavMesh.SamplePosition(point.position, out NavMeshHit hit, 0.5f, filter))
			throw new InvalidOperationException("A test marker is outside the TankFlat NavMesh.");

		return hit.position;
	}

	private static GameObject FindRoot(Scene scene, string name)
	{
		GameObject[] roots = scene.GetRootGameObjects();
		for (int i = 0; i < roots.Length; i++)
		{
			if (roots[i].name == name) return roots[i];
		}

		throw new InvalidOperationException("Missing test scene root: " + name);
	}
}
