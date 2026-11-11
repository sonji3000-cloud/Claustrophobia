using Fusion;
using UnityEngine;

// Standalone local test; the same TankAI command is used by the real host.
public sealed class TankAttackDemo : MonoBehaviour
{
	[SerializeField] private NetworkRunner runner;
	[SerializeField] private NetworkSceneManagerDefault sceneManager;
	[SerializeField] private NetworkObject enemyPrefab;
	[SerializeField] private Transform spawnPoint;
	[SerializeField] private Transform targetPoint;

	private TankAI.AttackState lastState;
	private string lastReason;
	public TankAI Enemy { get; private set; }

	private async void Start()
	{
		if (runner == null || sceneManager == null || enemyPrefab == null || spawnPoint == null || targetPoint == null)
		{
			Debug.LogError("[AI ATTACK] Demo references are missing.", this);
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
			Debug.LogError("[AI ATTACK] Single session failed: " + result.ShutdownReason, this);
			return;
		}
		NetworkObject entity = runner.Spawn(enemyPrefab, spawnPoint.position, spawnPoint.rotation);
		Enemy = entity.GetComponent<TankAI>();
		AttackTarget();
	}

	private void Update()
	{
		if (Enemy == null || Enemy.AttackStatus == lastState && Enemy.AttackBlockReason == lastReason) return;
		lastState = Enemy.AttackStatus;
		lastReason = Enemy.AttackBlockReason;
		Debug.Log("[AI ATTACK] " + lastState + (string.IsNullOrEmpty(lastReason) ? "" : ": " + lastReason), this);
	}

	[ContextMenu("Attack Target")]
	public void AttackTarget()
	{
		if (!Application.isPlaying || Enemy == null) return;
		if (!Enemy.Attack(targetPoint)) Debug.LogWarning("[AI ATTACK] Target command rejected.", this);
	}

	[ContextMenu("Stop Attack")]
	public void StopAttack()
	{
		if (Application.isPlaying && Enemy != null) Enemy.StopAttack();
	}

	private void OnDisable()
	{
		if (Enemy != null) Enemy.StopAttack();
	}
}
