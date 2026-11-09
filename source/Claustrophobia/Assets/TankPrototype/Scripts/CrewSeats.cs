using System;
using UnityEngine;

/// <summary>Requests a seat and applies an approved position and local role together.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(TankCrew))]
public sealed class CrewSeats : MonoBehaviour
{
	[SerializeField] private TankCrew crew;
	[SerializeField] private CrewLook look;
	[SerializeField] private CrewDebugView debugView;
	[SerializeField] private Transform driver;
	[SerializeField] private Transform gunner;
	[SerializeField] private Transform commander;

	[Tooltip("Local verification only. Turn off when an external approver handles SeatRequested.")]
	[SerializeField] private bool localSeats = true;
	[SerializeField] private TankCrew.Role startSeat = TankCrew.Role.Driver;

	private uint nextRequest;
	private bool started;

	public TankCrew.Role CurrentRole => crew != null ? crew.CurrentRole : TankCrew.Role.None;
	public TankCrew.Role RequestedRole { get; private set; }
	public uint PendingRequest { get; private set; }
	public Transform Anchor { get; private set; }
	public TankCrew Crew => crew;
	public bool IsInitialized => started;

	// Subscribe with localSeats disabled. The approver returns this request ID to ResolveSeat.
	public event Action<TankCrew.Role, uint> SeatRequested;
	public event Action<TankCrew.Role> SeatApplied;

	private void Awake()
	{
		CacheRefs();
	}

	private void CacheRefs()
	{
		if (crew == null) crew = GetComponent<TankCrew>();
		if (look == null) look = GetComponent<CrewLook>();
	}

	private void Start() => Initialize();

	/// <summary>Call after the interior's Awake phase. Repeated calls preserve the selected seat.</summary>
	public bool Initialize()
	{
		if (started) return true;
		CacheRefs();
		if (!isActiveAndEnabled || crew == null || crew.gameObject != gameObject || !crew.isActiveAndEnabled) return false;
		if (localSeats && !CanUseAnchor(startSeat)) return false;

		started = true;
		if (localSeats) RequestSeat(startSeat);
		return true;
	}

	public bool ValidateRefs(out string reason)
	{
		if (crew == null || crew.gameObject != gameObject)
		{
			reason = "CrewSeats requires its own TankCrew.";
			return false;
		}
		if (driver == null || gunner == null || commander == null)
		{
			reason = "CrewSeats requires all three seat anchors.";
			return false;
		}
		if (driver.IsChildOf(transform) || gunner.IsChildOf(transform) || commander.IsChildOf(transform))
		{
			reason = "Seat anchors must be outside the player hierarchy.";
			return false;
		}

		reason = null;
		return true;
	}

	private void OnEnable()
	{
		if (started && localSeats) RequestSeat(startSeat);
	}

	/// <summary>Returns whether a request was accepted for processing, not whether it was approved.</summary>
	public bool RequestSeat(TankCrew.Role role)
	{
		if (!CanApply(role) || (Anchor != null && role == CurrentRole && PendingRequest == 0)
			|| (PendingRequest != 0 && role == RequestedRole)) return false;

		nextRequest++;
		if (nextRequest == 0) nextRequest = 1;

		PendingRequest = nextRequest;
		RequestedRole = role;

		if (localSeats) ResolveSeat(PendingRequest, true);
		else SeatRequested?.Invoke(role, PendingRequest);

		return true;
	}

	/// <summary>Applies only the latest pending approval. Denial leaves the current seat unchanged.</summary>
	public bool ResolveSeat(uint request, bool approved)
	{
		if (!isActiveAndEnabled || request == 0 || request != PendingRequest) return false;

		TankCrew.Role role = RequestedRole;
		PendingRequest = 0;
		RequestedRole = TankCrew.Role.None;
		return approved && ApplyRole(role);
	}

	public bool ApplyRole(TankCrew.Role role)
	{
		if (!started || !isActiveAndEnabled || crew == null || !crew.isActiveAndEnabled) return false;

		Transform anchor = role == TankCrew.Role.None ? null : GetAnchor(role);
		if (role != TankCrew.Role.None && !CanUseAnchor(anchor)) return false;
		if (role == CurrentRole && anchor == Anchor) return true;

		PendingRequest = 0;
		RequestedRole = TankCrew.Role.None;
		if (anchor != null) transform.SetPositionAndRotation(anchor.position, anchor.rotation);
		crew.SetRole(role);
		Anchor = anchor;
		if (look != null) look.ResetLook();

		SeatApplied?.Invoke(role);
		return true;
	}

	private bool CanApply(TankCrew.Role role)
	{
		return started && CanUseAnchor(role);
	}

	private bool CanUseAnchor(TankCrew.Role role) => CanUseAnchor(GetAnchor(role));

	private bool CanUseAnchor(Transform anchor)
	{
		return isActiveAndEnabled && crew != null && crew.gameObject == gameObject
			&& crew.isActiveAndEnabled && anchor != null && anchor.gameObject.activeInHierarchy
			&& !anchor.IsChildOf(transform);
	}

	private Transform GetAnchor(TankCrew.Role role)
	{
		switch (role)
		{
			case TankCrew.Role.Driver: return driver;
			case TankCrew.Role.Gunner: return gunner;
			case TankCrew.Role.Commander: return commander;
			default: return null;
		}
	}

	private void OnDisable()
	{
		PendingRequest = 0;
		RequestedRole = TankCrew.Role.None;
		Anchor = null;
		if (crew != null && crew.gameObject == gameObject) crew.SetRole(TankCrew.Role.None);
	}
}
