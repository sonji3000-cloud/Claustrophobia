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
	[Networked] public float GunPitch { get; private set; }
	public float CurrentYaw => Object != null && Object.IsValid ? TurretYaw : localYaw;
	public float CurrentPitch { get; private set; }
	public float TurretInput { get; private set; }
	public float GunInput { get; private set; }

	private Quaternion turretBase;
	private Quaternion gunBase;
	private TankCrewRegistry crewRegistry;
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
		crewRegistry = GetComponent<TankCrewRegistry>();
	}

	public override void Spawned()
	{
		ApplyYaw(TurretYaw);
		ApplyPitch(GunPitch);
	}

	public override void FixedUpdateNetwork()
	{
		if (!HasStateAuthority || !isActiveAndEnabled || crewRegistry == null) return;

		PlayerRef gunner = crewRegistry.GunnerPlayer;
		Vector2 input = !gunner.IsNone && Runner.TryGetInputForPlayer<TankCrewNetworkInput>(gunner, out var data)
			? data.Aim
			: Vector2.zero;
		float yaw = Mathf.Clamp(input.x, -1f, 1f);
		float pitch = Mathf.Clamp(input.y, -1f, 1f);

		if (logYaw && yaw != lastLoggedInput)
		{
			lastLoggedInput = yaw;
			Debug.Log($"[INPUT AIM] Player={gunner.PlayerId}, X={yaw:F2}", this);
		}

		if (yaw != 0f && turretPivot != null)
		{
			TurretYaw = StepYaw(TurretYaw, yaw, Runner.DeltaTime);
			ApplyYaw(TurretYaw);
		}

		if (pitch != 0f && gunPivot != null)
		{
			GunPitch = StepPitch(GunPitch, pitch, Runner.DeltaTime);
			ApplyPitch(GunPitch);
		}
	}

	public override void Render()
	{
		if (!isActiveAndEnabled) return;

		float yaw = TurretYaw;
		bool host = HasStateAuthority;
		if (!host)
		{
			ApplyYaw(yaw);
			ApplyPitch(GunPitch);
		}

		if (!logYaw || yaw == lastLoggedYaw || Time.unscaledTime < nextYawLog) return;

		lastLoggedYaw = yaw;
		nextYawLog = Time.unscaledTime + 0.5f;
		Debug.Log($"[TURRET {(host ? "HOST" : "CLIENT")}] Yaw={yaw:F2}", this);
	}

	public void ApplyInput(float turret, float gun, float dt)
	{
		TurretInput = Mathf.Clamp(turret, -1f, 1f);
		GunInput = Mathf.Clamp(gun, -1f, 1f);
		if (hasNetworkObject || !isActiveAndEnabled || turretPivot == null || gunPivot == null) return;

		localYaw = StepYaw(localYaw, TurretInput, dt);
		ApplyYaw(localYaw);
		ApplyPitch(StepPitch(CurrentPitch, GunInput, dt));
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

	private float StepPitch(float pitch, float input, float dt)
	{
		return Mathf.Clamp(pitch + input * PitchSpeed * dt, MinPitch, MaxPitch);
	}

	private void ApplyPitch(float pitch)
	{
		CurrentPitch = pitch;
		if (gunPivot != null)
			gunPivot.localRotation = gunBase * Quaternion.AngleAxis(-pitch, Vector3.right);
	}

	private void OnValidate()
	{
		MaxPitch = Mathf.Max(MinPitch, MaxPitch);
	}
}
