using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Translates prototype keyboard and mouse input into TankControl requests.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(20)]
[RequireComponent(typeof(TankControl))]
public sealed class TankInput : MonoBehaviour
{
	[SerializeField, Min(0f)] private float leverChangeRate = 1f;
	[SerializeField] private TankControl control;
	[SerializeField] private TankTestMode testMode;
	[SerializeField] private CrewSeats seats;
	[SerializeField] private CrewInteract interaction;

	private TankCrewRegistry crewRegistry;
	private bool inputReady;
	private bool spaceHeld;
	private bool enterHeld;
	private bool bound;
	private bool disconnected;
	private uint inputVersion;

	public bool IsBound => bound;
	public bool IsInputReady => inputReady && control != null && inputVersion == control.InputVersion;
	public bool CanInput => isActiveAndEnabled && CanRead;
	private bool CanRead => !disconnected && control != null && control.CanInput;

	/// <summary>Invalid targets leave the previous binding unchanged.</summary>
	public bool Bind(CrewSeats nextSeats, CrewInteract nextInteraction, TankTestMode nextMode)
	{
		if (nextSeats == null || nextInteraction == null || nextMode == null
			|| nextSeats.gameObject != nextInteraction.gameObject) return false;
		if (seats == nextSeats && interaction == nextInteraction && testMode == nextMode && !disconnected)
		{
			bound = true;
			return true;
		}

		if (isActiveAndEnabled)
		{
			SuspendInput();
			if (testMode != null) testMode.StopSight();
		}
		else inputReady = false;
		seats = nextSeats;
		interaction = nextInteraction;
		testMode = nextMode;
		bound = true;
		disconnected = false;
		return true;
	}

	public void Unbind()
	{
		if (disconnected) return;
		SuspendInput();
		if (testMode != null) testMode.StopSight();
		seats = null;
		interaction = null;
		testMode = null;
		bound = false;
		disconnected = true;
	}

	private void Awake()
	{
		control = GetComponent<TankControl>();
		crewRegistry = GetComponent<TankCrewRegistry>();
	}

	private void Update()
	{
		if (!CanRead)
		{
			if (testMode != null) testMode.StopSight();
			if (inputReady) SuspendInput();
			return;
		}

		Keyboard keyboard = Keyboard.current;
		if (!Application.isFocused || keyboard == null)
		{
			if (inputReady) SuspendInput();
			inputReady = false;
			return;
		}

		ReadKeys(keyboard);
	}

	private void ReadKeys(Keyboard keyboard)
	{
		if (!CanRead)
		{
			if (testMode != null) testMode.StopSight();
			return;
		}
		if (keyboard == null) return;
		if (inputVersion != control.InputVersion)
		{
			inputVersion = control.InputVersion;
			inputReady = false;
		}

		if (!inputReady)
		{
			spaceHeld = keyboard.spaceKey.isPressed;
			enterHeld = keyboard.enterKey.isPressed;
			if (keyboard.anyKey.isPressed) return;

			inputReady = true;
		}

		bool firePressed = keyboard.enterKey.isPressed && !enterHeld;
		enterHeld = keyboard.enterKey.isPressed;

		ReadSeat(keyboard);

		if (interaction != null && interaction.isActiveAndEnabled) interaction.RefreshSelection();
		ReadUse(keyboard);

		if (control == null || !control.isActiveAndEnabled)
		{
			if (testMode != null) testMode.StopSight();
			return;
		}

		float dt = Time.deltaTime;
		ReadLevers(keyboard, dt);
		Vector2 aim = ReadAim(keyboard);

		if (testMode != null && testMode.IsInterior)
		{
			control.ApplyAim(interaction, aim.x, aim.y, dt);
			ReadZoom();
			if (firePressed) control.RequestFire(interaction);
			return;
		}

		// Apply local pitch before the weapon reads the muzzle direction.
		control.ApplyAim(aim.x, aim.y, dt);
		ReadWeapon(keyboard, firePressed);

		ReadView(keyboard);
	}

	private void LateUpdate()
	{
		if (!inputReady || !CanRead || !Application.isFocused) return;

		ReadLook(Mouse.current);
	}

	private void ReadLook(Mouse mouse)
	{
		if (!CanRead) return;

		if (testMode != null && testMode.IsInterior)
		{
			if (testMode.IsUsingSight && testMode.SetDrag(mouse != null && mouse.rightButton.isPressed))
				control.Look(mouse.delta.ReadValue());
			return;
		}

		if (mouse == null || !mouse.rightButton.isPressed) return;

		control.Look(mouse.delta.ReadValue());
	}

	public void SuspendInput()
	{
		ResetInput();
		if (interaction != null) interaction.StopUse();
		if (control != null)
		{
			control.EndDrive(interaction);
		}
	}

	public void ResetInput()
	{
		inputReady = false;
		if (control == null || !Application.isPlaying) return;

		control.EndAim(interaction);
		control.ApplyAim(0f, 0f, 0f);
	}

	private void ReadSeat(Keyboard keyboard)
	{
		if (crewRegistry != null && keyboard.f10Key.wasPressedThisFrame)
		{
			crewRegistry.RequestSwap();
			return;
		}

		TankCrew.Role role;
		if (keyboard.digit1Key.wasPressedThisFrame) role = TankCrew.Role.Driver;
		else if (keyboard.digit2Key.wasPressedThisFrame) role = TankCrew.Role.Gunner;
		else if (keyboard.digit3Key.wasPressedThisFrame) role = TankCrew.Role.Commander;
		else return;

		if (crewRegistry != null) crewRegistry.RequestSeat(role);
		else if (seats != null && seats.isActiveAndEnabled) seats.RequestSeat(role);
	}

	private void ReadUse(Keyboard keyboard)
	{
		bool pressed = keyboard.spaceKey.isPressed && !spaceHeld;
		spaceHeld = keyboard.spaceKey.isPressed;
		if (interaction == null || !interaction.isActiveAndEnabled) return;

		if (keyboard.escapeKey.wasPressedThisFrame)
		{
			if (interaction.Active != null) interaction.StopUse();
			else if (interaction.Carry != null) interaction.Carry.ReturnRound();
			return;
		}
		if (!pressed) return;

		if (interaction.Active != null) interaction.StopUse();
		else interaction.TryUse(interaction.Selected);
	}

	private static Vector2 ReadAim(Keyboard keyboard)
	{
		float turret = keyboard == null ? 0f
			: (keyboard.rightArrowKey.isPressed ? 1f : 0f) - (keyboard.leftArrowKey.isPressed ? 1f : 0f);
		float barrel = keyboard == null ? 0f
			: (keyboard.upArrowKey.isPressed ? 1f : 0f) - (keyboard.downArrowKey.isPressed ? 1f : 0f);

		return new Vector2(turret, barrel);
	}

	private void ReadZoom()
	{
		Mouse mouse = Mouse.current;
		if (mouse == null) return;

		// The project's Input System normalizes each scroll tick to one unit.
		float steps = mouse.scroll.ReadValue().y;
		if (steps != 0f) control.ZoomSight(interaction, steps);
	}

	private void ReadWeapon(Keyboard keyboard, bool firePressed)
	{
		if (keyboard == null) return;

		if (firePressed) control.RequestFire(interaction);
		else if (keyboard.rKey.wasPressedThisFrame) control.RequestInsert();
		else if (keyboard.tKey.wasPressedThisFrame) control.RequestClose();
		else if (keyboard.yKey.wasPressedThisFrame) control.RequestOpenEmpty();
	}

	private void ReadLevers(Keyboard keyboard, float dt)
	{
		if (keyboard == null || !control.CanDrive(interaction)) return;

		bool hasPreset = TryReadPreset(keyboard, out Vector2 preset);
		float step = leverChangeRate * dt;
		float left = control.LeftLever;
		float right = control.RightLever;

		if (keyboard.qKey.isPressed) left = 0f;
		else if (hasPreset) left = preset.x;
		else
		{
			float axis = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
			left += axis * step;
		}

		if (keyboard.uKey.isPressed) right = 0f;
		else if (hasPreset) right = preset.y;
		else
		{
			float axis = (keyboard.iKey.isPressed ? 1f : 0f) - (keyboard.kKey.isPressed ? 1f : 0f);
			right += axis * step;
		}

		control.SetLeverInputs(interaction, left, right);
	}

	private static bool TryReadPreset(Keyboard keyboard, out Vector2 levers)
	{
		if (keyboard.numpad1Key.wasPressedThisFrame) levers = new Vector2(1f, 1f);
		else if (keyboard.numpad2Key.wasPressedThisFrame) levers = new Vector2(1f, 0.3f);
		else if (keyboard.numpad3Key.wasPressedThisFrame) levers = new Vector2(1f, -1f);
		else if (keyboard.numpad4Key.wasPressedThisFrame) levers = new Vector2(-1f, 1f);
		else if (keyboard.numpad5Key.wasPressedThisFrame) levers = new Vector2(-1f, -1f);
		else if (keyboard.numpad6Key.wasPressedThisFrame) levers = new Vector2(-1f, -0.3f);
		else
		{
			levers = Vector2.zero;
			return false;
		}

		return true;
	}

	private void ReadView(Keyboard keyboard)
	{
		if (keyboard == null) return;

		if (keyboard.f3Key.wasPressedThisFrame) control.SwitchView(2);
		else if (keyboard.f4Key.wasPressedThisFrame) control.SwitchView(3);
	}

	private void OnEnable()
	{
		inputReady = false;
	}

	private void OnApplicationFocus(bool focused)
	{
		inputReady = false;
		if (!focused) SuspendInput();
	}

	private void OnDisable()
	{
		inputReady = false;
		SuspendInput();
	}
}
