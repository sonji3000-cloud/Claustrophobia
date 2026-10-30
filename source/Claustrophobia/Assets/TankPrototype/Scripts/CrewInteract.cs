using System;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[DefaultExecutionOrder(10)]
public sealed class CrewInteract : MonoBehaviour
{
	public enum Reason { Ready, Disabled, Role, Busy, Range, Behind, Outside, Blocked, Layer }

	public struct Candidate
	{
		public Reason Reason;
		public Vector3 Point;
		public float Offset;
		public float Distance;
		public bool Direct;
	}

	[SerializeField] private Camera view;
	[SerializeField] private TankCrew crew;
	[SerializeField] private CrewSeats seats;
	[SerializeField] private CrewDebugView debugView;
	[SerializeField] private CrewDevice[] devices = Array.Empty<CrewDevice>();
	[SerializeField, Min(0.1f)] private float reach = 2f;
	[Tooltip("Radius as a fraction of the camera's pixel height.")]
	[SerializeField, Range(0.01f, 0.25f)] private float radius = 0.05f;
	[SerializeField, Range(1f, 2f)] private float retainScale = 1.3f;
	[SerializeField] private LayerMask blockers = Physics.DefaultRaycastLayers;
	[SerializeField] private LayerMask targets;

	private Candidate[] candidates;
	private CrewDevice highlighted;
	private TankCrew.Role activeRole;
	private bool spaceHeld;
	private RaycastHit centerHit;

	public Camera View => view;
	public TankCrew Crew => crew;
	public CrewDevice[] Devices => devices;
	public Candidate[] Candidates => candidates;
	public float Radius => radius;
	public float RetainRadius => radius * retainScale;
	public float Reach => GetReach(Active != null ? Active : Selected);
	public CrewDevice Selected { get; private set; }
	public CrewDevice Active { get; private set; }
	public bool IsObserving => debugView != null && debugView.IsObserving;
	public event Action<CrewDevice, bool> UseChanged;

	private void Awake()
	{
		candidates = new Candidate[devices.Length];
	}

	private void OnEnable()
	{
		if (seats != null) seats.SeatApplied += OnSeatApplied;
		spaceHeld = Keyboard.current != null && Keyboard.current.spaceKey.isPressed;
	}

	private void LateUpdate()
	{
		RefreshSelection();
		if (Application.isFocused) ReadKeys(Keyboard.current);
		else spaceHeld = Keyboard.current != null && Keyboard.current.spaceKey.isPressed;
	}

	public void RefreshSelection()
	{
		if (Active != null && (!Active.CanUse(crew) || !HasTarget(Active)
			|| Active.User != this || crew.CurrentRole != activeRole)) StopUse();

		CrewDevice best = null;
		float bestOffset = float.PositiveInfinity;
		float bestDistance = float.PositiveInfinity;
		bool bestDirect = false;
		Ray ray = view != null ? new Ray(view.transform.position, view.transform.forward) : default;
		bool hitCenter = view != null && HitCenter(ray);

		for (int i = 0; i < devices.Length; i++)
		{
			CrewDevice device = devices[i];
			Candidate candidate = Evaluate(device, ray, hitCenter ? centerHit.collider : null);
			candidates[i] = candidate;
			if (candidate.Reason != Reason.Ready) continue;

			float offset = candidate.Offset;
			// Prefer the current device around a boundary without overriding a direct hit.
			if (device == Selected) offset /= retainScale;
			if (best != null && (!candidate.Direct && bestDirect
				|| candidate.Direct == bestDirect && (offset > bestOffset + .0001f
				|| Mathf.Abs(offset - bestOffset) <= .0001f && candidate.Distance >= bestDistance))) continue;

			best = device;
			bestOffset = offset;
			bestDistance = candidate.Distance;
			bestDirect = candidate.Direct;
		}

		Selected = best;
		SetHighlight(Active != null ? Active : Selected);
	}

	private Candidate Evaluate(CrewDevice device, Ray center, Collider direct)
	{
		Candidate result = new Candidate { Reason = Reason.Disabled, Offset = float.PositiveInfinity };
		if (view == null || device == null || !device.isActiveAndEnabled) return result;

		if (device.Parts.Length > 0 && device.Parts[0] != null) result.Point = device.Parts[0].bounds.center;
		if (!HasTarget(device))
		{
			result.Reason = Reason.Layer;
			return result;
		}

		if (!device.CanUse(crew))
		{
			result.Reason = Reason.Role;
			return result;
		}

		if (device.User != null && device.User != this)
		{
			result.Reason = Reason.Busy;
			return result;
		}

		float range = GetReach(device);
		if (direct != null && centerHit.distance <= range && device.Contains(direct))
			return new Candidate { Reason = Reason.Ready, Point = centerHit.point, Distance = centerHit.distance, Direct = true };

		result.Reason = Reason.Range;
		float limit = radius * (device == Selected ? retainScale : 1f);
		Collider[] parts = device.Parts;
		for (int i = 0; i < parts.Length; i++)
		{
			Collider part = parts[i];
			if (!IsTarget(part)) continue;

			SamplePart(part, center, limit, range, ref result);
		}

		return result;
	}

	private float GetReach(CrewDevice device) => device != null && device.Reach > 0f ? device.Reach : reach;

	private bool HasTarget(CrewDevice device)
	{
		if ((targets.value & (1 << device.gameObject.layer)) == 0) return false;

		for (int i = 0; i < device.Parts.Length; i++)
			if (IsTarget(device.Parts[i])) return true;

		return false;
	}

	private bool IsTarget(Collider part)
	{
		return part != null && part.enabled && !part.isTrigger && part.gameObject.activeInHierarchy
			&& (targets.value & (1 << part.gameObject.layer)) != 0;
	}

	private bool HitCenter(Ray ray)
	{
		float range = reach;
		for (int i = 0; i < devices.Length; i++)
		{
			CrewDevice device = devices[i];
			if (device != null && device.CanUse(crew) && HasTarget(device)) range = Mathf.Max(range, GetReach(device));
		}

		return Physics.Raycast(ray, out centerHit, range, targets, QueryTriggerInteraction.Ignore)
			&& Physics.Raycast(ray, out RaycastHit first, centerHit.distance + .01f, blockers, QueryTriggerInteraction.Ignore)
			&& first.collider == centerHit.collider;
	}

	private void SamplePart(Collider part, Ray center, float limit, float range, ref Candidate result)
	{
		Bounds bounds = part.bounds;
		// Sample the central ray, center and inset corners to cover partial visibility.
		for (int i = 0; i < 10; i++)
		{
			Vector3 delta = GetSample(bounds, center, i) - view.transform.position;
			if (delta.sqrMagnitude < .000001f) continue;

			Ray ray = new Ray(view.transform.position, delta.normalized);
			if (part.Raycast(ray, out RaycastHit surface, range)) CheckSurface(part, ray, surface, limit, ref result);
		}
	}

	private static Vector3 GetSample(Bounds bounds, Ray center, int index)
	{
		if (index == 0) return bounds.ClosestPoint(center.GetPoint(Vector3.Dot(bounds.center - center.origin, center.direction)));
		if (index == 1) return bounds.center;

		int corner = index - 2;
		Vector3 dir = new Vector3((corner & 1) == 0 ? -1f : 1f,
			(corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f);
		return bounds.center + Vector3.Scale(bounds.extents * .85f, dir);
	}

	private void CheckSurface(Collider part, Ray ray, RaycastHit surface, float limit, ref Candidate result)
	{
		Vector3 viewport = view.WorldToViewportPoint(surface.point);
		if (viewport.z <= view.nearClipPlane)
		{
			if (result.Reason == Reason.Range) result.Reason = Reason.Behind;
			return;
		}

		float offset = new Vector2((viewport.x - .5f) * view.aspect, viewport.y - .5f).magnitude;
		if (result.Reason != Reason.Ready)
		{
			result.Point = surface.point;
			result.Offset = offset;
			result.Distance = surface.distance;
		}

		if (offset > limit)
		{
			if (result.Reason != Reason.Ready && result.Reason != Reason.Blocked) result.Reason = Reason.Outside;
			return;
		}

		if (!Physics.Raycast(ray, out RaycastHit first, surface.distance + .01f, blockers, QueryTriggerInteraction.Ignore)
			|| first.collider != part)
		{
			if (result.Reason != Reason.Ready) result.Reason = Reason.Blocked;
			return;
		}

		if (result.Reason == Reason.Ready && offset >= result.Offset) return;
		result = new Candidate { Reason = Reason.Ready, Point = surface.point, Offset = offset, Distance = surface.distance };
	}

	private void ReadKeys(Keyboard keyboard)
	{
		if (keyboard == null) return;

		bool pressed = keyboard.spaceKey.isPressed && !spaceHeld;
		spaceHeld = keyboard.spaceKey.isPressed;
		if (keyboard.escapeKey.wasPressedThisFrame)
		{
			StopUse();
			return;
		}
		if (!pressed) return;

		if (Active != null) StopUse();
		else TryUse(Selected);
	}

	/// <summary>Begins local use of the currently selected device after revalidation.</summary>
	public bool TryUse(CrewDevice device)
	{
		if (!isActiveAndEnabled || Active != null || device == null) return false;

		RefreshSelection();
		if (Selected != device || !device.TryBegin(this)) return false;

		Active = device;
		activeRole = crew.CurrentRole;
		SetHighlight(device);
		device.ReportUse(this, true);
		UseChanged?.Invoke(device, true);
		return Active == device;
	}

	public void StopUse()
	{
		if (Active == null) return;

		CrewDevice device = Active;
		Active = null;
		device.EndUse(this);
		SetHighlight(null);
		device.ReportUse(this, false);
		UseChanged?.Invoke(device, false);
	}

	private void SetHighlight(CrewDevice device)
	{
		if (highlighted == device) return;

		if (highlighted != null) highlighted.SetHighlight(false);
		highlighted = device;
		if (highlighted != null) highlighted.SetHighlight(true);
	}

	private void OnSeatApplied(TankCrew.Role role) => StopUse();

	private void OnDrawGizmosSelected()
	{
		for (int i = 0; i < devices.Length; i++)
		{
			if (devices[i] == null) continue;
			Gizmos.color = Color.cyan;
			foreach (Collider part in devices[i].Parts)
				if (part != null) Gizmos.DrawWireCube(part.bounds.center, part.bounds.size);

			if (!Application.isPlaying || candidates == null || view == null) continue;
			Gizmos.color = candidates[i].Reason == Reason.Ready ? Color.green : Color.red;
			Gizmos.DrawLine(view.transform.position, candidates[i].Point);
			Gizmos.DrawWireSphere(candidates[i].Point, .025f);
		}
	}

	private void OnDisable()
	{
		if (seats != null) seats.SeatApplied -= OnSeatApplied;
		StopUse();
		Selected = null;
		SetHighlight(null);
	}
}
