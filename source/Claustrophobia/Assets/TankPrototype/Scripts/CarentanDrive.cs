using System;
using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class CarentanDrive : MonoBehaviour
{
	private enum Layout
	{
		[InspectorName("기존 도로 주행")] Road,
		[InspectorName("광장 순찰만")] MarketPatrol,
		[InspectorName("광장 20m 교전")] MarketNear,
		[InspectorName("광장 35m 교전")] MarketFar,
		[InspectorName("시장 남쪽 진입")] SouthEntry,
		[InspectorName("광장 사선 차단 · 위치 변경")] ShotBlock,
		[InspectorName("광장 근거리 · 후방 열림")] RearOpen,
		[InspectorName("광장 근거리 · 후방 막힘")] RearBlocked,
		[InspectorName("진입 비교 · 남쪽 1대")] EntrySouth,
		[InspectorName("진입 검증 · 서쪽 1대")] EntryWest,
		[InspectorName("진입 비교 · 2대 순차")] EntryStaggered,
		[InspectorName("진입 비교 · 2대 동시")] EntryTogether,
		[InspectorName("맵 변경 · 중앙 연결로 주행")] CentralDrive,
		[InspectorName("맵 변경 · 시장 서쪽 틈 교전")] WestGap,
		[InspectorName("적 배치 디버그")] Deployment
	}

	[Flags]
	private enum DeploymentEnemies
	{
		None = 0,
		A = 1 << 0,
		B = 1 << 1,
		C = 1 << 2,
		D = 1 << 3,
		All = A | B | C | D
	}

	private enum DeploymentStart
	{
		[InspectorName("남쪽 도로")] SouthRoad,
		[InspectorName("남쪽 코너")] SouthCorner,
		[InspectorName("서쪽 틈 건물 뒤")] WestGap
	}

	private enum DeploymentState { Preparing, Ready, Running, Stopped }

	[Serializable]
	private sealed class DeploymentSlot
	{
		public Transform Spawn;
		public Transform[] Waypoints;
	}

	[SerializeField] private TankSceneLink sceneLink;
	[SerializeField] private NetworkObject enemyPrefab;
	[SerializeField] private Transform enemySpawn;
	[SerializeField] private Transform[] patrol;
	[SerializeField] private Transform offRoad;
	[SerializeField] private Camera followCamera;

	[Header("시장 광장 시험")]
	[SerializeField, Tooltip("Play를 누르기 전에 배치를 선택합니다.")] private Layout layout;
	[SerializeField] private Transform marketSpawn;
	[SerializeField] private Transform[] marketPatrol;
	[SerializeField] private Transform nearTarget;
	[SerializeField] private Transform farTarget;

	[Header("시장 남쪽 진입 시험")]
	[SerializeField] private Transform southSpawn;
	[SerializeField] private Transform[] southPatrol;
	[SerializeField] private Transform southPlayerStart;
	[SerializeField] private Transform southContact;
	[SerializeField] private Transform southCover;

	[Header("광장 교전 기동 시험")]
	[SerializeField] private Transform maneuverSpawn;
	[SerializeField] private Transform[] maneuverPatrol;
	[SerializeField] private Transform blockedShotTarget;
	[SerializeField] private Transform closeTarget;
	[SerializeField] private GameObject shotObstacle;
	[SerializeField] private GameObject rearObstacle;

	[Header("광장 진입로 비교")]
	[SerializeField] private Transform entryTiger;
	[SerializeField] private Transform southEntrySpawn;
	[SerializeField] private Transform[] southEntryPatrol;
	[SerializeField] private Transform westEntrySpawn;
	[SerializeField] private Transform[] westEntryPatrol;
	[SerializeField] private Transform entryOverview;

	[Header("맵 변경 확인")]
	[SerializeField] private Transform centralSpawn;
	[SerializeField] private Transform centralGoal;
	[SerializeField] private Transform westGapSpawn;
	[SerializeField] private Transform[] westGapPatrol;
	[SerializeField] private Transform westGapCover;
	[SerializeField] private Transform westGapOpening;

	[Header("적 배치 디버그 · A / B / C / D")]
	[SerializeField] private DeploymentEnemies deploymentEnemies = DeploymentEnemies.All;
	[SerializeField] private DeploymentStart deploymentStart;
	[SerializeField] private bool deploymentSoloDrive = true;
	[SerializeField, Range(.1f, 1f)] private float soloLever = .5f;
	[SerializeField] private DeploymentSlot[] deployment;
	[SerializeField] private Transform deploymentTiger;
	[SerializeField] private Transform deploymentCorner;
	[SerializeField] private Transform deploymentCover;
	[SerializeField] private Transform deploymentGoal;
	[SerializeField] private Transform deploymentTop;
	[SerializeField, Min(1f)] private float topHalfWidth = 145f;
	[SerializeField, Min(1f)] private float topHalfDepth = 132f;

	public const int DeploymentCount = 4;
	private const double EntryDelay = 4d;
	private static readonly string[] DeploymentNames = { "A", "B", "C", "D" };
	private readonly TankAI[] deployed = new TankAI[DeploymentCount];
	private DeploymentState deploymentState;
	private NetworkRunner deploymentRunner;
	private double deploymentAt;
	private double deploymentDuration;
	private TankMotor playerMotor;
	private bool soloArmed;

	private TankAI enemy;
	private TankAI westEnemy;
	private Transform observedHull;
	private bool entryStarted;
	private bool showOverview;
	private bool showTop;
	private double westEntryAt = -1d;
	private int patrolIndex = -1;
	private AudioListener followAudio;
	private Layout loadedLayout;
	private NetworkTransform playerMotion;
	private Rigidbody playerBody;

	public TankAI Enemy => enemy;
	public TankAI WestEnemy => IsWestOnly ? enemy : westEnemy;
	public bool IsWestOnly => layout == Layout.EntryWest;
	public bool IsEntryTest => layout is Layout.EntrySouth or Layout.EntryWest or Layout.EntryStaggered or Layout.EntryTogether;
	public bool HasWestOrder => westEntryAt >= 0d;
	public double WestWait => HasWestOrder && enemy != null && enemy.Runner != null
		? System.Math.Max(0d, westEntryAt - enemy.Runner.SimulationTime) : 0d;
	public bool CanStartEntry => IsEntryTest && CanStartMarket && !entryStarted
		&& PlayerHealth != null && !PlayerHealth.Destroyed;

	public TankHealth PlayerHealth { get; private set; }
	public bool IsSouthEntry => loadedLayout == Layout.SouthEntry;
	public bool IsManeuverTest => loadedLayout is Layout.ShotBlock or Layout.RearOpen or Layout.RearBlocked;
	public bool IsCentralDrive => layout == Layout.CentralDrive;
	public bool IsWestGap => layout == Layout.WestGap;
	public bool IsDeployment => layout == Layout.Deployment;
	public bool CanObserve => followCamera != null && sceneLink != null && sceneLink.IsReady;
	public Transform DeploymentPoint => deploymentStart switch
	{
		DeploymentStart.SouthCorner => deploymentCorner,
		DeploymentStart.WestGap => westGapCover,
		_ => deploymentTiger
	};
	public Transform DeploymentCover => deploymentCover;
	public Transform DeploymentGoal => deploymentGoal;
	public bool DeploymentStarted => deploymentState == DeploymentState.Running;
	public bool DeploymentStopped => deploymentState == DeploymentState.Stopped;
	public bool CanRestartDeployment => IsDeployment && sceneLink != null
		&& sceneLink.Session != null && sceneLink.Session.IsLocalSession;
	public bool DeploymentSoloDrive => deploymentSoloDrive && CanRestartDeployment;
	public double DeploymentSeconds => DeploymentStarted && deploymentRunner != null && deploymentRunner.IsRunning
		? deploymentRunner.SimulationTime - deploymentAt : deploymentDuration;
	public string DeploymentError { get; private set; } = "";
	public TankSceneLink.Stage SceneStage => sceneLink != null ? sceneLink.CurrentStage : TankSceneLink.Stage.Idle;
	public string SceneError => sceneLink != null ? sceneLink.LastError ?? sceneLink.Session?.LastError : null;

	public bool CanStartMarket => loadedLayout != Layout.Road && sceneLink != null && sceneLink.IsReady
		&& enemy != null && enemy.CanAct;
	public bool CanStartCentralDrive => IsCentralDrive && CanStartMarket && enemy.MoveStatus != TankAI.MoveState.Moving;
	private bool CanUseRoadKeys => !IsEntryTest && !IsCentralDrive && !IsWestGap && !IsDeployment;

	public bool CanStartDeployment
	{
		get
		{
			if (!IsDeployment || deploymentState != DeploymentState.Ready || sceneLink == null || !sceneLink.IsReady
				|| PlayerHealth == null || PlayerHealth.Destroyed) return false;

			for (int i = 0; i < DeploymentCount; i++)
			{
				if (!IsDeploymentSelected(i)) continue;

				TankAI tank = GetDeployed(i);
				if (tank == null || !tank.CanAct || tank.Combat == null) return false;
			}

			return true;
		}
	}

	public bool IsDeploymentSelected(int index) => (uint)index < DeploymentCount
		&& ((int)deploymentEnemies & (1 << index)) != 0;

	public TankAI GetDeployed(int index)
	{
		if ((uint)index >= DeploymentCount) return null;

		TankAI tank = deployed[index];
		return tank != null && tank.Object != null && tank.Object.IsValid ? tank : null;
	}

	private void Awake()
	{
		if (followCamera != null) followCamera.TryGetComponent(out followAudio);
	}

	private void OnEnable()
	{
		if (sceneLink == null) return;
		sceneLink.StageChanged += OnStageChanged;
		if (sceneLink.IsReady) OnStageChanged(TankSceneLink.Stage.Ready);
	}

	private void OnStageChanged(TankSceneLink.Stage stage)
	{
		if (stage != TankSceneLink.Stage.Ready)
		{
			StopPatrol();
			if (IsDeployment)
			{
				ClearSoloLevers();
				StopFollowing();
			}
			return;
		}

		sceneLink.Interior.Mode.SetMode(TankTestMode.Mode.Interior);
		NetworkRunner runner = sceneLink.Session.Runner;
		if (runner == null || !runner.IsServer || enemy != null) return;

		TankControl player = sceneLink.Session.tankControl;
		player.TryGetComponent(out TankHealth health);
		PlayerHealth = health;
		if (IsDeployment)
		{
			PrepareDeployment(runner, player);
			return;
		}

		Transform spawn = layout switch
		{
			Layout.EntryWest => westEntrySpawn,
			Layout.EntrySouth or Layout.EntryStaggered or Layout.EntryTogether => southEntrySpawn,
			Layout.Road => enemySpawn,
			Layout.SouthEntry => southSpawn,
			Layout.ShotBlock or Layout.RearOpen or Layout.RearBlocked => maneuverSpawn,
			Layout.CentralDrive => centralSpawn,
			Layout.WestGap => westGapSpawn,
			_ => marketSpawn
		};
		Transform target = layout switch
		{
			Layout.EntrySouth or Layout.EntryWest or Layout.EntryStaggered or Layout.EntryTogether => entryTiger,
			Layout.MarketNear => nearTarget,
			Layout.MarketFar => farTarget,
			Layout.SouthEntry => southPlayerStart,
			Layout.ShotBlock => blockedShotTarget,
			Layout.RearOpen or Layout.RearBlocked => closeTarget,
			Layout.WestGap => westGapCover,
			_ => null
		};
		bool needsTarget = layout is not (Layout.Road or Layout.MarketPatrol or Layout.CentralDrive);
		if (spawn == null || needsTarget && !CanPlaceTiger(player, target))
		{
			Debug.LogError("[Carentan] Missing or unavailable tank placement.", this);
			return;
		}

		enemy = SpawnEnemy(runner, spawn);
		if (enemy != null)
		{
			if (shotObstacle != null) shotObstacle.SetActive(layout == Layout.ShotBlock);
			if (rearObstacle != null) rearObstacle.SetActive(layout == Layout.RearBlocked);
			if (needsTarget) PlaceTiger(target);

			loadedLayout = layout;
			return;
		}

		Debug.LogError("Carentan could not start the Sherman in NavMesh mode.", this);
	}

	private bool CanPlaceTiger(TankControl player, Transform point)
	{
		return point != null && PlayerHealth != null && player.TryGetComponent(out playerMotion)
			&& player.TryGetComponent(out playerBody) && playerMotion.HasStateAuthority;
	}

	private void PrepareDeployment(NetworkRunner runner, TankControl player)
	{
		if (loadedLayout == Layout.Deployment) return;

		Transform start = DeploymentPoint;
		if (!CanPlaceTiger(player, start))
		{
			ReportDeploymentError("Tiger 전차 또는 선택한 출발점이 연결되지 않았습니다.");
			return;
		}

		if ((deploymentEnemies & DeploymentEnemies.All) != DeploymentEnemies.None
			&& (deployment == null || deployment.Length != DeploymentCount || enemyPrefab == null))
		{
			ReportDeploymentError("적 배치 슬롯 또는 전차 프리팹이 연결되지 않았습니다.");
			return;
		}

		for (int i = 0; i < DeploymentCount; i++)
		{
			if (!IsDeploymentSelected(i)) continue;

			DeploymentSlot slot = deployment[i];
			if (slot != null && slot.Spawn != null && slot.Waypoints != null && slot.Waypoints.Length != 0
				&& Array.TrueForAll(slot.Waypoints, point => point != null)) continue;

			ReportDeploymentError($"{DeploymentNames[i]}의 시작점 또는 경유점이 빠졌습니다.");
			return;
		}

		for (int i = 0; i < DeploymentCount; i++)
		{
			if (!IsDeploymentSelected(i)) continue;

			TankAI tank = SpawnEnemy(runner, deployment[i].Spawn);
			deployed[i] = tank;
			if (tank == null || tank.Combat == null)
			{
				for (int j = 0; j <= i; j++)
				{
					TankAI spawned = GetDeployed(j);
					if (spawned != null) runner.Despawn(spawned.Object);
					deployed[j] = null;
				}

				ReportDeploymentError($"{DeploymentNames[i]} 전차를 생성하지 못했습니다.");
				return;
			}

			tank.name = "Sherman " + DeploymentNames[i];
		}

		if (shotObstacle != null) shotObstacle.SetActive(false);
		if (rearObstacle != null) rearObstacle.SetActive(false);
		PlaceTiger(start);
		player.TryGetComponent(out playerMotor);
		deploymentRunner = runner;
		deploymentState = DeploymentState.Ready;
		loadedLayout = Layout.Deployment;
		DeploymentError = "";
	}

	private void ReportDeploymentError(string reason)
	{
		DeploymentError = reason;
		Debug.LogError("[Carentan] " + reason, this);
	}

	private TankAI SpawnEnemy(NetworkRunner runner, Transform spawn)
	{
		if (spawn == null) return null;

		NetworkObject tank = runner.Spawn(enemyPrefab, spawn.position, spawn.rotation);
		if (tank == null) return null;
		if (tank.TryGetComponent(out TankAI ai) && ai.SetMoveMode(TankAI.MoveMode.NavMesh)) return ai;

		runner.Despawn(tank);
		return null;
	}

	private void Update()
	{
		bool canDrive = enemy != null && sceneLink.IsReady;
		if (canDrive && HasWestOrder) UpdateWestEntry();

		Keyboard keys = Keyboard.current;
		if (keys != null && Application.isFocused)
		{
			if (IsDeployment)
			{
				if (keys.digit0Key.wasPressedThisFrame || keys.numpad0Key.wasPressedThisFrame) StartDeployment();
				else if (keys.digit9Key.wasPressedThisFrame || keys.numpad9Key.wasPressedThisFrame) StopDeployment();
			}
			else if (CanUseRoadKeys && (keys.digit7Key.wasPressedThisFrame || keys.numpad7Key.wasPressedThisFrame)) StartPatrol();
			else if (canDrive)
			{
				if (CanUseRoadKeys && (keys.digit8Key.wasPressedThisFrame || keys.numpad8Key.wasPressedThisFrame)) MoveOffRoad();
				else if (keys.digit9Key.wasPressedThisFrame || keys.numpad9Key.wasPressedThisFrame) StopPatrol();
				else if (keys.digit0Key.wasPressedThisFrame || keys.numpad0Key.wasPressedThisFrame) StartMarketCombat();
				else if (keys.f5Key.wasPressedThisFrame) FollowEnemy();
			}
		}

		if (!canDrive || patrolIndex < 0 || enemy.MoveStatus == TankAI.MoveState.Moving) return;
		if (enemy.MoveStatus != TankAI.MoveState.Arrived)
		{
			Debug.LogWarning($"Carentan Sherman stopped on the way to {patrol[patrolIndex].name}: {enemy.MoveFailure}", this);
			patrolIndex = -1;
			return;
		}

		Debug.Log($"Carentan Sherman arrived at {patrol[patrolIndex].name}.", this);
		if (++patrolIndex == patrol.Length) patrolIndex = -1;
		else MovePatrol();
	}

	public void StartDeployment()
	{
		if (!CanStartDeployment) return;

		deploymentAt = deploymentRunner.SimulationTime;
		deploymentState = DeploymentState.Running;
		PlayerHealth.Died += StopDeployment;

		for (int i = 0; i < DeploymentCount; i++)
		{
			if (!IsDeploymentSelected(i)) continue;

			if (deployed[i].Combat.StartPatrol(deployment[i].Waypoints)) continue;

			StopDeployment();
			ReportDeploymentError($"{DeploymentNames[i]}의 순찰·경계 명령을 시작하지 못했습니다.");
			return;
		}

		DeploymentError = "";
	}

	public void StopDeployment()
	{
		if (!DeploymentStarted) return;

		deploymentDuration = DeploymentSeconds;
		deploymentState = DeploymentState.Stopped;
		if (PlayerHealth != null) PlayerHealth.Died -= StopDeployment;

		ClearSoloLevers();
		for (int i = 0; i < DeploymentCount; i++) GetDeployed(i)?.StopActions();
	}

	private void UpdateSoloLevers()
	{
		if (playerMotor == null) return;

		Keyboard keys = Keyboard.current;
		if (!DeploymentStarted || !DeploymentSoloDrive || !Application.isFocused || keys == null
			|| !sceneLink.IsReady || !playerMotor.CanDriveSolo)
		{
			ClearSoloLevers();
			return;
		}

		TankTestMode mode = sceneLink.Interior.Mode;
		if (!mode.IsInterior || mode.IsObserving)
		{
			ClearSoloLevers();
			return;
		}

		bool forward = keys.wKey.isPressed;
		bool reverse = keys.sKey.isPressed;
		bool left = keys.aKey.isPressed;
		bool right = keys.dKey.isPressed;
		if (!soloArmed)
		{
			soloArmed = !(forward || reverse || left || right);
			return;
		}

		int drive = (forward ? 1 : 0) - (reverse ? 1 : 0);
		int turn = (right ? 1 : 0) - (left ? 1 : 0);
		playerMotor.SetSoloLevers(new Vector2(Mathf.Clamp(drive + turn, -1, 1),
			Mathf.Clamp(drive - turn, -1, 1)) * soloLever);
	}

	private void ClearSoloLevers()
	{
		soloArmed = false;
		if (playerMotor != null) playerMotor.SetSoloLevers(Vector2.zero);
	}

	private void OnApplicationFocus(bool focused)
	{
		if (!focused) ClearSoloLevers();
	}

	[ContextMenu("Start Sherman Patrol")]
	private void StartPatrol()
	{
		if (!Application.isPlaying || !CanUseRoadKeys) return;
		if (enemy == null || !sceneLink.IsReady)
		{
			Debug.LogWarning($"Carentan Sherman is not ready for patrol. Scene stage: {sceneLink.CurrentStage}.", this);
			return;
		}
		if (patrol.Length == 0)
		{
			Debug.LogWarning("Carentan Sherman has no patrol waypoints.", this);
			return;
		}

		StopPatrol();
		patrolIndex = 0;
		MovePatrol();
		FollowEnemy();
	}

	public void StartMarketCombat()
	{
		if (IsEntryTest)
		{
			StartEntry();
			return;
		}

		if (IsCentralDrive)
		{
			StartCentralDrive();
			return;
		}

		if (!CanStartMarket)
		{
			Debug.LogWarning("[Carentan] Select a market layout before entering Play Mode and wait for the host.", this);
			return;
		}

		if (PlayerHealth != null && PlayerHealth.Destroyed)
		{
			Debug.LogWarning("[Carentan] Restart Play Mode to reset the destroyed Tiger.", this);
			return;
		}

		TankCombat combat = enemy.Combat;
		if (combat == null || combat.State != TankCombat.CombatState.Idle) return;
		Transform[] points = loadedLayout switch
		{
			Layout.SouthEntry => southPatrol,
			Layout.ShotBlock or Layout.RearOpen or Layout.RearBlocked => maneuverPatrol,
			Layout.WestGap => westGapPatrol,
			_ => marketPatrol
		};
		if (!combat.StartPatrol(points))
		{
			Debug.LogWarning("[Carentan] The Sherman could not start the market patrol. Check its sensor and waypoints.", this);
			return;
		}

		patrolIndex = -1;
		Debug.Log($"[Carentan] {loadedLayout}: market patrol started at {enemy.transform.position:F2}.", this);
		if (IsSouthEntry) StopFollowing();
		else FollowEnemy();
	}

	private void StartCentralDrive()
	{
		if (!CanStartCentralDrive) return;
		if (centralGoal == null || !enemy.MoveTo(centralGoal.position))
		{
			Debug.LogWarning($"[Carentan Map] Central drive could not start. {enemy.MoveBlockReason}", this);
			return;
		}

		Debug.Log($"[Carentan Map] Central drive to {centralGoal.position:F2}.", this);
		FollowEnemy();
	}

	private void StartEntry()
	{
		if (!CanStartEntry) return;

		bool needsWest = layout is Layout.EntryTogether or Layout.EntryStaggered;
		if (needsWest && westEntrySpawn == null)
		{
			Debug.LogError("[Carentan Entry] Missing west spawn.", this);
			return;
		}

		if (enemy.Combat == null || !enemy.Combat.StartPatrol(IsWestOnly ? westEntryPatrol : southEntryPatrol))
		{
			Debug.LogError("[Carentan Entry] First Sherman could not start its route.", this);
			return;
		}

		entryStarted = true;
		enemy.name = IsWestOnly ? "Sherman - West" : "Sherman - South";
		Debug.Log($"[Carentan Entry] {layout}: {enemy.name} departed.", enemy);

		if (layout == Layout.EntryStaggered) westEntryAt = enemy.Runner.SimulationTime + EntryDelay;
		else if (layout == Layout.EntryTogether) StartWestEntry();

		ObserveOverview();
	}

	private void UpdateWestEntry()
	{
		if (PlayerHealth == null || PlayerHealth.Destroyed)
		{
			westEntryAt = -1d;
			Debug.Log("[Carentan Entry] West departure cancelled: Tiger destroyed.", this);
			return;
		}

		if (enemy.Runner.SimulationTime >= westEntryAt) StartWestEntry();
	}

	private void StartWestEntry()
	{
		westEntryAt = -1d;
		NetworkRunner runner = enemy.Runner;
		westEnemy = SpawnEnemy(runner, westEntrySpawn);
		if (westEnemy != null && westEnemy.Combat != null && westEnemy.Combat.StartPatrol(westEntryPatrol))
		{
			westEnemy.name = "Sherman - West";
			Debug.Log("[Carentan Entry] Sherman - West departed.", westEnemy);
			return;
		}

		if (westEnemy != null) runner.Despawn(westEnemy.Object);
		westEnemy = null;
		StopPatrol();
		Debug.LogError("[Carentan Entry] West Sherman could not start its route.", this);
	}

	public void MoveSouthTiger(bool exposed)
	{
		if (IsSouthEntry) MoveTestTiger(exposed ? southContact : southCover);
	}

	public void MoveWestGapTiger(bool exposed)
	{
		if (IsWestGap) MoveTestTiger(exposed ? westGapOpening : westGapCover);
	}

	private void MoveTestTiger(Transform point)
	{
		if (!CanStartMarket || point == null || playerMotion == null || playerBody == null
			|| !playerMotion.HasStateAuthority || PlayerHealth == null || PlayerHealth.Destroyed) return;

		PlaceTiger(point);
		Debug.Log($"[Carentan] Tiger at {point.name} {point.position:F2}.", this);
	}

	private void PlaceTiger(Transform point)
	{
		playerMotion.Teleport(point.position, point.rotation);
		playerBody.position = point.position;
		playerBody.rotation = point.rotation;
		playerBody.linearVelocity = playerBody.angularVelocity = Vector3.zero;
		Physics.SyncTransforms();
	}

	private void FollowEnemy() => ObserveEnemy(enemy);

	public void ObserveEnemy(TankAI tank)
	{
		if (tank != null) ObserveHull(tank.transform);
	}

	public void ObserveTiger()
	{
		if (PlayerHealth != null) ObserveHull(PlayerHealth.transform);
	}

	private void ObserveHull(Transform hull)
	{
		if (!CanObserve || !sceneLink.Interior.Mode.TryObserve(followCamera, followAudio)) return;

		ClearSoloLevers();
		showTop = false;
		showOverview = false;
		observedHull = hull;
		followCamera.orthographic = false;
		PlaceCamera(hull);
	}

	public void ObserveOverview()
	{
		if (!CanObserve || entryOverview == null || !sceneLink.Interior.Mode.TryObserve(followCamera, followAudio)) return;

		ClearSoloLevers();
		showTop = false;
		showOverview = true;
		observedHull = null;
		followCamera.orthographic = false;
		followCamera.transform.SetPositionAndRotation(entryOverview.position, entryOverview.rotation);
	}

	public void ObserveDeploymentTop()
	{
		if (!IsDeployment || !CanObserve || deploymentTop == null
			|| !sceneLink.Interior.Mode.TryObserve(followCamera, followAudio)) return;

		ClearSoloLevers();
		showTop = true;
		showOverview = true;
		observedHull = null;
		followCamera.transform.SetPositionAndRotation(deploymentTop.position, deploymentTop.rotation);
		followCamera.orthographic = true;
		FitDeploymentTop();
	}

	public void ReturnToCabin()
	{
		if (!CanObserve) return;

		StopFollowing();
		sceneLink.Interior.Mode.SetMode(TankTestMode.Mode.Interior);
	}

	private void LateUpdate()
	{
		if (IsDeployment) UpdateSoloLevers();

		if (followCamera == null || !followCamera.enabled) return;
		if (showTop) FitDeploymentTop();
		if (showOverview) return;

		if (observedHull != null && observedHull.gameObject.activeInHierarchy) PlaceCamera(observedHull);
		else StopFollowing();
	}

	private void FitDeploymentTop()
	{
		followCamera.orthographicSize = Mathf.Max(topHalfDepth, topHalfWidth / Mathf.Max(.01f, followCamera.aspect));
	}

#if UNITY_EDITOR
	private GUIStyle mapLabel;

	private void OnGUI()
	{
		if (!showTop || followCamera == null || !followCamera.enabled || Event.current.type != EventType.Repaint) return;

		mapLabel ??= new GUIStyle(GUI.skin.box) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
		mapLabel.fontSize = Mathf.Clamp(Screen.height / 65, 15, 22);
		mapLabel.normal.textColor = Color.white;

		if (PlayerHealth != null) DrawMapLabel(PlayerHealth.transform, "Tiger", mapLabel.fontSize * 3f);
		for (int i = 0; i < DeploymentCount; i++)
		{
			TankAI tank = GetDeployed(i);
			if (tank != null) DrawMapLabel(tank.transform, DeploymentNames[i], mapLabel.fontSize + 12f);
		}
	}

	private void DrawMapLabel(Transform hull, string label, float width)
	{
		Vector3 point = followCamera.WorldToScreenPoint(hull.position);
		if (point.z <= 0f || !followCamera.pixelRect.Contains(new Vector2(point.x, point.y))) return;

		float height = mapLabel.fontSize + 8f;
		GUI.Box(new Rect(point.x - width * .5f, Screen.height - point.y - height - 9f, width, height), label, mapLabel);
	}
#endif

	private void PlaceCamera(Transform hull)
	{
		Vector3 forward = Vector3.ProjectOnPlane(hull.forward, Vector3.up).normalized;
		Vector3 origin = hull.position + Vector3.up * 4f;
		Vector3 offset = -forward * 8f + Vector3.up * 1.5f;
		Vector3 dir = offset.normalized;
		float distance = offset.magnitude;

		// Cast above the hull so the rear view can move in when it meets a building.
		if (Physics.SphereCast(origin, 0.25f, dir, out RaycastHit hit, distance,
			followCamera.cullingMask & Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
			distance = Mathf.Max(0f, hit.distance - 0.1f);

		Vector3 pos = origin + dir * distance;
		Vector3 lookAt = hull.position + Vector3.up * 1.6f + forward;
		followCamera.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(lookAt - pos));
	}

	private void StopFollowing()
	{
		showTop = false;
		showOverview = false;
		observedHull = null;
		if (followCamera != null) followCamera.orthographic = false;
		if (sceneLink != null && sceneLink.Interior != null)
			sceneLink.Interior.Mode.StopObserving(followCamera);
	}

	private void MovePatrol()
	{
		Transform point = patrol[patrolIndex];
		if (!enemy.MoveTo(point.position))
		{
			Debug.LogWarning($"Carentan Sherman rejected the move to {point.name}. {enemy.MoveBlockReason}", this);
			patrolIndex = -1;
			return;
		}

		if (enemy.MoveStatus == TankAI.MoveState.Moving)
			Debug.Log($"Carentan Sherman moving to {point.name}.", this);
	}

	[ContextMenu("Request Off-road Destination")]
	private void MoveOffRoad()
	{
		if (!CanUseRoadKeys || enemy == null || !enemy.CanAct) return;

		StopPatrol();
		enemy.MoveTo(offRoad.position);
		Debug.Log($"Carentan off-road destination: {enemy.MoveStatus} {enemy.MoveFailure}", this);
	}

	[ContextMenu("Stop Sherman Patrol")]
	public void StopPatrol()
	{
		if (IsDeployment) StopDeployment();
		westEntryAt = -1d;
		if (westEnemy != null && (westEnemy.Combat == null || !westEnemy.Combat.CancelOrder())) westEnemy.StopActions();

		bool hadRoute = patrolIndex >= 0;
		patrolIndex = -1;
		if (enemy == null) return;

		if (enemy.Combat != null && enemy.Combat.CancelOrder()) return;
		if (hadRoute || enemy.MoveStatus != TankAI.MoveState.Idle || enemy.AttackStatus != TankAI.AttackState.Idle)
			enemy.StopActions();
	}

	private void OnDisable()
	{
		if (sceneLink != null) sceneLink.StageChanged -= OnStageChanged;
		StopFollowing();
		StopPatrol();
	}
}
