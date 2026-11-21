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
		[InspectorName("광장 근거리 · 후방 막힘")] RearBlocked
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

	private TankAI enemy;
	private int patrolIndex = -1;
	private AudioListener followAudio;
	private Layout loadedLayout;
	private NetworkTransform playerMotion;
	private Rigidbody playerBody;

	public TankAI Enemy => enemy;
	public TankHealth PlayerHealth { get; private set; }
	public bool IsSouthEntry => loadedLayout == Layout.SouthEntry;
	public bool IsManeuverTest => loadedLayout is Layout.ShotBlock or Layout.RearOpen or Layout.RearBlocked;
	public bool CanStartMarket => loadedLayout != Layout.Road && sceneLink != null && sceneLink.IsReady
		&& enemy != null && enemy.CanAct;

	private void Awake()
	{
		if (followCamera != null) followCamera.TryGetComponent(out followAudio);
	}

	private void OnEnable()
	{
		sceneLink.StageChanged += OnStageChanged;
		if (sceneLink.IsReady) OnStageChanged(TankSceneLink.Stage.Ready);
	}

	private void OnStageChanged(TankSceneLink.Stage stage)
	{
		if (stage != TankSceneLink.Stage.Ready)
		{
			StopPatrol();
			return;
		}

		sceneLink.Interior.Mode.SetMode(TankTestMode.Mode.Interior);
		NetworkRunner runner = sceneLink.Session.Runner;
		if (!runner.IsServer || enemy != null) return;

		TankControl player = sceneLink.Session.tankControl;
		player.TryGetComponent(out TankHealth health);
		PlayerHealth = health;

		Transform spawn = layout switch
		{
			Layout.Road => enemySpawn,
			Layout.SouthEntry => southSpawn,
			Layout.ShotBlock or Layout.RearOpen or Layout.RearBlocked => maneuverSpawn,
			_ => marketSpawn
		};
		Transform target = layout switch
		{
			Layout.MarketNear => nearTarget,
			Layout.MarketFar => farTarget,
			Layout.SouthEntry => southPlayerStart,
			Layout.ShotBlock => blockedShotTarget,
			Layout.RearOpen or Layout.RearBlocked => closeTarget,
			_ => null
		};
		bool needsTarget = layout is not (Layout.Road or Layout.MarketPatrol);
		if (spawn == null || needsTarget && (target == null || health == null
			|| !player.TryGetComponent(out playerMotion) || !player.TryGetComponent(out playerBody) || !playerMotion.HasStateAuthority))
		{
			Debug.LogError("[Carentan] Missing or unavailable tank placement.", this);
			return;
		}

		NetworkObject tank = runner.Spawn(enemyPrefab, spawn.position, spawn.rotation);
		if (tank != null && tank.TryGetComponent(out enemy) && enemy.SetMoveMode(TankAI.MoveMode.NavMesh))
		{
			if (shotObstacle != null) shotObstacle.SetActive(layout == Layout.ShotBlock);
			if (rearObstacle != null) rearObstacle.SetActive(layout == Layout.RearBlocked);
			if (needsTarget) PlaceTiger(target);

			loadedLayout = layout;
			return;
		}

		if (tank != null) runner.Despawn(tank);
		enemy = null;
		Debug.LogError("Carentan could not start the Sherman in NavMesh mode.", this);
	}

	private void Update()
	{
		bool canDrive = enemy != null && sceneLink.IsReady;
		Keyboard keys = Keyboard.current;
		if (keys != null && Application.isFocused)
		{
			if (keys.digit7Key.wasPressedThisFrame || keys.numpad7Key.wasPressedThisFrame) StartPatrol();
			else if (canDrive)
			{
				if (keys.digit8Key.wasPressedThisFrame || keys.numpad8Key.wasPressedThisFrame) MoveOffRoad();
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

	[ContextMenu("Start Sherman Patrol")]
	private void StartPatrol()
	{
		if (!Application.isPlaying) return;
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
		Transform[] points = IsSouthEntry ? southPatrol : IsManeuverTest ? maneuverPatrol : marketPatrol;
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

	public void MoveSouthTiger(bool exposed)
	{
		Transform point = exposed ? southContact : southCover;
		if (!IsSouthEntry || !CanStartMarket || point == null || playerMotion == null || playerBody == null
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

	private void FollowEnemy()
	{
		if (followCamera == null) return;

		PlaceCamera();
		sceneLink.Interior.Mode.TryObserve(followCamera, followAudio);
	}

	private void LateUpdate()
	{
		if (followCamera == null || !followCamera.enabled) return;
		if (enemy != null && enemy.isActiveAndEnabled) PlaceCamera();
		else StopFollowing();
	}

	private void PlaceCamera()
	{
		Transform hull = enemy.transform;
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
		if (enemy == null || !enemy.CanAct) return;

		StopPatrol();
		enemy.MoveTo(offRoad.position);
		Debug.Log($"Carentan off-road destination: {enemy.MoveStatus} {enemy.MoveFailure}", this);
	}

	[ContextMenu("Stop Sherman Patrol")]
	public void StopPatrol()
	{
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
