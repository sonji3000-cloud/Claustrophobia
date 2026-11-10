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
	private TankCrew.Role seatPending;

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
		seatPending = TankCrew.Role.None;
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
		if (role == TankCrew.Role.None) return;
		SetPlayer(role, PlayerRef.None);

		Debug.Log($"[CREW LEFT] Player={player}, Released={role}", this);
	}

	public bool RequestSwap()
	{
		if (!spawned || !HasStateAuthority || DriverPlayer.IsNone || GunnerPlayer.IsNone) return false;

		swapPending = true;
		seatPending = TankCrew.Role.None;
		return true;
	}

	public bool RequestSeat(TankCrew.Role role)
	{
		if (!spawned || !HasStateAuthority || role == TankCrew.Role.None
			|| (uint)role > (uint)TankCrew.Role.Commander) return false;

		TankCrew.Role current = GetRole(Runner.LocalPlayer);
		if (current == TankCrew.Role.None || current == role) return false;

		seatPending = role;
		swapPending = false;
		return true;
	}

	public override void FixedUpdateNetwork()
	{
		if (seatPending != TankCrew.Role.None)
		{
			TankCrew.Role target = seatPending;
			seatPending = TankCrew.Role.None;
			if (HasStateAuthority) MoveHost(target);
			return;
		}

		if (!swapPending) return;

		swapPending = false;
		if (!HasStateAuthority || DriverPlayer.IsNone || GunnerPlayer.IsNone) return;

		PlayerRef driver = DriverPlayer;
		DriverPlayer = GunnerPlayer;
		GunnerPlayer = driver;
		Debug.Log($"[CREW SWAP] Driver={DriverPlayer}, Gunner={GunnerPlayer}", this);
	}

	private void MoveHost(TankCrew.Role target)
	{
		PlayerRef host = Runner.LocalPlayer;
		TankCrew.Role current = GetRole(host);
		if (current == TankCrew.Role.None || current == target) return;

		SetPlayer(current, GetPlayer(target));
		SetPlayer(target, host);
		Debug.Log($"[CREW SEAT] Host={host}, {current} -> {target}, Driver={DriverPlayer}, Gunner={GunnerPlayer}, Commander={CommanderPlayer}", this);
	}

	private PlayerRef GetPlayer(TankCrew.Role role) => role switch
	{
		TankCrew.Role.Driver => DriverPlayer,
		TankCrew.Role.Gunner => GunnerPlayer,
		TankCrew.Role.Commander => CommanderPlayer,
		_ => PlayerRef.None
	};

	private void SetPlayer(TankCrew.Role role, PlayerRef player)
	{
		switch (role)
		{
			case TankCrew.Role.Driver: DriverPlayer = player; break;
			case TankCrew.Role.Gunner: GunnerPlayer = player; break;
			case TankCrew.Role.Commander: CommanderPlayer = player; break;
		}
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
