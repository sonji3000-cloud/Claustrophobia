using UnityEngine;

/// <summary>Connects the interior breech and its round seat to the gun.</summary>
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
	private TankGun loadedGun;
	private TankGun subscribedGun;
	private CrewInteract interaction;

	public TankGun Gun => gun;
	internal Transform LoadPoint => loadPoint;

	public bool Bind(TankGun next, CrewInteract user)
	{
		if (!CanBind(next) || user == null || user.Carry == null) return false;
		if (gun == next && interaction == user)
		{
			if (isActiveAndEnabled) Attach();
			return true;
		}

		Detach();
		gun = next;
		interaction = user;
		if (isActiveAndEnabled) Attach();
		return true;
	}

	public bool CanBind(TankGun next) => next != null && (loaded == null || loadedGun == null || loadedGun == next);
	public void Unbind()
	{
		Detach();
		gun = null;
		interaction = null;
	}

	public bool CanInsert(CrewInteract user)
	{
		return isActiveAndEnabled && device != null && gun != null && gun.CanInsert
			&& loadPoint != null && loadPoint.gameObject.activeInHierarchy
			&& user != null && user.CanSelect && user.Active == null && user.Crew != null
			&& gun.CanLoad(user.Crew) && device.CanUse(user.Crew) && device.User == null
			&& user.Carry != null && user.Carry.isActiveAndEnabled && user.Carry.Held != null
			&& (gun.UsesNetwork || user.Carry.Held.Holder == user.Carry);
	}

	internal bool TryUse(CrewInteract user)
	{
		if (!isActiveAndEnabled || gun == null || !gun.isActiveAndEnabled || device == null
			|| user == null || !user.CanSelect || user.Selected != device || user.Active != null
			|| !gun.CanLoad(user.Crew) || !device.CanUse(user.Crew)) return false;

		bool holding = user.Carry != null && user.Carry.Held != null;
		if (holding && gun.State != TankGun.ChamberState.EmptyClosed) return TryInsert(user);

		if (gun.State == TankGun.ChamberState.EmptyClosed || gun.State == TankGun.ChamberState.Ready)
			return gun.RequestOpen();
		return gun.RequestClose();
	}

	private bool TryInsert(CrewInteract user)
	{
		if (!CanInsert(user) || user.Selected != device) return false;
		if (gun.UsesNetwork)
		{
			if (!gun.RequestLoad(user.Carry.Held)) return false;
		}
		else
		{
			if (!gun.RequestInsert()) return false;
			loaded = user.Carry.CommitLoad(loadPoint);
			loadedGun = gun;
			if (gun.State == TankGun.ChamberState.Ready) loaded.gameObject.SetActive(false);
		}

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
		if (gun == null) return;
		bool open = state == TankGun.ChamberState.EmptyOpen || state == TankGun.ChamberState.LoadedOpen;
		Transform pose = open ? openPose : closedPose;
		if (block != null && pose != null) block.SetPositionAndRotation(pose.position, pose.rotation);
		if (loaded != null) loaded.gameObject.SetActive(open && gun.HasLiveRound);
		if (loaded == null || gun.HasLiveRound) return;
		loaded.Consume();
		loaded = null;
		loadedGun = null;
	}

	private void OnEnable() => Attach();
	private void Attach()
	{
		if (gun == null || subscribedGun == gun) return;
		subscribedGun = gun;
		subscribedGun.StateChanged += OnStateChanged;
		if (gun.UsesNetwork && interaction != null)
		{
			gun.BindBreech(this, interaction);
			interaction.Carry.BindGun(gun);
		}
		OnStateChanged(gun.State);
	}

	private void OnDisable() => Detach();
	private void Detach()
	{
		if (subscribedGun != null)
		{
			subscribedGun.StateChanged -= OnStateChanged;
			if (subscribedGun.UsesNetwork && interaction != null)
			{
				CrewCarry carry = interaction.Carry;
				if (carry != null)
				{
					carry.ReturnRound();
					carry.BindGun(null);
				}
				subscribedGun.UnbindBreech(this);
			}
		}
		subscribedGun = null;
	}
}
