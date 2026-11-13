using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(TankMotor), typeof(TankAim), typeof(TankGun))]
[RequireComponent(typeof(TankHealth))]
public sealed class TankAI : NetworkBehaviour
{
	public enum MoveState { Idle, Moving, Arrived, Failed }
	public enum MoveMode { Direct, NavMesh }
	public enum AttackState { Idle, Aiming, Reloading, Attacking, Blocked, TargetDown }

	[SerializeField] private TankMotor motor;
	[SerializeField] private TankAim aim;
	[SerializeField] private TankGun gun;
	[SerializeField] private TankHealth health;
	[SerializeField] private Rigidbody body;
	[SerializeField] private TankBallistics ballistics;
	[SerializeField] private TankPath path;

	[Header("Point movement")]
	[SerializeField, Min(0.05f)] private float stopRadius = 0.5f;
	[SerializeField, Min(0.1f)] private float slowDistance = 3f;
	[SerializeField, Range(5f, 90f)] private float turnAngle = 45f;
	[SerializeField, Min(0.1f)] private float turnGain = 2f;
	[SerializeField, Range(0.1f, 5f)] private float turnTolerance = 1.5f;
	[SerializeField, Min(0.01f)] private float stopSpeed = 0.05f;
	[SerializeField, Min(0.1f)] private float stopTurnSpeed = 0.5f;

	[SerializeField, Min(0.05f)] private float heightTolerance = 0.5f;
	[SerializeField, Min(0.5f)] private float groundTimeout = 5f;
	[SerializeField, Min(0.02f)] private float settleTime = 0.15f;
	[SerializeField, Min(0.01f)] private float stopNormalSpeed = 0.2f;

	[Header("Path movement")]
	[SerializeField] private MoveMode moveMode;
	[SerializeField, Min(0.05f)] private float cornerRadius = 0.25f;
	[SerializeField, Range(5f, 90f)] private float sharpAngle = 30f;
	[SerializeField, Min(0.1f)] private float cornerSpeed = 1.5f;
	[SerializeField, Range(1f, 15f)] private float alignAngle = 3f;

	[Header("Path recovery")]
	[SerializeField, Min(0.1f)] private float stallTimeout = 4f;
	[SerializeField, Min(0.01f)] private float progressDistance = 0.25f;
	[SerializeField, Min(0.1f)] private float turnGrace = 8f;
	[SerializeField, Min(0f)] private float retryDelay = 0.5f;
	[SerializeField, Range(0, 5)] private int retryLimit = 2;

	[Header("Target attack")]
	[SerializeField, Range(0.05f, 5f)] private float aimTolerance = 0.5f;
	[SerializeField, Min(0.02f)] private float shotCheckInterval = 0.1f;

	private float leftLever;
	private float rightLever;
	private Vector2 aimInput;
	private Transform attackTarget;
	private TankHealth targetHealth;
	private float shotCheckLeft;
	private bool alignPath;
	private int watchIndex = -1;
	private float bestDistance;
	private float bestAngle;
	private float stallSeconds;
	private float turnSeconds;
	private float turnLimit;
	private float retryLeft;
	private float airSeconds;
	private float stopSeconds;
	private bool stopping;
	private float stopRange;
	private Vector3 stopPos;
	private Vector3 stopForward;

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
	public MoveMode Movement => moveMode;
	public TankPath Path => path;
	public uint CommandId { get; private set; }
	public int RetryCount { get; private set; }
	public int RetryLimit => retryLimit;
	public bool IsRetrying { get; private set; }
	public float StallSeconds => stallSeconds;
	public string RetryReason { get; private set; } = "";
	public string MoveFailure { get; private set; } = "";
	public float StopRadius => stopRadius;
	public float LeftDriveInput => leftLever;
	public float RightDriveInput => rightLever;
	public string MoveBlockReason
	{
		get
		{
			if (!CanAct) return "AI is not an active host or is destroyed";
			if (body == null) return "Missing Rigidbody";
			if (body.isKinematic) return "Rigidbody is kinematic";
			if (!IsFinite(motor.MaxMoveSpeed) || motor.MaxMoveSpeed <= 0f
				|| !IsFinite(motor.MaxTurnSpeed) || motor.MaxTurnSpeed <= 0f) return "Invalid motor speed";
			if (!IsFinite(motor.MoveAcceleration) || motor.MoveAcceleration <= 0f
				|| !IsFinite(motor.TurnAcceleration) || motor.TurnAcceleration <= 0f) return "Invalid motor acceleration";
			if (!IsFinite(motor.MoveDeceleration) || motor.MoveDeceleration <= 0f
				|| !IsFinite(motor.TurnDeceleration) || motor.TurnDeceleration <= 0f) return "Invalid motor deceleration";
			return "";
		}
	}
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
		if (path == null) path = GetComponent<TankPath>();
	}

	private void OnEnable()
	{
		if (health != null) health.Died += StopActions;
	}

	public override void Spawned()
	{
		TickCount = 0;
		CommandId = 0;
		RetryCount = 0;
		leftLever = rightLever = 0f;
		aimInput = Vector2.zero;
		if (body != null) body.isKinematic = !IsHost;
		MoveStatus = MoveState.Idle;
		ClearPath();
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
		EnsureHostBody();

		if (MoveStatus == MoveState.Moving) UpdateMove();

		motor.SetLeverInputs(leftLever, rightLever);
		if (AttackStatus != AttackState.Idle) UpdateAttack();
		else aim.ApplyHostInput(aimInput.x, aimInput.y);
	}

	public bool SetDrive(float left, float right)
	{
		if (!CanAct || !IsFinite(left) || !IsFinite(right)) return false;

		CommandId++;
		ClearAttack();
		MoveStatus = MoveState.Idle;
		ClearPath();
		RetryCount = 0;
		leftLever = Mathf.Clamp(left, -1f, 1f);
		rightLever = Mathf.Clamp(right, -1f, 1f);
		return true;
	}

	// Invalid requests preserve the previous destination and command.
	public bool MoveTo(Vector3 point)
	{
		if (!CanAct || !IsFinite(point.x) || !IsFinite(point.y) || !IsFinite(point.z)) return false;
		EnsureHostBody();
		if (!CanMove()) return false;
		Vector3 offset = point - body.position;
		offset.y = 0f;
		if (!IsFinite(offset.sqrMagnitude)) return false;

		CommandId++;
		ClearAttack();
		ClearPath();
		RetryCount = 0;
		airSeconds = 0f;
		Destination = point;
		if (moveMode == MoveMode.NavMesh)
		{
			if (path == null || !path.isActiveAndEnabled)
			{
				FailMove("Path component is missing or disabled");
				return true;
			}
			if (!path.TryBuild(body.position, point, out string reason))
			{
				FailMove(reason);
				return true;
			}
		}
		MoveStatus = MoveState.Moving;
		return true;
	}

	public bool SetMoveMode(MoveMode mode)
	{
		if (!CanAct || mode != MoveMode.Direct && mode != MoveMode.NavMesh) return false;
		if (mode == moveMode) return true;

		StopMove();
		moveMode = mode;
		return true;
	}

	public void StopMove()
	{
		if (!IsHost) return;

		CommandId++;
		ClearMove();
	}

	private void ClearMove()
	{
		MoveStatus = MoveState.Idle;
		ClearPath();
		RetryCount = 0;
		leftLever = rightLever = 0f;
		if (motor != null) motor.Stop();
	}

	private void ClearPath()
	{
		stopping = false;
		stopSeconds = 0f;
		if (path != null) path.Clear();
		alignPath = false;
		IsRetrying = false;
		retryLeft = 0f;
		RetryReason = "";
		MoveFailure = "";
		ResetProgress();
	}

	private void FailMove(string reason)
	{
		float elapsed = stallSeconds;
		ClearPath();
		stallSeconds = elapsed;
		MoveStatus = MoveState.Failed;
		MoveFailure = reason;
		leftLever = rightLever = 0f;
		if (motor != null) motor.Stop();
	}

	private bool CanMove()
	{
		return MoveBlockReason == "";
	}

	private void EnsureHostBody()
	{
		if (IsHost && body != null && body.isKinematic) body.isKinematic = false;
	}

	private void UpdateMove()
	{
		if (!IsHost) return;
		if (!CanMove())
		{
			FailMove(MoveBlockReason);
			return;
		}

		// Keep the command while airborne, but never consume corners or arrive there.
		if (motor.Ground == null || !motor.Ground.isActiveAndEnabled)
		{
			FailMove("Ground probe is unavailable");
			return;
		}
		if (!motor.Ground.IsGrounded)
		{
			leftLever = rightLever = 0f;
			ResetProgress();
			stopping = false;
			stopSeconds = 0f;
			airSeconds += Runner.DeltaTime;
			if (airSeconds >= groundTimeout) FailMove("Ground contact was not restored");
			return;
		}
		airSeconds = 0f;

		Vector3 velocity = body.linearVelocity;
		float speed = new Vector2(velocity.x, velocity.z).magnitude;
		bool usePath = moveMode == MoveMode.NavMesh;
		if (usePath && !UpdateRecovery()) return;
		if (usePath && !UpdatePath(speed)) return;

		Vector3 point = usePath ? path.Point : Destination;
		Vector3 offset = point - body.position;
		offset.y = 0f;
		float distance = offset.magnitude;
		bool finalPoint = !usePath || path.IsLast;
		float arrivalRadius = stopping ? stopRange : stopRadius;
		if (finalPoint && distance <= arrivalRadius && IsNearHeight(point))
		{
			// Keep braking across tiny boundary changes instead of alternating drive and turn.
			if (!stopping)
			{
				stopPos = body.position;
				stopForward = body.rotation * Vector3.forward;
				float brakeSpeed = Mathf.Max(speed, Mathf.Abs(motor.AppliedForwardSpeed));
				stopRange = stopRadius + brakeSpeed * brakeSpeed / (2f * motor.MoveDeceleration)
					+ brakeSpeed * Runner.DeltaTime * 2f + 0.1f;
			}
			stopping = true;
			leftLever = rightLever = 0f;
			Vector3 normal = motor.Ground.GroundNormal;
			Vector3 drift = body.position - stopPos;
			float yaw = Mathf.Abs(Vector3.SignedAngle(Vector3.ProjectOnPlane(stopForward, normal),
				Vector3.ProjectOnPlane(body.rotation * Vector3.forward, normal), normal));
			// Judge a quiet pose over a short window, rather than noisy single-tick contact velocities.
			if (Mathf.Abs(motor.AppliedForwardSpeed) <= stopSpeed
				&& Mathf.Abs(motor.AppliedTurnSpeed) <= stopTurnSpeed
				&& drift.magnitude <= stopSpeed * settleTime && yaw <= stopTurnSpeed * settleTime
				&& Mathf.Abs(Vector3.Dot(velocity, normal)) <= stopNormalSpeed)
				stopSeconds += Runner.DeltaTime;
			else
			{
				stopSeconds = 0f;
				stopPos = body.position;
				stopForward = body.rotation * Vector3.forward;
			}

			if (stopSeconds >= settleTime)
			{
				motor.Stop();
				MoveStatus = MoveState.Arrived;
				ResetProgress();
			}
			return;
		}

		stopping = false;
		stopSeconds = 0f;

		Vector3 forward = body.rotation * Vector3.forward;
		forward.y = 0f;
		float angle = Vector3.SignedAngle(forward, offset, Vector3.up);
		float absAngle = Mathf.Abs(angle);
		if (alignPath && absAngle <= alignAngle) alignPath = false;
		float turn = 0f;
		if (absAngle > turnTolerance)
		{
			float turnSpeed = Mathf.Min(absAngle * turnGain,
				Mathf.Sqrt(2f * motor.TurnDeceleration * absAngle));
			turn = Mathf.Sign(angle) * Mathf.Clamp01(turnSpeed / motor.MaxTurnSpeed);
		}

		// Leave half the arrival radius and one tick of travel as a braking margin.
		float radius = finalPoint ? stopRadius : cornerRadius;
		float exitSpeed = finalPoint || path.CornerAngle >= sharpAngle ? 0f : cornerSpeed;
		float brakeDistance = Mathf.Max(0f, distance - radius * 0.5f - speed * Runner.DeltaTime);
		float moveSpeed = Mathf.Min(motor.MaxMoveSpeed,
			Mathf.Sqrt(exitSpeed * exitSpeed + 2f * motor.MoveDeceleration * brakeDistance));
		if (finalPoint) moveSpeed = Mathf.Min(moveSpeed, motor.MaxMoveSpeed * Mathf.Clamp01(distance / slowDistance));
		if (alignPath) moveSpeed = 0f;
		float drive = moveSpeed / motor.MaxMoveSpeed * Mathf.Clamp01(1f - absAngle / turnAngle);

		float left = drive + turn;
		float right = drive - turn;
		float scale = Mathf.Max(1f, Mathf.Max(Mathf.Abs(left), Mathf.Abs(right)));
		leftLever = left / scale;
		rightLever = right / scale;
	}

	// False pauses at a corner or terminates an unavailable route.
	private bool UpdatePath(float speed)
	{
		if (path == null || !path.isActiveAndEnabled || !path.HasPath)
		{
			FailMove("Active path is unavailable");
			return false;
		}

		Vector3 offset = path.Point - body.position;
		offset.y = 0f;
		if (path.IsLast || offset.sqrMagnitude > cornerRadius * cornerRadius || !IsNearHeight(path.Point)) return true;

		bool sharp = path.CornerAngle >= sharpAngle;
		if (sharp && speed > stopSpeed)
		{
			leftLever = rightLever = 0f;
			return false;
		}

		if (sharp) motor.Stop();
		path.Advance();
		alignPath = sharp;
		return true;
	}

	private bool IsNearHeight(Vector3 point)
	{
		TankGroundProbe ground = motor.Ground;
		Vector3 foot = ground.transform.TransformPoint(ground.Center);
		return Mathf.Abs(Vector3.Dot(point - foot, ground.GroundNormal)) <= heightTolerance;
	}

	private void ResetProgress()
	{
		watchIndex = -1;
		stallSeconds = 0f;
		turnSeconds = 0f;
		turnLimit = 0f;
	}

	private bool UpdateRecovery()
	{
		if (path == null || !path.isActiveAndEnabled)
		{
			FailMove("Path component is missing or disabled");
			return false;
		}
		if (!IsFinite(Runner.DeltaTime) || Runner.DeltaTime <= 0f)
		{
			FailMove("Invalid movement timestep");
			return false;
		}
		if (IsRetrying && !UpdateRetry()) return false;
		if (!path.HasPath)
		{
			FailMove("Active path is unavailable");
			return false;
		}

		return WatchProgress();
	}

	private bool WatchProgress()
	{
		Vector3 offset = path.Point - body.position;
		offset.y = 0f;
		float distance = offset.magnitude;
		Vector3 forward = body.rotation * Vector3.forward;
		forward.y = 0f;
		float angle = Vector3.Angle(forward, offset);
		if (watchIndex != path.CornerIndex)
		{
			ResetProgress();
			watchIndex = path.CornerIndex;
			bestDistance = distance;
			bestAngle = angle;
			return true;
		}

		float required = Mathf.Min(progressDistance, motor.MaxMoveSpeed * stallTimeout * 0.25f);
		if (bestDistance - distance >= required)
		{
			bestDistance = distance;
			bestAngle = angle;
			stallSeconds = turnSeconds = 0f;
			return true;
		}

		float radius = path.IsLast ? stopRadius : cornerRadius;
		bool turning = distance > radius && angle > alignAngle;
		if (turning)
		{
			if (turnSeconds == 0f)
			{
				bestAngle = angle;
				float expected = 2f * angle / motor.MaxTurnSpeed + 2f * motor.MaxTurnSpeed / motor.TurnAcceleration;
				turnLimit = IsFinite(expected) ? Mathf.Max(turnGrace, expected) : turnGrace;
			}
			turnSeconds += Runner.DeltaTime;
			float turnProgress = Mathf.Min(1f, motor.MaxTurnSpeed * stallTimeout * 0.25f);
			if (bestAngle - angle >= turnProgress && turnSeconds < turnLimit)
			{
				bestAngle = angle;
				stallSeconds = 0f;
				return true;
			}
		}

		stallSeconds += Runner.DeltaTime;
		if (stallSeconds < stallTimeout && (!turning || turnSeconds < turnLimit)) return true;

		BeginRetry(turning && turnSeconds >= turnLimit ? "Turning made no travel progress" : "Movement made no progress");
		return false;
	}

	private void BeginRetry(string reason)
	{
		if (RetryCount >= retryLimit)
		{
			FailMove("Retry limit reached: " + reason);
			return;
		}

		RetryCount++;
		RetryReason = reason;
		IsRetrying = true;
		retryLeft = retryDelay;
		path.Clear();
		alignPath = false;
		leftLever = rightLever = 0f;
		motor.Stop();
		ResetProgress();
	}

	private bool UpdateRetry()
	{
		leftLever = rightLever = 0f;
		retryLeft = Mathf.Max(0f, retryLeft - Runner.DeltaTime);
		if (retryLeft > 0f) return false;

		IsRetrying = false;
		if (!path.TryBuild(body.position, Destination, out string reason))
		{
			BeginRetry(reason);
			return false;
		}

		ResetProgress();
		return true;
	}

	public bool SetAim(float turret, float barrel)
	{
		if (!CanAct || !IsFinite(turret) || !IsFinite(barrel)) return false;

		CommandId++;
		ClearAttack();
		aimInput = new Vector2(Mathf.Clamp(turret, -1f, 1f), Mathf.Clamp(barrel, -1f, 1f));
		return true;
	}

	public bool RequestWeapon(TankInputButton action)
	{
		if (!CanAct) return false;
		if (action != TankInputButton.Fire && action != TankInputButton.Insert && action != TankInputButton.Close
			&& action != TankInputButton.OpenEmpty && action != TankInputButton.Open) return false;

		CommandId++;
		ClearAttack();
		return gun.RequestAI(action);
	}

	// The point must belong to a damageable target. Invalid requests leave existing commands intact.
	public bool Attack(Transform point)
	{
		if (!CanAct || point == null || !point.gameObject.activeInHierarchy || point.IsChildOf(transform)
			|| ballistics == null || !ballistics.isActiveAndEnabled || gun.Muzzle == null || gun.ProjectilePrefab == null) return false;
		TankHealth victim = point.GetComponentInParent<TankHealth>();
		if (victim == null || victim == health || !victim.isActiveAndEnabled || victim.Destroyed) return false;

		CommandId++;
		ClearAttack();
		ClearMove();
		attackTarget = point;
		targetHealth = victim;
		AttackStatus = AttackState.Aiming;
		return true;
	}

	public void StopAttack()
	{
		if (!IsHost) return;

		CommandId++;
		ClearAttack();
	}

	private void ClearAttack()
	{
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
			ClearAttack();
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

		CommandId++;
		ClearMove();
		ClearAttack();
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
		stopNormalSpeed = IsFinite(stopNormalSpeed) ? Mathf.Max(0.01f, stopNormalSpeed) : 0.2f;
		settleTime = IsFinite(settleTime) ? Mathf.Max(0.02f, settleTime) : 0.15f;
		heightTolerance = IsFinite(heightTolerance) ? Mathf.Max(0.05f, heightTolerance) : 0.5f;
		groundTimeout = IsFinite(groundTimeout) ? Mathf.Max(0.5f, groundTimeout) : 5f;
		stopRadius = IsFinite(stopRadius) ? Mathf.Max(0.05f, stopRadius) : 0.5f;
		slowDistance = IsFinite(slowDistance) ? Mathf.Max(stopRadius, slowDistance) : 3f;
		turnAngle = IsFinite(turnAngle) ? Mathf.Clamp(turnAngle, 5f, 90f) : 45f;
		turnGain = IsFinite(turnGain) ? Mathf.Max(0.1f, turnGain) : 2f;
		turnTolerance = IsFinite(turnTolerance) ? Mathf.Clamp(turnTolerance, 0.1f, 5f) : 1.5f;
		stopSpeed = IsFinite(stopSpeed) ? Mathf.Max(0.01f, stopSpeed) : 0.05f;
		stopTurnSpeed = IsFinite(stopTurnSpeed) ? Mathf.Max(0.1f, stopTurnSpeed) : 0.5f;
		cornerRadius = IsFinite(cornerRadius) ? Mathf.Max(0.05f, cornerRadius) : 0.25f;
		sharpAngle = IsFinite(sharpAngle) ? Mathf.Clamp(sharpAngle, 5f, 90f) : 30f;
		cornerSpeed = IsFinite(cornerSpeed) ? Mathf.Max(0.1f, cornerSpeed) : 1.5f;
		alignAngle = IsFinite(alignAngle) ? Mathf.Clamp(alignAngle, 1f, 15f) : 3f;
		stallTimeout = IsFinite(stallTimeout) ? Mathf.Max(0.1f, stallTimeout) : 4f;
		progressDistance = IsFinite(progressDistance) ? Mathf.Max(0.01f, progressDistance) : 0.25f;
		turnGrace = IsFinite(turnGrace) ? Mathf.Max(0.1f, turnGrace) : 8f;
		retryDelay = IsFinite(retryDelay) ? Mathf.Max(0f, retryDelay) : 0.5f;
		retryLimit = Mathf.Clamp(retryLimit, 0, 5);
		aimTolerance = IsFinite(aimTolerance) ? Mathf.Clamp(aimTolerance, 0.05f, 5f) : 0.5f;
		shotCheckInterval = IsFinite(shotCheckInterval) ? Mathf.Max(0.02f, shotCheckInterval) : 0.1f;
	}
}
