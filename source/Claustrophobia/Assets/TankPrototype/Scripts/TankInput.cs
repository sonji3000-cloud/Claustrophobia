using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Reads the prototype's keyboard and mouse controls in one place.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TankMotor))]
public sealed class TankInput : MonoBehaviour
{
	[SerializeField, Min(0f)] private float leverChangeRate = 1f;
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
		Keyboard keyboard = Keyboard.current;
		float dt = Time.deltaTime;

		// Keep aim ahead of recovery and weapon requests, as in the original driver.
		ReadAim(keyboard, dt);
		ReadWeapon(keyboard);
		ReadLevers(keyboard, dt);
		motor.SetLeverInputs(LeftLever, RightLever);

		ReadView(keyboard);
	}

	private void LateUpdate()
	{
		if (cameras == null || !Application.isFocused) return;

		Mouse mouse = Mouse.current;
		if (mouse == null || !mouse.rightButton.isPressed) return;

		cameras.Look(mouse.delta.ReadValue());
	}

	private void ReadAim(Keyboard keyboard, float dt)
	{
		if (aim == null) return;

		float turret = keyboard == null ? 0f
			: (keyboard.rightArrowKey.isPressed ? 1f : 0f)
			- (keyboard.leftArrowKey.isPressed ? 1f : 0f);
		float barrel = keyboard == null ? 0f
			: (keyboard.upArrowKey.isPressed ? 1f : 0f)
			- (keyboard.downArrowKey.isPressed ? 1f : 0f);

		aim.ApplyInput(turret, barrel, dt);
	}

	private void ReadWeapon(Keyboard keyboard)
	{
		if (gun == null) return;

		// Recovery also advances when no keyboard is connected.
		gun.TickRecovery();
		if (keyboard == null) return;

		if (keyboard.enterKey.wasPressedThisFrame) gun.RequestFire();
		else if (keyboard.rKey.wasPressedThisFrame) gun.RequestInsert();
		else if (keyboard.tKey.wasPressedThisFrame) gun.RequestClose();
		else if (keyboard.yKey.wasPressedThisFrame) gun.RequestOpenEmpty();
	}

	private void ReadLevers(Keyboard keyboard, float dt)
	{
		if (keyboard == null) return;

		bool hasPreset = TryReadPreset(keyboard, out Vector2 preset);
		bool neutral = keyboard.spaceKey.isPressed;
		float step = leverChangeRate * dt;

		if (neutral || keyboard.qKey.isPressed) LeftLever = 0f;
		else if (hasPreset) LeftLever = preset.x;
		else
		{
			float axis = (keyboard.wKey.isPressed ? 1f : 0f)
				- (keyboard.sKey.isPressed ? 1f : 0f);
			LeftLever = Mathf.Clamp(LeftLever + axis * step, -1f, 1f);
		}

		if (neutral || keyboard.uKey.isPressed) RightLever = 0f;
		else if (hasPreset) RightLever = preset.y;
		else
		{
			float axis = (keyboard.iKey.isPressed ? 1f : 0f)
				- (keyboard.kKey.isPressed ? 1f : 0f);
			RightLever = Mathf.Clamp(RightLever + axis * step, -1f, 1f);
		}
	}

	private static bool TryReadPreset(Keyboard keyboard, out Vector2 levers)
	{
		if (keyboard.digit1Key.wasPressedThisFrame) levers = new Vector2(1f, 1f);
		else if (keyboard.digit2Key.wasPressedThisFrame) levers = new Vector2(1f, 0.3f);
		else if (keyboard.digit3Key.wasPressedThisFrame) levers = new Vector2(1f, -1f);
		else if (keyboard.digit4Key.wasPressedThisFrame) levers = new Vector2(-1f, 1f);
		else if (keyboard.digit5Key.wasPressedThisFrame) levers = new Vector2(-1f, -1f);
		else if (keyboard.digit6Key.wasPressedThisFrame) levers = new Vector2(-1f, -0.3f);
		else
		{
			levers = Vector2.zero;
			return false;
		}

		return true;
	}

	private void ReadView(Keyboard keyboard)
	{
		if (keyboard == null || cameras == null || !cameras.isActiveAndEnabled) return;

		if (keyboard.f1Key.wasPressedThisFrame) cameras.SwitchView(0);
		else if (keyboard.f2Key.wasPressedThisFrame) cameras.SwitchView(1);
		else if (keyboard.f3Key.wasPressedThisFrame) cameras.SwitchView(2);
		else if (keyboard.f4Key.wasPressedThisFrame) cameras.SwitchView(3);
	}
}
