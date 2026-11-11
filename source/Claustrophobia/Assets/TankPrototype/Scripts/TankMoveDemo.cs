using Fusion;
using UnityEngine;

// Isolated Single-mode scene for checking the enemy prefab without player crew controls.
public sealed class TankMoveDemo : MonoBehaviour
{
	[SerializeField] private NetworkRunner runner;
	[SerializeField] private NetworkSceneManagerDefault sceneManager;
	[SerializeField] private NetworkObject enemyPrefab;
	[SerializeField] private Transform spawnPoint;
	[SerializeField] private Transform destination;

	private TankAI.MoveState lastState;

	public TankAI Enemy { get; private set; }

	private async void Start()
	{
		if (runner == null || sceneManager == null || enemyPrefab == null || spawnPoint == null || destination == null)
		{
			Debug.LogError("[AI MOVE] Demo references are missing.", this);
			return;
		}

		var result = await runner.StartGame(new StartGameArgs
		{
			GameMode = GameMode.Single,
			SceneManager = sceneManager
		});
		if (this == null || !isActiveAndEnabled) return;
		if (!result.Ok)
		{
			Debug.LogError("[AI MOVE] Single session failed: " + result.ShutdownReason, this);
			return;
		}

		NetworkObject entity = runner.Spawn(enemyPrefab, spawnPoint.position, spawnPoint.rotation);
		Enemy = entity.GetComponent<TankAI>();
		MoveToGoal();
	}

	private void Update()
	{
		if (Enemy == null || Enemy.MoveStatus == lastState) return;

		lastState = Enemy.MoveStatus;
		Debug.Log(lastState == TankAI.MoveState.Arrived ? "[AI MOVE] Arrived and stopped."
			: lastState == TankAI.MoveState.Moving ? "[AI MOVE] Moving to goal." : "[AI MOVE] Movement cancelled.", this);
	}

	[ContextMenu("Move To Goal")]
	public void MoveToGoal()
	{
		if (!Application.isPlaying || Enemy == null || destination == null) return;

		Enemy.MoveTo(destination.position);
	}

	[ContextMenu("Stop Movement")]
	public void StopMovement()
	{
		if (!Application.isPlaying || Enemy == null) return;

		Enemy.StopMove();
	}

	private void OnDisable()
	{
		if (Enemy != null) Enemy.StopMove();
	}
}
