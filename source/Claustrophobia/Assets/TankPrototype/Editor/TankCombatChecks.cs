using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using State = TankCombat.CombatState;

internal sealed class TankCombatChecks
{
	internal enum Scenario { BriefCover, Reacquire, Return, All }

	internal const string SuitePath = "Builds/TankCombatIntegration/play-results.txt";
	internal const string ScenarioPath = "Builds/TankCombatIntegration/last-scenario.txt";
	internal const int SuiteCount = 12;
	private const string MenuPath = "Tools/Tank/AI Test/Run Combat Checks";

	internal static TankCombatChecks Current { get; private set; }

	private readonly TankAttackDemo demo;
	private readonly Scenario scenario;
	private readonly StringBuilder report = new StringBuilder();
	private readonly HashSet<State> visited = new HashSet<State>();
	private TankAI sampledEnemy;
	private TankGun gun;
	private State lastState;
	private double startTime;
	private int quietShots = -1;
	private Vector3 lastSeen;
	private double seenTime;
	private int waypoint;
	private bool cancelled;
	private bool hostReady;

	internal readonly List<string> Results = new List<string>();
	internal readonly List<string> Timeline = new List<string>();
	internal bool IsRunning { get; private set; }
	internal string ActiveCase { get; private set; } = "";
	internal string Status { get; private set; } = "준비 중";
	internal string ReportPath => scenario == Scenario.All ? SuitePath : ScenarioPath;

	private TankAI AI => demo.Enemy;
	private TankCombat Combat => AI.Combat;
	private double SimulationTime => AI.Runner.SimulationTime;

	private TankCombatChecks(TankAttackDemo demo, Scenario scenario)
	{
		this.demo = demo;
		this.scenario = scenario;
	}

	[MenuItem(MenuPath)]
	private static void RunMenu()
	{
		TankAttackDemo demo = UnityEngine.Object.FindAnyObjectByType<TankAttackDemo>();
		if (demo != null && demo.HasSearchTests) Start(demo, Scenario.All);
		else Debug.LogWarning("TankAIIntegrationTest 씬을 다시 열고 자율 교전 검사를 실행하세요.");
	}

	[MenuItem(MenuPath, true)]
	private static bool CanRunMenu() => Application.isPlaying && Current?.IsRunning != true;

	internal bool IsFor(TankAttackDemo source) => demo == source;

	internal static void Start(TankAttackDemo demo, Scenario scenario)
	{
		if (!Application.isPlaying || !demo.HasSearchTests || Current?.IsRunning == true) return;
		Current = new TankCombatChecks(demo, scenario);
		Current.Run();
	}

	internal void Cancel()
	{
		cancelled = true;
		if (demo != null && Application.isPlaying) demo.StopAllActions();
	}

	private async void Run()
	{
		IsRunning = true;
		report.AppendLine($"교전 통합 검사 | {DateTime.Now:yyyy-MM-dd HH:mm:ss} | Unity {Application.unityVersion} | 로컬 Fusion 호스트");
		try
		{
			if (!demo.CanRunTests && !demo.isActiveAndEnabled)
			{
				TankTestMenu menu = UnityEngine.Object.FindAnyObjectByType<TankTestMenu>();
				Require(menu != null && menu.gameObject.scene == demo.gameObject.scene, "시험장 메뉴를 찾지 못했어요.");
				menu.ShowAI();
			}
			await Wait(() => demo.CanRunTests && !demo.IsStarting && AI != null && AI.TickCount > 0,
				10f, "로컬 호스트의 첫 시뮬레이션 틱을 확인하지 못했어요.");
			hostReady = true;

			switch (scenario)
			{
				case Scenario.BriefCover: await RunCase("짧은 가림", BriefCover); break;
				case Scenario.Reacquire: await RunCase("수색 중 재발견", Reacquire); break;
				case Scenario.Return: await RunCase("끝까지 못 찾음 → 순찰 복귀", Return); break;
				default: await RunAll(); break;
			}
			int passed = 0;
			foreach (string result in Results)
				if (result.StartsWith("PASS", StringComparison.Ordinal)) passed++;
			Status = $"완료 · {passed}/{Results.Count} 통과";
		}
		catch (OperationCanceledException)
		{
			Status = "중지됨";
		}
		catch (Exception error)
		{
			Status = "실행 실패 · " + error.Message;
		}
		finally
		{
			if (demo != null && Application.isPlaying && !cancelled) demo.StopAllActions();
			IsRunning = false;
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
				File.WriteAllText(ReportPath, report.AppendLine(Status).ToString(), new UTF8Encoding(false));
			}
			catch (IOException error) { Status += " · 기록 저장 실패: " + error.Message; }
			catch (UnauthorizedAccessException error) { Status += " · 기록 저장 실패: " + error.Message; }
		}
	}

	private async Task RunAll()
	{
		await RunCase("01 짧은 가림", BriefCover);
		await RunCase("02 수색 중 재발견", Reacquire);
		await RunCase("03 끝까지 못 찾음 → 순찰 복귀", Return);
		await RunCase("04 목격 지점으로 갈 수 없음 → 복귀", SearchFailure);
		await RunCase("05 복귀 경로 없음 → 정지", ReturnFailure);
		await RunCase("06 표적 제거 → 복귀", RemoveTarget);
		await RunCase("07 수색 중 전체 중지", StopSearch);
		await RunCase("08 원거리 접근 후 사격", () => CheckShot(TankAttackDemo.CombatCase.Far, true));
		await RunCase("09 근거리 정지 사격", () => CheckShot(TankAttackDemo.CombatCase.Near, false));
		await RunCase("10 접근 경로 없음 → 정지", NoApproachPath);
		await RunCase("11 주변 확인 중 재발견", SweepReacquire);
		await RunCase("12 목격 지점 이동 시간 초과 → 복귀", SearchTimeout);
	}

	private async Task RunCase(string name, Func<Task<string>> test)
	{
		EnsureActive();
		ActiveCase = name;
		Status = "실행 중";
		Timeline.Clear();
		visited.Clear();
		sampledEnemy = null;
		quietShots = -1;
		string result;
		try { result = $"PASS  {name} · {await test()}"; }
		catch (OperationCanceledException)
		{
			AppendCase("중지  " + name);
			throw;
		}
		catch (Exception error) { result = $"FAIL  {name} · {error.Message}"; }
		Results.Add(result);
		AppendCase(result);
	}

	private void AppendCase(string result)
	{
		report.AppendLine().AppendLine(result);
		foreach (string step in Timeline) report.AppendLine("  " + step);
	}

	private async Task<string> BriefCover()
	{
		TankTarget target = await BeginCover();
		double loss = SimulationTime;
		await Wait(() => SimulationTime - loss >= 0.15, 1f, "가림 대기를 확인하지 못했어요.");
		demo.SetCombatCover(false);
		await WaitForAttack(target);
		Require(!visited.Contains(State.MoveToLastSeen) && Combat.PatrolIndex == waypoint, "짧은 가림에서 수색을 시작했거나 경유점이 바뀌었어요.");
		return "발사 중단, 수색 이동 없이 같은 표적과 재교전";
	}

	private async Task<string> Reacquire()
	{
		TankTarget target = await BeginCover();
		double loss = SimulationTime;
		await Wait(() => Combat.State == State.MoveToLastSeen && AI.MoveStatus == TankAI.MoveState.Moving, 3f,
			"0.75초 대기 뒤 목격 지점으로 출발하지 않았어요.");
		double wait = SimulationTime - loss;
		Require(wait >= 0.68 && wait <= 0.87, $"시야 상실 대기가 예상과 달라요. {wait:F2}초");
		Vector3 goal = AI.Destination;
		Vector3 start = AI.transform.position;
		Require(Vector3.Distance(goal, lastSeen) <= 0.76f && AI.Path.HasPath, "수색 목적지가 마지막 목격 위치와 달라요.");
		Require(demo.MoveCombatTarget(demo.SearchContact + Vector3.forward * 3f), "가려진 표적을 옮기지 못했어요.");
		await Wait(() => FlatDistance(AI.transform.position, start) >= 0.5f
			&& FlatDistance(target.GroundPoint.position, lastSeen) >= 2f, 3f, "수색 주행 또는 가려진 표적 이동을 확인하지 못했어요.");
		Require(Combat.State == State.MoveToLastSeen && AI.Destination == goal, "가려진 표적을 따라 수색 목적지가 바뀌었어요.");
		demo.SetCombatCover(false);
		await WaitForAttack(target);
		Require(Combat.LastSeenTime > seenTime && Combat.PatrolIndex == waypoint, "재발견 후 목격 정보나 경유점이 맞지 않아요.");
		return "가려진 표적 이동에도 목적지 고정, 0.5m 이상 주행, 같은 표적에 공격 재개";
	}

	private async Task<string> Return()
	{
		StartSearch(true);
		await Wait(() => Combat.PatrolIndex == 1 && AI.MoveStatus == TankAI.MoveState.Moving, 9f, "2번 경유점으로 출발하지 않았어요.");
		Require(demo.MoveCombatTarget(demo.SearchContact), "표적을 보여주지 못했어요.");
		await WaitForShot();
		Require(Combat.PatrolIndex == 1, "교전 직전 경유점이 2번이 아니에요.");
		demo.SetCombatCover(true);
		await EnterCover();
		SendToHideout();

		await Wait(() => Combat.State == State.CheckSurroundings, 9f, "목격 지점 도착과 주변 확인을 보지 못했어요.");
		Require(visited.Contains(State.MoveToLastSeen) && FlatDistance(AI.transform.position, lastSeen) < 1f, "목격 지점에 도착하지 않았어요.");
		Vector3 forward = AI.transform.forward;
		double sweepStart = SimulationTime;
		float left = 0f, right = 0f;
		await Wait(() =>
		{
			Vector3 heading = AI.transform.forward;
			forward.y = heading.y = 0f;
			float angle = Vector3.SignedAngle(forward, heading, Vector3.up);
			left = Mathf.Min(left, angle);
			right = Mathf.Max(right, angle);
			return Combat.State == State.ReturnToPatrol;
		}, 4f, "주변 확인 뒤 복귀를 시작하지 않았어요.");
		double sweepTime = SimulationTime - sweepStart;
		Require(left <= -42f && right >= 42f && sweepTime <= 3.1, $"회전 범위 또는 시간이 달라요. {left:F1}° / {right:F1}°, {sweepTime:F2}초");
		Require(Combat.PatrolIndex == 1 && AI.Destination == demo.SecondWaypoint, "복귀 목적지가 2번 경유점이 아니에요.");
		await Wait(() => Combat.State == State.Patrol, 15f, "복귀 지점에서 순찰이 재개되지 않았어요.");
		Require(FlatDistance(AI.transform.position, demo.SecondWaypoint) < 1f, "2번 경유점에 도착하기 전에 순찰을 재개했어요.");
		await Wait(() => Combat.PatrolIndex == 0 && AI.MoveStatus == TankAI.MoveState.Moving, 2f, "순찰의 다음 경유점으로 출발하지 않았어요.");
		return $"{left:F1}° / +{right:F1}° ({sweepTime:F2}초), 2번에 복귀해 다음 경유점으로 출발";
	}

	private async Task<string> SearchFailure()
	{
		StartCombatCase(TankAttackDemo.CombatCase.Far);
		await Wait(() => Combat.State == State.Approach, 9f, "원거리 표적을 발견하지 못했어요.");
		Require(FlatDistance(Combat.EngageOrigin, Combat.LastSeenPosition) > 35f, "목격 지점이 시험 범위 밖에 놓이지 않았어요.");
		int patrolIndex = Combat.PatrolIndex;
		Require(demo.MoveCombatTarget(demo.SearchHideout), "원거리 표적을 숨기지 못했어요.");
		await Wait(() => Combat.State == State.ReturnToPatrol, 3f, "수색 불가 뒤 순찰 지점으로 복귀하지 않았어요.");
		Require(!visited.Contains(State.MoveToLastSeen) && Combat.PatrolIndex == patrolIndex, "범위 밖 수색을 시작했거나 경유점을 잃었어요.");
		await Wait(() => Combat.State == State.Patrol, 15f, "수색 실패 후 순찰이 재개되지 않았어요.");
		return "35m 밖 목격 지점으로 출발하지 않고 순찰 재개";
	}

	private async Task<string> ReturnFailure()
	{
		await BeginCover();
		AI.Path.enabled = false;
		try
		{
			await Wait(() => Combat.State == State.Idle, 4f, "복귀 경로 실패 후 정지하지 않았어요.");
			Require(Combat.LastStopReason.StartsWith("Patrol return failed:", StringComparison.Ordinal)
				&& AI.MoveStatus == TankAI.MoveState.Idle && AI.LeftDriveInput == 0f && AI.RightDriveInput == 0f, "정지 상태 또는 실패 사유를 확인하지 못했어요.");
			return Combat.LastStopReason;
		}
		finally { if (AI != null && AI.Path != null) AI.Path.enabled = true; }
	}

	private async Task<string> RemoveTarget()
	{
		StartSearch();
		await WaitForShot();
		int patrolIndex = Combat.PatrolIndex;
		AI.Runner.Despawn(Combat.Target.Health.Object);
		await Wait(() => Combat.State == State.ReturnToPatrol, 2f, "표적 제거 후 바로 복귀하지 않았어요.");
		Require(Combat.PatrolIndex == patrolIndex && AI.AttackStatus == TankAI.AttackState.Idle
			&& !visited.Contains(State.WaitForSight), "제거된 표적을 계속 추적했어요.");
		return "제거된 표적에 대한 공격 중단, 가림 대기 없이 복귀";
	}

	private async Task<string> StopSearch()
	{
		await BeginCover();
		await Wait(() => Combat.State == State.MoveToLastSeen, 3f, "중지 시험용 수색 이동이 시작되지 않았어요.");
		demo.StopAllActions();
		uint command = AI.CommandId;
		int shots = gun.ShotCount;
		double stopped = SimulationTime;
		await Wait(() => SimulationTime - stopped >= 1.0, 3f, "정지 후 관찰 시간이 지나지 않았어요.");
		Require(Combat.State == State.Idle && AI.MoveStatus == TankAI.MoveState.Idle && AI.AttackStatus == TankAI.AttackState.Idle
			&& AI.CommandId == command && gun.ShotCount == shots, "전체 중지 후 자동 명령이나 발사가 재개됐어요.");
		return "수색 이동 중지, 이후 1초간 새 명령·발사 없음";
	}

	private async Task<string> CheckShot(TankAttackDemo.CombatCase test, bool approach)
	{
		StartCombatCase(test);
		await WaitForShot(24f);
		Require(visited.Contains(State.Approach) == approach && visited.Contains(State.StopAndFire), "거리에 따른 상태 전이가 예상과 달라요.");
		return $"실제 발사 {gun.ShotCount}발, 기대한 교전 상태 확인";
	}

	private async Task<string> NoApproachPath()
	{
		StartCombatCase(TankAttackDemo.CombatCase.NoPath);
		await Wait(() => Combat.State == State.Idle && !string.IsNullOrEmpty(Combat.LastStopReason), 6f, "접근 경로 실패 후 정지하지 않았어요.");
		Require(Combat.LastStopReason == "No reachable approach point" && gun.ShotCount == 0, "접근 실패 사유 또는 발사 수가 예상과 달라요.");
		return "접근 실패 사유 기록, 발사 0발";
	}

	private async Task<string> SweepReacquire()
	{
		TankTarget target = await BeginCover();
		SendToHideout();
		await Wait(() => Combat.State == State.CheckSurroundings, 9f, "주변 확인 단계에 들어가지 않았어요.");
		Vector3 forward = AI.transform.forward;
		forward.y = 0f;
		await Wait(() => Vector3.SignedAngle(forward, AI.transform.forward, Vector3.up) < -18f,
			2f, "차체가 왼쪽을 확인하지 않았어요.");
		Vector3 contact = AI.transform.position + Quaternion.AngleAxis(-75f, Vector3.up) * forward.normalized * 12f;
		contact.y = demo.SearchContact.y;
		Require(demo.MoveCombatTarget(contact), "표적을 회전 방향에 놓지 못했어요.");
		await WaitForAttack(target);
		Require(!visited.Contains(State.ReturnToPatrol) && AI.LeftDriveInput == 0f && AI.RightDriveInput == 0f
			&& Combat.PatrolIndex == waypoint, "재발견 뒤에도 회전하거나 순찰 복귀를 시작했어요.");
		return "차체 회전을 끊고 기존 표적에 공격 재개";
	}

	private async Task<string> SearchTimeout()
	{
		await BeginCover();
		await Wait(() => Combat.State == State.MoveToLastSeen, 3f, "시간 제한 시험용 수색 이동에 들어가지 않았어요.");
		double start = SimulationTime;
		await Wait(() => Combat.State == State.ReturnToPatrol, 8f, "벽에 막힌 수색 이동 뒤 복귀하지 않았어요.");
		double duration = SimulationTime - start;
		Require(duration >= 5.85 && duration <= 6.15 && !visited.Contains(State.CheckSurroundings)
			&& Combat.PatrolIndex == waypoint, $"6초 제한 뒤 바로 복귀하지 않았어요. 경과 {duration:F2}초");
		return $"벽에 막힌 주행을 {duration:F2}초에 중단하고 기존 경유점으로 복귀";
	}

	private void StartSearch(bool hidden = false)
	{
		Require(demo.RunSearchTest(ActiveCase, hidden), "시험용 전차 또는 순찰을 시작하지 못했어요.");
	}

	private void SendToHideout()
	{
		Require(demo.MoveCombatTarget(demo.SearchHideout), "표적 은신 지점 이동에 실패했어요.");
		demo.SetCombatCover(false);
	}

	private void StartCombatCase(TankAttackDemo.CombatCase test)
	{
		TankAI previous = AI;
		demo.RunCombatTest(test);
		Require(AI != null && AI != previous && demo.CombatTargetHealth != null, "시험용 전차 또는 표적을 생성하지 못했어요.");
	}

	private async Task<TankTarget> BeginCover()
	{
		StartSearch();
		await WaitForShot();
		TankTarget target = Combat.Target;
		demo.SetCombatCover(true);
		await EnterCover();
		return target;
	}

	private async Task EnterCover()
	{
		await Wait(() => Combat.State == State.WaitForSight, 2f, "벽을 켠 뒤 시야 상실 대기로 들어가지 않았어요.");
		quietShots = gun.ShotCount;
		lastSeen = Combat.LastSeenPosition;
		seenTime = Combat.LastSeenTime;
		waypoint = Combat.PatrolIndex;
		CheckHidden();
	}

	private Task WaitForShot(float seconds = 12f) =>
		Wait(() => gun != null && gun.ShotCount > 0 && Combat.State == State.StopAndFire, seconds, "정지 사격에서 실제 포탄이 발사되지 않았어요.");

	private Task WaitForAttack(TankTarget target) =>
		Wait(() => Combat.State == State.StopAndFire && Combat.Target == target && AI.AttackStatus != TankAI.AttackState.Idle,
			4f, "벽을 없앤 뒤 같은 표적을 재발견해 공격하는 상태로 돌아가지 않았어요.");

	private async Task Wait(Func<bool> ready, float seconds, string failure)
	{
		double previous = EditorApplication.timeSinceStartup;
		double remaining = seconds;
		while (true)
		{
			EnsureActive();
			double now = EditorApplication.timeSinceStartup;
			if (!EditorApplication.isPaused)
			{
				Sample();
				if (ready()) return;
				remaining -= now - previous;
				if (remaining <= 0) throw new TimeoutException(failure);
			}
			previous = now;
			await Task.Delay(15);
		}
	}

	private void EnsureActive()
	{
		if (cancelled || demo == null || !EditorApplication.isPlayingOrWillChangePlaymode || hostReady && !demo.CanRunTests)
			throw new OperationCanceledException();
	}

	private void Sample()
	{
		if (AI == null || Combat == null || AI.Runner == null) return;
		bool newEnemy = sampledEnemy != AI;
		if (newEnemy)
		{
			sampledEnemy = AI;
			gun = AI.GetComponent<TankGun>();
			startTime = SimulationTime;
		}

		if (newEnemy || lastState != Combat.State)
		{
			lastState = Combat.State;
			visited.Add(lastState);
			string waypointText = Combat.PatrolIndex >= 0 ? (Combat.PatrolIndex + 1).ToString() : "—";
			Timeline.Add($"+{SimulationTime - startTime:F2}초  {Label(lastState)}  |  경유점 {waypointText}  |  발사 {gun.ShotCount}발");
			if (lastState == State.MoveToLastSeen || lastState == State.ReturnToPatrol)
				Timeline.Add($"이동 목적지 {AI.Destination:F2}");
			if (lastState == State.Idle && !string.IsNullOrEmpty(Combat.LastStopReason))
				Timeline.Add(Combat.LastStopReason);
		}
		CheckHidden();
	}

	private void CheckHidden()
	{
		if (quietShots < 0 || Combat.State is not (State.WaitForSight or State.MoveToLastSeen or State.CheckSurroundings or State.ReturnToPatrol)) return;

		Require(gun.ShotCount == quietShots && AI.AttackStatus == TankAI.AttackState.Idle, "시야를 잃은 뒤 공격 요청 또는 발사가 계속됐어요.");
		Require(Combat.LastSeenTime == seenTime && Combat.LastSeenPosition == lastSeen && Combat.PatrolIndex == waypoint,
			"가려진 사이 마지막 목격 정보 또는 경유점이 바뀌었어요.");
		if (Combat.State == State.WaitForSight)
			Require(AI.MoveStatus == TankAI.MoveState.Idle && AI.LeftDriveInput == 0f && AI.RightDriveInput == 0f,
				"가림 대기 중 이동 입력이 남아 있어요.");
	}

	private static float FlatDistance(Vector3 from, Vector3 to)
	{
		from.y = to.y = 0f;
		return Vector3.Distance(from, to);
	}

	private static void Require(bool condition, string message)
	{
		if (!condition) throw new InvalidOperationException(message);
	}

	internal static string Label(State state) => state switch
	{
		State.Patrol => "순찰",
		State.Approach => "접근",
		State.StopAndFire => "정지 사격",
		State.WaitForSight => "시야 상실 대기",
		State.MoveToLastSeen => "목격 지점 이동",
		State.CheckSurroundings => "주변 확인",
		State.ReturnToPatrol => "순찰 복귀",
		_ => "정지"
	};
}
