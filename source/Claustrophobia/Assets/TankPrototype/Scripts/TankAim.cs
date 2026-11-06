using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class TankAim : NetworkBehaviour
{
	[SerializeField] private Transform turretPivot;
	[SerializeField] private Transform gunPivot;
	[Min(0f)] public float YawSpeed = 60f;
	[Min(0f)] public float PitchSpeed = 30f;
	public float MinPitch = -10f;
	public float MaxPitch = 30f;

	[SerializeField] private bool logYaw;

	[Networked] public float TurretYaw { get; private set; }
	public float CurrentYaw => Object != null && Object.IsValid ? TurretYaw : localYaw;
	public float CurrentPitch { get; private set; }
	public float TurretInput { get; private set; }
	public float GunInput { get; private set; }

	private Quaternion turretBase;
	private Quaternion gunBase;
	private bool hasNetworkObject;
	private float localYaw;

	private float lastLoggedInput;
	private float lastLoggedYaw = float.NaN;
	private float nextYawLog;

	private void Awake()
	{
		turretBase = turretPivot != null ? turretPivot.localRotation : Quaternion.identity;
		gunBase = gunPivot != null ? gunPivot.localRotation : Quaternion.identity;
		hasNetworkObject = TryGetComponent<NetworkObject>(out _);
	}

	public override void Spawned() => ApplyYaw(TurretYaw);

	public override void FixedUpdateNetwork()
	{
		if (!HasStateAuthority || !isActiveAndEnabled || turretPivot == null) return;

		PlayerRef gunner = Runner.LocalPlayer;
		float input = Runner.TryGetInputForPlayer<TankCrewNetworkInput>(gunner, out var data)
			? Mathf.Clamp(data.Aim.x, -1f, 1f)
			: 0f;

		if (logYaw && input != lastLoggedInput)
		{
			lastLoggedInput = input;
			Debug.Log($"[INPUT AIM] Player={gunner.PlayerId}, X={input:F2}", this);
		}

		if (input == 0f) return;
		TurretYaw = StepYaw(TurretYaw, input, Runner.DeltaTime);
		ApplyYaw(TurretYaw);
	}

	public override void Render()
	{
		if (!isActiveAndEnabled) return;

		float yaw = TurretYaw;
		bool host = HasStateAuthority;
		if (!host) ApplyYaw(yaw);

		if (!logYaw || yaw == lastLoggedYaw || Time.unscaledTime < nextYawLog) return;

		lastLoggedYaw = yaw;
		nextYawLog = Time.unscaledTime + 0.5f;
		Debug.Log($"[TURRET {(host ? "HOST" : "CLIENT")}] Yaw={yaw:F2}", this);
	}

	public void ApplyInput(float turret, float gun, float dt)
	{
		TurretInput = Mathf.Clamp(turret, -1f, 1f);
		GunInput = Mathf.Clamp(gun, -1f, 1f);
		if (!isActiveAndEnabled || turretPivot == null || gunPivot == null) return;

		if (!hasNetworkObject)
		{
			localYaw = StepYaw(localYaw, TurretInput, dt);
			ApplyYaw(localYaw);
		}

		CurrentPitch = Mathf.Clamp(CurrentPitch + GunInput * PitchSpeed * dt, MinPitch, MaxPitch);
		gunPivot.localRotation = gunBase * Quaternion.AngleAxis(-CurrentPitch, Vector3.right);
	}

	private float StepYaw(float yaw, float input, float dt)
	{
		return Mathf.Repeat(yaw + input * YawSpeed * dt, 360f);
	}

	private void ApplyYaw(float yaw)
	{
		if (turretPivot != null)
			turretPivot.localRotation = turretBase * Quaternion.AngleAxis(yaw, Vector3.up);
	}

	private void OnValidate()
	{
		MaxPitch = Mathf.Max(MinPitch, MaxPitch);
	}
}
