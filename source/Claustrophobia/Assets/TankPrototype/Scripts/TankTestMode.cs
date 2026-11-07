using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Owns local screen output and the commander's interior sight transition.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)]
public sealed class TankTestMode : MonoBehaviour
{
	public enum Mode { Exterior, Interior }

	[SerializeField] private Mode startMode = Mode.Exterior;
	[SerializeField] private TankInput tankInput;
	[SerializeField] private TankCrewCameras tankCameras;
	[SerializeField] private TankKeyboardDebug tankHud;
	[SerializeField] private CrewLook look;
	[SerializeField] private CrewDebugView debugView;
	[SerializeField] private GameObject interiorHud;
	[SerializeField] private Text modeLabel;
	[SerializeField] private CrewDevice commanderSight;
	[SerializeField] private CrewCarry carry;
	[Tooltip("Wait for Bind and ApplyMode instead of applying output in Start.")]
	[SerializeField] private bool waitForBind;

	private bool initialized;
	private bool outputApplied;
	private bool bound;
	private bool disconnected;
	private bool savedRearView;
	private bool savedLook;
	private bool savedCursor;
	private bool ownsCursor;
	private CursorLockMode savedLock;
	private bool savedVisible;

	public Mode CurrentMode { get; private set; }
	public bool IsInterior => outputApplied && CurrentMode == Mode.Interior;
	public bool IsUsingSight { get; private set; }
	public bool IsBound => bound;
	public bool CanApply => isActiveAndEnabled && !disconnected && (!waitForBind || bound);

	/// <summary>Stores valid external references. ApplyMode enables output separately.</summary>
	public bool Bind(TankInput input, TankCrewCameras cameras, TankKeyboardDebug hud)
	{
		if (input == null || cameras == null || hud == null || input.gameObject != cameras.gameObject
			|| input.gameObject != hud.gameObject) return false;
		if (tankInput == input && tankCameras == cameras && tankHud == hud && !disconnected)
		{
			bound = true;
			return true;
		}

		disconnected = true;
		if (isActiveAndEnabled) ClearOutput();
		else outputApplied = false;
		tankInput = input;
		tankCameras = cameras;
		tankHud = hud;
		bound = true;
		disconnected = false;
		return true;
	}

	public void Unbind()
	{
		if (disconnected) return;
		disconnected = true;
		ClearOutput();
		tankInput = null;
		tankCameras = null;
		tankHud = null;
		bound = false;
	}

	public bool ApplyMode()
	{
		if (!CanApply) return false;
		SetMode(initialized ? CurrentMode : startMode);
		return outputApplied;
	}

	private void Start()
	{
		if (!ApplyMode()) ClearOutput();
	}

	private void Update()
	{
		if (!CanApply) return;

		if (IsUsingSight && (!CanUseSight(commanderSight != null ? commanderSight.User : null)
			|| tankCameras.CurrentStation != TankCrewCameras.CrewStation.Commander
			|| tankCameras.ActiveCamera == null || !tankCameras.ActiveCamera.isActiveAndEnabled)) StopSight();

		if (!Application.isFocused) return;

		ReadKeys(Keyboard.current);
	}

	private void ReadKeys(Keyboard keyboard)
	{
		if (!CanApply || keyboard == null) return;

		if (keyboard.f8Key.wasPressedThisFrame) SetMode(Mode.Exterior);
		else if (keyboard.f9Key.wasPressedThisFrame) SetMode(Mode.Interior);
	}

	public void SetMode(Mode mode)
	{
		if (!CanApply) return;
		if (mode != Mode.Exterior && mode != Mode.Interior) return;
		if (outputApplied && CurrentMode == mode) return;

		StopSight();
		if (carry != null) carry.ReturnRound();
		if (tankInput != null) tankInput.ResetInput();
		if (look != null) look.ReleaseCursor();

		// Turn both output owners off before enabling the selected side.
		if (debugView != null) debugView.SetOutput(false);
		if (tankCameras != null) tankCameras.SetOutput(false);

		CurrentMode = mode;
		initialized = true;
		outputApplied = true;
		if (IsInterior)
		{
			if (debugView != null) debugView.SetOutput(true);
		}
		else if (tankCameras != null)
		{
			tankCameras.SetOutput(true);
		}

		if (interiorHud != null) interiorHud.SetActive(IsInterior);
		if (tankHud != null) tankHud.enabled = !IsInterior;
		RefreshLabel();
	}

	private bool CanUseSight(CrewInteract user)
	{
		return CanApply && IsInterior && tankInput != null && tankInput.CanInput
			&& tankCameras != null && tankCameras.isActiveAndEnabled && debugView != null
			&& user != null && user.isActiveAndEnabled && user.Crew != null && user.Crew.CanLook
			&& commanderSight != null && commanderSight.CanUse(user.Crew)
			&& commanderSight.User == user && user.Active == commanderSight;
	}

	private void OnSightUse(CrewInteract user, bool active)
	{
		if (!active)
		{
			EndSight();
			return;
		}
		if (!CanUseSight(user))
		{
			user.StopUse();
			return;
		}

		savedRearView = debugView.IsObserving;
		savedLook = look != null && look.enabled;
		savedCursor = look != null && look.HasCursor;
		IsUsingSight = true;

		if (look != null) look.enabled = false;
		debugView.SetOutput(false);
		tankCameras.SwitchView((int)TankCrewCameras.CrewStation.Commander);
		if (tankCameras.CurrentStation != TankCrewCameras.CrewStation.Commander
			|| tankCameras.ActiveCamera == null || !tankCameras.ActiveCamera.gameObject.activeInHierarchy)
		{
			user.StopUse();
			return;
		}

		tankCameras.SetOutput(true);
		user.ClearSelection();
		if (interiorHud != null) interiorHud.SetActive(false);
		RefreshLabel();
	}

	public void StopSight()
	{
		if (!IsUsingSight) return;

		CrewInteract user = commanderSight != null ? commanderSight.User : null;
		if (user != null && user.Active == commanderSight) user.StopUse();
		EndSight();
	}

	private void EndSight()
	{
		if (!IsUsingSight) return;

		IsUsingSight = false;
		ReleaseDrag();
		if (tankCameras != null) tankCameras.SetOutput(false);

		bool restore = CanApply && IsInterior;
		if (restore && debugView != null)
		{
			debugView.SetOutput(true);
			if (savedRearView) debugView.SetObserving(true);
		}
		if (look != null)
		{
			look.enabled = savedLook;
			if (restore && savedCursor) look.CaptureCursor();
		}

		if (interiorHud != null) interiorHud.SetActive(restore);
		if (tankInput != null) tankInput.ResetInput();
		RefreshLabel();
	}

	// TankInput checks focus before calling. Ignore the capture frame's delta to prevent a jump.
	public bool SetDrag(bool active)
	{
		if (!CanApply || tankInput == null || !tankInput.CanInput || !IsUsingSight || !active)
		{
			ReleaseDrag();
			return false;
		}
		if (ownsCursor) return true;

		savedLock = Cursor.lockState;
		savedVisible = Cursor.visible;
		ownsCursor = true;
		Cursor.lockState = CursorLockMode.Locked;
		Cursor.visible = false;
		return false;
	}

	private void ReleaseDrag()
	{
		if (!ownsCursor) return;

		ownsCursor = false;
		Cursor.lockState = savedLock;
		Cursor.visible = savedVisible;
	}

	private void RefreshLabel()
	{
		if (modeLabel == null) return;

		modeLabel.text = IsUsingSight
			? "COMMANDER SIGHT  |  Hold RMB - Look  |  Space / Esc - Return  |  F8 Exterior"
			: IsInterior
				? "INTERIOR [F9]  |  F8 Exterior  |  1/2/3 Seat  |  Space Use  |  F7 Rear view  |  Joystick: Arrows Aim / Wheel Zoom / Enter Fire"
				: "EXTERIOR [F8]  |  F9 Interior  |  1/2/3 Seat  |  Space / Esc Exit device  |  F3 Commander / F4 Overview";
	}

	private void OnDisable()
	{
		if (commanderSight != null) commanderSight.UseChanged -= OnSightUse;
		ClearOutput();
	}

	private void ClearOutput()
	{
		bool wasUsingSight = IsUsingSight;
		StopSight();
		if (carry != null) carry.ReturnRound();
		if (!wasUsingSight && tankInput != null) tankInput.ResetInput();
		if (look != null) look.ReleaseCursor();
		if (debugView != null) debugView.SetOutput(false);
		if (tankCameras != null) tankCameras.SetOutput(false);
		if (interiorHud != null) interiorHud.SetActive(false);
		if (tankHud != null) tankHud.enabled = false;
		outputApplied = false;
		RefreshLabel();
	}

	private void OnEnable()
	{
		if (commanderSight != null)
		{
			commanderSight.UseChanged += OnSightUse;
			if (commanderSight.User != null) commanderSight.User.StopUse();
		}
		if (!initialized) return;

		ApplyMode();
	}
}
