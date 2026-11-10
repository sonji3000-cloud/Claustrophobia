using UnityEngine;

/// <summary>Turns the seated crew's head toward the view without moving the body.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public sealed class CrewHeadIK : MonoBehaviour
{
	[SerializeField] private Animator anim;
	[SerializeField] private CrewLook look;

	[SerializeField, Range(0f, 1f)] private float weight = 1f;
	[SerializeField, Range(0f, 89f)] private float maxYaw = 75f;
	[SerializeField, Range(-89f, 0f)] private float minPitch = -40f;
	[SerializeField, Range(0f, 89f)] private float maxPitch = 55f;
	[SerializeField, Min(1f)] private float turnSpeed = 360f;
	[SerializeField, Min(1f)] private float distance = 5f;

	private Quaternion rot = Quaternion.identity;

	private void Awake()
	{
		if (anim == null) anim = GetComponent<Animator>();
		if (look == null) look = GetComponentInParent<CrewLook>();
	}

	private void OnEnable()
	{
		rot = Quaternion.identity;
	}

	private void Update()
	{
		if (look == null || look.Pivot == null) return;

		Vector3 dir = look.transform.InverseTransformDirection(look.Pivot.forward);
		float yaw = Mathf.Clamp(Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, -maxYaw, maxYaw);
		float pitch = Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg,
			minPitch, maxPitch);

		Quaternion target = Quaternion.Euler(pitch, yaw, 0f);
		rot = Quaternion.RotateTowards(rot, target, turnSpeed * Time.deltaTime);
	}

	private void OnAnimatorIK(int layer)
	{
		if (anim == null || !anim.isHuman) return;

		bool canLook = look != null && look.isActiveAndEnabled && look.Pivot != null;
		anim.SetLookAtWeight(canLook ? weight : 0f, 0f, 1f, 0f, 0f);
		if (!canLook) return;

		Vector3 dir = look.transform.TransformDirection(rot * Vector3.forward);
		anim.SetLookAtPosition(look.Pivot.position + dir * distance);
	}
}
