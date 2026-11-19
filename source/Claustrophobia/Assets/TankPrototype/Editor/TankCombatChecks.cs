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
	internal enum Scenario
	{
		BriefCover, Reacquire, Return, All, ShotBlock, RepositionFire,
		NoRepositionPoint, RepositionStalled, RepositionStillBlocked, RepositionReturnUnavailable,
		ForceMove, Retreat, CancelOrder, HitReturn, HitEngage, HitOrder, Reapproach, RangeSight, RangeNoPath,
		ReplaceOrder, Manual, ApproachTracking
	}

	internal sealed class Result
	{
		internal readonly string Name;
		internal readonly bool Passed;
		internal readonly string Detail;
		internal readonly string FinalState;
		internal readonly string[] Timeline;

		internal Result(string name, bool passed, string detail, string finalState, string[] timeline)
		{
			Name = name;
			Passed = passed;
			Detail = detail;
			FinalState = finalState;
			Timeline = timeline;
		}
	}

	internal const string SuitePath = "Builds/TankCombatIntegration/play-results.txt";
	internal const string ScenarioPath = "Builds/TankCombatIntegration/last-scenario.txt";
	internal const int SuiteCount = 33;
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
	private bool keepScene;

	internal readonly List<Result> Results = new List<Result>();
	internal readonly List<string> Timeline = new List<string>();
	internal bool IsRunning { get; private set; }
	internal bool IsManual => scenario == Scenario.Manual;
	internal bool ManualReady => IsManual && hostReady && !IsRunning && demo != null && demo.CanRunTests && AI != null;
	internal int ExpectedCount => scenario == Scenario.All ? SuiteCount : 1;
	internal int PassedCount { get; private set; }
	internal Vector3? LastDestination { get; private set; }
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
		if (Current != null) EditorApplication.update -= Current.SampleManual;
		Current = new TankCombatChecks(demo, scenario);
		Current.Run();
	}

	internal void Cancel()
	{
		cancelled = true;
		if (demo != null && Application.isPlaying) demo.StopAllActions();
	}

	internal void OrderManual(bool retreat)
	{
		Vector3 point = retreat ? demo.SecondWaypoint : demo.FirstWaypoint;
		bool accepted = retreat ? Combat.RetreatTo(point) : Combat.ForceMove(point);
		string action = retreat ? "퇴각 A" : "목적지 B";
		if (accepted) LastDestination = point;
		RecordManual($"{action} {point:F2} · {(accepted ? "접수" : "거절")}");
	}

	internal void StopManual()
	{
		bool stopped = Combat.CancelOrder();
		RecordManual(stopped ? "전체 정지 · 취소 접수" : "전체 정지 · 진행 중인 명령 없음");
	}

	internal void DamageManual(bool lethal)
	{
		if (lethal) demo.DestroyEnemy();
		else demo.DamageEnemy();
		RecordManual($"{(lethal ? "자기 전차 파괴" : "비치명 피격")} · HP {demo.EnemyHealth.CurrentHP:F0}");
	}

	private void RecordManual(string action)
	{
		Status = action;
		Timeline.Add($"+{SimulationTime - startTime:F2}초  {action}");
		Sample();
	}

	private void SampleManual()
	{
		if (ManualReady) Sample();
		else EditorApplication.update -= SampleManual;
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
			if (IsManual)
			{
				ActiveCase = "수동 명령 · 피격 시험";
				Require(demo.RunManualPatrol(ActiveCase), "수동 시험용 전차 또는 순찰을 시작하지 못했어요.");
				hostReady = true;
				Status = "순찰 시작 · 조작 가능";
				Sample();
				EditorApplication.update += SampleManual;
				return;
			}
			hostReady = true;

			switch (scenario)
			{
				case Scenario.BriefCover: await RunCase("짧은 가림", BriefCover); break;
				case Scenario.Reacquire: await RunCase("수색 중 재발견", Reacquire); break;
				case Scenario.Return: await RunCase("끝까지 못 찾음 → 순찰 복귀", Return); break;
				case Scenario.ShotBlock: await RunCase("사선 차단 확인", ShotBlock); break;
				case Scenario.RepositionFire: await RunCase("사선 차단 → 위치 변경 → 사격", RepositionFire); break;
				case Scenario.NoRepositionPoint: await RunCase("옆으로 갈 곳 없음 → 순찰 복귀", NoRepositionPoint); break;
				case Scenario.RepositionStalled: await RunCase("이동 중 막힘 → 순찰 복귀", RepositionStalled); break;
				case Scenario.RepositionStillBlocked: await RunCase("옮긴 뒤에도 사선 차단 → 순찰 복귀", RepositionStillBlocked); break;
				case Scenario.RepositionReturnUnavailable: await RunCase("복귀 경로도 없음 → 정지", RepositionReturnUnavailable); break;
				case Scenario.ForceMove: await RunCase("강제 이동 → 순찰 복귀", ForceMove); break;
				case Scenario.Retreat: await RunCase("퇴각 → 도착 후 정지", Retreat); break;
				case Scenario.CancelOrder: await RunCase("이동 중 명령 취소", CancelOrder); break;
				case Scenario.HitReturn: await RunCase("피격 → 주변 확인 → 순찰 복귀", HitReturn); break;
				case Scenario.HitEngage: await RunCase("피격 확인 중 표적 발견 → 교전", HitEngage); break;
				case Scenario.HitOrder: await RunCase("피격 확인 중 퇴각 명령 우선", HitOrder); break;
				case Scenario.Reapproach: await RunCase("32m 이탈 시간에 따른 재접근", Reapproach); break;
				case Scenario.RangeSight: await RunCase("거리 초과 중 시야 상실 → 수색", RangeSight); break;
				case Scenario.RangeNoPath: await RunCase("재접근 경로 없음 → 정지", RangeNoPath); break;
				case Scenario.ReplaceOrder: await RunCase("퇴각 A → 목적지 B → 정지", ReplaceOrder); break;
				case Scenario.ApproachTracking: await RunCase("접근 중 포탑 추적 → 정지 사격", ApproachTracking); break;
				default: await RunAll(); break;
			}
			Status = $"완료 · {PassedCount}/{ExpectedCount} 통과";
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
			IsRunning = false;
			if (!IsManual)
			{
				if (demo != null && Application.isPlaying && !cancelled)
				{
					if (keepScene) AI.StopActions();
					else demo.StopAllActions();
				}
				SaveReport();
			}
		}
	}

	private void SaveReport()
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
			File.WriteAllText(ReportPath, report.AppendLine(Status).ToString(), new UTF8Encoding(false));
		}
		catch (IOException error) { Status += " · 기록 저장 실패: " + error.Message; }
		catch (UnauthorizedAccessException error) { Status += " · 기록 저장 실패: " + error.Message; }
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
		await RunCase("08 접근 중 포탑 추적 → 정지 사격", ApproachTracking);
		await RunCase("09 근거리 정지 사격", NearShot);
		await RunCase("10 접근 경로 없음 → 정지", NoApproachPath);
		await RunCase("11 주변 확인 중 재발견", SweepReacquire);
		await RunCase("12 목격 지점 이동 시간 초과 → 복귀", SearchTimeout);
		await RunCase("13 사선 차단 → 위치 변경 시작", ShotBlock);
		await RunCase("14 짧은 사선 차단 뒤 예상 명중 없음", ShotBlockClears);
		await RunCase("15 위치 변경 후 정지 사격", RepositionFire);
		await RunCase("16 옆으로 갈 곳 없음 → 순찰 복귀", NoRepositionPoint);
		await RunCase("17 이동 중 막힘 → 순찰 복귀", RepositionStalled);
		await RunCase("18 옮긴 뒤에도 사선 차단 → 순찰 복귀", RepositionStillBlocked);
		await RunCase("19 복귀 경로도 없음 → 정지", RepositionReturnUnavailable);
		await RunCase("20 강제 이동 → 순찰 복귀", ForceMove);
		await RunCase("21 퇴각 → 도착 후 정지", Retreat);
		await RunCase("22 이동 중 명령 취소", CancelOrder);
		await RunCase("23 갈 수 없는 명령 목적지 → 정지", OrderPathFailure);
		await RunCase("24 연속 피격 → 주변 확인 후 기존 순찰 복귀", HitReturn);
		await RunCase("25 피격 확인 중 표적 발견 → 교전", HitEngage);
		await RunCase("26 피격 확인 중 퇴각 명령 우선", HitOrder);
		await RunCase("27 피격 확인 중 파괴 → 정지", HitDestroyed);
		await RunCase("28 짧은 거리 이탈·0.5초 연속 이탈 → 재접근", Reapproach);
		await RunCase("29 거리 초과 중 시야 상실 → 수색", RangeSight);
		await RunCase("30 거리 초과와 가림 중 표적 파괴 → 복귀", RangeTargetDestroyed);
		await RunCase("31 재접근 경로 없음 → 정지", RangeNoPath);
		await RunCase("32 거리 초과 중 취소·자기 파괴 → 정지", RangeStop);
		await RunCase("33 퇴각 A → 목적지 B → 정지", ReplaceOrder);
	}

	private async Task RunCase(string name, Func<Task<string>> test)
	{
		EnsureActive();
		ActiveCase = name;
		Status = "실행 중";
		Timeline.Clear();
		visited.Clear();
		sampledEnemy = null;
		LastDestination = null;
		quietShots = -1;
		bool passed = false;
		string detail;
		try
		{
			detail = await test();
			passed = true;
			PassedCount++;
		}
		catch (OperationCanceledException)
		{
			AppendCase("중지  " + name);
			throw;
		}
		catch (Exception error) { detail = error.Message; }
		string finalState = DescribeState();
		Results.Add(new Result(name, passed, detail, finalState, Timeline.ToArray()));
		AppendCase($"{(passed ? "PASS" : "FAIL")}  {name} · {detail}\n  {finalState}");
	}

	internal string DescribeState()
	{
		if (AI == null || sampledEnemy != AI || Combat == null || gun == null) return "상태 기록 없음";
		string point = LastDestination.HasValue ? LastDestination.Value.ToString("F2") : "—";
		string index = Combat.PatrolIndex >= 0 ? (Combat.PatrolIndex + 1).ToString() : "—";
		string reason = !string.IsNullOrEmpty(Combat.LastStopReason) ? Combat.LastStopReason : Combat.LastRepositionFailure;
		return $"{Label(Combat.State)} | 경유점 {index} | 마지막 목적지 {point} | 실제 발사 {gun.ShotCount}발"
			+ (string.IsNullOrEmpty(reason) ? "" : $"\n종료 사유 {reason}");
	}

	private void AppendCase(string result)
	{
		report.AppendLine().AppendLine(result);
		foreach (string step in Timeline) report.AppendLine("  " + step);
	}

	private async Task<string> BriefCover()
	{
		TankTarget target = await BeginCover(frameContact: true);
		Require(demo.IsCombatCoverOn, "시야 상실 때 가림 벽이 켜져 있어야 해요.");
		Timeline.Add($"가림 벽 켜짐 | {Label(Combat.State)} | 발사 {gun.ShotCount}발");
		double loss = SimulationTime;
		await Wait(() => SimulationTime - loss >= 0.15, 1f, "가림 대기를 확인하지 못했어요.");
		demo.SetCombatCover(false);
		await WaitForAttack(target);
		Require(!visited.Contains(State.MoveToLastSeen) && !visited.Contains(State.Reposition)
			&& Combat.PatrolIndex == waypoint, "짧은 가림에서 수색·위치 변경을 시작했거나 경유점이 바뀌었어요.");
		if (scenario == Scenario.BriefCover)
		{
			demo.SetCombatCover(true);
			keepScene = true;
		}
		return "발사 중단, 수색 이동 없이 같은 표적과 재교전";
	}

	private async Task<string> ShotBlock()
	{
		await BeginReposition();
		keepScene = scenario == Scenario.ShotBlock;
		return $"표적 보임 / 사선 차단 {Combat.RepositionBlockSeconds:F2}초 / 위치 변경 {Combat.RepositionCount}회 / 실제 발사 {gun.ShotCount}발";
	}

	private async Task<string> RepositionFire()
	{
		Vector3 start = await BeginReposition();
		Vector3 goal = AI.Destination;
		Vector3 forward = Combat.LastSeenPosition - start;
		Vector3 side = new Vector3(forward.z, 0f, -forward.x).normalized;
		TankTarget target = Combat.Target;
		int patrolIndex = Combat.PatrolIndex;
		uint move = AI.CommandId;
		TankAim aim = AI.GetComponent<TankAim>();
		float startYaw = aim.CurrentYaw;
		float turn = 0f;
		TankGun.ChamberState chamber = gun.State;
		bool shotBeforeStop = false;

		void OnChamberChanged(TankGun.ChamberState state)
		{
			if (state == TankGun.ChamberState.Recovering && (Combat.State != State.StopAndFire
				|| AI.MoveStatus == TankAI.MoveState.Moving || AI.LeftDriveInput != 0f || AI.RightDriveInput != 0f))
				shotBeforeStop = true;
		}

		TankGun weapon = gun;
		weapon.StateChanged += OnChamberChanged;
		try
		{
			await Wait(() =>
			{
				if (Combat.State != State.Reposition) return true;
				Require(gun.ShotCount == 0, "차체가 이동을 마치기 전에 포탄이 발사됐어요.");
				Require(AI.AttackStatus == TankAI.AttackState.Idle && AI.CommandId == move && AI.Destination == goal && gun.State == chamber,
					"이동 중 장전·공격 요청이 들어갔거나 이동 목적지가 바뀌었어요.");
				if (FlatDistance(AI.transform.position, start) >= 0.5f)
					turn = Mathf.Max(turn, Mathf.Abs(Mathf.DeltaAngle(startYaw, aim.CurrentYaw)));
				return false;
			}, 10f, "위치 변경 이동을 마치지 못했어요.");
		}
		finally { weapon.StateChanged -= OnChamberChanged; }
		Require(!shotBeforeStop, "발사 시점에 차체가 아직 이동 중이었어요.");

		float sideDistance = Mathf.Abs(Vector3.Dot(AI.transform.position - start, side));
		if (Combat.State == State.WaitForSight)
		{
			TankSensor sensor = AI.Sensor;
			for (int i = 0; i < sensor.CandidateCount; i++)
			{
				TankSensor.Reading reading = sensor.ReadCandidate(i);
				if (reading.Target != target) continue;
				Timeline.Add($"이동 중 시야 상실: {reading.State} / 가림 물체 {reading.Blocker?.name ?? "—"}");
				break;
			}
		}
		Require(Combat.State == State.StopAndFire && FlatDistance(AI.transform.position, goal) <= 1f && sideDistance >= 2f
			&& AI.LeftDriveInput == 0f && AI.RightDriveInput == 0f,
			$"목적지에 차체를 세우고 사격을 재시도해야 해요. 현재 {Label(Combat.State)} / 옆으로 {sideDistance:F1}m");
		Require(turn >= 5f, $"재배치 중 포탑이 표적을 따라 돌지 않았어요. 포탑 회전 {turn:F1}°");
		Timeline.Add($"이동 완료 / 옆으로 {sideDistance:F1}m / 이동 중 실제 발사 0발");

		await WaitForShot(8f);
		Require(Combat.RepositionCount == 1 && Combat.Target == target && Combat.PatrolIndex == patrolIndex && AI.CommandId == move + 1,
			"위치를 한 번만 바꾸고 같은 표적에 사격을 재개해야 해요.");
		Timeline.Add($"사격 재개 / 위치 변경 {Combat.RepositionCount}회 / 실제 발사 {gun.ShotCount}발");
		keepScene = scenario == Scenario.RepositionFire;
		return $"옆으로 {sideDistance:F1}m / 포탑 회전 {turn:F1}° / 이동 중 0발 / 정지 후 {gun.ShotCount}발";
	}

	private async Task<string> NoRepositionPoint()
	{
		Require(demo.RunShotBlockTest(ActiveCase, testReturn: true), "복귀 시험을 시작하지 못했어요.");
		using (var settings = new SerializedObject(Combat))
		{
			settings.FindProperty("engageRadius").floatValue = 3f;
			settings.ApplyModifiedPropertiesWithoutUndo();
		}

		await WaitForShotBlock();
		int patrolIndex = Combat.PatrolIndex;
		TankTarget target = Combat.Target;
		await CheckRepositionReturn(patrolIndex, 0, "No reachable reposition point");

		double returned = SimulationTime;
		await Wait(() =>
		{
			Require(Combat.State == State.Patrol && Combat.Target == null && Combat.SuppressedTargetCount == 1
				&& AI.Sensor.IsVisible && AI.Sensor.SelectedTarget == target && gun.ShotCount == 0,
				"복귀 직후 계속 보이는 같은 표적에 재교전했거나 시야가 끊겼어요.");
			return SimulationTime - returned >= 0.4;
		}, 2f, "복귀 직후 재교전 대기를 확인하지 못했어요.");
		Timeline.Add("같은 표적 계속 보임 / 재교전 없음 / 발사 0발");

		Require(demo.MoveCombatTarget(demo.SearchHideout), "표적을 시야 밖으로 옮기지 못했어요.");
		await Wait(() => !AI.Sensor.IsVisible && Combat.SuppressedTargetCount == 0, 2f, "시야 상실 뒤 재교전 대기가 풀리지 않았어요.");
		Require(demo.MoveCombatTarget(demo.ShotBlockPoint), "같은 표적을 다시 보여주지 못했어요.");
		await WaitForAttack(target);
		Require(gun.ShotCount == 0, "다시 보인 표적을 상대로 사선이 막혀 있는데 발사했어요.");
		Timeline.Add("시야 상실 → 같은 표적 재발견 → 공격 시도 허용 / 발사 0발");

		keepScene = scenario == Scenario.NoRepositionPoint;
		return $"{patrolIndex + 1}번 경유점 복귀 / 계속 보이면 재교전 대기 / 재발견 후 재교전 허용 / 발사 0발";
	}

	private async Task<string> RepositionStalled()
	{
		await BeginReposition(testReturn: true);
		int patrolIndex = Combat.PatrolIndex;
		Require(demo.BlockRepositionRoute(), "움직이기 시작한 전차 앞에 장애물을 놓지 못했어요.");
		Timeline.Add("이동 명령을 보낸 뒤 전차 앞에 주행 장애물 배치");

		await CheckRepositionReturn(patrolIndex, 1, "Reposition ");
		string reason = Combat.LastRepositionFailure;
		Require(reason == "Reposition timed out" || reason.StartsWith("Reposition failed: ", StringComparison.Ordinal),
			"장애물에 막힌 이동 실패 또는 시간 초과가 기록되어야 해요.");
		keepScene = scenario == Scenario.RepositionStalled;
		return $"{reason} / {patrolIndex + 1}번 경유점 복귀 / 발사 0발";
	}

	private async Task<string> RepositionStillBlocked()
	{
		await BeginReposition(testReturn: true);
		int patrolIndex = Combat.PatrolIndex;
		await Wait(() => Combat.State != State.Reposition || (FlatDistance(AI.transform.position, AI.Destination) <= AI.StopRadius
			&& AI.LeftDriveInput == 0f && AI.RightDriveInput == 0f),
			10f, "두 번째 벽을 켤 수 있도록 재배치 목적지에 접근하지 못했어요.");
		Require(Combat.State == State.Reposition && gun.ShotCount == 0, "두 번째 벽을 켜기 전에 재배치가 끝났어요.");
		Require(demo.ShowRepositionShotWall(), "재배치 뒤 사선을 막을 낮은 벽을 켜지 못했어요.");
		Timeline.Add("재배치 목적지에서 차체 안정 대기 중 두 번째 사선 차단 벽 켜짐 / 실제 발사 0발");
		await Wait(() =>
		{
			Require(gun.ShotCount == 0, "이동을 마치기 전에 포탄이 발사됐어요.");
			return Combat.State != State.Reposition;
		}, 10f, "두 번째 벽 앞에서 위치 변경을 끝내지 못했어요.");
		Require(Combat.State == State.StopAndFire && AI.Sensor.IsVisible, "위치를 옮긴 뒤 표적을 계속 봐야 해요.");

		await CheckRepositionReturn(patrolIndex, 1, "Shot path still blocked after reposition", clearShotWalls: true);
		keepScene = scenario == Scenario.RepositionStillBlocked;
		return $"옮긴 뒤에도 사선 차단 / 위치 변경 1회 / {patrolIndex + 1}번 경유점 복귀 / 발사 0발";
	}

	private async Task<string> RepositionReturnUnavailable()
	{
		await BeginReposition(testReturn: true);
		TankPath path = AI.Path;
		path.enabled = false;
		try
		{
			await Wait(() => Combat.State == State.Idle, 3f, "위치 변경과 복귀가 모두 막힌 뒤 정지하지 않았어요.");
			Require(Combat.LastRepositionFailure.StartsWith("Reposition failed:", StringComparison.Ordinal)
				&& Combat.LastStopReason.StartsWith("Patrol return failed:", StringComparison.Ordinal)
				&& AI.MoveStatus == TankAI.MoveState.Idle && AI.AttackStatus == TankAI.AttackState.Idle
				&& AI.LeftDriveInput == 0f && AI.RightDriveInput == 0f && gun.ShotCount == 0,
				"복귀 실패 뒤 전차가 완전히 정지했는지 확인하지 못했어요.");
			keepScene = scenario == Scenario.RepositionReturnUnavailable;
			return $"{Combat.LastStopReason} / 정지 / 발사 0발";
		}
		finally { if (path != null) path.enabled = true; }
	}

	private async Task CheckRepositionReturn(int patrolIndex, int moves, string reason, bool clearShotWalls = false)
	{
		await Wait(() =>
		{
			Require(gun.ShotCount == 0, "복귀하기 전에 포탄이 발사됐어요.");
			Require(Combat.State != State.WaitForSight, "시험 중 낮은 벽에 표적 시야까지 가려졌어요.");
			return Combat.State == State.ReturnToPatrol || Combat.State == State.Idle;
		}, 12f, $"위치 변경 실패 뒤 순찰로 복귀하지 않았어요. 현재 {Label(Combat.State)}");
		Vector3 goal = demo.GetShotReturnPoint(patrolIndex);
		Require(Combat.State == State.ReturnToPatrol && Combat.PatrolIndex == patrolIndex && AI.Destination == goal
			&& Combat.RepositionCount == moves && AI.AttackStatus == TankAI.AttackState.Idle
			&& Combat.LastRepositionFailure.StartsWith(reason, StringComparison.Ordinal),
			$"복귀 경유점 또는 실패 사유가 달라요. {Combat.PatrolIndex + 1}번 / {Combat.LastRepositionFailure}");
		Timeline.Add($"{Combat.LastRepositionFailure} / 복귀 경유점 {patrolIndex + 1}번 / 발사 0발");
		if (clearShotWalls)
		{
			demo.ClearShotWalls();
			Timeline.Add("사선 차단 판정 확인 후 시험용 벽 끔");
		}

		try
		{
			await Wait(() =>
			{
				Require(gun.ShotCount == 0, "순찰 복귀 중 포탄이 발사됐어요.");
				return Combat.State == State.Patrol || Combat.State == State.Idle;
			}, 12f, "원래 경유점에 도착해 순찰을 재개하지 않았어요.");
		}
		catch (TimeoutException)
		{
			Timeline.Add($"복귀 중 위치 {AI.transform.position:F2} / 정체 {AI.StallSeconds:F2}초 / 재시도 {AI.RetryCount}회");
			throw;
		}
		Require(Combat.State == State.Patrol && FlatDistance(AI.transform.position, goal) <= 1f,
			$"원래 {patrolIndex + 1}번 경유점에서 순찰을 재개해야 해요. {Combat.LastStopReason}");
	}

	private async Task<Vector3> BeginReposition(bool testReturn = false)
	{
		TankBallistics ballistics = await BeginShotBlock(testReturn);
		Require(gun.TryGetShotConditions(out TankShotConditions conditions, out string reason), reason);
		TankPrediction hit = ballistics.Predict(conditions, gun.OwnerColliders);
		Require(hit.HasHit && hit.Collider != null && hit.Collider.gameObject == demo.ShotBlockWall,
			$"포탄 경로의 첫 충돌체가 낮은 벽이어야 해요. 실제 결과 {hit.Collider?.name ?? hit.Reason}");
		Timeline.Add($"표적 보임 / 사선 차단 ({hit.Collider.name}) / 실제 발사 {gun.ShotCount}발");

		Vector3 start = AI.transform.position;
		uint attack = AI.CommandId;
		int patrolIndex = Combat.PatrolIndex;
		await Wait(() =>
		{
			Require(AI.Sensor.IsVisible && gun.ShotCount == 0, "사선 차단 중 시야가 끊겼거나 포탄이 발사됐어요.");
			return Combat.State == State.Reposition;
		}, 2f, "차단이 이어져도 위치 변경을 시작하지 않았어요.");
		float delay = Combat.RepositionBlockSeconds;
		Require(delay >= 0.29f && delay <= 0.45f, $"사선 차단 확인 시간이 약 0.3초여야 해요. 실제 {delay:F2}초");
		Require(Combat.RepositionCount == 1 && Combat.PatrolIndex == patrolIndex && AI.CommandId == attack + 1
			&& AI.MoveStatus == TankAI.MoveState.Moving && AI.Path.HasPath && AI.AttackStatus == TankAI.AttackState.Idle
			&& !AI.IsShotBlocked && gun.ShotCount == 0, "위치 변경 경로 한 개를 받고 공격을 해제해야 해요.");
		return start;
	}

	private async Task<string> ShotBlockClears()
	{
		TankBallistics ballistics = await BeginShotBlock();
		await Wait(() => AI.ShotBlockedSeconds >= 0.08f, 2f, "짧은 사선 차단 시간이 쌓이지 않았어요.");
		Require(Combat.State == State.StopAndFire && AI.ShotBlockedSeconds < 0.3f,
			"시험 중 사선 차단이 0.3초를 넘기기 전에 끊기지 않았어요.");

		float predictionTime = ballistics.MaxPredictionTime;
		ballistics.MaxPredictionTime = 0f;
		try
		{
			await Wait(() => AI.AttackStatus == TankAI.AttackState.Blocked && AI.AttackBlockReason == "No target hit predicted",
				2f, "예상 명중 없음 상태를 확인하지 못했어요.");
			double missed = SimulationTime;
			await Wait(() =>
			{
				Require(Combat.State == State.StopAndFire && AI.Sensor.IsVisible && !AI.IsShotBlocked
					&& AI.ShotBlockedSeconds == 0f && gun.ShotCount == 0,
					"예상 명중 없음 상태에서 기존 사선 차단 시간이 남았거나 위치 변경을 시작했어요.");
				return SimulationTime - missed >= 0.35;
			}, 2f, "예상 명중 없음 상태를 계속 확인하지 못했어요.");
		}
		finally { ballistics.MaxPredictionTime = predictionTime; }

		return "사선 차단 시간이 0초로 초기화되고, 예상 명중 없음이 이어져도 위치 변경 0회";
	}

	private Task<TankBallistics> BeginShotBlock(bool testReturn = false)
	{
		Require(demo.RunShotBlockTest(ActiveCase, testReturn), "사선 차단 시험을 시작하지 못했어요.");
		return WaitForShotBlock();
	}

	private async Task<TankBallistics> WaitForShotBlock()
	{
		await Wait(() => Combat.State == State.WaitForSight || AI.AttackStatus == TankAI.AttackState.Blocked
			|| gun.ShotCount > 0, 8f, "조준 후 포탄 경로 차단을 확인하지 못했어요.");
		Require(Combat.State == State.StopAndFire && AI.Sensor.IsVisible && Combat.Target == AI.Sensor.SelectedTarget,
			"낮은 벽 앞에서 표적이 보이지 않아요.");
		Require(gun.ShotCount == 0 && AI.IsShotBlocked,
			$"실제 발사 {gun.ShotCount}발 · 차단 사유 {AI.AttackBlockReason}");
		return AI.GetComponent<TankBallistics>();
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
		TankAim aim = AI.GetComponent<TankAim>();
		Require(demo.MoveCombatTarget(demo.SearchHideout), "원거리 표적을 숨기지 못했어요.");
		await Wait(() => Combat.State == State.WaitForSight, 2f, "이동 중 시야 상실을 확인하지 못했어요.");
		Require(aim.TurretInput == 0f && aim.GunInput == 0f && gun.ShotCount == 0,
			"시야를 잃은 뒤 포탑·포신 입력이나 발사가 남았어요.");
		await Wait(() => Combat.State == State.ReturnToPatrol, 3f, "수색 불가 뒤 순찰 지점으로 복귀하지 않았어요.");
		Require(!visited.Contains(State.MoveToLastSeen) && Combat.PatrolIndex == patrolIndex, "범위 밖 수색을 시작했거나 경유점을 잃었어요.");
		await Wait(() => Combat.State == State.Patrol, 15f, "수색 실패 후 순찰이 재개되지 않았어요.");
		return "이동 중 시야 상실 → 포탑·포신 입력 0 / 발사 0발 / 35m 밖 수색 포기 후 순찰 재개";
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

	private async Task<string> ForceMove()
	{
		StartSearch(frameContact: true);
		await WaitForShot();
		int shots = gun.ShotCount;
		int index = Combat.PatrolIndex;
		int scan = AI.Sensor.ScanCount;
		Vector3 point = AI.transform.position + new Vector3(-4f, 0f, 5f);
		Require(Combat.ForceMove(point) && Combat.State == State.ForcedMove && AI.Destination == point,
			"강제 이동 명령이 사격을 넘겨받지 못했어요.");

		bool sawTarget = false;
		await Wait(() =>
		{
			CheckQuiet(shots);
			if (Combat.State == State.ForcedMove && AI.Sensor.ScanCount > scan && AI.Sensor.IsVisible) sawTarget = true;
			return Combat.State == State.ReturnToPatrol;
		}, 14f, "강제 이동을 마치고 순찰 복귀를 시작하지 못했어요.");
		Require(sawTarget && Combat.Target == null && Combat.PatrolIndex == index
			&& FlatDistance(AI.transform.position, point) <= AI.StopRadius + 0.5f,
			"표적이 보이는 동안의 이동, 목적지 도착 또는 기존 경유점을 확인하지 못했어요.");
		await Wait(() => Combat.State == State.Patrol, 12f, "기존 경유점에 도착한 뒤 순찰이 재개되지 않았어요.");
		return $"표적이 보이는 동안 추가 발사 0발, 기존 {index + 1}번 경유점 복귀";
	}

	private async Task<string> Retreat()
	{
		StartSearch(frameContact: true);
		await WaitForShot();
		int shots = gun.ShotCount;
		Vector3 start = AI.transform.position;
		Require(Combat.ForceMove(start + new Vector3(-4f, 0f, 5f)), "교체할 강제 이동 명령이 들어가지 않았어요.");
		await Wait(() => FlatDistance(start, AI.transform.position) >= 2.5f, 8f, "퇴각 전 실제 이동을 확인하지 못했어요.");
		Require(Combat.State == State.ForcedMove && Combat.RetreatTo(start)
			&& Combat.State == State.Retreat && AI.Destination == start,
			"퇴각이 기존 이동 목적지를 교체하지 못했어요. " + Combat.LastStopReason);

		await Wait(() =>
		{
			CheckQuiet(shots);
			return Combat.State == State.Idle;
		}, 15f, "퇴각 후 정지하지 않았어요.");
		Require(Combat.LastStopReason == "Retreat completed"
			&& FlatDistance(AI.transform.position, start) <= AI.StopRadius + 0.5f,
			"지정한 퇴각 지점에 도착하지 않았어요. " + Combat.LastStopReason);

		Vector3 contact = AI.transform.position + AI.transform.forward * 10f;
		contact.y = demo.SearchContact.y;
		int scan = AI.Sensor.ScanCount;
		Require(demo.MoveCombatTarget(contact), "퇴각 지점 앞으로 표적을 옮기지 못했어요.");
		await Wait(() => AI.Sensor.ScanCount > scan && AI.Sensor.IsVisible, 3f, "퇴각 후 전방의 표적이 센서에 잡히지 않았어요.");
		await CheckOrderStopped(shots);
		return "강제 이동을 교체해 지정 지점 도착, 표적을 다시 봐도 대기";
	}

	private async Task<string> CancelOrder()
	{
		StartSearch(frameContact: true);
		await WaitForShot();
		int shots = gun.ShotCount;
		Vector3 start = AI.transform.position;
		Vector3 point = start + new Vector3(-4f, 0f, 5f);
		Require(Combat.ForceMove(point), "취소 시험용 강제 이동을 시작하지 못했어요.");
		await Wait(() => FlatDistance(start, AI.transform.position) >= 1f, 8f, "취소 전 실제 이동을 확인하지 못했어요.");
		Require(Combat.State == State.ForcedMove && Combat.CancelOrder() && Combat.LastStopReason == "Order cancelled",
			"주행 중인 명령을 취소하지 못했어요.");
		Vector3 stopped = AI.transform.position;
		await CheckOrderStopped(shots);
		Require(FlatDistance(stopped, AI.transform.position) < 0.5f && FlatDistance(point, AI.transform.position) > AI.StopRadius,
			"취소 이후에도 차체가 목적지로 이동했어요.");
		return "실제 주행 중 취소, 1초간 추가 주행 명령·발사 없음";
	}

	private async Task<string> OrderPathFailure()
	{
		StartSearch(frameContact: true);
		await WaitForShot();
		int shots = gun.ShotCount;
		uint command = AI.CommandId;
		Require(!Combat.ForceMove(new Vector3(float.NaN, 0f, 0f)) && AI.CommandId == command && Combat.State == State.StopAndFire,
			"잘못된 좌표가 기존 교전 명령을 변경했어요.");
		Require(Combat.RetreatTo(AI.transform.position + Vector3.up * 1000f)
			&& Combat.LastStopReason.StartsWith("Retreat failed:", StringComparison.Ordinal),
			"도달 불가능한 퇴각 목적지가 실패로 처리되지 않았어요.");
		await CheckOrderStopped(shots);
		return Combat.LastStopReason;
	}

	private async Task<string> HitReturn()
	{
		await StartHiddenPatrol();
		int index = Combat.PatrolIndex;
		Vector3 goal = AI.Destination;
		Vector3 forward = AI.transform.forward;
		await BeginHitCheck();
		double started = SimulationTime;
		await Wait(() => Vector3.SignedAngle(forward, AI.transform.forward, Vector3.up) <= -15f,
			2f, "피격 후 차체가 실제로 왼쪽을 돌아보지 않았어요.");
		await Wait(() => SimulationTime - started >= 1.1, 2f, "두 번째 피격 시점까지 진행하지 못했어요.");
		Require(Combat.State == State.CheckAfterHit, "두 번째 피격 전 주변 확인이 이미 끝났어요.");
		demo.DamageEnemy();
		Require(demo.EnemyHealth.DamageHitCount == 2 && !demo.EnemyHealth.Destroyed, "두 번째 비치명 피해가 적용되지 않았어요.");

		await Wait(() => Combat.State == State.ReturnToPatrol, 4f, "표적을 못 찾고도 순찰 복귀가 시작되지 않았어요.");
		double elapsed = SimulationTime - started;
		Require(elapsed <= 3.08 && Combat.PatrolIndex == index && AI.Destination == goal
			&& Combat.Target == null && gun.ShotCount == 0, $"피격 확인 시간, 복귀 경유점 또는 발사가 예상과 달라요. {elapsed:F2}초");

		demo.DamageEnemy();
		await Wait(() => Combat.State == State.Patrol, 12f, "기존 경유점에 도착해 순찰을 재개하지 않았어요.");
		double resumed = SimulationTime;
		await Wait(() =>
		{
			Require(Combat.State == State.Patrol && demo.EnemyHealth.DamageHitCount == 3,
				"복귀 중 피격이 순찰을 재개한 뒤 다시 처리됐어요.");
			return SimulationTime - resumed >= 0.6;
		}, 2f, "순찰 재개 이후 관찰 시간이 지나지 않았어요.");
		return $"연속 피격에도 {elapsed:F2}초에 확인 종료, 기존 {index + 1}번 경유점 복귀, 발사 0발";
	}

	private async Task<string> HitEngage()
	{
		await StartHiddenPatrol();
		Vector3 forward = AI.transform.forward;
		await BeginHitCheck();
		await Wait(() => Vector3.SignedAngle(forward, AI.transform.forward, Vector3.up) < -18f,
			2f, "표적을 옮기기 전 차체 회전을 확인하지 못했어요.");
		Vector3 contact = AI.transform.position + Quaternion.AngleAxis(-65f, Vector3.up) * forward * 12f;
		contact.y = demo.SearchContact.y;
		TankTarget target = demo.CombatTargetHealth.GetComponent<TankTarget>();
		Require(demo.MoveCombatTarget(contact), "차체 확인 방향으로 표적을 옮기지 못했어요.");
		await WaitForAttack(target);
		await WaitForShot();

		demo.DamageEnemy();
		double hit = SimulationTime;
		await Wait(() =>
		{
			Require(Combat.State == State.StopAndFire && Combat.Target == target, "교전 중 피격으로 원래 표적을 잃었어요.");
			return SimulationTime - hit >= 0.4;
		}, 2f, "교전 중 피격 후 진행하지 못했어요.");
		return "실제 시야에 들어온 표적에 조준·발사, 교전 중 추가 피격에도 교전 유지";
	}

	private async Task<string> HitOrder()
	{
		await StartHiddenPatrol();
		await BeginHitCheck();
		Vector3 point = AI.transform.position + new Vector3(-4f, 0f, 5f);
		Require(Combat.RetreatTo(point) && Combat.State == State.Retreat, "퇴각이 피격 확인을 중단시키지 못했어요.");
		demo.DamageEnemy();
		await Wait(() =>
		{
			CheckQuiet(0);
			Require(AI.Destination == point && Combat.State is State.Retreat or State.Idle, "퇴각 도중 다른 행동이 시작됐어요.");
			return Combat.State == State.Idle;
		}, 15f, "피격 후 퇴각 지점에 도착하지 않았어요.");
		Require(Combat.LastStopReason == "Retreat completed" && demo.EnemyHealth.DamageHitCount == 2,
			"퇴각 또는 두 번째 피격을 확인하지 못했어요. " + Combat.LastStopReason);
		return "피격 확인 중 새 퇴각 명령으로 전환, 다시 맞아도 목적지까지 이동";
	}

	private async Task<string> HitDestroyed()
	{
		await StartHiddenPatrol();
		await BeginHitCheck();
		demo.DestroyEnemy();
		await Wait(() => !AI.CanAct && Combat.State == State.Idle, 2f, "파괴 후에도 피격 확인이 끝나지 않았어요.");
		Require(Combat.LastStopReason == "Self destroyed" && !Combat.ForceMove(AI.transform.position)
			&& AI.MoveStatus == TankAI.MoveState.Idle && AI.LeftDriveInput == 0f && AI.RightDriveInput == 0f,
			"파괴 후 주행 입력이 남았거나 새 명령을 받았어요.");
		Require(demo.DestructionCheck.StartsWith("PASS", StringComparison.Ordinal), demo.DestructionCheck);
		return "파괴 즉시 회전과 이동 중단, 새 이동 명령 거절";
	}

	private async Task StartHiddenPatrol()
	{
		StartSearch(hidden: true, frameContact: true);
		Vector3 start = AI.transform.position;
		await Wait(() => Combat.State == State.Patrol && AI.MoveStatus == TankAI.MoveState.Moving
			&& !AI.Sensor.IsVisible && FlatDistance(start, AI.transform.position) >= 0.7f,
			8f, "표적을 보지 못한 상태로 실제 순찰 주행을 시작하지 않았어요.");
	}

	private async Task BeginHitCheck()
	{
		demo.DamageEnemy();
		await Wait(() => Combat.State == State.CheckAfterHit, 2f, "비치명 피해 직후 주변 확인으로 전환하지 않았어요.");
		Require(!demo.EnemyHealth.Destroyed && demo.EnemyHealth.DamageHitCount == 1
			&& Combat.Target == null && Combat.LastSeenTime < 0 && AI.MoveStatus == TankAI.MoveState.Idle,
			"피격 후 순찰 이동이 남았거나 시야 없이 표적을 얻었어요.");
		CheckQuiet(0);
	}

	private void CheckQuiet(int shots)
	{
		Require(gun.ShotCount == shots && AI.AttackStatus == TankAI.AttackState.Idle,
			"명령 이동 또는 정지 중에 공격이 재개됐어요.");
	}

	private Task CheckOrderStopped(int shots)
	{
		uint command = AI.CommandId;
		double stopped = SimulationTime;
		return Wait(() =>
		{
			CheckQuiet(shots);
			Require(Combat.State == State.Idle && AI.MoveStatus == TankAI.MoveState.Idle && AI.CommandId == command
				&& !AI.IsRetrying && !AI.Path.HasPath && AI.LeftDriveInput == 0f && AI.RightDriveInput == 0f,
				"정지 이후 경로, 이동 입력 또는 새 명령이 남아 있어요.");
			return SimulationTime - stopped >= 1.0;
		}, 4f, "정지 후 관찰 시간이 지나지 않았어요.");
	}

	private async Task<string> Reapproach()
	{
		StartSearch(frameContact: true);
		await WaitForShot();
		TankTarget target = Combat.Target;
		Vector3 origin = Combat.EngageOrigin;
		int index = Combat.PatrolIndex;
		int repositionCount = Combat.RepositionCount;

		await WaitForRangeHold();
		int shots = gun.ShotCount;
		await Wait(() =>
		{
			CheckQuiet(shots);
			return Combat.OutOfRangeSeconds >= 0.22f;
		}, 1f, "짧은 거리 이탈 중 사격 보류를 확인하지 못했어요.");
		MoveContact(30f);
		await Wait(() => Combat.OutOfRangeSeconds == 0f, 1f, "32m 안으로 돌아온 뒤 타이머가 초기화되지 않았어요.");
		double inside = SimulationTime;
		await Wait(() => SimulationTime - inside >= 0.35, 1f, "거리 복귀 뒤 상태를 관찰하지 못했어요.");
		Require(Combat.State == State.StopAndFire && !visited.Contains(State.Approach), "짧은 거리 이탈로 재접근을 시작했어요.");
		Timeline.Add("첫 거리 이탈 후 30m 복귀 | 재접근 없음");

		await WaitForRangeHold();
		double started = SimulationTime - Combat.OutOfRangeSeconds;
		shots = gun.ShotCount;
		await Wait(() =>
		{
			CheckQuiet(shots);
			return Combat.State == State.Approach;
		}, 2f, "32m 밖의 표적을 계속 보면서도 재접근하지 않았어요.");
		double elapsed = SimulationTime - started;
		Require(elapsed >= 0.49 && elapsed <= 0.58 && Combat.Target == target && Combat.EngageOrigin == origin
			&& Combat.PatrolIndex == index && Combat.RepositionCount == repositionCount
			&& AI.MoveStatus == TankAI.MoveState.Moving && FlatDistance(origin, AI.Path.Goal) <= 35f,
			$"재접근 시간, 기존 교전 정보 또는 35m 경로 제한이 예상과 달라요. {elapsed:F2}초");
		await Wait(() =>
		{
			CheckQuiet(shots);
			return Combat.State == State.StopAndFire;
		}, 9f, "재접근 뒤 사격 가능한 거리에서 정지하지 않았어요.");
		await Wait(() => gun.ShotCount > shots && AI.MoveStatus == TankAI.MoveState.Idle,
			10f, "재접근 후 정지와 실제 발사를 확인하지 못했어요.");
		return $"짧은 이탈은 유지, 두 번째 이탈 {elapsed:F2}초에 재접근, 정지 뒤 다시 발사";
	}

	private async Task<string> RangeSight()
	{
		StartSearch(frameContact: true);
		await WaitForShot();
		await WaitForRangeHold(33.5f);
		demo.SetCombatCover(true);
		await EnterCover();
		await Wait(() => Combat.State == State.MoveToLastSeen, 3f, "시야 상실 뒤 마지막 목격 지점 수색을 시작하지 않았어요.");
		Require(!visited.Contains(State.Approach) && Combat.OutOfRangeSeconds == 0f, "시야를 잃은 뒤에도 거리로 재접근했어요.");
		return "거리 대기 초기화, 사격 없이 마지막 목격 지점으로 수색";
	}

	private async Task<string> RangeTargetDestroyed()
	{
		StartSearch(frameContact: true);
		await WaitForShot();
		await WaitForRangeHold();
		int shots = gun.ShotCount;
		int index = Combat.PatrolIndex;
		demo.SetCombatCover(true);
		TankHealth health = demo.CombatTargetHealth;
		Require(health.ApplyDamage(health.CurrentHP, 0, out _, out _, out _), "표적 파괴 시험 피해를 적용하지 못했어요.");
		await Wait(() => Combat.State == State.ReturnToPatrol, 2f, "파괴된 표적을 놓고 순찰 복귀하지 않았어요.");
		CheckQuiet(shots);
		Require(!visited.Contains(State.WaitForSight) && !visited.Contains(State.Approach)
			&& Combat.OutOfRangeSeconds == 0f && Combat.PatrolIndex == index, "표적 파괴보다 거리 판단이나 수색이 먼저 실행됐어요.");
		return "시야 대기·재접근 없이 기존 경유점 복귀";
	}

	private async Task<string> RangeNoPath()
	{
		StartSearch(frameContact: true);
		await WaitForShot();
		await WaitForRangeHold();
		int shots = gun.ShotCount;
		TankPath path = AI.Path;
		path.enabled = false;
		try
		{
			await Wait(() => Combat.State == State.Idle, 3f, "재접근 경로가 없는데도 정지하지 않았어요.");
			Require(Combat.LastStopReason == "No reachable approach point", "기존 접근 실패 사유가 기록되지 않았어요.");
			await CheckOrderStopped(shots);
			return Combat.LastStopReason;
		}
		finally { if (path != null) path.enabled = true; }
	}

	private async Task<string> RangeStop()
	{
		StartSearch(frameContact: true);
		await WaitForShot();
		await WaitForRangeHold();
		Require(Combat.CancelOrder(), "거리 대기 중 취소가 받아들여지지 않았어요.");
		await CheckOrderStopped(gun.ShotCount);

		StartSearch(frameContact: true);
		await WaitForShot();
		await WaitForRangeHold();
		demo.DestroyEnemy();
		int shots = gun.ShotCount;
		Require(demo.DestructionCheck.StartsWith("PASS", StringComparison.Ordinal), demo.DestructionCheck);
		double destroyed = SimulationTime;
		await Wait(() =>
		{
			CheckQuiet(shots);
			Require(!AI.CanAct && Combat.State == State.Idle && AI.MoveStatus == TankAI.MoveState.Idle
				&& Combat.OutOfRangeSeconds == 0f && Combat.LastStopReason == "Self destroyed", "자기 파괴 후 이전 행동이 남았어요.");
			return SimulationTime - destroyed >= 0.6;
		}, 2f, "파괴 이후 상태를 관찰하지 못했어요.");
		return "취소·자기 파괴 뒤 재접근과 발사 모두 중단";
	}

	private async Task<string> ReplaceOrder()
	{
		StartSearch();
		await WaitForShot();
		int shots = gun.ShotCount;
		Vector3 start = AI.transform.position;
		Vector3 a = demo.SecondWaypoint;
		Vector3 b = demo.FirstWaypoint;
		Require(Combat.RetreatTo(a) && Combat.State == State.Retreat && AI.Destination == a,
			"퇴각 A를 시작하지 못했어요.");
		await Wait(() => FlatDistance(start, AI.transform.position) >= 0.6f, 5f, "A를 향해 실제 주행하지 않았어요.");

		Vector3 beforeReplace = AI.transform.position;
		uint retreatCommand = AI.CommandId;
		Require(Combat.ForceMove(b) && Combat.State == State.ForcedMove && AI.Destination == b && AI.CommandId != retreatCommand,
			"B가 A의 목적지를 교체하지 못했어요.");
		await Wait(() =>
		{
			CheckQuiet(shots);
			return FlatDistance(beforeReplace, AI.transform.position) >= 0.6f;
		}, 5f, "B로 바뀐 뒤 실제 주행하지 않았어요.");
		Require(Combat.State == State.ForcedMove && Combat.CancelOrder() && AI.Destination == b,
			"주행 중 B를 취소하지 못했어요.");
		await CheckOrderStopped(shots);
		return "A 주행 중 B로 변경, B 주행 중 취소, 이후 1초간 이동과 발사 중단";
	}

	private Task WaitForRangeHold(float distance = 36f)
	{
		MoveContact(distance);
		return Wait(() => Combat.OutOfRangeSeconds >= 0.06f && Combat.State == State.StopAndFire,
			1.5f, "32m 밖에서 거리 대기가 시작되지 않았어요.");
	}

	private void MoveContact(float distance)
	{
		Vector3 point = AI.transform.position + Vector3.forward * distance;
		point.y = demo.SearchContact.y;
		Require(demo.MoveCombatTarget(point), "거리 시험용 표적 재배치에 실패했어요.");
	}

	private async Task<string> ApproachTracking()
	{
		StartCombatCase(TankAttackDemo.CombatCase.Far);
		await Wait(() => Combat.State == State.Approach && AI.LeftDriveInput + AI.RightDriveInput > 0.1f,
			9f, "표적을 발견하고 전진하는 접근 동작을 확인하지 못했어요.");

		TankTarget target = Combat.Target;
		TankAim aim = AI.GetComponent<TankAim>();
		Vector3 start = AI.transform.position;
		float startYaw = aim.CurrentYaw;
		uint move = AI.CommandId;
		Vector3 goal = AI.Destination;
		TankGun.ChamberState chamber = gun.State;
		Vector3 offset = new Vector3(2.5f, 0f, 0f);
		Require(demo.MoveCombatTarget(target.Health.transform.position + offset), "이동 추적 시험용 표적을 옆으로 옮기지 못했어요.");
		Timeline.Add("접근 중 표적을 옆으로 2.5m 이동");

		await Wait(() =>
		{
			Require(gun.ShotCount == 0, "접근을 마치기 전에 포탄이 발사됐어요.");
			if (Combat.State != State.Approach) return false;
			Require(AI.CommandId == move && AI.Destination == goal && AI.AttackStatus == TankAI.AttackState.Idle && gun.State == chamber,
				"포탑 추적 중 이동 명령이나 약실 상태가 바뀌었어요.");

			Vector3 toTarget = target.AimPoint.position - gun.Muzzle.position;
			float yawError = Vector3.Angle(Vector3.ProjectOnPlane(gun.Muzzle.forward, Vector3.up),
				Vector3.ProjectOnPlane(toTarget, Vector3.up));
			return FlatDistance(start, AI.transform.position) >= 0.5f
				&& Mathf.Abs(Mathf.DeltaAngle(startYaw, aim.CurrentYaw)) >= 2f && yawError <= 1.5f;
		}, 3f, "접근 중 포탑이 옆으로 이동한 표적을 따라가지 못했어요.");
		float turn = Mathf.Abs(Mathf.DeltaAngle(startYaw, aim.CurrentYaw));
		Timeline.Add($"전진 중 포탑 회전 {turn:F1}° / 이동 명령·약실 상태 유지 / 발사 0발");

		await Wait(() =>
		{
			Require(gun.ShotCount == 0, "차체가 정지하기 전에 포탄이 발사됐어요.");
			return Combat.State == State.StopAndFire;
		}, 9f, "접근 뒤 정지 사격으로 넘어가지 않았어요.");
		await WaitForShot(8f);
		Require(Combat.Target == target, "정지한 뒤 같은 표적에 사격하지 않았어요.");
		keepScene = scenario == Scenario.ApproachTracking;
		return $"포탑 회전 {turn:F1}° / 이동 중 0발 / 정지 후 {gun.ShotCount}발";
	}

	private async Task<string> NearShot()
	{
		StartCombatCase(TankAttackDemo.CombatCase.Near);
		await WaitForShot(24f);
		Require(!visited.Contains(State.Approach), "근거리에서 접근을 시작했어요.");
		return $"정지 사격에서 실제 발사 {gun.ShotCount}발";
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

	private void StartSearch(bool hidden = false, bool frameContact = false)
	{
		Require(demo.RunSearchTest(ActiveCase, hidden, frameContact), "시험용 전차 또는 순찰을 시작하지 못했어요.");
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

	private async Task<TankTarget> BeginCover(bool frameContact = false)
	{
		StartSearch(frameContact: frameContact);
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
			LastDestination = null;
		}
		if (AI.MoveStatus is TankAI.MoveState.Moving or TankAI.MoveState.Arrived or TankAI.MoveState.Failed)
			LastDestination = AI.Destination;

		if (newEnemy || lastState != Combat.State)
		{
			lastState = Combat.State;
			visited.Add(lastState);
			string waypointText = Combat.PatrolIndex >= 0 ? (Combat.PatrolIndex + 1).ToString() : "—";
			Timeline.Add($"+{SimulationTime - startTime:F2}초  {Label(lastState)}  |  경유점 {waypointText}  |  발사 {gun.ShotCount}발");
			if (lastState == State.Reposition)
				Timeline.Add($"위치 변경 {Combat.RepositionCount}회 | 이동 목적지 {AI.Destination:F2}");
			else if (lastState == State.CheckAfterHit)
				Timeline.Add($"HP {demo.EnemyHealth.CurrentHP:F0} | 피격 {demo.EnemyHealth.DamageHitCount}회");
			else if (lastState is State.Approach or State.MoveToLastSeen or State.ReturnToPatrol or State.ForcedMove or State.Retreat)
				Timeline.Add($"이동 목적지 {AI.Destination:F2}");
			if (lastState == State.Idle && !string.IsNullOrEmpty(Combat.LastStopReason))
				Timeline.Add(Combat.LastStopReason);
			if (lastState == State.Idle && !string.IsNullOrEmpty(Combat.LastRepositionFailure))
				Timeline.Add(Combat.LastRepositionFailure);
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
		State.Reposition => "위치 변경 시작",
		State.ForcedMove => "강제 이동",
		State.Retreat => "퇴각",
		State.CheckAfterHit => "피격 후 주변 확인",
		_ => "정지"
	};
}
