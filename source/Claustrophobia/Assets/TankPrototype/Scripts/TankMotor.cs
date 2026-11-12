using UnityEngine;
using Fusion;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class TankMotor : NetworkBehaviour
{
    [Header("Flat-ground motor")]
    [Min(0f)] public float MaxMoveSpeed = 5f;
    [Min(0f)] public float MaxTurnSpeed = 60f;
    [Min(0f)] public float MoveAcceleration = 2f;
    [Min(0f)] public float MoveDeceleration = 4f;
    [Min(0f)] public float TurnAcceleration = 90f;
    [Min(0f)] public float TurnDeceleration = 180f;

    private Rigidbody body;
    private NetworkObject networkObject;
    private TankCrewRegistry crewRegistry;
    private PlayerRef lastDriver;
    private float requestedLeft;
    private float requestedRight;

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
    }

    public void SetLeverInputs(float left, float right)
    {
        requestedLeft = Mathf.Clamp(left, -1f, 1f);
        requestedRight = Mathf.Clamp(right, -1f, 1f);
    }

    private void FixedUpdate()
    {
		if (networkObject != null && (!networkObject.IsValid || !HasStateAuthority)) return;

        if (UsesCrewRegistry)
        {
            if (Object == null || !Object.IsValid || !HasStateAuthority) return;
            if (lastDriver != crewRegistry.DriverPlayer) SetLeverInputs(0f, 0f);
        }

        PhysicsTickCount++;
        if (!body.isKinematic && (requestedLeft != 0f || requestedRight != 0f)) body.WakeUp();

        LeftInput = requestedLeft;
        RightInput = requestedRight;
        ForwardCommand = (LeftInput + RightInput) * 0.5f;
        TurnCommand = (LeftInput - RightInput) * 0.5f;
        TargetForwardSpeed = ForwardCommand * MaxMoveSpeed;
        TargetTurnSpeed = TurnCommand * MaxTurnSpeed;

        Vector3 velocity = body.linearVelocity;
        Vector3 forward = body.rotation * Vector3.forward;
        float currentForward = Vector3.Dot(velocity, forward);
        float currentTurn = body.angularVelocity.y * Mathf.Rad2Deg;
        float nextForward = Approach(currentForward, TargetForwardSpeed,
            MoveAcceleration, MoveDeceleration, Time.fixedDeltaTime, 0.05f);
        float nextTurn = Approach(currentTurn, TargetTurnSpeed,
            TurnAcceleration, TurnDeceleration, Time.fixedDeltaTime, 0.5f);
        AppliedForwardSpeed = nextForward;
        AppliedTurnSpeed = nextTurn;

        // The flat-ground model removes lateral motion and preserves gravity's Y velocity.
        Vector3 appliedVelocity = forward * nextForward;
        appliedVelocity.y = velocity.y;
        body.linearVelocity = appliedVelocity;
        body.angularVelocity = new Vector3(0f, nextTurn * Mathf.Deg2Rad, 0f);
    }

	public void Stop()
	{
		if (networkObject != null && (!networkObject.IsValid || !HasStateAuthority)) return;

		SetLeverInputs(0f, 0f);
		LeftInput = RightInput = ForwardCommand = TurnCommand = 0f;
		TargetForwardSpeed = TargetTurnSpeed = 0f;
		AppliedForwardSpeed = AppliedTurnSpeed = 0f;
		if (body == null || body.isKinematic) return;

		body.linearVelocity = new Vector3(0f, body.linearVelocity.y, 0f);
		body.angularVelocity = Vector3.zero;
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
