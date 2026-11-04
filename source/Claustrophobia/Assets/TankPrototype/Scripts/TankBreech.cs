using UnityEngine;

/// <summary>Loads a carried scene round through the gun's existing chamber state.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CrewDevice))]
public sealed class TankBreech : MonoBehaviour
{
	[SerializeField] private CrewDevice device;
	[SerializeField] private TankGun gun;
	[SerializeField] private Transform loadPoint;
	[SerializeField] private Transform block;
	[SerializeField] private Transform openPose;
	[SerializeField] private Transform closedPose;

	private TankRound loaded;

	public bool CanInsert(CrewInteract user)
	{
		return isActiveAndEnabled && device != null && gun != null && gun.CanInsert
			&& loadPoint != null && loadPoint.gameObject.activeInHierarchy
			&& user != null && user.CanSelect && user.Active == null && user.Crew != null
			&& user.Crew.CanLoad && device.CanUse(user.Crew) && device.User == null
			&& user.Carry != null && user.Carry.isActiveAndEnabled && user.Carry.Held != null
			&& user.Carry.Held.isActiveAndEnabled && user.Carry.Held.Holder == user.Carry;
	}

	internal bool TryUse(CrewInteract user)
	{
		if (!isActiveAndEnabled || gun == null || !gun.isActiveAndEnabled || device == null
			|| user == null || !user.CanSelect || user.Selected != device || user.Active != null
			|| user.Crew == null || !user.Crew.CanLoad || !device.CanUse(user.Crew)) return false;

		bool holding = user.Carry != null && user.Carry.Held != null;
		if (holding && gun.State != TankGun.ChamberState.EmptyClosed) return TryInsert(user);

		if (gun.State == TankGun.ChamberState.EmptyClosed || gun.State == TankGun.ChamberState.Ready)
			return gun.RequestOpen();

		return gun.RequestClose();
	}

	private bool TryInsert(CrewInteract user)
	{
		if (!CanInsert(user) || user.Selected != device) return false;
		if (!gun.RequestInsert()) return false;

		loaded = user.Carry.CommitLoad(loadPoint);
		user.ClearSelection();
		return true;
	}

	public string GetPrompt(CrewInteract user)
	{
		if (gun == null || !gun.isActiveAndEnabled) return "Chamber unavailable";
		if (CanInsert(user)) return "OPEN | EMPTY | Space - Insert round | Esc - Return to rack";
		bool holding = user != null && user.Carry != null && user.Carry.Held != null;
		switch (gun.State)
		{
			case TankGun.ChamberState.EmptyOpen: return "OPEN | EMPTY | Space - Close | Pick up a round to load";
			case TankGun.ChamberState.EmptyClosed: return "CLOSED | EMPTY | Space - Open";
			case TankGun.ChamberState.LoadedOpen: return holding
				? "OPEN | LOADED | Esc - Return extra round" : "OPEN | LOADED | Space - Close";
			case TankGun.ChamberState.Ready: return holding
				? "CLOSED | LOADED | Esc - Return extra round" : "CLOSED | LOADED | Space - Open";
			default: return "CLOSED | FIRED | Recovering";
		}
	}

	private void OnStateChanged(TankGun.ChamberState state)
	{
		bool open = state == TankGun.ChamberState.EmptyOpen || state == TankGun.ChamberState.LoadedOpen;
		Transform pose = open ? openPose : closedPose;
		if (block != null && pose != null) block.SetPositionAndRotation(pose.position, pose.rotation);
		if (loaded != null) loaded.gameObject.SetActive(open && gun.HasLiveRound);
		if (loaded == null || gun.HasLiveRound) return;

		loaded.gameObject.SetActive(false);
		loaded = null;
	}

	private void OnEnable()
	{
		if (gun == null) return;

		gun.StateChanged += OnStateChanged;
		OnStateChanged(gun.State);
	}

	private void OnDisable()
	{
		if (gun != null) gun.StateChanged -= OnStateChanged;
	}
}
