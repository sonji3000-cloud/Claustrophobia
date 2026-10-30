using System;
using UnityEngine;
using UnityEngine.InputSystem;

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

	// Subscribe with localSeats disabled. The approver returns this request ID to ResolveSeat.
	public event Action<TankCrew.Role, uint> SeatRequested;
	public event Action<TankCrew.Role> SeatApplied;

	private void Awake()
	{
		if (crew == null) crew = GetComponent<TankCrew>();
		if (look == null) look = GetComponent<CrewLook>();
	}

	private void Start()
	{
		started = true;
		if (localSeats) RequestSeat(startSeat);
	}

	private void OnEnable()
	{
		if (started && localSeats) RequestSeat(startSeat);
	}

	private void Update()
	{
		if (!Application.isFocused) return;

		ReadKeys(Keyboard.current);
	}

	private void ReadKeys(Keyboard keyboard)
	{
		if (keyboard == null) return;

		if (keyboard.digit1Key.wasPressedThisFrame) RequestSeat(TankCrew.Role.Driver);
		else if (keyboard.digit2Key.wasPressedThisFrame) RequestSeat(TankCrew.Role.Gunner);
		else if (keyboard.digit3Key.wasPressedThisFrame) RequestSeat(TankCrew.Role.Commander);
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
		if (!approved || !CanApply(role)) return false;

		Transform anchor = GetAnchor(role);
		transform.SetPositionAndRotation(anchor.position, anchor.rotation);
		crew.SetRole(role);
		Anchor = anchor;
		if (look != null) look.ResetLook();

		SeatApplied?.Invoke(role);
		return true;
	}

	private bool CanApply(TankCrew.Role role)
	{
		Transform anchor = GetAnchor(role);
		return started && isActiveAndEnabled && crew != null && crew.gameObject == gameObject
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
