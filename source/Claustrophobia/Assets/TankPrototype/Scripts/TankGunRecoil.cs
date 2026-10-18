using UnityEngine;

/// <summary>Drives the barrel-only recoil layer from shots committed by TankGun.</summary>
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
[RequireComponent(typeof(TankGun), typeof(Animator))]
public sealed class TankGunRecoil : MonoBehaviour
{
    [SerializeField] private TankGun gun;
    [SerializeField] private Animator recoilAnimator;
    [SerializeField] private AnimationClip recoilClip;

    private static readonly int PhaseId = Animator.StringToHash("GunRecoilPhase");
    private int observedShotCount;
    private float recoilStartedAt = -1f;

    public float Duration => recoilClip != null ? recoilClip.length : 0f;
    public float RecoilPhase { get; private set; } = 1f;
    public bool IsRecoiling => RecoilPhase < 1f;

    private void OnEnable()
    {
        if (gun == null) gun = GetComponent<TankGun>();
        if (recoilAnimator == null) recoilAnimator = GetComponent<Animator>();
        observedShotCount = gun != null ? gun.ShotCount : 0;
        recoilStartedAt = -1f;
        SetPhase(1f);
    }

    private void Update()
    {
        if (gun == null || recoilClip == null) return;
        // Failed or blocked requests never advance ShotCount. Observing the
        // committed shot also leaves projectile creation and chamber logic intact.
        if (gun.ShotCount != observedShotCount)
        {
            observedShotCount = gun.ShotCount;
            recoilStartedAt = gun.LastSuccessfulFireTime;
        }

        float phase = recoilStartedAt < 0f ? 1f
            : Mathf.Clamp01((Time.time - recoilStartedAt) / Mathf.Max(0.0001f, Duration));
        SetPhase(phase);
    }

    private void OnDisable()
    {
        recoilStartedAt = -1f;
        SetPhase(1f);
    }

    private void SetPhase(float phase)
    {
        RecoilPhase = phase;
        if (recoilAnimator != null && recoilAnimator.runtimeAnimatorController != null)
            recoilAnimator.SetFloat(PhaseId, phase);
    }
}
