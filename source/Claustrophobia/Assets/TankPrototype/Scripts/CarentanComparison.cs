#if UNITY_EDITOR
using System;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public sealed class CarentanComparison : IDisposable
{
	private enum Phase { Scene, Seat, Round, Insert, Close, Approach, Retreat, Turn, Cover, Hold, Finished }

	public const string ScenePath = "Assets/TankPrototype/Scenes/Carentan.unity";
	public const int RunCount = 6;
	public const float StartHP = 300f;
	private const string RunKey = "Carentan.Comparison.Run";
	private const float ExposureDistance = 39.5f;
	private const float CoverRadius = 2.5f;
	private const float MovingSpeed = .5f;
	private const double HoldSeconds = 5d;
	private const double RunLimit = 65d;
	private const double AimTrackSeconds = 1.75d;

	private readonly CarentanDrive drive;
	private readonly TankSceneLink link;
	private readonly bool counterfire;
	private readonly bool runInBackgroundBefore;

	private Phase phase;
	private TankHealth health;
	private TankHealth enemyHealth;
	private TankAI enemy;
	private TankControl control;
	private TankInput input;
	private TankGun gun;
	private TankMotor motor;
	private TankAim aim;
	private TankTarget target;
	private Rigidbody body;
	private Rigidbody enemyBody;
	private TankCrewRegistry crew;
	private Vector3 north;
	private Vector3 exposure;
	private Vector3 turn;
	private Vector3 cover;
	private Vector3 previous;
	private double firstContact = -1;
	private double retreatAt = -1;
	private double coverAt = -1;
	private double holdAt;
	private float coverHP;
	private int backgroundSteps;
	private float backgroundMetres;
	private float maxSpeed;

	private bool requested;
	private float shotSpeed;
	private string shotResult = "none";
	private string outcome;

	public static int RunIndex
	{
		get => SessionState.GetInt(RunKey, -1);
		set
		{
			if (value < 0) SessionState.EraseInt(RunKey);
			else SessionState.SetInt(RunKey, value);
		}
	}

	public static string Strategy(int run) => run % 4 is 1 or 2 ? "moving_counterfire" : "cover_only";
	public bool Finished => phase == Phase.Finished;
	public string Status => Finished ? outcome : phase.ToString();

	internal CarentanComparison(CarentanDrive drive, TankSceneLink link, int run)
	{
		this.drive = drive;
		this.link = link;
		counterfire = Strategy(run) == "moving_counterfire";
		runInBackgroundBefore = Application.runInBackground;
		Application.runInBackground = true;
	}

	internal void Step()
	{
		if (Finished) return;
		if (phase <= Phase.Close)
		{
			Prepare();
			return;
		}

		if (!drive.DeploymentStarted)
		{
			End(health.Destroyed ? "destroyed" : "interrupted");
			return;
		}

		double seconds = drive.DeploymentSeconds;
		Vector3 pos = body.position;
		float speed = Flat(body.linearVelocity).magnitude;
		maxSpeed = Mathf.Max(maxSpeed, speed);
		if (!Application.isFocused)
		{
			backgroundSteps++;
			backgroundMetres += Flat(pos - previous).magnitude;
		}
		previous = pos;

		if (firstContact < 0 && enemy.Combat.Target != null) firstContact = seconds;
		if (seconds >= RunLimit)
		{
			End(retreatAt < 0 && firstContact < 0 ? "no_contact" : "time_limit");
			return;
		}

		if (phase is Phase.Approach or Phase.Retreat) AimAtEnemy(seconds, speed);
		switch (phase)
		{
			case Phase.Approach:
				Approach(pos, seconds);
				break;
			case Phase.Retreat:
				if (Vector3.Dot(pos - turn, north) > 0f) SetDrive(-.65f, 0);
				else
				{
					SetDrive(0, 0);
					if (speed < .20f) phase = Phase.Turn;
				}
				break;
			case Phase.Turn:
				if (Align(cover - pos)) phase = Phase.Cover;
				break;
			case Phase.Cover:
				DriveToCover(pos, speed, seconds);
				break;
			case Phase.Hold:
				if (seconds >= holdAt + HoldSeconds) End("covered");
				break;
		}
	}

	private void Prepare()
	{
		if (phase == Phase.Scene)
		{
			if (drive.SceneStage == TankSceneLink.Stage.Failed || drive.DeploymentError.Length != 0)
			{
				End(drive.DeploymentError.Length != 0 ? drive.DeploymentError : drive.SceneError ?? "scene_failed");
				return;
			}
			if (!drive.CanStartDeployment) return;
			if (!BindTanks())
			{
				End("reset_or_binding_failed");
				return;
			}
			input.SetTrialInput(true);
			if (!crew.RequestSeat(TankCrew.Role.Gunner))
			{
				End("gunner_request_failed");
				return;
			}
			phase = Phase.Seat;
			return;
		}

		TankInterior interior = link.Interior;
		if (interior.Crew.CurrentRole != TankCrew.Role.Gunner) return;
		switch (phase)
		{
			case Phase.Seat:
				var devices = interior.Interaction.Devices;
				for (int i = 0; i < devices.Length; i++)
				{
					CrewDevice device = devices[i];
					if (device == null || device.AmmoRack == null || !device.CanUse(interior.Crew)) continue;
					LoadNext(gun.RequestTake(device), Phase.Round, "take_round_failed");
					return;
				}
				End("ammo_rack_missing");
				break;
			case Phase.Round:
				if (interior.Interaction.Carry.Held != null)
					LoadNext(control.RequestInsert(), Phase.Insert, "insert_round_failed");
				break;
			case Phase.Insert:
				if (gun.State == TankGun.ChamberState.LoadedOpen)
					LoadNext(control.RequestClose(), Phase.Close, "close_chamber_failed");
				break;
			case Phase.Close:
				if (!gun.CanFire) return;
				gun.FireTraced += OnFire;
				gun.ImpactTraced += OnImpact;
				drive.StartDeployment();
				if (!drive.DeploymentStarted) { End("start_failed"); return; }
				previous = body.position;
				phase = Phase.Approach;
				break;
		}
	}

	private bool BindTanks()
	{
		health = drive.PlayerHealth;
		enemy = drive.GetDeployed(0);
		if (enemy == null || drive.GetDeployed(1) != null || drive.GetDeployed(2) != null || drive.GetDeployed(3) != null
			|| !enemy.TryGetComponent(out target) || !enemy.TryGetComponent(out enemyBody)
			|| !enemy.TryGetComponent(out enemyHealth) || drive.DeploymentCover == null
			|| !health.TryGetComponent(out control) || !health.TryGetComponent(out input) || !health.TryGetComponent(out body)
			|| !health.TryGetComponent(out gun) || !health.TryGetComponent(out aim)
			|| !health.TryGetComponent(out crew) || !health.TryGetComponent(out motor))
			return false;

		Transform start = drive.DeploymentPoint;
		north = Flat(start.forward).normalized;
		exposure = start.position + north * ExposureDistance;
		cover = drive.DeploymentCover.position;
		turn = start.position + Vector3.Project(cover - start.position, north);
		return Flat(body.position - start.position).sqrMagnitude < .25f
			&& Quaternion.Angle(body.rotation, start.rotation) < 3f
			&& health.CurrentHP == StartHP && health.DamageHitCount == 0 && !health.IsInvulnerable
			&& gun.State == TankGun.ChamberState.EmptyOpen && gun.ShotCount == 0
			&& enemy.Combat.State == TankCombat.CombatState.Idle && enemy.Combat.PatrolIndex < 0
			&& link.Interior.Interaction.Carry.Held == null && Object.FindAnyObjectByType<TankProjectile>() == null;
	}

	private void LoadNext(bool accepted, Phase next, string error)
	{
		if (accepted) phase = next;
		else End(error);
	}

	private void Approach(Vector3 pos, double seconds)
	{
		float remaining = Vector3.Dot(exposure - pos, north);
		if (remaining > 0.25f)
		{
			SetDrive(Mathf.Clamp(remaining / 4f, .18f, .65f), 0);
			return;
		}

		if (firstContact < 0)
		{
			SetDrive(0, 0);
			return;
		}

		retreatAt = seconds;
		phase = Phase.Retreat;
		SetDrive(-.65f, 0);
	}

	private void AimAtEnemy(double seconds, float speed)
	{
		Vector3 from = gun.Muzzle.position;
		Vector3 point = target.AimPoint != null ? target.AimPoint.position : target.transform.position;
		Vector3 delta = point - from;
		bool hasSolution = GetLaunch(delta, out Vector3 launch, out float travel);
		if (hasSolution && GetLaunch(delta + Flat(enemyBody.linearVelocity) * travel, out Vector3 lead, out _))
			launch = lead;

		if (!aim.TryGetAim(launch, out Vector2 angles))
		{
			control.ApplyAim(0, 0, Time.fixedDeltaTime);
			return;
		}

		float yaw = Mathf.DeltaAngle(aim.CurrentYaw, angles.x);
		float pitch = angles.y - aim.CurrentPitch;
		control.ApplyAim(Mathf.Clamp(yaw / 4f, -1f, 1f), Mathf.Clamp(pitch / 3f, -1f, 1f), Time.fixedDeltaTime);

		if (!counterfire || requested || !hasSolution || seconds - retreatAt < AimTrackSeconds || phase != Phase.Retreat
			|| speed < MovingSpeed || Vector3.Dot(body.linearVelocity, north) > -MovingSpeed
			|| Mathf.Abs(yaw) > 2.5f || Mathf.Abs(pitch) > 2f)
			return;

		requested = true;
		gun.RequestFire();
	}

	private bool GetLaunch(Vector3 delta, out Vector3 launch, out float travel)
	{
		float range = Flat(delta).magnitude;
		float gravity = -Physics.gravity.y;
		float speedSq = gun.ProjectileSpeed * gun.ProjectileSpeed;
		float discriminant = speedSq * speedSq - gravity * (gravity * range * range + 2f * delta.y * speedSq);
		launch = delta;
		travel = 0f;
		if (range < .1f || gravity <= 0f || discriminant < 0f) return false;

		float tangent = (speedSq - Mathf.Sqrt(discriminant)) / (gravity * range);
		launch.y = range * tangent;
		travel = range * Mathf.Sqrt(1f + tangent * tangent) / gun.ProjectileSpeed;
		return true;
	}

	private void DriveToCover(Vector3 pos, float speed, double seconds)
	{
		Vector3 offset = Flat(cover - pos);
		float distance = offset.magnitude;
		if (coverAt < 0 && distance <= CoverRadius)
		{
			coverAt = seconds;
			coverHP = health.CurrentHP;
		}

		if (distance <= .8f)
		{
			SetDrive(0, 0);
			if (speed < .2f)
			{
				holdAt = seconds;
				phase = Phase.Hold;
			}
			return;
		}

		float heading = Vector3.SignedAngle(Flat(body.rotation * Vector3.forward), offset, Vector3.up);
		SetDrive(Mathf.Clamp(distance / 3f, .12f, .57f) * Mathf.Clamp01(1f - Mathf.Abs(heading) / 45f),
			Mathf.Clamp(heading / 32f, -.55f, .55f));
	}

	private bool Align(Vector3 direction)
	{
		float heading = Vector3.SignedAngle(Flat(body.rotation * Vector3.forward), Flat(direction), Vector3.up);
		float turnSpeed = motor.AppliedTurnSpeed;
		float coast = turnSpeed * Mathf.Abs(turnSpeed) / Mathf.Max(1f, 2f * motor.TurnDeceleration);
		float error = heading - coast;
		SetDrive(0f, Mathf.Abs(error) < 2f ? 0f : Mathf.Clamp(error / 24f, -.65f, .65f));
		return Mathf.Abs(heading) < 3f && Mathf.Abs(turnSpeed) < 3f;
	}

	private void SetDrive(float throttle, float steer)
	{
		drive.SetTrialLevers(new Vector2(Mathf.Clamp(throttle + steer, -1f, 1f),
			Mathf.Clamp(throttle - steer, -1f, 1f)));
	}

	private void OnFire(TankGun source, TankGun.FireTrace trace, int id, string reason)
	{
		if (trace == TankGun.FireTrace.Fired)
		{
			shotSpeed = Flat(body.linearVelocity).magnitude;
			shotResult = "in_flight";
		}
		else if (trace == TankGun.FireTrace.Rejected) shotResult = "rejected: " + reason;
	}

	private void OnImpact(TankGun source, TankProjectile.Impact impact)
	{
		bool hitA = impact.Health == enemyHealth && impact.DamageOutcome == TankHealth.DamageOutcome.Applied;
		shotResult = hitA ? "A damaged" : impact.Reason + ": " + impact.Collider;
	}

	private void End(string result)
	{
		outcome = result;
		phase = Phase.Finished;
		drive.SetTrialLevers(Vector2.zero);
		if (control != null) control.ApplyAim(0, 0, Time.fixedDeltaTime);
		if (drive.DeploymentStarted) drive.StopDeployment();
	}

	public static string Header => "run\tstrategy\toutcome\tcontact_s\tretreat_s\tcover_s\tcover_hp\tend_hp\tA_end_hp\tTiger_hits\tshot_requested\tshot_speed_mps\tshot_result\tmax_speed_mps\tbackground_steps\tbackground_metres\tlog";

	public string GetRow(int run)
	{
		string fire = counterfire && !requested ? "no moving firing solution" : shotResult;
		return FormattableString.Invariant(
			$"{run + 1}\t{Strategy(run)}\t{Tsv(outcome)}\t{TimeField(firstContact)}\t{TimeField(retreatAt)}\t{TimeField(coverAt)}\t{(coverAt < 0 ? "-" : coverHP.ToString("F0", CultureInfo.InvariantCulture))}\t{health?.CurrentHP:F0}\t{enemyHealth?.CurrentHP:F0}\t{health?.DamageHitCount}\t{requested}\t{shotSpeed:F2}\t{Tsv(fire)}\t{maxSpeed:F2}\t{backgroundSteps}\t{backgroundMetres:F2}\t{drive.DeploymentLogPath}");
	}

	private static string TimeField(double time) => time < 0 ? "-" : time.ToString("F3", CultureInfo.InvariantCulture);
	private static string Tsv(string value) => value?.Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
	private static Vector3 Flat(Vector3 vector) => new(vector.x, 0, vector.z);

	public void Dispose()
	{
		if (input != null) input.SetTrialInput(false);
		if (gun != null)
		{
			gun.FireTraced -= OnFire;
			gun.ImpactTraced -= OnImpact;
		}
		Application.runInBackground = runInBackgroundBefore;
	}
}
#endif
