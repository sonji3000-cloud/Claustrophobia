using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Fusion;
using UnityEditor;
using UnityEngine;

// Editor-only integration checks. Physics, sensing and weapons run on real Fusion ticks.
public static class TankSensorChecks
{
	[Serializable]
	private sealed class Check
	{
		public string Name;
		public bool Passed;
		public string State;
	}

	[Serializable]
	private sealed class Report
	{
		public string Started;
		public string Finished;
		public string Project;
		public string Unity;
		public string Mode = "Unity Play Mode / Fusion Single; no remote clients";
		public bool Completed;
		public int Passed;
		public int Failed;
		public List<Check> Checks = new List<Check>();
	}

	private static readonly Stack<IEnumerator> steps = new Stack<IEnumerator>();
	private static TankSensorTest test;
	private static TankAttackDemo demo;
	private static Report report;
	private static NetworkObject extraEnemy;
	private static NetworkObject networkTarget;
	private static TankGun mutedGun;
	private static TankSensor disabledSensor;
	private static TankHealth disabledHealth;
	private static double started;

	public static bool IsRunning { get; private set; }
	public static string Status { get; private set; } = "미실행";
	public static string ResultPath => Path.GetFullPath("Builds/TankSensorIntegration/play-results.txt");

	[MenuItem("Tools/Tank/AI Test/Run Integration Checks")]
	private static void RunMenu() => Run(UnityEngine.Object.FindAnyObjectByType<TankSensorTest>());

	public static void Run(TankSensorTest source)
	{
		if (IsRunning || !Application.isPlaying || source == null) return;

		test = source;
		demo = source.Demo;
		if (demo == null || source.TargetA == null || source.TargetB == null) return;
		if (source.TargetA.Health.Destroyed)
		{
			Status = "표적이 파괴됐습니다. Play를 종료하고 다시 시작하세요.";
			return;
		}

		report = new Report
		{
			Started = DateTime.UtcNow.ToString("O"),
			Project = Path.GetDirectoryName(Application.dataPath),
			Unity = Application.unityVersion
		};
		started = EditorApplication.timeSinceStartup;
		IsRunning = true;
		steps.Push(RunChecks());
		EditorApplication.update += Update;
		EditorApplication.playModeStateChanged += OnPlayChanged;
		AssemblyReloadEvents.beforeAssemblyReload += OnReload;
		Application.logMessageReceived += OnLog;
		Status = "로컬 호스트 준비 중";
		Save();
	}

	public static void Cancel()
	{
		if (!IsRunning) return;

		Record("Cancelled before completion", false);
		Finish(false);
	}

	private static void Update()
	{
		if (!Application.isPlaying || test == null || demo == null)
		{
			Cancel();
			return;
		}
		if (EditorApplication.isPaused) return;

		try
		{
			if (EditorApplication.timeSinceStartup - started > 900)
				throw new TimeoutException("Integration checks exceeded 15 minutes.");

			while (steps.Count > 0)
			{
				IEnumerator step = steps.Peek();
				if (!step.MoveNext()) { steps.Pop(); continue; }
				if (step.Current is IEnumerator nested) { steps.Push(nested); continue; }
				return;
			}
			Finish(true);
		}
		catch (Exception error)
		{
			Record(error.ToString(), false);
			Finish(false);
		}
	}

	private static IEnumerator RunChecks()
	{
		if (test.IsRunning) test.EndTest();
		Vector3 posA = test.TargetA.transform.position;
		Vector3 posB = test.TargetB.transform.position;
		bool activeA = test.TargetA.gameObject.activeSelf;
		bool activeB = test.TargetB.gameObject.activeSelf;
		test.StartTest();
		yield return Await(() => test.IsRunning, 45f);
		if (!test.IsRunning) throw new InvalidOperationException("Sensor test could not start on the Single host.");

		TankAI ai = demo.Enemy;
		TankSensor sensor = test.Sensor;
		NetworkRunner runner = ai.Runner;
		uint command = ai.CommandId;
		Record("Real Single host and configured targets", ai.IsHost && runner.IsServer
			&& test.TargetA.IsConfigured && test.TargetB.IsConfigured && test.TargetA != test.TargetB);
		yield return Case(TankSensorTest.TestCase.Front);
		Record("Front: selected A after continuous confirmation", Sees(test.TargetA));
		Record("Observing does not issue movement or attack commands", ai.CommandId == command
			&& ai.MoveStatus == TankAI.MoveState.Idle && ai.AttackStatus == TankAI.AttackState.Idle);
		sensor.ClearState();
		Record("Fresh discovery cannot inherit confirmation", !sensor.IsVisible && !sensor.HasMemory);
		yield return Wait(0.65f);

		yield return Case(TankSensorTest.TestCase.Rear);
		Record("Rear: visibility lost immediately, memory retained", !sensor.IsVisible && sensor.HasMemory
			&& Reading(test.TargetA).State == TankSensor.SightState.OutOfView);
		sensor.ClearState();
		yield return Case(TankSensorTest.TestCase.Far);
		Record("60m: cannot acquire outside range", !sensor.HasMemory
			&& Reading(test.TargetA).State == TankSensor.SightState.OutOfRange);
		yield return Case(TankSensorTest.TestCase.Wall);
		Record("High wall: fully occluded", !sensor.IsVisible
			&& Reading(test.TargetA).State == TankSensor.SightState.Occluded);
		yield return Case(TankSensorTest.TestCase.Partial);
		Record("Low wall: only turret point visible", Sees(test.TargetA) && Reading(test.TargetA).VisibleMask == 4);

		yield return Case(TankSensorTest.TestCase.Two);
		Record("Two targets: acquire nearer A", Sees(test.TargetA));
		yield return Case(TankSensorTest.TestCase.NearerB);
		Record("Keep A when B becomes nearer", Sees(test.TargetA));
		yield return Case(TankSensorTest.TestCase.SwitchToB);
		Record("Switch to visible B when A is behind", Sees(test.TargetB));
		yield return Case(TankSensorTest.TestCase.Front);
		yield return Case(TankSensorTest.TestCase.KeepRange);
		Record("45m: maintain previously seen A", Sees(test.TargetA));
		yield return Case(TankSensorTest.TestCase.BeyondRange);
		Record("60m: lose sight while keeping last memory", !sensor.IsVisible && sensor.HasMemory);
		yield return Case(TankSensorTest.TestCase.Front);
		yield return Case(TankSensorTest.TestCase.KeepAngle);
		Record("65 degrees: maintain previously seen A", Sees(test.TargetA));
		yield return Case(TankSensorTest.TestCase.BeyondAngle);
		Record("85 degrees: lose sight while keeping last memory", !sensor.IsVisible && sensor.HasMemory);

		yield return Case(TankSensorTest.TestCase.Front);
		sensor.TryGetLastSeen(out Vector3 seen, out double seenTime);
		yield return Case(TankSensorTest.TestCase.Wall);
		yield return Case(TankSensorTest.TestCase.HiddenMove);
		Record("Hidden motion does not change last position or time", sensor.TryGetLastSeen(out Vector3 hidden, out double hiddenTime)
			&& hidden == seen && hiddenTime == seenTime && !sensor.IsVisible);
		Record("Memory marker follows snapshot, not hidden target", test.MemoryMarker != null
			&& test.MemoryMarker.gameObject.activeSelf && Vector3.Distance(test.MemoryMarker.position, seen + Vector3.up * 0.12f) < 0.01f);
		yield return Case(TankSensorTest.TestCase.Reappear);
		Record("Reappearance updates position and timestamp", Sees(test.TargetA)
			&& sensor.TryGetLastSeen(out Vector3 renewed, out double renewedTime) && renewed != seen && renewedTime > seenTime);
		yield return Case(TankSensorTest.TestCase.Wall);
		Status = "10초 기억 만료 확인 중";
		yield return Wait(sensor.MemoryTime + 0.25f);
		Record("Memory expires after last sighting plus configured duration", !sensor.HasMemory && !sensor.IsVisible
			&& sensor.SelectedTarget == null && !sensor.TryGetLastSeen(out _, out _) && !test.MemoryMarker.gameObject.activeSelf);

		yield return Case(TankSensorTest.TestCase.Front);
		test.SetCase(TankSensorTest.TestCase.RemoveA);
		Record("Deactivate immediately invalidates sighting", !sensor.HasMemory);
		test.SetCase(TankSensorTest.TestCase.RestoreA);
		Record("Reactivate does not inherit previous life memory", !sensor.HasMemory);
		yield return Wait(0.65f);
		Record("Reactivated target is freshly discovered", Sees(test.TargetA));
		disabledHealth = test.TargetA.Health;
		disabledHealth.enabled = false;
		Record("Disabled target HP invalidates memory", !sensor.HasMemory);
		yield return Wait(0.25f);
		disabledHealth.enabled = true;
		disabledHealth = null;
		yield return Wait(0.65f);
		Record("Restored HP permits new discovery", Sees(test.TargetA));
		command = ai.CommandId;
		test.ResetMemory();
		Record("Explicit memory reset preserves command", !sensor.HasMemory && ai.CommandId == command);
		yield return Wait(0.65f);
		mutedGun = ai.GetComponent<TankGun>();
		mutedGun.enabled = false;
		int scans = sensor.ScanCount;
		yield return Wait(0.35f);
		Record("Disabled gun does not disable observation", sensor.ScanCount > scans && Sees(test.TargetA) && !ai.CanAct);
		mutedGun.enabled = true;
		mutedGun = null;
		disabledSensor = sensor;
		sensor.enabled = false;
		Record("Disabled sensor clears all sighting state", !sensor.HasMemory && sensor.SelectedTarget == null);
		sensor.enabled = true;
		disabledSensor = null;
		sensor.SetLocalTargets(true);
		yield return Wait(0.65f);

		Status = "두 관측 전차의 독립 기억 확인 중";
		NetworkObject enemyPrefab = AssetDatabase.LoadAssetAtPath<NetworkObject>("Assets/TankPrototype/Prefabs/EnemyTank-Sherman.prefab");
		Vector3 secondPos = ai.transform.position + ai.transform.right * 20f;
		Vector3 dir = test.TargetA.transform.position - secondPos;
		dir.y = 0f;
		extraEnemy = runner.Spawn(enemyPrefab, secondPos, Quaternion.LookRotation(dir));
		TankSensor second = extraEnemy.GetComponent<TankSensor>();
		second.SetLocalTargets(true);
		yield return Wait(0.65f);
		Record("Both observers initially see the same A", Sees(test.TargetA) && second.IsVisible && second.SelectedTarget == test.TargetA);
		sensor.TryGetLastSeen(out seen, out seenTime);
		yield return Case(TankSensorTest.TestCase.Wall);
		yield return Case(TankSensorTest.TestCase.HiddenMove);
		Record("Wall affects only the blocked observer", !sensor.IsVisible && second.IsVisible);
		Record("Observers own independent position snapshots", sensor.TryGetLastSeen(out hidden, out hiddenTime)
			&& second.TryGetLastSeen(out Vector3 otherSeen, out _) && hidden == seen && hiddenTime == seenTime && otherSeen != seen);
		runner.Despawn(extraEnemy);
		extraEnemy = null;

		Status = "Fusion 표적 생성·제거·재생성 확인 중";
		yield return Case(TankSensorTest.TestCase.Front);
		Vector3 targetPos = test.TargetA.transform.position;
		test.TargetA.gameObject.SetActive(false);
		test.TargetB.gameObject.SetActive(false);
		sensor.SetLocalTargets(false);
		NetworkObject prefab = AssetDatabase.LoadAssetAtPath<NetworkObject>("Assets/TankPrototype/Prefabs/TankAINetworkTestTarget.prefab");
		networkTarget = runner.Spawn(prefab, targetPos, Quaternion.identity);
		yield return Wait(0.65f);
		Record("Network target discovered with local targets disabled", Sees(networkTarget.GetComponent<TankTarget>()));
		NetworkId oldId = networkTarget.Id;
		runner.Despawn(networkTarget);
		networkTarget = null;
		Record("Despawn invalidates network target memory", !sensor.HasMemory);
		networkTarget = runner.Spawn(prefab, targetPos, Quaternion.identity);
		Record("New network life has a new identity and no inherited memory", networkTarget.Id != oldId && !sensor.HasMemory);
		yield return Wait(0.65f);
		Record("Respawned network target is acquired normally", Sees(networkTarget.GetComponent<TankTarget>()));
		runner.Despawn(networkTarget);
		networkTarget = null;

		test.EndTest();
		Record("End restores target placement and activation", test.TargetA.transform.position == posA
			&& test.TargetB.transform.position == posB && test.TargetA.gameObject.activeSelf == activeA
			&& test.TargetB.gameObject.activeSelf == activeB && !test.Wall.gameObject.activeSelf
			&& !sensor.AllowsLocalTargets && !sensor.HasMemory);

		Status = "기존 ㄱ자 경로 주행 확인 중";
		Record("Existing L path command accepted", demo.RunPathTest(1));
		ai = demo.Enemy;
		Vector3 startPos = ai.transform.position;
		command = ai.CommandId;
		yield return Wait(1f);
		Record("Path motion and sensor ticks coexist without extra commands", Vector3.Distance(ai.transform.position, startPos) > 0.05f
			&& ai.Sensor.ScanCount > 0 && ai.CommandId == command);
		yield return Await(() => ai.MoveStatus != TankAI.MoveState.Moving, 100f);
		Record("L path reaches and stops at goal", ai.MoveStatus == TankAI.MoveState.Arrived);

		Status = "이동 중 명령 변경·취소 확인 중";
		demo.RunPathTest(1);
		ai = demo.Enemy;
		yield return Wait(0.6f);
		command = ai.CommandId;
		demo.ReturnToStart();
		Record("Replacement movement command changes command id", ai.CommandId > command);
		demo.StopAllActions();
		command = ai.CommandId;
		yield return Wait(0.5f);
		Record("Cancelled command cannot restart through sensor updates", ai.CommandId == command
			&& ai.MoveStatus == TankAI.MoveState.Idle && ai.AttackStatus == TankAI.AttackState.Idle);

		Status = "기존 정체·재시도 한도 확인 중";
		demo.RunStallTest();
		ai = demo.Enemy;
		yield return Await(() => ai.MoveStatus != TankAI.MoveState.Moving, 100f);
		Record("Physical stall terminates with bounded retries", ai.MoveStatus == TankAI.MoveState.Failed
			&& ai.RetryCount <= ai.RetryLimit && !string.IsNullOrEmpty(ai.MoveFailure));
		demo.SetBlocker(false);

		Status = "이동 → 실제 포탄 발사 → 표적 파괴 확인 중";
		float hp = demo.TargetHealth.CurrentHP;
		demo.RunIntegrationTest();
		yield return Await(() => demo.TargetHealth.Destroyed || demo.Enemy.MoveStatus == TankAI.MoveState.Failed, 150f);
		ai = demo.Enemy;
		yield return Wait(0.35f);
		Record("Integration uses real spawned projectiles", ai.GetComponent<TankGun>().ShotCount > 0);
		Record("Real projectile collision damages the target", demo.TargetHealth.CurrentHP < hp);
		Record("Move then attack completes with TargetDown", demo.TargetHealth.Destroyed && ai.AttackStatus == TankAI.AttackState.TargetDown);

		Status = "관측 전차 파괴 후 중단 확인 중";
		demo.RunPathTest(1);
		yield return Wait(0.5f);
		demo.DestroyEnemy();
		yield return Wait(0.35f);
		ai = demo.Enemy;
		Record("Observer death clears memory and rejects new actions", !ai.Sensor.CanObserve && !ai.Sensor.HasMemory
			&& !ai.MoveTo(startPos) && !ai.Attack(test.TargetA.AimPoint) && demo.DestructionCheck.StartsWith("PASS"));
	}

	private static IEnumerator Case(TankSensorTest.TestCase value)
	{
		Status = "탐지 확인: " + value;
		test.SetCase(value);
		yield return Wait(0.65f);
	}

	private static IEnumerator Wait(float seconds)
	{
		NetworkRunner runner = demo.Enemy.Runner;
		double until = runner.SimulationTime + seconds;
		double limit = EditorApplication.timeSinceStartup + seconds * 3f + 10f;
		while (runner != null && runner.IsRunning && runner.SimulationTime < until)
		{
			if (EditorApplication.timeSinceStartup > limit) throw new TimeoutException("Host simulation is not advancing.");
			yield return null;
		}
	}

	private static IEnumerator Await(Func<bool> condition, float seconds)
	{
		double until = EditorApplication.timeSinceStartup + seconds;
		while (!condition() && EditorApplication.timeSinceStartup < until) yield return null;
	}

	private static bool Sees(TankTarget target) => test.Sensor != null && test.Sensor.IsVisible
		&& test.Sensor.SelectedTarget == target;

	private static TankSensor.Reading Reading(TankTarget target)
	{
		for (int i = 0; i < test.Sensor.CandidateCount; i++)
		{
			TankSensor.Reading value = test.Sensor.ReadCandidate(i);
			if (value.Target == target) return value;
		}
		return default;
	}

	private static void Record(string name, bool passed)
	{
		TankAI ai = demo != null ? demo.Enemy : null;
		TankSensor sensor = ai != null ? ai.Sensor : null;
		string state = ai == null ? "No enemy" : $"command={ai.CommandId}; move={ai.MoveStatus}; attack={ai.AttackStatus}; "
			+ $"reason={ai.MoveFailure}; scans={(sensor != null ? sensor.ScanCount : 0)}; "
			+ $"visible={(sensor != null && sensor.IsVisible)}; memory={(sensor != null && sensor.HasMemory)}";
		report.Checks.Add(new Check { Name = name, Passed = passed, State = state });
		if (passed) report.Passed++;
		else report.Failed++;
		Save();
	}

	private static void OnLog(string message, string stack, UnityEngine.LogType type)
	{
		if (type == UnityEngine.LogType.Exception || type == UnityEngine.LogType.Assert)
			Record("Runtime exception: " + message + "\n" + stack, false);
	}

	private static void OnPlayChanged(PlayModeStateChange state)
	{
		if (state == PlayModeStateChange.ExitingPlayMode) Cancel();
	}

	private static void OnReload() => Cancel();

	private static void Finish(bool completed)
	{
		IsRunning = false;
		EditorApplication.update -= Update;
		EditorApplication.playModeStateChanged -= OnPlayChanged;
		AssemblyReloadEvents.beforeAssemblyReload -= OnReload;
		Application.logMessageReceived -= OnLog;
		steps.Clear();
		if (mutedGun != null) mutedGun.enabled = true;
		if (disabledSensor != null) disabledSensor.enabled = true;
		if (disabledHealth != null) disabledHealth.enabled = true;
		Remove(extraEnemy);
		Remove(networkTarget);
		extraEnemy = networkTarget = null;
		mutedGun = null;
		disabledSensor = null;
		disabledHealth = null;
		if (test != null) test.EndTest();
		if (demo != null) { demo.SetBlocker(false); demo.StopAllActions(); }
		report.Completed = completed;
		report.Finished = DateTime.UtcNow.ToString("O");
		Status = (completed ? "완료" : "중단") + $": PASS {report.Passed} / FAIL {report.Failed}";
		Save();
		Debug.Log("[AI INTEGRATION CHECKS] " + Status + "\n" + ResultPath);
	}

	private static void Remove(NetworkObject obj)
	{
		if (obj != null && obj.IsValid && obj.Runner != null && obj.Runner.IsRunning && obj.HasStateAuthority)
			obj.Runner.Despawn(obj);
	}

	private static void Save()
	{
		string folder = Path.GetDirectoryName(ResultPath);
		Directory.CreateDirectory(folder);
		File.WriteAllText(Path.Combine(folder, "play-results.json"), JsonUtility.ToJson(report, true), new UTF8Encoding(false));
		var text = new StringBuilder();
		text.AppendLine(report.Mode);
		text.AppendLine("Started UTC: " + report.Started);
		text.AppendLine("Project: " + report.Project);
		for (int i = 0; i < report.Checks.Count; i++)
		{
			Check row = report.Checks[i];
			text.AppendLine((row.Passed ? "PASS " : "FAIL ") + row.Name);
			text.AppendLine("  " + row.State);
		}
		text.AppendLine($"Completed={report.Completed}; PASS={report.Passed}; FAIL={report.Failed}");
		File.WriteAllText(ResultPath, text.ToString(), new UTF8Encoding(false));
	}
}
