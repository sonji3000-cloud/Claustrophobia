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

	private void Awake()
	{
		if (handle == null || handle.parent != transform) return;

		pos = handle.localPosition;
		rot = handle.localRotation;
		initialized = true;
	}

	private void Update()
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

	private void OnDisable()
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
