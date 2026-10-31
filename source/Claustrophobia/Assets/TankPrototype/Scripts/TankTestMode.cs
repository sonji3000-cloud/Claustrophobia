using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>Chooses the local test input source and its screen output.</summary>
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
	[SerializeField] private CrewInteract interaction;
	[SerializeField] private CrewDebugView debugView;
	[SerializeField] private GameObject interiorHud;
	[SerializeField] private Text modeLabel;

	private bool initialized;
	private bool inputReady;
	private int switchFrame;

	public Mode CurrentMode { get; private set; }
	public bool IsInterior => initialized && CurrentMode == Mode.Interior;
	public bool CanReadInterior => isActiveAndEnabled && IsInterior && inputReady;
	public bool CanReadExterior => isActiveAndEnabled && initialized && !IsInterior && inputReady;

	private void Start()
	{
		SetMode(startMode);
	}

	private void Update()
	{
		if (!Application.isFocused)
		{
			inputReady = false;
			return;
		}

		ReadKeys(Keyboard.current);
	}

	private void ReadKeys(Keyboard keyboard)
	{
		if (keyboard != null)
		{
			if (keyboard.f8Key.wasPressedThisFrame) SetMode(Mode.Exterior);
			else if (keyboard.f9Key.wasPressedThisFrame) SetMode(Mode.Interior);
		}

		if (inputReady || Time.frameCount == switchFrame) return;
		if (keyboard != null && keyboard.anyKey.isPressed) return;

		Mouse mouse = Mouse.current;
		if (mouse != null && (mouse.leftButton.isPressed || mouse.rightButton.isPressed
			|| mouse.middleButton.isPressed || mouse.forwardButton.isPressed || mouse.backButton.isPressed)) return;

		inputReady = true;
	}

	public void SetMode(Mode mode)
	{
		if (mode != Mode.Exterior && mode != Mode.Interior) return;
		if (initialized && CurrentMode == mode) return;

		inputReady = false;
		switchFrame = Time.frameCount;
		if (tankInput != null) tankInput.SuspendInput();
		if (interaction != null) interaction.ClearSelection();
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
			tankCameras.ExitObservation();
		}

		if (interiorHud != null) interiorHud.SetActive(IsInterior);
		if (tankHud != null) tankHud.enabled = !IsInterior;
		if (modeLabel != null) modeLabel.text = IsInterior
			? "INTERIOR [F9]  |  F8 Exterior  |  1/2/3 Seat  |  Space Use  |  F7 Rear view"
			: "EXTERIOR [F8]  |  F9 Interior  |  1/2/3 Test role  |  Space Neutral  |  F1-F4 View";
	}

	private void OnDisable()
	{
		inputReady = false;
		if (tankInput != null) tankInput.SuspendInput();
		if (interaction != null) interaction.ClearSelection();
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
