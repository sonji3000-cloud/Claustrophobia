using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Fusion;
using UnityEngine;

internal sealed class CarentanTrialLog
{
	private sealed class Tank
	{
		public string Name;
		public TankGun Gun;
		public TankAI Ai;
		public TankSensor.SightState Sight;
		public Collider Blocker;
		public bool SeenTiger;
		public Vector3 LastSeen;
		public TankAI.MoveState Move;
		public int Waypoint = -1;
		public uint Command;
		public double SlowSince = -1d;
		public bool ReportedSlow;
		public int TigerHits;
		public int BlockedHits;
	}

	internal static string DirectoryPath => Application.isEditor
		? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Builds", "CarentanTrials"))
		: Path.Combine(Application.persistentDataPath, "CarentanTrials");

	private readonly List<Tank> tanks = new(5);
	private readonly Dictionary<Component, Tank> sources = new(25);
	private readonly StringBuilder rows = new(8192);
	private readonly NetworkRunner runner;
	private readonly TankHealth tiger;
	private readonly Transform cover;
	private readonly Transform goal;
	private readonly double startedAt;
	private float actualHP;
	private double lastTime;
	private double firstHit = -1d;
	private double counterHit = -1d;
	private double firstSpotted = -1d;
	private double firstMove = -1d;
	private double coverAt = -1d;
	private double goalAt = -1d;
	private double destroyedAt = -1d;

	public float VirtualHP { get; private set; }
	public int BlockedHits { get; private set; }
	public double VirtualDestroyedAt { get; private set; } = -1d;

	internal CarentanTrialLog(NetworkRunner runner, TankHealth tiger, TankAI[] enemies, Transform start,
		Transform cover, Transform goal, bool invulnerable, double startedAt)
	{
		this.runner = runner;
		this.tiger = tiger;
		this.cover = cover;
		this.goal = goal;
		this.startedAt = startedAt;
		actualHP = VirtualHP = tiger.CurrentHP;

		Watch("Tiger", tiger.GetComponent<TankGun>(), null);
		for (int i = 0; i < enemies.Length; i++)
		{
			TankAI ai = enemies[i];
			if (ai != null) Watch(((char)('A' + i)).ToString(), ai.GetComponent<TankGun>(), ai);
		}

		rows.AppendLine("seconds\tactor\tevent\trequest\tshot\tdetail");
		var names = new StringBuilder();
		for (int i = 1; i < tanks.Count; i++)
		{
			if (i > 1) names.Append(',');
			names.Append(tanks[i].Name);
		}

		Write("Trial", "start", $"hp={Num(VirtualHP)} invulnerable={invulnerable} enemies={names} start={start.name} pos={Pos(start.position)} yaw={Num(start.eulerAngles.y)}");
	}

	private void Watch(string name, TankGun gun, TankAI ai)
	{
		var tank = new Tank { Name = name, Gun = gun, Ai = ai };
		tanks.Add(tank);
		if (gun != null)
		{
			sources.Add(gun, tank);
			gun.FireTraced += OnFire;
			gun.ImpactTraced += OnImpact;
		}

		TankHealth health = ai != null ? ai.GetComponent<TankHealth>() : tiger;
		if (health != null) sources.Add(health, tank);
		if (ai == null) return;
		sources.Add(ai, tank);
		ai.ShotBlockChanged += OnShotBlock;
		if (ai.Combat != null)
		{
			sources.Add(ai.Combat, tank);
			ai.Combat.StateChanged += OnCombat;
		}
		if (ai.Sensor != null)
		{
			sources.Add(ai.Sensor, tank);
			ai.Sensor.Scanned += OnScan;
		}
	}

	internal void MarkSpotted(Vector3 pos)
	{
		if (firstSpotted >= 0d) return;
		firstSpotted = Now();
		Write("Tiger", "player_spotted", $"pos={Pos(pos)}");
	}

	internal void TrackPosition(Vector3 pos, bool driving)
	{
		if (driving && firstMove < 0d)
		{
			firstMove = Now();
			Write("Tiger", "first_drive_input", $"pos={Pos(pos)}");
		}

		if (coverAt < 0d && Reached(pos, cover, 2.5f))
		{
			coverAt = Now();
			Write("Tiger", "cover_point_reached", $"pos={Pos(pos)}");
		}
		if (goalAt < 0d && Reached(pos, goal, 4f))
		{
			goalAt = Now();
			Write("Tiger", "goal_reached", $"pos={Pos(pos)} hp={Num(tiger.CurrentHP)}");
		}
	}

	private static bool Reached(Vector3 pos, Transform marker, float radius)
	{
		if (marker == null) return false;
		Vector3 offset = pos - marker.position;
		offset.y = 0f;
		return offset.sqrMagnitude <= radius * radius;
	}

	private void OnFire(TankGun gun, TankGun.FireTrace trace, int request, string reason)
	{
		Tank tank = sources[gun];
		Rigidbody body = gun.Body;
		string motion = body != null
			? $"pos={Pos(body.position)} speed={Num(body.linearVelocity.magnitude)}m/s rotation={Pos(body.rotation.eulerAngles)} angular={Pos(body.angularVelocity)}rad/s"
			: "body=unavailable";
		string detail = $"chamber={gun.State} {motion}";
		if (trace == TankGun.FireTrace.Fired)
			Write(tank.Name, "shot_fired", detail + $" muzzle={Pos(gun.Muzzle.position)}", request, gun.ShotCount);
		else if (trace == TankGun.FireTrace.Request)
			Write(tank.Name, "fire_request", detail, request);
		else Write(tank.Name, "fire_rejected", detail + $" reason={reason}", request);
	}

	private void OnImpact(TankGun gun, TankProjectile.Impact impact)
	{
		Tank tank = sources[gun];
		string target = impact.Health != null ? impact.Health.name : impact.Collider;
		if (impact.Health != null && sources.TryGetValue(impact.Health, out Tank hitTank))
			target = hitTank.Name;

		string detail = $"target={target} collider={impact.Collider} pos={Pos(impact.Point)} reason={impact.Reason}";
		if (impact.Health != null)
			detail += $" damage={Num(impact.Damage)} outcome={impact.DamageOutcome} actualHP={Num(impact.Before)}->{Num(impact.After)}";

		bool hitTiger = impact.Health == tiger && impact.DamageOutcome != TankHealth.DamageOutcome.Rejected;
		if (hitTiger)
		{
			actualHP = impact.After;
			if (firstHit < 0d) firstHit = Now();
			tank.TigerHits++;
			if (impact.DamageOutcome == TankHealth.DamageOutcome.Invulnerable)
			{
				tank.BlockedHits++;
				BlockedHits++;
				float before = VirtualHP;
				VirtualHP = Mathf.Max(0f, before - impact.Damage);
				detail += $" virtualHP={Num(before)}->{Num(VirtualHP)}";
			}
			else VirtualHP = impact.After;
		}
		else if (counterHit < 0d && tank.Ai == null && impact.DamageOutcome == TankHealth.DamageOutcome.Applied)
			counterHit = Now();

		Write(tank.Name, "shot_impact", detail, shot: impact.Shot);
		if (!hitTiger) return;
		if (impact.DamageOutcome == TankHealth.DamageOutcome.Invulnerable && VirtualHP <= 0f && VirtualDestroyedAt < 0d)
		{
			VirtualDestroyedAt = Now();
			Write("Tiger", "virtual_destroyed", $"source={tank.Name} shot={impact.Shot} actualHP={Num(impact.After)}");
		}
		if (impact.DamageOutcome == TankHealth.DamageOutcome.Applied && impact.After <= 0f && destroyedAt < 0d)
		{
			destroyedAt = Now();
			Write("Tiger", "destroyed", $"source={tank.Name} shot={impact.Shot} pos={Pos(tiger.transform.position)}");
		}
	}

	private void OnCombat(TankCombat combat, TankCombat.CombatState from, string reason)
	{
		Tank tank = sources[combat];
		string seen = combat.LastSeenTime >= 0d ? Pos(combat.LastSeenPosition) : "-";
		Write(tank.Name, "combat_state", $"{from}->{combat.State} reason={reason} lastSeen={seen} {Motion(tank)}");
	}

	private void OnShotBlock(TankAI ai, bool blocked, string reason) =>
		Write(sources[ai].Name, blocked ? "shot_path_blocked" : "shot_path_block_cleared", reason);

	private void OnScan(TankSensor sensor)
	{
		Tank tank = sources[sensor];
		TankSensor.Reading reading = default;
		for (int i = 0; i < sensor.CandidateCount; i++)
		{
			TankSensor.Reading candidate = sensor.ReadCandidate(i);
			if (candidate.Target == null || candidate.Target.Health != tiger) continue;
			reading = candidate;
			break;
		}

		bool visible = reading.State == TankSensor.SightState.Visible;
		if (tank.Sight != reading.State || tank.Blocker != reading.Blocker)
		{
			if (visible)
			{
				Write(tank.Name, tank.SeenTiger ? "sight_reacquired" : "sight_found",
					$"Tiger={Pos(reading.SeenPosition)} enemy={Pos(tank.Ai.transform.position)}");
				tank.SeenTiger = true;
			}
			else if (tank.Sight == TankSensor.SightState.Visible)
				Write(tank.Name, "sight_lost", $"state={reading.State} blocker={reading.Blocker?.name} lastSeen={Pos(tank.LastSeen)}");
			else Write(tank.Name, "sight_state", $"{tank.Sight}->{reading.State} blocker={reading.Blocker?.name}");

			tank.Sight = reading.State;
			tank.Blocker = reading.Blocker;
		}
		if (visible) tank.LastSeen = reading.SeenPosition;
		TrackMove(tank);
	}

	private void TrackMove(Tank tank)
	{
		TankAI ai = tank.Ai;
		int waypoint = ai.Combat != null ? ai.Combat.PatrolIndex : -1;
		if (tank.Move != ai.MoveStatus || tank.Command != ai.CommandId || tank.Waypoint != waypoint)
		{
			tank.Move = ai.MoveStatus;
			tank.Command = ai.CommandId;
			tank.Waypoint = waypoint;
			Write(tank.Name, "move_state", Motion(tank) + $" reason={ai.MoveFailure}");
		}

		Rigidbody body = tank.Gun != null ? tank.Gun.Body : null;
		if (ai.MoveStatus != TankAI.MoveState.Moving || body == null || body.linearVelocity.sqrMagnitude >= .0225f
			|| Mathf.Abs(ai.LeftDriveInput) + Mathf.Abs(ai.RightDriveInput) < .05f)
		{
			tank.SlowSince = -1d;
			tank.ReportedSlow = false;
			return;
		}

		double now = Now();
		if (tank.SlowSince < 0d) tank.SlowSince = now;
		if (tank.ReportedSlow || now - tank.SlowSince < 1d) return;
		tank.ReportedSlow = true;
		Write(tank.Name, "move_stalled", Motion(tank) + $" reason={ai.RetryReason}");
	}

	private static string Motion(Tank tank)
	{
		TankAI ai = tank.Ai;
		Rigidbody body = tank.Gun != null ? tank.Gun.Body : null;
		return $"move={ai.MoveStatus} pos={Pos(ai.transform.position)} waypoint={(ai.Combat != null ? ai.Combat.PatrolIndex + 1 : 0)} "
			+ $"destination={Pos(ai.Destination)} speed={(body != null ? Num(body.linearVelocity.magnitude) : "-")}m/s";
	}

	internal string Finish(string reason, double duration)
	{
		lastTime = Math.Max(lastTime, duration);
		Write("Trial", "summary", $"reason={reason} actualHP={Num(actualHP)} virtualHP={Num(VirtualHP)} "
			+ $"blockedHits={BlockedHits} spotted={Stamp(firstSpotted)} firstMove={Stamp(firstMove)} firstHit={Stamp(firstHit)} cover={Stamp(coverAt)} "
			+ $"counterHit={Stamp(counterHit)} actualDestroyed={Stamp(destroyedAt)} virtualDestroyed={Stamp(VirtualDestroyedAt)} goal={Stamp(goalAt)}");
		foreach (Tank tank in tanks)
		{
			if (tank.Ai != null)
			{
				Write(tank.Name, "hits_summary", $"TigerHits={tank.TigerHits} blocked={tank.BlockedHits}");
				tank.Ai.ShotBlockChanged -= OnShotBlock;
				if (tank.Ai.Combat != null) tank.Ai.Combat.StateChanged -= OnCombat;
				if (tank.Ai.Sensor != null) tank.Ai.Sensor.Scanned -= OnScan;
			}
			if (tank.Gun == null) continue;
			tank.Gun.FireTraced -= OnFire;
			tank.Gun.ImpactTraced -= OnImpact;
		}

		try
		{
			Directory.CreateDirectory(DirectoryPath);
			string path = Path.Combine(DirectoryPath, $"Carentan-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.tsv");
			File.WriteAllText(path, rows.ToString());
			return path;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			Debug.LogWarning($"[Carentan] Could not save trial: {exception.Message}");
			return null;
		}
	}

	private double Now() => runner != null && runner.IsRunning
		? Math.Max(lastTime, runner.SimulationTime - startedAt) : lastTime;

	private void Write(string actor, string entry, string detail, int request = 0, int shot = 0)
	{
		lastTime = Now();
		rows.Append(Stamp(lastTime)).Append('\t').Append(actor).Append('\t').Append(entry).Append('\t');
		if (request != 0) rows.Append(request);
		rows.Append('\t');
		if (shot != 0) rows.Append(shot);
		rows.Append('\t').AppendLine(detail?.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' '));
	}

	private static string Num(float value) => value.ToString("F2", CultureInfo.InvariantCulture);
	private static string Pos(Vector3 value) => value.ToString("F2", CultureInfo.InvariantCulture);
	private static string Stamp(double value) => value >= 0d ? value.ToString("F3", CultureInfo.InvariantCulture) : "-";
}
