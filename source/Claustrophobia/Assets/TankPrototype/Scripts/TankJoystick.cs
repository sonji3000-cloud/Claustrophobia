using UnityEngine;

/// <summary>Tilts the joystick and its grip target from the active gunner's aim input.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(30)]
public sealed class TankJoystick : MonoBehaviour
{
	[SerializeField] private TankControl control;
	[SerializeField] private TankAim aim;
	[SerializeField] private CrewDevice device;
	[SerializeField] private Transform handle;
	[Tooltip("Socket position in this joystick's local space. The handle must be a direct child.")]
	[SerializeField] private Vector3 pivot;
	[Tooltip("Maximum tilt in degrees, including diagonal input.")]
	[SerializeField, Range(0f, 30f)] private float angle = 8f;

	private Vector3 pos;
	private Quaternion rot;
	private Vector2 lastInput = new Vector2(float.NaN, float.NaN);
	private float lastAngle = float.NaN;
	private bool initialized;

	public TankControl Control => control;
	public TankAim Aim => aim;

	/// <summary>Connects only control when the existing aim reference is explicitly deferred.</summary>
	public bool BindControl(TankControl next)
	{
		if (next == null || aim != null && aim.gameObject != next.gameObject) return false;
		if (control == next) return true;
		control = next;
		if (isActiveAndEnabled) ApplyHandle();
		return true;
	}

	public void UnbindControl()
	{
		control = null;
		if (isActiveAndEnabled) RestoreHandle();
	}

	public bool Bind(TankControl nextControl, TankAim nextAim)
	{
		if (nextControl == null || nextAim == null || nextControl.gameObject != nextAim.gameObject) return false;
		if (control == nextControl && aim == nextAim) return true;

		control = nextControl;
		aim = nextAim;
		if (isActiveAndEnabled) ApplyHandle();
		return true;
	}

	public void Unbind()
	{
		control = null;
		aim = null;
		if (isActiveAndEnabled) RestoreHandle();
	}

	private void Awake()
	{
		if (handle == null || handle.parent != transform) return;

		pos = handle.localPosition;
		rot = handle.localRotation;
		initialized = true;
	}

	private void OnEnable() => ApplyHandle();

	private void Update() => ApplyHandle();

	private void ApplyHandle()
	{
		if (!initialized || handle == null) return;

		Vector2 input = Vector2.zero;
		if (control != null && aim != null && aim.isActiveAndEnabled
			&& device != null && control.AimDevice == device && control.CanAim(device.User))
			input = Vector2.ClampMagnitude(new Vector2(aim.TurretInput, aim.GunInput), 1f);

		if (input == lastInput && angle == lastAngle) return;

		lastInput = input;
		lastAngle = angle;
		Quaternion tilt = Quaternion.AngleAxis(input.magnitude * angle,
			new Vector3(input.y, 0f, -input.x));
		handle.SetLocalPositionAndRotation(pivot + tilt * (pos - pivot), tilt * rot);
	}

	private void OnDisable() => RestoreHandle();

	private void RestoreHandle()
	{
		if (initialized && handle != null) handle.SetLocalPositionAndRotation(pos, rot);

		lastInput = new Vector2(float.NaN, float.NaN);
	}

	private void OnDrawGizmosSelected()
	{
		Gizmos.color = Color.yellow;
		Gizmos.DrawWireSphere(transform.TransformPoint(pivot), .015f);
	}
}
