using UnityEngine;

/// <summary>Drives two adapted Tiger track clips from actual hull motion.</summary>
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(Animator))]
public sealed class TankTrackAnimation : MonoBehaviour
{
    [SerializeField] private Animator trackAnimator;
    [SerializeField, Min(0.01f)] private float trackGauge = 2.9f;
    [SerializeField, Min(0.01f)] private float metresPerCycle = 2.4511f;
    [SerializeField, Min(0f)] private float stoppedSpeed = 0.001f;

    private Rigidbody body;
    private static readonly int LeftPhaseId = Animator.StringToHash("LeftTrackPhase");
    private static readonly int RightPhaseId = Animator.StringToHash("RightTrackPhase");

    public float LeftTrackSpeed { get; private set; }
    public float RightTrackSpeed { get; private set; }
    public float LeftPhase { get; private set; }
    public float RightPhase { get; private set; }
    public float TrackGauge => trackGauge;
    public float MetresPerCycle => metresPerCycle;

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        if (trackAnimator == null) trackAnimator = GetComponent<Animator>();
    }

    private void Update() => UpdateAnimation(Time.deltaTime);

    public void UpdateAnimation(float deltaTime)
    {
        if (body == null || trackAnimator == null || trackAnimator.runtimeAnimatorController == null) return;
        Vector3 forward = body.rotation * Vector3.forward;
        float linear = Vector3.Dot(body.linearVelocity, forward);
        float yaw = Vector3.Dot(body.angularVelocity, body.rotation * Vector3.up);
        float differential = yaw * trackGauge * 0.5f;
        LeftTrackSpeed = CleanSpeed(linear + differential);
        RightTrackSpeed = CleanSpeed(linear - differential);

        // The supplied clip turns wheels around -X; decreasing its phase rolls them forward along +Z.
        // Motion-time parameters support both directions and hold the last pose when stopped.
        float cyclesPerMetre = 1f / Mathf.Max(0.01f, metresPerCycle);
        float step = Mathf.Max(0f, deltaTime) * cyclesPerMetre;
        LeftPhase = Mathf.Repeat(LeftPhase - LeftTrackSpeed * step, 1f);
        RightPhase = Mathf.Repeat(RightPhase - RightTrackSpeed * step, 1f);
        trackAnimator.SetFloat(LeftPhaseId, LeftPhase);
        trackAnimator.SetFloat(RightPhaseId, RightPhase);
    }

    private float CleanSpeed(float speed) => Mathf.Abs(speed) < stoppedSpeed ? 0f : speed;

    private void OnValidate()
    {
        trackGauge = Mathf.Max(0.01f, trackGauge);
        metresPerCycle = Mathf.Max(0.01f, metresPerCycle);
        stoppedSpeed = Mathf.Max(0f, stoppedSpeed);
    }
}
