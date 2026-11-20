using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class CarentanDrive : MonoBehaviour
{
	[SerializeField] private TankSceneLink sceneLink;
	[SerializeField] private NetworkObject enemyPrefab;
	[SerializeField] private Transform enemySpawn;
	[SerializeField] private Transform[] patrol;
	[SerializeField] private Transform offRoad;
	[SerializeField] private Camera followCamera;

	private TankAI enemy;
	private int patrolIndex = -1;
	private AudioListener followAudio;

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

		NetworkObject tank = runner.Spawn(enemyPrefab, enemySpawn.position, enemySpawn.rotation);
		if (tank != null && tank.TryGetComponent(out enemy) && enemy.SetMoveMode(TankAI.MoveMode.NavMesh)) return;

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

		patrolIndex = 0;
		MovePatrol();
		FollowEnemy();
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

		patrolIndex = -1;
		enemy.MoveTo(offRoad.position);
		Debug.Log($"Carentan off-road destination: {enemy.MoveStatus} {enemy.MoveFailure}", this);
	}

	[ContextMenu("Stop Sherman Patrol")]
	private void StopPatrol()
	{
		patrolIndex = -1;
		if (enemy != null) enemy.StopMove();
	}

	private void OnDisable()
	{
		if (sceneLink != null) sceneLink.StageChanged -= OnStageChanged;
		StopFollowing();
		StopPatrol();
	}
}
