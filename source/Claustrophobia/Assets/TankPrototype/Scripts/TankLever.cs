using UnityEngine;

/// <summary>Tilts a lever handle around its socket from the matching drive input.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(30)]
public sealed class TankLever : MonoBehaviour
{
	[SerializeField] private TankControl control;
	[SerializeField] private Transform handle;
	[SerializeField] private bool right;
	[Tooltip("Socket position in this lever's local space. The handle must be a direct child.")]
	[SerializeField] private Vector3 pivot;
	[Tooltip("Degrees at full input. Keep the grip within the character's arm reach.")]
	[SerializeField, Range(0f, 30f)] private float angle = 6f;

	private Vector3 pos;
	private Quaternion rot;
	private float lastInput = float.NaN;
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

		float input = control != null ? (right ? control.RightLever : control.LeftLever) : 0f;
		if (input == lastInput && angle == lastAngle) return;

		lastInput = input;
		lastAngle = angle;
		Quaternion tilt = Quaternion.AngleAxis(input * angle, Vector3.right);
		handle.SetLocalPositionAndRotation(pivot + tilt * (pos - pivot), tilt * rot);
	}

	private void OnDisable()
	{
		if (initialized && handle != null) handle.SetLocalPositionAndRotation(pos, rot);

		lastInput = float.NaN;
	}

	private void OnDrawGizmosSelected()
	{
		Gizmos.color = Color.yellow;
		Gizmos.DrawWireSphere(transform.TransformPoint(pivot), .025f);
	}
}
