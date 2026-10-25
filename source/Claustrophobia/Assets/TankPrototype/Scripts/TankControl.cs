using UnityEngine;

/// <summary>Routes tank action and observation requests to their owning components.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TankMotor))]
[RequireComponent(typeof(TankCrew))]
public sealed class TankControl : MonoBehaviour
{
	[SerializeField] private TankMotor motor;
	[SerializeField] private TankAim aim;
	[SerializeField] private TankGun gun;
	[SerializeField] private TankCrewCameras cameras;
	[SerializeField] private TankCrew crew;

	private TankCrew.Role lastRole;

	public float LeftLever { get; private set; }
	public float RightLever { get; private set; }

	private void Awake()
	{
		if (motor == null) motor = GetComponent<TankMotor>();
		if (aim == null) aim = GetComponent<TankAim>();
		if (gun == null) gun = GetComponent<TankGun>();
		if (cameras == null) cameras = GetComponent<TankCrewCameras>();
		if (crew == null) crew = GetComponent<TankCrew>();

		LeftLever = RightLever = 0f;
		motor.SetLeverInputs(0f, 0f);
	}

	private void Start()
	{
		RefreshCrew();
	}

	private void Update()
	{
		RefreshCrew();

		// Recovery continues even when an input source is disabled or disconnected.
		if (gun != null) gun.TickRecovery();
	}

	// Local prototype shortcut; gameplay observation requests never assign roles.
	public void SetTestRole(TankCrew.Role role)
	{
		if (!isActiveAndEnabled || crew == null || !crew.isActiveAndEnabled) return;

		crew.SetRole(role);
		RefreshCrew();
	}

	public void SetLeverInputs(float left, float right)
	{
		if (!isActiveAndEnabled || crew == null || !crew.CanDrive) return;

		LeftLever = Mathf.Clamp(left, -1f, 1f);
		RightLever = Mathf.Clamp(right, -1f, 1f);
		motor.SetLeverInputs(LeftLever, RightLever);
	}

	public void ApplyAim(float turret, float barrel, float dt)
	{
		if (!isActiveAndEnabled || crew == null || !crew.CanAim || aim == null) return;

		RefreshCrew();
		aim.ApplyInput(turret, barrel, dt);
	}

	public bool RequestInsert()
	{
		return isActiveAndEnabled && crew != null && crew.CanLoad
			&& gun != null && gun.RequestInsert();
	}

	public bool RequestClose()
	{
		return isActiveAndEnabled && crew != null && crew.CanLoad
			&& gun != null && gun.RequestClose();
	}

	public bool RequestOpenEmpty()
	{
		return isActiveAndEnabled && crew != null && crew.CanLoad
			&& gun != null && gun.RequestOpenEmpty();
	}

	public bool RequestFire()
	{
		return isActiveAndEnabled && crew != null && crew.CanFire
			&& gun != null && gun.RequestFire();
	}

	public void SwitchView(int station)
	{
		if (!isActiveAndEnabled || crew == null || !crew.CanView(station)
			|| cameras == null || !cameras.isActiveAndEnabled) return;

		cameras.SwitchView(station);
	}

	public void Look(Vector2 delta)
	{
		if (!isActiveAndEnabled || crew == null || !crew.CanLook
			|| cameras == null || !cameras.isActiveAndEnabled) return;

		cameras.Look(delta);
	}

	private void RefreshCrew()
	{
		TankCrew.Role role = crew != null ? crew.CurrentRole : TankCrew.Role.None;
		if (role != lastRole)
		{
			lastRole = role;
			if (aim != null) aim.ApplyInput(0f, 0f, 0f);
		}

		if (cameras == null || !cameras.isActiveAndEnabled
			|| cameras.CurrentStation == TankCrewCameras.CrewStation.ThirdPerson) return;

		if (crew == null || !crew.CanView((int)cameras.CurrentStation))
			cameras.ExitObservation();
	}
}
