using UnityEngine;

/// <summary>Routes tank action and observation requests to their owning components.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TankMotor))]
public sealed class TankControl : MonoBehaviour
{
	[SerializeField] private TankMotor motor;
	[SerializeField] private TankAim aim;
	[SerializeField] private TankGun gun;
	[SerializeField] private TankCrewCameras cameras;

	public float LeftLever { get; private set; }
	public float RightLever { get; private set; }

	private void Awake()
	{
		if (motor == null) motor = GetComponent<TankMotor>();
		if (aim == null) aim = GetComponent<TankAim>();
		if (gun == null) gun = GetComponent<TankGun>();
		if (cameras == null) cameras = GetComponent<TankCrewCameras>();

		LeftLever = RightLever = 0f;
		motor.SetLeverInputs(0f, 0f);
	}

	private void Update()
	{
		// Recovery continues even when an input source is disabled or disconnected.
		if (gun != null) gun.TickRecovery();
	}

	public void SetLeverInputs(float left, float right)
	{
		if (!isActiveAndEnabled) return;

		LeftLever = Mathf.Clamp(left, -1f, 1f);
		RightLever = Mathf.Clamp(right, -1f, 1f);
		motor.SetLeverInputs(LeftLever, RightLever);
	}

	public void ApplyAim(float turret, float barrel, float dt)
	{
		if (!isActiveAndEnabled || aim == null) return;

		aim.ApplyInput(turret, barrel, dt);
	}

	public bool RequestInsert()
	{
		return isActiveAndEnabled && gun != null && gun.RequestInsert();
	}

	public bool RequestClose()
	{
		return isActiveAndEnabled && gun != null && gun.RequestClose();
	}

	public bool RequestOpenEmpty()
	{
		return isActiveAndEnabled && gun != null && gun.RequestOpenEmpty();
	}

	public bool RequestFire()
	{
		return isActiveAndEnabled && gun != null && gun.RequestFire();
	}

	public void SwitchView(int station)
	{
		if (!isActiveAndEnabled || cameras == null || !cameras.isActiveAndEnabled) return;

		cameras.SwitchView(station);
	}

	public void Look(Vector2 delta)
	{
		if (!isActiveAndEnabled || cameras == null || !cameras.isActiveAndEnabled) return;

		cameras.Look(delta);
	}
}
