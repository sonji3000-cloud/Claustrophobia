using Fusion;
using Photon.Client.StructWrapping;
using System;
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
	[SerializeField] private CrewDevice aimDevice;
	[SerializeField] private GunnerSight sight;
	[Tooltip("Wait for explicit binding before accepting input. Legacy scenes leave this off.")]
	[SerializeField] private bool waitForBind;

	private bool bound;
	private bool attached;
	private bool disconnected;
	private bool inputEnabled = true;

	private CrewInteract driver;
	private TankCrew.Role lastRole;

    // 입력을 전송하기 위한 구조체
    private TankCrewNetworkInput pendingInput;

    public float LeftLever { get; private set; }
	public float RightLever { get; private set; }
	public bool IsDriving => CanDrive(driver);
	public bool IsBound => bound;
	public uint InputVersion { get; private set; }
	public bool CanInput => isActiveAndEnabled && inputEnabled && !disconnected && (!waitForBind || bound);

	/// <summary>Invalid targets leave the previous binding unchanged. Does not open the input gate.</summary>
	public bool Bind(TankCrew nextCrew, CrewDevice drive, CrewDevice nextAim, GunnerSight nextSight)
	{
		if (nextCrew == null || drive == null || nextAim == null || nextSight == null || drive == nextAim) return false;
		if (crew == nextCrew && driveDevice == drive && aimDevice == nextAim && sight == nextSight && !disconnected)
		{
			bound = true;
			if (isActiveAndEnabled) Attach();
			return true;
		}

		if (isActiveAndEnabled)
		{
			Detach();
			ClearCommands();
		}
		crew = nextCrew;
		driveDevice = drive;
		aimDevice = nextAim;
		sight = nextSight;
		bound = true;
		disconnected = false;
		if (isActiveAndEnabled) Attach();
		return true;
	}

	public void Unbind()
	{
		if (disconnected) return;
		Detach();
		ClearCommands();
		inputEnabled = false;
		bound = false;
		disconnected = true;
		crew = null;
		driveDevice = null;
		aimDevice = null;
		sight = null;
	}

	public void SetInputEnabled(bool enabled)
	{
		if (inputEnabled == enabled) return;
		inputEnabled = enabled;
		if (!enabled) ClearCommands();
	}

	private void ClearCommands()
	{
		InputVersion++;
		StopDrive();
		if (aimDevice != null && aimDevice.User != null) aimDevice.User.StopUse();
		ClearAim();
	}

	public event Action DriveChanged;

	private void Awake()
	{
		if (motor == null) motor = GetComponent<TankMotor>();
		if (aim == null) aim = GetComponent<TankAim>();
		if (gun == null) gun = GetComponent<TankGun>();
		if (cameras == null) cameras = GetComponent<TankCrewCameras>();
		if (crew == null && !waitForBind) crew = GetComponent<TankCrew>();
		if (waitForBind) inputEnabled = false;

		StopDrive();
	}

	private void OnEnable() => Attach();

	private void Attach()
	{
		if (attached || disconnected) return;
		attached = true;
		if (driveDevice != null)
		{
			driveDevice.UseChanged += OnDriveUse;
			if (driveDevice.User != null) driveDevice.User.StopUse();
		}

		if (aimDevice != null)
		{
			aimDevice.UseChanged += OnAimUse;
			if (aimDevice.User != null) aimDevice.User.StopUse();
		}
	}

	private void Start()
	{
		if (CanInput) RefreshCrew();
	}

	private void Update()
	{
		if (CanInput) RefreshCrew();

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
		DriveChanged?.Invoke();
		return CanDrive(user);
	}

	public bool CanDrive(CrewInteract user)
	{
		return user != null && driver == user && CanUseDrive(user);
	}

	public void SetLeverInputs(CrewInteract user, float left, float right)
	{
		if (!CanDrive(user)) return;

		LeftLever = Mathf.Clamp(left, -1f, 1f);
		RightLever = Mathf.Clamp(right, -1f, 1f);

        pendingInput.LeftLever = LeftLever;
        pendingInput.RightLever = RightLever;

		Debug.Log(
			$"[Leftlever] ={pendingInput.LeftLever} " +
			$"[righttlever] ={pendingInput.RightLever} "
			);

        //motor.SetLeverInputs(LeftLever, RightLever);
    }

	public void EndDrive(CrewInteract user)
	{
		if (driver != user) return;

		StopDrive();
	}

	private bool CanUseDrive(CrewInteract user)
	{
		return CanInput && motor != null && motor.isActiveAndEnabled
			&& user != null && user.isActiveAndEnabled
			&& user.Crew != null && user.Crew.CanDrive && driveDevice != null
			&& driveDevice.CanUse(user.Crew) && driveDevice.User == user && user.Active == driveDevice;
	}

	private void StopDrive()
	{
		CrewInteract user = driver;
		bool hadDriver = !ReferenceEquals(user, null);
		driver = null;
		LeftLever = RightLever = 0f;
		pendingInput.LeftLever = pendingInput.RightLever = 0f;
		if (motor != null) motor.SetLeverInputs(0f, 0f);

		if (user != null && user.Active == driveDevice) user.StopUse();
		if (hadDriver) DriveChanged?.Invoke();
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
		Detach();
		if (Application.isPlaying) ClearCommands();
		else StopDrive();
	}

	private void Detach()
	{
		if (!attached) return;
		attached = false;
		if (driveDevice != null) driveDevice.UseChanged -= OnDriveUse;
		if (aimDevice != null)
		{
			aimDevice.UseChanged -= OnAimUse;
		}
	}

	public bool CanAim(CrewInteract user)
	{
		return CanInput && aim != null && aim.isActiveAndEnabled
			&& user != null && user.isActiveAndEnabled
			&& user.Crew != null && user.Crew.CanAim && aimDevice != null
			&& aimDevice.CanUse(user.Crew) && aimDevice.User == user && user.Active == aimDevice;
	}

	public void ApplyAim(CrewInteract user, float turret, float barrel, float dt)
	{
		if (!CanAim(user))
		{
			pendingInput.Aim = Vector2.zero;
			return;
		}

		SetAim(turret, barrel, dt);
	}

	public void ZoomSight(CrewInteract user, float steps)
	{
		if (!CanAim(user) || sight == null) return;

		sight.ApplyZoom(steps);
	}

	public void EndAim(CrewInteract user)
	{
		if (user == null || aimDevice == null || aimDevice.User != user) return;

		user.StopUse();
	}

	private void OnAimUse(CrewInteract user, bool active)
	{
		if (!active) ClearAim();
		else if (!CanAim(user)) user.StopUse();
	}

	private void ClearAim()
	{
		pendingInput.Aim = Vector2.zero;
		if (aim != null) aim.ApplyInput(0f, 0f, 0f);
	}

	public void ApplyAim(float turret, float barrel, float dt)
	{
		if (!CanInput || crew == null || !crew.CanAim || aim == null || !aim.isActiveAndEnabled)
		{
			pendingInput.Aim = Vector2.zero;
			return;
		}

		SetAim(turret, barrel, dt);
	}

	private void SetAim(float turret, float barrel, float dt)
	{
		RefreshCrew();
		pendingInput.Aim = new Vector2(turret, barrel);
		aim.ApplyInput(turret, barrel, dt);
	}

	public bool RequestInsert()
	{
		return CanInput && crew != null && crew.CanLoad
			&& gun != null && gun.RequestInsert();
	}

	public bool RequestClose()
	{
		return CanInput && crew != null && crew.CanLoad
			&& gun != null && gun.RequestClose();
	}

	public bool RequestOpenEmpty()
	{
		return CanInput && crew != null && crew.CanLoad
			&& gun != null && gun.RequestOpenEmpty();
	}

	public CrewDevice AimDevice => aimDevice;

	public string GetFireBlock(CrewInteract user)
	{
		if (!CanInput) return "Controls unavailable";
		if (user == null || user.Crew == null || !user.Crew.CanFire) return "Gunner role required";
		if (!CanAim(user) || !user.CanSelect) return "Use gunner joystick";
		if (user.Carry != null && user.Carry.Held != null) return "Return the carried round first";
		if (gun == null) return "Gun unavailable";

		return gun.FireBlockReason;
	}

	public bool RequestFire(CrewInteract user)
	{
		if (GetFireBlock(user).Length != 0) return false;

		return gun.RequestFire();
	}

	public bool RequestFire() => RequestFire(aimDevice != null ? aimDevice.User : null);

	public void SwitchView(int station)
	{
		if (!CanInput || crew == null || !crew.CanView(station)
			|| cameras == null || !cameras.isActiveAndEnabled) return;

		cameras.SwitchView(station);
	}

	public void Look(Vector2 delta)
	{
		if (!CanInput || crew == null || !crew.CanLook
			|| cameras == null || !cameras.isActiveAndEnabled) return;

		cameras.Look(delta);
	}

	private void RefreshCrew()
	{
		TankCrew.Role role = crew != null ? crew.CurrentRole : TankCrew.Role.None;
		if (role != lastRole)
		{
			lastRole = role;
			ClearAim();
		}

		if (cameras == null || !cameras.isActiveAndEnabled
			|| cameras.CurrentStation == TankCrewCameras.CrewStation.ThirdPerson) return;

		if (crew == null || !crew.CanView((int)cameras.CurrentStation))
			cameras.ExitObservation();
	}

	public void SubmitNetworkInput(NetworkInput input)
	{
		if (!CanInput)
		{
			input.Set(default(TankCrewNetworkInput));
			return;
		}

        Debug.Log(
			$"[SUBMIT] ID={GetEntityId()} " +
			$"[SUBMIT] Role={crew.CurrentRole}, " +
			$"L={pendingInput.LeftLever:F2}, " +
			$"R={pendingInput.RightLever:F2}"
		);

        input.Set(pendingInput);
    }
}
