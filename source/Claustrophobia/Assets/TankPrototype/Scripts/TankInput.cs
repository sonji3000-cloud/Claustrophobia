using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Translates prototype keyboard and mouse input into TankControl requests.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TankControl))]
public sealed class TankInput : MonoBehaviour
{
	[SerializeField, Min(0f)] private float leverChangeRate = 1f;
	[SerializeField] private TankControl control;

	private void Awake()
	{
		if (control == null) control = GetComponent<TankControl>();
	}

	private void Update()
	{
		if (control == null || !control.isActiveAndEnabled) return;

		Keyboard keyboard = Keyboard.current;
		float dt = Time.deltaTime;

		ReadRole(keyboard);

		// Apply aim before a weapon request so a shot uses this frame's direction.
		ReadAim(keyboard, dt);
		ReadWeapon(keyboard);
		ReadLevers(keyboard, dt);

		ReadView(keyboard);
	}

	private void LateUpdate()
	{
		if (control == null || !control.isActiveAndEnabled || !Application.isFocused) return;

		Mouse mouse = Mouse.current;
		if (mouse == null || !mouse.rightButton.isPressed) return;

		control.Look(mouse.delta.ReadValue());
	}

	private void ReadRole(Keyboard keyboard)
	{
		if (keyboard == null) return;

		if (keyboard.digit1Key.wasPressedThisFrame) control.SetTestRole(TankCrew.Role.Driver);
		else if (keyboard.digit2Key.wasPressedThisFrame) control.SetTestRole(TankCrew.Role.Gunner);
		else if (keyboard.digit3Key.wasPressedThisFrame) control.SetTestRole(TankCrew.Role.Commander);
	}

	private void ReadAim(Keyboard keyboard, float dt)
	{
		float turret = keyboard == null ? 0f
			: (keyboard.rightArrowKey.isPressed ? 1f : 0f)
			- (keyboard.leftArrowKey.isPressed ? 1f : 0f);
		float barrel = keyboard == null ? 0f
			: (keyboard.upArrowKey.isPressed ? 1f : 0f)
			- (keyboard.downArrowKey.isPressed ? 1f : 0f);

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
		if (keyboard == null) return;

		bool hasPreset = TryReadPreset(keyboard, out Vector2 preset);
		bool neutral = keyboard.spaceKey.isPressed;
		float step = leverChangeRate * dt;
		float left = control.LeftLever;
		float right = control.RightLever;

		if (neutral || keyboard.qKey.isPressed) left = 0f;
		else if (hasPreset) left = preset.x;
		else
		{
			float axis = (keyboard.wKey.isPressed ? 1f : 0f)
				- (keyboard.sKey.isPressed ? 1f : 0f);
			left += axis * step;
		}

		if (neutral || keyboard.uKey.isPressed) right = 0f;
		else if (hasPreset) right = preset.y;
		else
		{
			float axis = (keyboard.iKey.isPressed ? 1f : 0f)
				- (keyboard.kKey.isPressed ? 1f : 0f);
			right += axis * step;
		}

		control.SetLeverInputs(left, right);
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
}
