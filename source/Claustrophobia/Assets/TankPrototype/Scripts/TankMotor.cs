using UnityEngine;
using Fusion;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(TankGroundProbe))]
public sealed class TankMotor : NetworkBehaviour
{
	[Header("Drive")]
	[Min(0f)] public float MaxMoveSpeed = 5f;
	[Min(0f)] public float MaxTurnSpeed = 60f;
	[Min(0f)] public float MoveAcceleration = 2f;
	[Min(0f)] public float MoveDeceleration = 4f;
	[Min(0f)] public float TurnAcceleration = 90f;
	[Min(0f)] public float TurnDeceleration = 180f;

	[Header("Ground movement")]
	[SerializeField] private TankGroundProbe ground;
	[SerializeField, Range(0f, 60f)] private float maxDriveSlope = 20f;
	[SerializeField, Min(0f)] private float tiltSpeed = 90f;
	[SerializeField, Min(0f)] private float tiltGain = 8f;

	public TankGroundProbe Ground => ground;
	public bool CanDrive => ground != null && ground.isActiveAndEnabled
		&& ground.IsGrounded && ground.SlopeAngle <= maxDriveSlope;

	private Rigidbody body;
	private NetworkObject networkObject;
	private TankCrewRegistry crewRegistry;
	private PlayerRef lastDriver;
	private float requestedLeft;
	private float requestedRight;
	private bool wasGrounded;

	// These describe the inputs and targets used by the last motor physics tick.
	public float LeftInput { get; private set; }
	public float RightInput { get; private set; }
	public float ForwardCommand { get; private set; }
	public float TurnCommand { get; private set; }
	public float TargetForwardSpeed { get; private set; }
	public float TargetTurnSpeed { get; private set; }
	public float AppliedForwardSpeed { get; private set; }
	public float AppliedTurnSpeed { get; private set; }
	public bool UsesCrewRegistry => crewRegistry != null;
	public int PhysicsTickCount { get; private set; }

	private void Awake()
	{
		body = GetComponent<Rigidbody>();
		networkObject = GetComponent<NetworkObject>();
		crewRegistry = GetComponent<TankCrewRegistry>();
		if (ground == null) ground = GetComponent<TankGroundProbe>();
	}

	public void SetLeverInputs(float left, float right)
	{
		requestedLeft = Mathf.Clamp(left, -1f, 1f);
		requestedRight = Mathf.Clamp(right, -1f, 1f);
	}

	private void FixedUpdate()
	{
		if (networkObject != null && (!networkObject.IsValid || !HasStateAuthority))
		{
			wasGrounded = false;
			return;
		}

		if (UsesCrewRegistry)
		{
			if (Object == null || !Object.IsValid || !HasStateAuthority) return;
			if (lastDriver != crewRegistry.DriverPlayer) SetLeverInputs(0f, 0f);
		}

		if (body.isKinematic)
		{
			wasGrounded = false;
			return;
		}

		PhysicsTickCount++;
		if (requestedLeft != 0f || requestedRight != 0f) body.WakeUp();

		LeftInput = requestedLeft;
		RightInput = requestedRight;
		ForwardCommand = (LeftInput + RightInput) * 0.5f;
		TurnCommand = (LeftInput - RightInput) * 0.5f;
		TargetForwardSpeed = ForwardCommand * MaxMoveSpeed;
		TargetTurnSpeed = TurnCommand * MaxTurnSpeed;

		if (!CanDrive)
		{
			AppliedForwardSpeed = AppliedTurnSpeed = 0f;
			wasGrounded = false;
			return;
		}

		DriveGround();
	}

	private void DriveGround()
	{
		Vector3 normal = ground.GroundNormal;
		Vector3 forward = Vector3.ProjectOnPlane(body.rotation * Vector3.forward, normal).normalized;
		if (forward.sqrMagnitude < 0.001f)
			forward = Vector3.Cross(body.rotation * Vector3.right, normal).normalized;

		Vector3 velocity = body.linearVelocity;
		float currentForward = Vector3.Dot(velocity, forward);
		float currentTurn = Vector3.Dot(body.angularVelocity, normal) * Mathf.Rad2Deg;

		// Contacts can absorb a small starting velocity every tick. Keep the drive's
		// acceleration while powered; neutral translation and landing use actual motion.
		float driveSpeed = wasGrounded && ForwardCommand != 0f ? AppliedForwardSpeed : currentForward;
		float turnSpeed = wasGrounded ? AppliedTurnSpeed : currentTurn;
		float nextForward = Approach(driveSpeed, TargetForwardSpeed,
			MoveAcceleration, MoveDeceleration, Time.fixedDeltaTime, 0.05f);
		float nextTurn = Approach(turnSpeed, TargetTurnSpeed,
			TurnAcceleration, TurnDeceleration, Time.fixedDeltaTime, 0.5f);
		AppliedForwardSpeed = nextForward;
		AppliedTurnSpeed = nextTurn;
		wasGrounded = true;

		// Keep normal motion for contact/launching; control only motion along the ground.
		body.linearVelocity = forward * nextForward + normal * Vector3.Dot(velocity, normal);
		if (body.useGravity)
			body.AddForce(-Vector3.ProjectOnPlane(Physics.gravity, normal), ForceMode.Acceleration);

		Vector3 angular = GetTiltVelocity(normal) + normal * (nextTurn * Mathf.Deg2Rad);
		float speed = angular.magnitude;
		body.angularVelocity = Vector3.zero;
		if (speed > 0.0001f)
			body.MoveRotation(Quaternion.AngleAxis(speed * Mathf.Rad2Deg * Time.fixedDeltaTime,
				angular / speed) * body.rotation);
	}

	private Vector3 GetTiltVelocity(Vector3 normal)
	{
		Quaternion tilt = Quaternion.FromToRotation(body.rotation * Vector3.up, normal);
		tilt.ToAngleAxis(out float angle, out Vector3 axis);
		if (angle > 180f) angle -= 360f;
		if (Mathf.Abs(angle) < 0.05f) return Vector3.zero;

		float speed = Mathf.Clamp(angle * tiltGain, -tiltSpeed, tiltSpeed);
		return axis * (speed * Mathf.Deg2Rad);
	}

	public void Stop()
	{
		if (networkObject != null && (!networkObject.IsValid || !HasStateAuthority)) return;

		SetLeverInputs(0f, 0f);
		LeftInput = RightInput = ForwardCommand = TurnCommand = 0f;
		TargetForwardSpeed = TargetTurnSpeed = 0f;
		AppliedForwardSpeed = AppliedTurnSpeed = 0f;
		if (body == null || body.isKinematic) return;
		if (ground != null) ground.Sample();
		if (!CanDrive) return;

		Vector3 normal = ground.GroundNormal;
		body.linearVelocity = normal * Vector3.Dot(body.linearVelocity, normal);
		body.angularVelocity = Vector3.ProjectOnPlane(body.angularVelocity, normal);
	}

	private static float Approach(float current, float target,
		float acceleration, float deceleration, float dt, float restSpeed)
	{
		if (target == 0f)
			return Mathf.MoveTowards(current, 0f, deceleration * dt);

		// Contacts can reintroduce a tiny opposite velocity after every braking step.
		// Treat that residual as rest so it cannot keep the motor braking forever.
		// Larger reversals still decelerate before driving in the new direction.
		if ((current > 0f && target < 0f) || (current < 0f && target > 0f))
		{
			if (Mathf.Abs(current) > restSpeed)
				return Mathf.MoveTowards(current, 0f, deceleration * dt);
			current = 0f;
		}

		float rate = Mathf.Abs(target) < Mathf.Abs(current) ? deceleration : acceleration;
		return Mathf.MoveTowards(current, target, rate * dt);
	}

	public override void Spawned()
	{
		Debug.Log(
			$"[TANK SPAWNED] " +
			$"Mode={Runner.GameMode}, " +
			$"TankID={GetEntityId()}",
			this
		);

		TankControl control = GetComponent<TankControl>();

		SessionJoinInit session =
			Runner.GetComponent<SessionJoinInit>();

		Debug.Log(
			$"[REGISTER CHECK] " +
			$"Control={control != null}, " +
			$"Session={session != null}"
		);
	}

	public override void FixedUpdateNetwork()
	{
		if (!HasStateAuthority || crewRegistry == null) return;

		PlayerRef driver = crewRegistry.DriverPlayer;
		bool wasNeutral = requestedLeft == 0f && requestedRight == 0f;
		if (!driver.IsNone && Runner.TryGetInputForPlayer<TankCrewNetworkInput>(driver, out var data))
			SetLeverInputs(data.LeftLever, data.RightLever);
		else
			SetLeverInputs(0f, 0f);

		bool isNeutral = requestedLeft == 0f && requestedRight == 0f;
		if (lastDriver != driver || wasNeutral != isNeutral)
			Debug.Log($"[DRIVE INPUT] Driver={driver}, Left={requestedLeft:F2}, Right={requestedRight:F2}", this);
		lastDriver = driver;
	}
}
