using Fusion;
using UnityEngine;

/// <summary>Routes tank action and observation requests to their owning components.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-10)]
[RequireComponent(typeof(TankMotor))]
[RequireComponent(typeof(TankCrew))]
public sealed class TankControl : MonoBehaviour
{
	[SerializeField] private TankMotor motor;
	[SerializeField] private TankAim aim;
	[SerializeField] private TankGun gun;
	[SerializeField] private TankCrewCameras cameras;
	[SerializeField] private TankCrew crew;
	[SerializeField] private CrewDevice driveDevice;

	private CrewInteract driver;
	private TankCrew.Role lastRole;

	private TankCrewNetworkInput pendingInput; // 공유할 데이터


	public float LeftLever { get; private set; }
	public float RightLever { get; private set; }

	private void Awake()
	{
		if (motor == null) motor = GetComponent<TankMotor>();
		if (aim == null) aim = GetComponent<TankAim>();
		if (gun == null) gun = GetComponent<TankGun>();
		if (cameras == null) cameras = GetComponent<TankCrewCameras>();
		if (crew == null) crew = GetComponent<TankCrew>();

		StopDrive();
	}

	private void OnEnable()
	{
		if (driveDevice == null) return;

		driveDevice.UseChanged += OnDriveUse;
		if (driveDevice.User != null) driveDevice.User.StopUse();
	}

	private void Start()
	{
        // 이동 테스트용: 로컬 역할을 조종수로 강제 지정
        SetTestRole(TankCrew.Role.Driver);

        RefreshCrew();
	}

	private void Update()
	{
		RefreshCrew();

		// Recovery continues even when an input source is disabled or disconnected.
		if (gun != null) gun.TickRecovery();
	}

	


    private void FixedUpdate()
	{
		// Validate before TankMotor consumes the next physics command, including destroyed users.
		if (!ReferenceEquals(driver, null) && !CanDrive(driver)) StopDrive();
	}

	public bool TryBeginDrive(CrewInteract user)
	{
		if (!CanUseDrive(user) || driver != null && driver != user) return false;
		if (driver == user) return true;

		StopDrive();
		driver = user;
		return true;
	}

	public bool CanDrive(CrewInteract user)
	{
		return user != null && driver == user && CanUseDrive(user);
	}

	public void SetLeverInputs(CrewInteract user, float left, float right)
	{
		if (!CanDrive(user)) return;
    }

    // Local prototype shortcut; gameplay observation requests never assign roles.
    public void SetTestRole(TankCrew.Role role)
	{
		if (!isActiveAndEnabled || crew == null || !crew.isActiveAndEnabled) return;

		crew.SetRole(role);

        Debug.Log(
        $"[ROLE CHANGE] {gameObject.name} " +
        $"ID={GetEntityId()} " +
        $"Role={crew.CurrentRole}"
    );

        RefreshCrew();
	}

	public void SetLeverInputs(float left, float right)
	{
		if (!isActiveAndEnabled || crew == null || !crew.CanDrive)
			return;

		LeftLever = Mathf.Clamp(left, -1f, 1f);
		RightLever = Mathf.Clamp(right, -1f, 1f);

        pendingInput.LeftLever = LeftLever;
        pendingInput.RightLever = RightLever;
    }
	public void EndDrive(CrewInteract user)
	{
		if (driver != user) return;

		StopDrive();
	}

	private bool CanUseDrive(CrewInteract user)
	{
		return isActiveAndEnabled && motor != null && motor.isActiveAndEnabled
			&& user != null && user.isActiveAndEnabled
			&& user.Crew != null && user.Crew.CanDrive && driveDevice != null
			&& driveDevice.CanUse(user.Crew) && driveDevice.User == user && user.Active == driveDevice;
	}

	private void StopDrive()
	{
		CrewInteract user = driver;
		driver = null;
		LeftLever = RightLever = 0f;
		if (motor != null) motor.SetLeverInputs(0f, 0f);

		if (user != null && user.Active == driveDevice) user.StopUse();
	}

	private void OnDriveUse(CrewInteract user, bool active)
	{
		if (active)
		{
			if (!TryBeginDrive(user)) user.StopUse();
		}
		else EndDrive(user);
	}

	private void OnDisable()
	{
		if (driveDevice != null) driveDevice.UseChanged -= OnDriveUse;
		StopDrive();
	}

	

    public void ApplyAim(float turret, float barrel, float dt)
	{
		if (!isActiveAndEnabled || crew == null || !crew.CanAim || aim == null) return;

        pendingInput.Aim = new Vector2(turret, barrel);
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
        if (crew == null || !crew.CanFire)
            return false;

        pendingInput.Buttons.Set(TankInputButton.Fire, true);
        return true;
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

    public void SubmitNetworkInput(NetworkInput input)
    {
        Debug.Log(
        $"[SUBMIT] ID={GetEntityId()} " +
        $"[SUBMIT] Role={crew.CurrentRole}, " +
        $"L={pendingInput.LeftLever:F2}, " +
        $"R={pendingInput.RightLever:F2}"
    );

        input.Set(pendingInput);
    }
}
