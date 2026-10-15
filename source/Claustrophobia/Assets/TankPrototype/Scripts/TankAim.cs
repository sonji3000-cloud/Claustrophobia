using UnityEngine;

[DisallowMultipleComponent]
public sealed class TankAim : MonoBehaviour
{
    [SerializeField] private Transform turretPivot;
    [SerializeField] private Transform gunPivot;
    [Min(0f)] public float YawSpeed = 60f;
    [Min(0f)] public float PitchSpeed = 30f;
    public float MinPitch = -10f;
    public float MaxPitch = 30f;

    public float TurretInput { get; private set; }
    public float GunInput { get; private set; }
    public float CurrentYaw { get; private set; }
    public float CurrentPitch { get; private set; }

    private Quaternion turretBase;
    private Quaternion gunBase;

    private void Awake()
    {
        turretBase = turretPivot != null ? turretPivot.localRotation : Quaternion.identity;
        gunBase = gunPivot != null ? gunPivot.localRotation : Quaternion.identity;
    }

    // The input driver explicitly calls this before requesting a shot.
    public void ApplyInput(float turret, float gun, float dt)
    {
        TurretInput = Mathf.Clamp(turret, -1f, 1f);
        GunInput = Mathf.Clamp(gun, -1f, 1f);
        if (!isActiveAndEnabled || turretPivot == null || gunPivot == null) return;
        CurrentYaw = Mathf.Repeat(CurrentYaw + TurretInput * YawSpeed * dt, 360f);
        CurrentPitch = Mathf.Clamp(CurrentPitch + GunInput * PitchSpeed * dt, MinPitch, MaxPitch);
        turretPivot.localRotation = turretBase * Quaternion.AngleAxis(CurrentYaw, Vector3.up);
        gunPivot.localRotation = gunBase * Quaternion.AngleAxis(-CurrentPitch, Vector3.right);
    }

    private void OnValidate()
    {
        MaxPitch = Mathf.Max(MinPitch, MaxPitch);
    }
}
