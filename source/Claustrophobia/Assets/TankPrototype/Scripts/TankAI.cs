using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(TankMotor), typeof(TankAim), typeof(TankGun))]
[RequireComponent(typeof(TankHealth))]
public sealed class TankAI : NetworkBehaviour
{
	[SerializeField] private TankMotor motor;
	[SerializeField] private TankAim aim;
	[SerializeField] private TankGun gun;
	[SerializeField] private TankHealth health;
	[SerializeField] private Rigidbody body;

	private float leftLever;
	private float rightLever;
	private Vector2 aimInput;

	public bool IsHost => Object != null && Object.IsValid
		&& Runner != null && Runner.IsRunning && Runner.IsServer && HasStateAuthority;
	public bool CanAct => IsHost && isActiveAndEnabled
		&& motor != null && motor.isActiveAndEnabled
		&& aim != null && aim.isActiveAndEnabled
		&& gun != null && gun.isActiveAndEnabled
		&& health != null && !health.Destroyed;
	public int TickCount { get; private set; }

	private void Awake()
	{
		if (motor == null) motor = GetComponent<TankMotor>();
		if (aim == null) aim = GetComponent<TankAim>();
		if (gun == null) gun = GetComponent<TankGun>();
		if (health == null) health = GetComponent<TankHealth>();
		if (body == null) body = GetComponent<Rigidbody>();
	}

	public override void Spawned()
	{
		TickCount = 0;
		leftLever = rightLever = 0f;
		aimInput = Vector2.zero;
		if (body != null) body.isKinematic = !IsHost;

		if (IsHost) StopActions();
	}

	public override void FixedUpdateNetwork()
	{
		if (!IsHost) return;

		TickCount++;
		if (!CanAct)
		{
			StopActions();
			return;
		}

		motor.SetLeverInputs(leftLever, rightLever);
		aim.ApplyHostInput(aimInput.x, aimInput.y);
	}

	public bool SetDrive(float left, float right)
	{
		if (!CanAct || !IsFinite(left) || !IsFinite(right)) return false;

		leftLever = Mathf.Clamp(left, -1f, 1f);
		rightLever = Mathf.Clamp(right, -1f, 1f);
		return true;
	}

	public bool SetAim(float turret, float barrel)
	{
		if (!CanAct || !IsFinite(turret) || !IsFinite(barrel)) return false;

		aimInput = new Vector2(Mathf.Clamp(turret, -1f, 1f), Mathf.Clamp(barrel, -1f, 1f));
		return true;
	}

	public bool RequestWeapon(TankInputButton action)
	{
		if (!CanAct) return false;

		return gun.RequestAI(action);
	}

	public void StopActions()
	{
		if (!IsHost) return;

		leftLever = rightLever = 0f;
		aimInput = Vector2.zero;
		if (motor != null) motor.Stop();
		if (aim != null) aim.ApplyHostInput(0f, 0f);
		if (gun != null) gun.ClearAIRequest();
	}

	private void OnDisable()
	{
		if (IsHost) StopActions();
	}

	private static bool IsFinite(float value)
	{
		return !float.IsNaN(value) && !float.IsInfinity(value);
	}
}
