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
	[SerializeField] private bool startWithMove;
	[SerializeField] private Transform movePoint;

	private TankAI.AttackState lastState;
	private string lastReason;
	private bool attackOnArrival;
	private bool deathChecked;
	public TankAI Enemy { get; private set; }
	public TankHealth EnemyHealth { get; private set; }
	public TankHealth TargetHealth { get; private set; }
	public bool HasMovePoint => movePoint != null;
	public string DestructionCheck { get; private set; } = "Not run";

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
		EnemyHealth = entity.GetComponent<TankHealth>();
		TargetHealth = targetPoint.GetComponentInParent<TankHealth>();
		if (startWithMove) MoveThenAttack();
		else AttackTarget();
	}

	private void Update()
	{
		if (Enemy == null) return;
		if (attackOnArrival)
		{
			if (!Enemy.CanAct || Enemy.MoveStatus == TankAI.MoveState.Idle) attackOnArrival = false;
			else if (Enemy.MoveStatus == TankAI.MoveState.Arrived)
			{
				Debug.Log("[AI INTEGRATION] Arrived and stopped; starting target attack.", this);
				AttackTarget();
			}
		}
		if (!deathChecked && EnemyHealth != null && EnemyHealth.Destroyed) CheckDestroyed();
		if (Enemy.AttackStatus == lastState && Enemy.AttackBlockReason == lastReason) return;
		lastState = Enemy.AttackStatus;
		lastReason = Enemy.AttackBlockReason;
		Debug.Log("[AI ATTACK] " + lastState + (string.IsNullOrEmpty(lastReason) ? "" : ": " + lastReason), this);
	}

	[ContextMenu("Attack Target")]
	public void AttackTarget()
	{
		if (!Application.isPlaying || Enemy == null) return;
		attackOnArrival = false;
		if (!Enemy.Attack(targetPoint)) Debug.LogWarning("[AI ATTACK] Target command rejected.", this);
	}

	[ContextMenu("Stop Attack")]
	public void StopAttack()
	{
		attackOnArrival = false;
		if (Application.isPlaying && Enemy != null) Enemy.StopAttack();
	}

	[ContextMenu("Move Then Attack")]
	public void MoveThenAttack()
	{
		if (!Application.isPlaying || Enemy == null || movePoint == null) return;
		attackOnArrival = Enemy.MoveTo(movePoint.position);
		if (attackOnArrival) Debug.Log("[AI INTEGRATION] Moving; target attack follows arrival.", this);
	}

	[ContextMenu("Stop All Actions")]
	public void StopAllActions()
	{
		attackOnArrival = false;
		if (Application.isPlaying && Enemy != null) Enemy.StopActions();
	}

	// Explicit test damage uses the production HP entry; it does not simulate a projectile hit.
	[ContextMenu("Test Enemy Damage 25")]
	public void DamageEnemy() => ApplyTestDamage(25f);

	[ContextMenu("Test Destroy Enemy")]
	public void DestroyEnemy()
	{
		if (EnemyHealth != null) ApplyTestDamage(EnemyHealth.CurrentHP);
	}

	private void ApplyTestDamage(float amount)
	{
		if (!Application.isPlaying || Enemy == null || !Enemy.IsHost || EnemyHealth == null || EnemyHealth.Destroyed) return;
		Debug.Log("[AI CHECK] Applying direct test damage to the enemy; no projectile involved.", this);
		EnemyHealth.ApplyDamage(amount, 0, out _, out _, out _);
		if (EnemyHealth.Destroyed) CheckDestroyed();
	}

	[ContextMenu("Check Destroyed AI")]
	public void CheckDestroyed()
	{
		if (!Application.isPlaying || Enemy == null || !Enemy.IsHost || EnemyHealth == null || !EnemyHealth.Destroyed) return;
		deathChecked = true;
		attackOnArrival = false;
		bool accepted = Enemy.SetDrive(1f, 1f);
		accepted |= Enemy.SetAim(1f, 1f);
		accepted |= Enemy.MoveTo(movePoint != null ? movePoint.position : transform.position);
		accepted |= Enemy.Attack(targetPoint);
		accepted |= Enemy.RequestWeapon(TankInputButton.Fire);
		Rigidbody body = Enemy.GetComponent<Rigidbody>();
		TankAim aim = Enemy.GetComponent<TankAim>();
		TankGun gun = Enemy.GetComponent<TankGun>();
		bool stopped = !Enemy.CanAct && !accepted && !gun.CanFire && Enemy.MoveStatus == TankAI.MoveState.Idle
			&& Enemy.AttackStatus == TankAI.AttackState.Idle && EnemyHealth.DestructionCount == 1
			&& aim.TurretInput == 0f && aim.GunInput == 0f && body != null
			&& new Vector2(body.linearVelocity.x, body.linearVelocity.z).sqrMagnitude < 0.0001f
			&& body.angularVelocity.sqrMagnitude < 0.0001f;
		DestructionCheck = stopped ? "PASS: stopped; new commands rejected" : "FAIL: inspect AI/body/weapon state";
		if (stopped) Debug.Log("[AI CHECK] " + DestructionCheck, this);
		else Debug.LogError("[AI CHECK] " + DestructionCheck, this);
	}

	private void OnDisable()
	{
		StopAllActions();
	}
}
