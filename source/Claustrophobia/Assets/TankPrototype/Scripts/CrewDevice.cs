using System;
using UnityEngine;

/// <summary>A logical device may have several separate selectable parts.</summary>
[DisallowMultipleComponent]
public sealed class CrewDevice : MonoBehaviour
{
	[Flags]
	public enum Roles { None = 0, Driver = 1, Gunner = 2, Commander = 4, All = 7 }

	[SerializeField] private string label = "Device";
	[SerializeField] private Roles roles = Roles.All;
	[Tooltip("Non-VR selection distance. Zero uses the player's default.")]
	[SerializeField, Min(0f)] private float reach;
	[SerializeField] private Collider[] parts = Array.Empty<Collider>();
	[SerializeField] private Renderer[] outlines = Array.Empty<Renderer>();

	public string Label => label;
	public float Reach => reach;
	public Collider[] Parts => parts;
	public CrewInteract User { get; private set; }
	public event Action<CrewInteract, bool> UseChanged;

	public bool CanUse(TankCrew crew)
	{
		if (!isActiveAndEnabled || crew == null || crew.CurrentRole == TankCrew.Role.None) return false;

		return ((int)roles & (1 << ((int)crew.CurrentRole - 1))) != 0;
	}

	public bool Contains(Collider part)
	{
		for (int i = 0; i < parts.Length; i++)
			if (parts[i] == part) return true;

		return false;
	}

	// Local ownership only. This does not grant network authority.
	internal bool TryBegin(CrewInteract user)
	{
		if (user == null || !CanUse(user.Crew) || User != null) return false;

		User = user;
		return true;
	}

	internal void EndUse(CrewInteract user)
	{
		if (User == user) User = null;
	}

	internal void ReportUse(CrewInteract user, bool active) => UseChanged?.Invoke(user, active);

	public void SetHighlight(bool visible)
	{
		for (int i = 0; i < outlines.Length; i++)
			if (outlines[i] != null) outlines[i].enabled = visible;
	}

	private void OnDisable()
	{
		if (User != null) User.StopUse();
		SetHighlight(false);
	}
}
