using System;
using System.Collections.Generic;
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

	[Header("Path test")]
	[SerializeField] private bool usePath;
	[SerializeField] private Transform testCases;
	[SerializeField, Range(0, 4)] private int testIndex = 1;
	[SerializeField] private Camera testCamera;
	[SerializeField] private GameObject stallBlocker;

	private TankAI.AttackState lastState;
	private string lastReason;
	private bool attackOnArrival;
	private uint attackCommand;
	private bool deathChecked;
	private bool started;
	private bool starting;
	private TankAI.MoveState lastMove;
	private readonly List<NetworkObject> cleanupObjects = new List<NetworkObject>(16);
	public TankAI Enemy { get; private set; }
	public TankHealth EnemyHealth { get; private set; }
	public TankHealth TargetHealth { get; private set; }
	public bool HasMovePoint => movePoint != null;
	public bool HasPathTests => usePath && testCases != null;
	public bool CanRunTests => isActiveAndEnabled && Application.isPlaying && runner != null && runner.IsRunning && runner.IsServer;
	public bool HasBlocker => stallBlocker != null;
	public bool BlockerEnabled => stallBlocker != null && stallBlocker.activeSelf;
	public string TestName { get; private set; } = "Legacy integration";
	public string DestructionCheck { get; private set; } = "Not run";

	private void Start()
	{
		started = true;
		StartTests();
	}

	private void OnEnable()
	{
		if (started) StartTests();
	}

	private async void StartTests()
	{
		if (starting || !isActiveAndEnabled) return;
		if (runner == null || sceneManager == null || enemyPrefab == null || spawnPoint == null || targetPoint == null)
		{
			Debug.LogError("[AI ATTACK] Demo references are missing.", this);
			return;
		}
		if (usePath && (testCases == null || testCases.childCount != 5 || testCamera == null))
		{
			Debug.LogError("[AI PATH] Five authored test cases and the observation camera are required.", this);
			return;
		}
		starting = true;
		try
		{
			if (!runner.IsRunning)
			{
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
			}
			if (this == null || !isActiveAndEnabled) return;

			TargetHealth = targetPoint.GetComponentInParent<TankHealth>();
			if (usePath)
			{
				RunPathTest(testIndex);
				return;
			}
			if (!SpawnEnemy(spawnPoint)) return;
			if (startWithMove) MoveThenAttack();
			else AttackTarget();
		}
		finally { starting = false; }
	}

	private void Update()
	{
		if (Enemy == null) return;
		if (attackOnArrival)
		{
			if (!Enemy.CanAct || Enemy.CommandId != attackCommand
				|| Enemy.MoveStatus == TankAI.MoveState.Idle || Enemy.MoveStatus == TankAI.MoveState.Failed) attackOnArrival = false;
			else if (Enemy.MoveStatus == TankAI.MoveState.Arrived)
			{
				Debug.Log("[AI INTEGRATION] Arrived and stopped; starting target attack.", this);
				AttackTarget();
			}
		}
		if (Enemy.MoveStatus != lastMove)
		{
			lastMove = Enemy.MoveStatus;
			Debug.Log("[AI MOVE] " + lastMove + (string.IsNullOrEmpty(Enemy.MoveFailure) ? "" : ": " + Enemy.MoveFailure), this);
		}
		if (!deathChecked && EnemyHealth != null && EnemyHealth.Destroyed) CheckDestroyed();
		if (Enemy.AttackStatus == lastState && Enemy.AttackBlockReason == lastReason) return;
		lastState = Enemy.AttackStatus;
		lastReason = Enemy.AttackBlockReason;
		Debug.Log("[AI ATTACK] " + lastState + (string.IsNullOrEmpty(lastReason) ? "" : ": " + lastReason), this);
	}

	public bool RunPathTest(int index)
	{
		if (!CanRunTests || !HasPathTests) return false;
		if (index < 0 || index >= testCases.childCount) return false;

		Transform test = testCases.GetChild(index);
		Transform start = test.Find("Start");
		Transform goal = test.Find("Goal");
		if (start == null || goal == null)
		{
			Debug.LogError("[AI PATH] Test case Start or Goal is missing.", this);
			return false;
		}

		attackOnArrival = false;
		SetBlocker(false);
		testIndex = index;
		TestName = test.name;
		if (!SpawnEnemy(start)) return false;
		bool accepted = Enemy.SetMoveMode(TankAI.MoveMode.NavMesh) && Enemy.MoveTo(goal.position);
		if (!accepted)
			Debug.LogError("[AI PATH] Movement request rejected.", this);

		if (testCamera == null) return accepted;
		Vector3 center = (start.position + goal.position) * 0.5f;
		testCamera.transform.SetPositionAndRotation(new Vector3(center.x, 55f, center.z), Quaternion.Euler(90f, 0f, 0f));
		testCamera.orthographic = true;
		testCamera.orthographicSize = 27f;
		testCamera.depth = 2f;
		testCamera.enabled = true;
		return accepted;
	}

	public void RunIntegrationTest()
	{
		if (!Application.isPlaying || TargetHealth == null || !TargetHealth.isActiveAndEnabled || TargetHealth.Destroyed)
		{
			Debug.LogWarning("[AI INTEGRATION] A live target is required. Restart Play Mode after the target is destroyed.", this);
			return;
		}
		if (!RunPathTest(0)) return;

		attackOnArrival = Enemy.MoveStatus != TankAI.MoveState.Failed;
		attackCommand = Enemy.CommandId;
	}

	public void RunStallTest()
	{
		if (!HasBlocker || !RunPathTest(4)) return;
		SetBlocker(true);
	}

	public void SetBlocker(bool active)
	{
		if (!Application.isPlaying || stallBlocker == null) return;

		stallBlocker.SetActive(active);
	}

	public void MoveToGoal() => MoveToMarker("Goal");
	public void ReturnToStart() => MoveToMarker("Start");

	private void MoveToMarker(string name)
	{
		if (!Application.isPlaying || Enemy == null || !HasPathTests) return;
		if (testIndex < 0 || testIndex >= testCases.childCount) return;

		Transform point = testCases.GetChild(testIndex).Find(name);
		if (point == null) return;

		attackOnArrival = false;
		if (!Enemy.MoveTo(point.position)) Debug.LogWarning("[AI PATH] Movement request rejected.", this);
	}

	private bool SpawnEnemy(Transform start)
	{
		if (Enemy != null)
		{
			Enemy.StopActions();
			ClearProjectiles();
			if (Enemy.Object != null && Enemy.Object.IsValid) runner.Despawn(Enemy.Object);
		}

		Vector3 pos = start.position;
		if (usePath) pos.y += 0.15f;
		Enemy = null;
		EnemyHealth = null;
		NetworkObject entity;
		try
		{
			entity = runner.Spawn(enemyPrefab, pos, start.rotation);
		}
		catch (Exception error)
		{
			Debug.LogError("[AI PATH] Enemy spawn failed: " + error.Message, this);
			return false;
		}
		if (entity == null)
		{
			Debug.LogError("[AI PATH] Enemy spawn failed.", this);
			return false;
		}
		Enemy = entity.GetComponent<TankAI>();
		EnemyHealth = entity.GetComponent<TankHealth>();
		if (Enemy == null)
		{
			if (entity.IsValid && entity.Runner == runner && entity.HasStateAuthority) runner.Despawn(entity);
			EnemyHealth = null;
			Debug.LogError("[AI PATH] The spawned enemy has no TankAI.", this);
			return false;
		}
		deathChecked = false;
		DestructionCheck = "Not run";
		lastMove = TankAI.MoveState.Idle;
		return true;
	}

	private void ClearProjectiles()
	{
		TankGun weapon = Enemy.GetComponent<TankGun>();
		if (weapon == null) return;

		runner.GetAllNetworkObjects(cleanupObjects);
		for (int i = 0; i < cleanupObjects.Count; i++)
		{
			NetworkObject obj = cleanupObjects[i];
			if (obj == null || !obj.IsValid || obj.Runner != runner || !obj.HasStateAuthority) continue;

			TankProjectile projectile = obj.GetComponent<TankProjectile>();
			if (projectile != null && projectile.SourceGun == weapon) projectile.Remove();
		}
		cleanupObjects.Clear();
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
		Transform goal = HasPathTests ? testCases.GetChild(testIndex).Find("Goal") : movePoint;
		if (goal == null) return;
		attackOnArrival = Enemy.MoveTo(goal.position) && Enemy.MoveStatus != TankAI.MoveState.Failed;
		attackCommand = Enemy.CommandId;
		if (attackOnArrival) Debug.Log("[AI INTEGRATION] Moving; target attack follows arrival.", this);
		else Debug.LogWarning("[AI INTEGRATION] Movement did not start: " + Enemy.MoveFailure, this);
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
		if (Enemy != null && runner != null && runner.IsRunning && runner.IsServer) ClearProjectiles();
		SetBlocker(false);
	}
}
