using System.Collections.Generic;
using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class TankCrewRegistry : NetworkBehaviour
{
	[Networked, OnChangedRender(nameof(OnRolesChanged))]
	public PlayerRef DriverPlayer { get; private set; }

	[Networked, OnChangedRender(nameof(OnRolesChanged))]
	public PlayerRef GunnerPlayer { get; private set; }

	[Networked, OnChangedRender(nameof(OnRolesChanged))]
	public PlayerRef CommanderPlayer { get; private set; }

	private readonly List<PlayerRef> pending = new(3);
	private CrewSeats seats;
	private bool spawned;
	private bool swapPending;

	public TankCrew.Role GetRole(PlayerRef player)
	{
		if (!spawned || player.IsNone) return TankCrew.Role.None;

		if (player == DriverPlayer) return TankCrew.Role.Driver;
		if (player == GunnerPlayer) return TankCrew.Role.Gunner;
		if (player == CommanderPlayer) return TankCrew.Role.Commander;
		return TankCrew.Role.None;
	}

	public bool CanDrive(PlayerRef player) => GetRole(player) == TankCrew.Role.Driver;
	public bool CanAim(PlayerRef player) => GetRole(player) == TankCrew.Role.Gunner;
	public bool CanCommand(PlayerRef player) => GetRole(player) == TankCrew.Role.Commander;

	public override void Spawned()
	{
		spawned = true;
		if (HasStateAuthority)
		{
			Register(Runner.LocalPlayer);
			foreach (PlayerRef player in pending) Register(player);
		}

		pending.Clear();
	}

	public override void Despawned(NetworkRunner runner, bool hasState)
	{
		spawned = false;
		swapPending = false;
		UnbindSeats(seats);
	}

	// The session forwards join and leave events on the Host, including before Spawned.
	internal void Register(PlayerRef player)
	{
		if (player.IsNone) return;
		if (!spawned)
		{
			if (!pending.Contains(player)) pending.Add(player);
			return;
		}
		if (!HasStateAuthority || GetRole(player) != TankCrew.Role.None) return;

		PlayerRef host = Runner.LocalPlayer;
		if (DriverPlayer.IsNone && (player == host || GetRole(host) != TankCrew.Role.None))
			DriverPlayer = player;
		else if (GunnerPlayer.IsNone)
			GunnerPlayer = player;
		else if (CommanderPlayer.IsNone)
			CommanderPlayer = player;
		else
			return;

		Debug.Log($"[CREW ASSIGNED] Player={player}, Role={GetRole(player)}", this);
	}

	internal void Unregister(PlayerRef player)
	{
		if (!spawned)
		{
			pending.Remove(player);
			return;
		}
		if (!HasStateAuthority) return;

		TankCrew.Role role = GetRole(player);
		switch (role)
		{
			case TankCrew.Role.Driver: DriverPlayer = PlayerRef.None; break;
			case TankCrew.Role.Gunner: GunnerPlayer = PlayerRef.None; break;
			case TankCrew.Role.Commander: CommanderPlayer = PlayerRef.None; break;
			default: return;
		}

		Debug.Log($"[CREW LEFT] Player={player}, Released={role}", this);
	}

	public bool RequestSwap()
	{
		if (!spawned || !HasStateAuthority || DriverPlayer.IsNone || GunnerPlayer.IsNone) return false;

		swapPending = true;
		return true;
	}

	public override void FixedUpdateNetwork()
	{
		if (!swapPending) return;

		swapPending = false;
		if (!HasStateAuthority || DriverPlayer.IsNone || GunnerPlayer.IsNone) return;

		PlayerRef driver = DriverPlayer;
		DriverPlayer = GunnerPlayer;
		GunnerPlayer = driver;
		Debug.Log($"[CREW SWAP] Driver={DriverPlayer}, Gunner={GunnerPlayer}", this);
	}

	internal bool BindSeats(CrewSeats next)
	{
		if (!spawned || next == null) return false;

		seats = next;
		return ApplyLocalRole();
	}

	internal void UnbindSeats(CrewSeats previous)
	{
		if (seats == null || seats != previous) return;

		seats = null;
		previous.ApplyRole(TankCrew.Role.None);
	}

	private void OnRolesChanged()
	{
		if (seats != null) ApplyLocalRole();
	}

	private bool ApplyLocalRole()
	{
		PlayerRef player = Runner.LocalPlayer;
		TankCrew.Role role = GetRole(player);
		bool changed = seats.CurrentRole != role;
		if (!seats.ApplyRole(role)) return false;

		if (changed) Debug.Log($"[CREW LOCAL] Player={player}, Role={role}", this);
		return true;
	}
}
