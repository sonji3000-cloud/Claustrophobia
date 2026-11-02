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

	private bool inputReady;
	private bool spaceHeld;

	private void Awake()
	{
		if (control == null) control = GetComponent<TankControl>();
	}

	private void Update()
	{
		Keyboard keyboard = Keyboard.current;
		if (!Application.isFocused || keyboard == null)
		{
			if (inputReady) SuspendInput();
			inputReady = false;
			return;
		}

		if (!inputReady)
		{
			spaceHeld = keyboard.spaceKey.isPressed;
			if (keyboard.anyKey.isPressed) return;

			inputReady = true;
		}

		ReadKeys(keyboard);
	}

	private void ReadKeys(Keyboard keyboard)
	{
		ReadSeat(keyboard);

		if (interaction != null && interaction.isActiveAndEnabled) interaction.RefreshSelection();
		ReadUse(keyboard);

		if (control == null || !control.isActiveAndEnabled) return;

		float dt = Time.deltaTime;
		ReadLevers(keyboard, dt);

		if (testMode != null && testMode.IsInterior) return;

		// Apply aim before a weapon request so a shot uses this frame's direction.
		ReadAim(keyboard, dt);
		ReadWeapon(keyboard);

		ReadView(keyboard);
	}

	private void LateUpdate()
	{
		if (!inputReady || control == null || !control.isActiveAndEnabled || !Application.isFocused
			|| testMode != null && testMode.IsInterior) return;

		Mouse mouse = Mouse.current;
		if (mouse == null || !mouse.rightButton.isPressed) return;

		control.Look(mouse.delta.ReadValue());
	}

	public void SuspendInput()
	{
		if (interaction != null) interaction.StopUse();
		if (control != null)
		{
			control.EndDrive(interaction);
			control.ApplyAim(0f, 0f, 0f);
		}
	}

	private void ReadSeat(Keyboard keyboard)
	{
		if (seats == null || !seats.isActiveAndEnabled) return;

		if (keyboard.digit1Key.wasPressedThisFrame) seats.RequestSeat(TankCrew.Role.Driver);
		else if (keyboard.digit2Key.wasPressedThisFrame) seats.RequestSeat(TankCrew.Role.Gunner);
		else if (keyboard.digit3Key.wasPressedThisFrame) seats.RequestSeat(TankCrew.Role.Commander);
	}

	private void ReadUse(Keyboard keyboard)
	{
		bool pressed = keyboard.spaceKey.isPressed && !spaceHeld;
		spaceHeld = keyboard.spaceKey.isPressed;
		if (interaction == null || !interaction.isActiveAndEnabled) return;

		if (keyboard.escapeKey.wasPressedThisFrame)
		{
			interaction.StopUse();
			return;
		}
		if (!pressed) return;

		if (interaction.Active != null) interaction.StopUse();
		else interaction.TryUse(interaction.Selected);
	}

	private void ReadAim(Keyboard keyboard, float dt)
	{
		float turret = keyboard == null ? 0f
			: (keyboard.rightArrowKey.isPressed ? 1f : 0f) - (keyboard.leftArrowKey.isPressed ? 1f : 0f);
		float barrel = keyboard == null ? 0f
			: (keyboard.upArrowKey.isPressed ? 1f : 0f) - (keyboard.downArrowKey.isPressed ? 1f : 0f);

		control.ApplyAim(turret, barrel, dt);
	}

	private void ReadWeapon(Keyboard keyboard)
	{
		if (keyboard == null) return;

		if (keyboard.enterKey.wasPressedThisFrame) control.RequestFire();
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

		if (keyboard.f1Key.wasPressedThisFrame) control.SwitchView(0);
		else if (keyboard.f2Key.wasPressedThisFrame) control.SwitchView(1);
		else if (keyboard.f3Key.wasPressedThisFrame) control.SwitchView(2);
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
