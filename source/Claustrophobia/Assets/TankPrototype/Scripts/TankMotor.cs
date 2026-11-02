using Fusion;
using UnityEngine;

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
    private float requestedLeft;
    private float requestedRight;

    // These describe the inputs and targets used by the last motor physics tick.
    public float LeftInput { get; private set; }
    public float RightInput { get; private set; }
    public float ForwardCommand { get; private set; }
    public float TurnCommand { get; private set; }
    public float TargetForwardSpeed { get; private set; }
    public float TargetTurnSpeed { get; private set; }

    private void Awake() => body = GetComponent<Rigidbody>();

    public void SetLeverInputs(float left, float right)
    {
        requestedLeft = Mathf.Clamp(left, -1f, 1f);
        requestedRight = Mathf.Clamp(right, -1f, 1f);
    }

    private void FixedUpdate()
    {
        if (Object == null || !HasStateAuthority)
            return;

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
            MoveAcceleration, MoveDeceleration, Time.fixedDeltaTime);
        float nextTurn = Approach(currentTurn, TargetTurnSpeed,
            TurnAcceleration, TurnDeceleration, Time.fixedDeltaTime);

        // The flat-ground model removes lateral motion and preserves gravity's Y velocity.
        Vector3 appliedVelocity = forward * nextForward;
        appliedVelocity.y = velocity.y;
        body.linearVelocity = appliedVelocity;
        body.angularVelocity = new Vector3(0f, nextTurn * Mathf.Deg2Rad, 0f);
    }

    private static float Approach(float current, float target,
        float acceleration, float deceleration, float dt)
    {
        if (target == 0f)
            return Mathf.MoveTowards(current, 0f, deceleration * dt);
        // Reverse only after reaching zero; no leftover step is used to cross zero.
        if ((current > 0f && target < 0f) || (current < 0f && target > 0f))
            return Mathf.MoveTowards(current, 0f, deceleration * dt);
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

        if (session != null && control != null)
        {
            session.RegisterTankControl(control);
        }

        // 이동 테스트용: Host에서만 Driver 지정
        if (Runner.IsServer && control != null)
        {
            control.SetTestRole(TankCrew.Role.Driver);
        }
    }
    public override void FixedUpdateNetwork()
    {
        if (!HasStateAuthority)
            return;

        PlayerRef driver = Runner.LocalPlayer;

        bool received =
            Runner.TryGetInputForPlayer<TankCrewNetworkInput>(
                driver, out var data
            );

        if (!received)
        {
            Debug.LogWarning(
                $"[RECV FAILED] Driver={driver}"
            );
            return;
        }

        Debug.Log(
            $"[RECV SUCCESS] Driver={driver}, " +
            $"Left={data.LeftLever:F2}, " +
            $"Right={data.RightLever:F2}"
        );

        SetLeverInputs(data.LeftLever, data.RightLever);
    }
}
