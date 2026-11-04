using UnityEngine;

/// <summary>A scene round that can be carried and returned to its rack.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CrewDevice))]
public sealed class TankRound : MonoBehaviour
{
	private CrewDevice device;
	private Transform home;
	private Vector3 homePos;
	private Quaternion homeRot;
	private Vector3 homeScale;
	private bool[] colliderStates;

	public CrewDevice Device => device;
	public CrewCarry Holder { get; private set; }

	private void Awake()
	{
		device = GetComponent<CrewDevice>();
		colliderStates = new bool[device.Parts.Length];
	}

	internal bool TryHold(CrewCarry holder, Transform grip)
	{
		if (!isActiveAndEnabled || Holder != null || holder == null || grip == null
			|| device == null || device.User != null) return false;

		home = transform.parent;
		homePos = transform.localPosition;
		homeRot = transform.localRotation;
		homeScale = transform.localScale;

		for (int i = 0; i < device.Parts.Length; i++)
		{
			Collider part = device.Parts[i];
			colliderStates[i] = part != null && part.enabled;
			if (part != null) part.enabled = false;
		}
		device.SetHighlight(false);

		Holder = holder;
		transform.SetParent(grip, true);
		transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
		return true;
	}

	internal void Return(CrewCarry holder)
	{
		if (Holder != holder) return;

		Holder = null;
		transform.SetParent(home, false);
		transform.SetLocalPositionAndRotation(homePos, homeRot);
		transform.localScale = homeScale;

		for (int i = 0; i < device.Parts.Length; i++)
			if (device.Parts[i] != null) device.Parts[i].enabled = colliderStates[i];
	}

	private void OnDisable()
	{
		if (Holder != null) Holder.ReturnRound();
	}
}
