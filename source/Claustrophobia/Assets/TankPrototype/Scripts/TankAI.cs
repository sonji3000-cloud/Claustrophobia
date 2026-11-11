using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(TankMotor), typeof(TankAim), typeof(TankGun))]
[RequireComponent(typeof(TankHealth))]
public sealed class TankAI : NetworkBehaviour
{
	public enum MoveState { Idle, Moving, Arrived }
	public enum AttackState { Idle, Aiming, Reloading, Attacking, Blocked, TargetDown }

	[SerializeField] private TankMotor motor;
	[SerializeField] private TankAim aim;
	[SerializeField] private TankGun gun;
	[SerializeField] private TankHealth health;
	[SerializeField] private Rigidbody body;
	[SerializeField] private TankBallistics ballistics;

	[Header("Point movement")]
	[SerializeField, Min(0.05f)] private float stopRadius = 0.5f;
	[SerializeField, Min(0.1f)] private float slowDistance = 3f;
	[SerializeField, Range(5f, 90f)] private float turnAngle = 45f;
	[SerializeField, Min(0.1f)] private float turnGain = 2f;
	[SerializeField, Range(0.1f, 5f)] private float turnTolerance = 1.5f;
	[SerializeField, Min(0.01f)] private float stopSpeed = 0.05f;
	[SerializeField, Min(0.1f)] private float stopTurnSpeed = 0.5f;

	[Header("Target attack")]
	[SerializeField, Range(0.05f, 5f)] private float aimTolerance = 0.5f;
	[SerializeField, Min(0.02f)] private float shotCheckInterval = 0.1f;

	private float leftLever;
	private float rightLever;
	private Vector2 aimInput;
	private Transform attackTarget;
	private TankHealth targetHealth;
	private float shotCheckLeft;

	public bool IsHost => Object != null && Object.IsValid
		&& Runner != null && Runner.IsRunning && Runner.IsServer && HasStateAuthority;
	public bool CanAct => IsHost && isActiveAndEnabled
		&& motor != null && motor.isActiveAndEnabled
		&& aim != null && aim.isActiveAndEnabled
		&& gun != null && gun.isActiveAndEnabled
		&& health != null && health.isActiveAndEnabled && !health.Destroyed;
	public int TickCount { get; private set; }
	public MoveState MoveStatus { get; private set; }
	public Vector3 Destination { get; private set; }
	public float StopRadius => stopRadius;
	public Transform Target => attackTarget;
	public TankHealth TargetHealth => targetHealth;
	public AttackState AttackStatus { get; private set; }
	public string AttackBlockReason { get; private set; } = "";
	internal bool HasLiveTarget => IsHost && attackTarget != null && attackTarget.gameObject.activeInHierarchy
		&& targetHealth != null && attackTarget.IsChildOf(targetHealth.transform)
		&& targetHealth.isActiveAndEnabled && !targetHealth.Destroyed;

	private void Awake()
	{
		if (motor == null) motor = GetComponent<TankMotor>();
		if (aim == null) aim = GetComponent<TankAim>();
		if (gun == null) gun = GetComponent<TankGun>();
		if (health == null) health = GetComponent<TankHealth>();
		if (body == null) body = GetComponent<Rigidbody>();
		if (ballistics == null) ballistics = GetComponent<TankBallistics>();
	}

	private void OnEnable()
	{
		if (health != null) health.Died += StopActions;
	}

	public override void Spawned()
	{
		TickCount = 0;
		leftLever = rightLever = 0f;
		aimInput = Vector2.zero;
		if (body != null) body.isKinematic = !IsHost;
		MoveStatus = MoveState.Idle;
		Destination = body != null ? body.position : transform.position;

		if (IsHost) StopActions();
	}

	public override void FixedUpdateNetwork()
	{
		if (!IsHost) return;

		TickCount++;
		if (!CanAct)
		{
			StopActions();
			return;
		}

		if (MoveStatus == MoveState.Moving) UpdateMove();

		motor.SetLeverInputs(leftLever, rightLever);
		if (AttackStatus != AttackState.Idle) UpdateAttack();
		else aim.ApplyHostInput(aimInput.x, aimInput.y);
	}

	public bool SetDrive(float left, float right)
	{
		if (!CanAct || !IsFinite(left) || !IsFinite(right)) return false;

		StopAttack();
		MoveStatus = MoveState.Idle;
		leftLever = Mathf.Clamp(left, -1f, 1f);
		rightLever = Mathf.Clamp(right, -1f, 1f);
		return true;
	}

	// Invalid requests preserve the previous destination and command.
	public bool MoveTo(Vector3 point)
	{
		if (!CanAct || !CanMove() || !IsFinite(point.x) || !IsFinite(point.y) || !IsFinite(point.z)) return false;
		Vector3 offset = point - body.position;
		offset.y = 0f;
		if (!IsFinite(offset.sqrMagnitude)) return false;

		StopAttack();
		Destination = point;
		MoveStatus = MoveState.Moving;
		return true;
	}

	public void StopMove()
	{
		if (!IsHost) return;

		MoveStatus = MoveState.Idle;
		leftLever = rightLever = 0f;
		if (motor != null) motor.Stop();
	}

	private bool CanMove()
	{
		return body != null && !body.isKinematic
			&& IsFinite(motor.MaxMoveSpeed) && motor.MaxMoveSpeed > 0f
			&& IsFinite(motor.MaxTurnSpeed) && motor.MaxTurnSpeed > 0f
			&& IsFinite(motor.MoveAcceleration) && motor.MoveAcceleration > 0f
			&& IsFinite(motor.TurnAcceleration) && motor.TurnAcceleration > 0f
			&& IsFinite(motor.MoveDeceleration) && motor.MoveDeceleration > 0f
			&& IsFinite(motor.TurnDeceleration) && motor.TurnDeceleration > 0f;
	}

	private void UpdateMove()
	{
		if (!IsHost) return;
		if (!CanMove())
		{
			StopMove();
			return;
		}

		Vector3 offset = Destination - body.position;
		offset.y = 0f;
		float distance = offset.magnitude;
		Vector3 velocity = body.linearVelocity;
		float speed = new Vector2(velocity.x, velocity.z).magnitude;

		if (distance <= stopRadius)
		{
			leftLever = rightLever = 0f;
			if (speed <= stopSpeed && Mathf.Abs(body.angularVelocity.y) * Mathf.Rad2Deg <= stopTurnSpeed)
			{
				motor.Stop();
				MoveStatus = MoveState.Arrived;
			}
			return;
		}

		Vector3 forward = body.rotation * Vector3.forward;
		forward.y = 0f;
		float angle = Vector3.SignedAngle(forward, offset, Vector3.up);
		float absAngle = Mathf.Abs(angle);
		float turn = 0f;
		if (absAngle > turnTolerance)
		{
			float turnSpeed = Mathf.Min(absAngle * turnGain,
				Mathf.Sqrt(2f * motor.TurnDeceleration * absAngle));
			turn = Mathf.Sign(angle) * Mathf.Clamp01(turnSpeed / motor.MaxTurnSpeed);
		}

		// Leave half the arrival radius and one tick of travel as a braking margin.
		float brakeDistance = Mathf.Max(0f, distance - stopRadius * 0.5f - speed * Runner.DeltaTime);
		float moveSpeed = Mathf.Min(motor.MaxMoveSpeed * Mathf.Clamp01(distance / slowDistance),
			Mathf.Sqrt(2f * motor.MoveDeceleration * brakeDistance));
		float drive = moveSpeed / motor.MaxMoveSpeed * Mathf.Clamp01(1f - absAngle / turnAngle);

		float left = drive + turn;
		float right = drive - turn;
		float scale = Mathf.Max(1f, Mathf.Max(Mathf.Abs(left), Mathf.Abs(right)));
		leftLever = left / scale;
		rightLever = right / scale;
	}

	public bool SetAim(float turret, float barrel)
	{
		if (!CanAct || !IsFinite(turret) || !IsFinite(barrel)) return false;

		StopAttack();
		aimInput = new Vector2(Mathf.Clamp(turret, -1f, 1f), Mathf.Clamp(barrel, -1f, 1f));
		return true;
	}

	public bool RequestWeapon(TankInputButton action)
	{
		if (!CanAct) return false;
		if (action != TankInputButton.Fire && action != TankInputButton.Insert && action != TankInputButton.Close
			&& action != TankInputButton.OpenEmpty && action != TankInputButton.Open) return false;

		StopAttack();
		return gun.RequestAI(action);
	}

	// The point must belong to a damageable target. Invalid requests leave existing commands intact.
	public bool Attack(Transform point)
	{
		if (!CanAct || point == null || !point.gameObject.activeInHierarchy || point.IsChildOf(transform)
			|| ballistics == null || !ballistics.isActiveAndEnabled || gun.Muzzle == null || gun.ProjectilePrefab == null) return false;
		TankHealth victim = point.GetComponentInParent<TankHealth>();
		if (victim == null || victim == health || !victim.isActiveAndEnabled || victim.Destroyed) return false;

		StopAttack();
		StopMove();
		attackTarget = point;
		targetHealth = victim;
		AttackStatus = AttackState.Aiming;
		return true;
	}

	public void StopAttack()
	{
		if (!IsHost) return;

		attackTarget = null;
		targetHealth = null;
		aimInput = Vector2.zero;
		shotCheckLeft = 0f;
		AttackStatus = AttackState.Idle;
		AttackBlockReason = "";
		if (gun != null) gun.ClearAIRequest();
	}

	private void UpdateAttack()
	{
		if (!IsHost) return;
		if (attackTarget == null || !attackTarget.gameObject.activeInHierarchy || targetHealth == null
			|| !targetHealth.isActiveAndEnabled || !attackTarget.IsChildOf(targetHealth.transform))
		{
			StopAttack();
			AttackBlockReason = "Target unavailable";
			return;
		}
		if (targetHealth.Destroyed)
		{
			aimInput = Vector2.zero;
			gun.ClearAIRequest();
			AttackStatus = AttackState.TargetDown;
			AttackBlockReason = "";
			return;
		}

		shotCheckLeft = Mathf.Max(0f, shotCheckLeft - Runner.DeltaTime);
		if (ballistics == null || !ballistics.isActiveAndEnabled || gun.Muzzle == null || gun.ProjectilePrefab == null
			|| !IsFinite(aim.YawSpeed) || !IsFinite(aim.PitchSpeed)
			|| aim.YawSpeed <= 0f || aim.PitchSpeed <= 0f || !IsFinite(Runner.DeltaTime) || Runner.DeltaTime <= 0f)
		{
			BlockAttack("Aim or weapon unavailable");
			return;
		}
		if (!TryGetDirection(out Vector3 direction))
		{
			BlockAttack("Target outside ballistic range");
			return;
		}
		if (!aim.TryGetAim(direction, out Vector2 angles))
		{
			BlockAttack("Target outside gun elevation limits");
			return;
		}

		aimInput.x = Mathf.Clamp(Mathf.DeltaAngle(aim.CurrentYaw, angles.x) / (aim.YawSpeed * Runner.DeltaTime), -1f, 1f);
		aimInput.y = Mathf.Clamp((angles.y - aim.CurrentPitch) / (aim.PitchSpeed * Runner.DeltaTime), -1f, 1f);
		aim.ApplyHostInput(aimInput.x, aimInput.y);

		switch (gun.State)
		{
			case TankGun.ChamberState.EmptyClosed:
				QueueLoad(TankInputButton.OpenEmpty); return;
			case TankGun.ChamberState.EmptyOpen:
				QueueLoad(TankInputButton.Insert); return;
			case TankGun.ChamberState.LoadedOpen:
				QueueLoad(TankInputButton.Close); return;
			case TankGun.ChamberState.Recovering:
				AttackStatus = AttackState.Reloading;
				AttackBlockReason = "";
				return;
		}

		// Muzzle position changes with the pivots. Recheck the actual pose after applying aim.
		if (!TryGetDirection(out direction) || Vector3.Angle(gun.Muzzle.forward, direction) > aimTolerance)
		{
			AttackStatus = AttackState.Aiming;
			AttackBlockReason = "";
			return;
		}
		if (shotCheckLeft > 0f) return;
		shotCheckLeft = shotCheckInterval;
		if (!gun.CanFire)
		{
			BlockAttack(gun.FireBlockReason);
			return;
		}
		if (!gun.TryGetShotConditions(out TankShotConditions conditions, out string reason))
		{
			BlockAttack(reason);
			return;
		}

		TankPrediction prediction = ballistics.Predict(conditions, gun.OwnerColliders);
		if (!prediction.HasHit || prediction.Collider == null || prediction.Collider.GetComponentInParent<TankHealth>() != targetHealth)
		{
			BlockAttack(prediction.HasHit ? "Shot path obstructed" : "No target hit predicted");
			return;
		}
		AttackBlockReason = "";
		if (gun.RequestAI(TankInputButton.Fire, true)) AttackStatus = AttackState.Attacking;
	}

	private bool TryGetDirection(out Vector3 direction)
	{
		return TankAimSolver.TrySolve(attackTarget.position - gun.Muzzle.position, Physics.gravity,
			gun.ProjectileSpeed, Time.fixedDeltaTime, gun.ProjectilePrefab.Lifetime, out direction, out _);
	}

	private void QueueLoad(TankInputButton action)
	{
		AttackStatus = AttackState.Reloading;
		AttackBlockReason = "";
		gun.RequestAI(action, true);
	}

	private void BlockAttack(string reason)
	{
		aimInput = Vector2.zero;
		gun.ClearAIRequest();
		AttackStatus = AttackState.Blocked;
		AttackBlockReason = reason;
	}

	public void StopActions()
	{
		if (!IsHost) return;

		StopMove();
		StopAttack();
		if (aim != null) aim.ApplyHostInput(0f, 0f);
		if (gun != null) gun.ClearAIRequest();
	}

	private void OnDisable()
	{
		if (health != null) health.Died -= StopActions;
		if (IsHost) StopActions();
	}

	private static bool IsFinite(float value)
	{
		return !float.IsNaN(value) && !float.IsInfinity(value);
	}

	private void OnValidate()
	{
		stopRadius = IsFinite(stopRadius) ? Mathf.Max(0.05f, stopRadius) : 0.5f;
		slowDistance = IsFinite(slowDistance) ? Mathf.Max(stopRadius, slowDistance) : 3f;
		turnAngle = IsFinite(turnAngle) ? Mathf.Clamp(turnAngle, 5f, 90f) : 45f;
		turnGain = IsFinite(turnGain) ? Mathf.Max(0.1f, turnGain) : 2f;
		turnTolerance = IsFinite(turnTolerance) ? Mathf.Clamp(turnTolerance, 0.1f, 5f) : 1.5f;
		stopSpeed = IsFinite(stopSpeed) ? Mathf.Max(0.01f, stopSpeed) : 0.05f;
		stopTurnSpeed = IsFinite(stopTurnSpeed) ? Mathf.Max(0.1f, stopTurnSpeed) : 0.5f;
		aimTolerance = IsFinite(aimTolerance) ? Mathf.Clamp(aimTolerance, 0.05f, 5f) : 0.5f;
		shotCheckInterval = IsFinite(shotCheckInterval) ? Mathf.Max(0.02f, shotCheckInterval) : 0.1f;
	}
}
