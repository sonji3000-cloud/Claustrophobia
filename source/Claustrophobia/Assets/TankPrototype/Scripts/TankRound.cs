using Fusion;
using UnityEngine;

/// <summary>A carried round, either authored in the scene or issued by an ammo rack.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CrewDevice))]
public sealed class TankRound : NetworkBehaviour
{
	[Networked, OnChangedRender(nameof(ApplyVisibility))]
	private NetworkBool Visible { get; set; }

	private CrewDevice device;
	private MeshRenderer mesh;
	private NetworkTransform networkTransform;
	private Transform home;
	private Vector3 homePos;
	private Quaternion homeRot;
	private Vector3 homeScale;
	private bool[] colliderStates;
	private bool issued;

	public CrewDevice Device => device;
	public CrewCarry Holder { get; private set; }
	public bool IsLoaded { get; private set; }

	internal void MarkIssued() => issued = true;

	private void Awake()
	{
		device = GetComponent<CrewDevice>();
		mesh = GetComponent<MeshRenderer>();
		networkTransform = GetComponent<NetworkTransform>();
		colliderStates = new bool[device.Parts.Length];
	}

	public override void Spawned()
	{
		foreach (Collider part in device.Parts)
			if (part != null) part.enabled = false;

		if (HasStateAuthority) Visible = true;
		ApplyVisibility();
	}

	internal void SetVisible(bool visible)
	{
		Visible = visible;
		ApplyVisibility();
	}

	internal void Seat(Vector3 position, Quaternion rotation) => networkTransform.Teleport(position, rotation);

	private void ApplyVisibility()
	{
		if (mesh != null) mesh.enabled = Visible;
	}

	internal bool TryHold(CrewCarry holder, Transform grip)
	{
		if (!isActiveAndEnabled || IsLoaded || Holder != null || holder == null || grip == null
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
		if (issued)
		{
			Consume();
			return;
		}

		transform.SetParent(home, false);
		transform.SetLocalPositionAndRotation(homePos, homeRot);
		transform.localScale = homeScale;

		for (int i = 0; i < device.Parts.Length; i++)
			if (device.Parts[i] != null) device.Parts[i].enabled = colliderStates[i];
	}

	internal void Load(CrewCarry holder, Transform loadPoint)
	{
		if (Holder != holder) return;

		Holder = null;
		IsLoaded = true;
		transform.SetParent(loadPoint, true);
		transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
	}

	internal void Consume()
	{
		gameObject.SetActive(false);
		if (issued) Destroy(gameObject);
	}

	private void OnDisable()
	{
		if (Holder != null) Holder.ReturnRound();
	}
}
