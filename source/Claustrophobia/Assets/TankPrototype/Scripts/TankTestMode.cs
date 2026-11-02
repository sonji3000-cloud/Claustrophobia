using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Chooses the local screen output without changing seats or device use.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-200)]
public sealed class TankTestMode : MonoBehaviour
{
	public enum Mode { Exterior, Interior }

	[SerializeField] private Mode startMode = Mode.Exterior;
	[SerializeField] private TankCrewCameras tankCameras;
	[SerializeField] private TankKeyboardDebug tankHud;
	[SerializeField] private CrewLook look;
	[SerializeField] private CrewDebugView debugView;
	[SerializeField] private GameObject interiorHud;
	[SerializeField] private Text modeLabel;

	private bool initialized;

	public Mode CurrentMode { get; private set; }
	public bool IsInterior => initialized && CurrentMode == Mode.Interior;

	private void Start()
	{
		SetMode(startMode);
	}

	private void Update()
	{
		if (!Application.isFocused) return;

		ReadKeys(Keyboard.current);
	}

	private void ReadKeys(Keyboard keyboard)
	{
		if (keyboard == null) return;

		if (keyboard.f8Key.wasPressedThisFrame) SetMode(Mode.Exterior);
		else if (keyboard.f9Key.wasPressedThisFrame) SetMode(Mode.Interior);
	}

	public void SetMode(Mode mode)
	{
		if (mode != Mode.Exterior && mode != Mode.Interior) return;
		if (initialized && CurrentMode == mode) return;

		if (look != null) look.ReleaseCursor();

		// Turn both output owners off before enabling the selected side.
		if (debugView != null) debugView.SetOutput(false);
		if (tankCameras != null) tankCameras.SetOutput(false);

		CurrentMode = mode;
		initialized = true;
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
		if (modeLabel != null) modeLabel.text = IsInterior
			? "INTERIOR [F9]  |  F8 Exterior  |  1/2/3 Seat  |  Space Use  |  F7 Rear view"
			: "EXTERIOR [F8]  |  F9 Interior  |  1/2/3 Seat  |  Space / Esc Exit device  |  F2-F4 View";
	}

	private void OnDisable()
	{
		if (look != null) look.ReleaseCursor();
		if (debugView != null) debugView.SetOutput(false);
		if (tankCameras != null) tankCameras.SetOutput(false);
	}

	private void OnEnable()
	{
		if (!initialized) return;

		initialized = false;
		SetMode(CurrentMode);
	}
}
