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

	private TankAI enemy;
	private int patrolIndex = -1;

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
		StopPatrol();
	}
}
