using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(TankMotor), typeof(TankAim), typeof(TankGun))]
[RequireComponent(typeof(TankHealth))]
public sealed class TankAI : NetworkBehaviour
{
	public enum MoveState { Idle, Moving, Arrived }

	[SerializeField] private TankMotor motor;
	[SerializeField] private TankAim aim;
	[SerializeField] private TankGun gun;
	[SerializeField] private TankHealth health;
	[SerializeField] private Rigidbody body;

	[Header("Point movement")]
	[SerializeField, Min(0.05f)] private float stopRadius = 0.5f;
	[SerializeField, Min(0.1f)] private float slowDistance = 3f;
	[SerializeField, Range(5f, 90f)] private float turnAngle = 45f;
	[SerializeField, Min(0.1f)] private float turnGain = 2f;
	[SerializeField, Range(0.1f, 5f)] private float turnTolerance = 1.5f;
	[SerializeField, Min(0.01f)] private float stopSpeed = 0.05f;
	[SerializeField, Min(0.1f)] private float stopTurnSpeed = 0.5f;

	private float leftLever;
	private float rightLever;
	private Vector2 aimInput;

	public bool IsHost => Object != null && Object.IsValid
		&& Runner != null && Runner.IsRunning && Runner.IsServer && HasStateAuthority;
	public bool CanAct => IsHost && isActiveAndEnabled
		&& motor != null && motor.isActiveAndEnabled
		&& aim != null && aim.isActiveAndEnabled
		&& gun != null && gun.isActiveAndEnabled
		&& health != null && !health.Destroyed;
	public int TickCount { get; private set; }
	public MoveState MoveStatus { get; private set; }
	public Vector3 Destination { get; private set; }
	public float StopRadius => stopRadius;

	private void Awake()
	{
		if (motor == null) motor = GetComponent<TankMotor>();
		if (aim == null) aim = GetComponent<TankAim>();
		if (gun == null) gun = GetComponent<TankGun>();
		if (health == null) health = GetComponent<TankHealth>();
		if (body == null) body = GetComponent<Rigidbody>();
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
		aim.ApplyHostInput(aimInput.x, aimInput.y);
	}

	public bool SetDrive(float left, float right)
	{
		if (!CanAct || !IsFinite(left) || !IsFinite(right)) return false;

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

		aimInput = new Vector2(Mathf.Clamp(turret, -1f, 1f), Mathf.Clamp(barrel, -1f, 1f));
		return true;
	}

	public bool RequestWeapon(TankInputButton action)
	{
		if (!CanAct) return false;

		return gun.RequestAI(action);
	}

	public void StopActions()
	{
		if (!IsHost) return;

		StopMove();
		aimInput = Vector2.zero;
		if (aim != null) aim.ApplyHostInput(0f, 0f);
		if (gun != null) gun.ClearAIRequest();
	}

	private void OnDisable()
	{
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
	}
}
