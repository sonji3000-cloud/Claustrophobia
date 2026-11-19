using UnityEngine;

[DisallowMultipleComponent]
public sealed class TankCombat : MonoBehaviour
{
	public enum CombatState { Idle, Patrol, Approach, StopAndFire, WaitForSight, MoveToLastSeen }

	private const float MaxSearchTime = 10f;

	[SerializeField] private TankAI owner;
	[SerializeField, Min(0f)] private float fireRange = 28f;
	[SerializeField, Min(0f)] private float approachRange = 24f;
	[SerializeField, Min(0f)] private float sideOffset = 4f;
	[SerializeField, Min(0f)] private float engageRadius = 35f;
	[SerializeField, Min(0f)] private float replanDistance = 3f;
	[SerializeField, Min(0.02f)] private float replanInterval = 0.5f;
	[SerializeField, Min(0.1f)] private float approachTimeout = 8f;
	[SerializeField, Min(0.02f)] private float sightWait = 0.75f;
	[SerializeField, Min(0.1f)] private float searchMoveTimeout = 6f;

	private Transform[] patrol;
	private uint moveCommand;
	private Vector3 plannedTarget;
	private double approachDeadline;
	private double nextPlanTime;
	private double searchStepDeadline;
	private double searchDeadline;
	private bool attackStarted;

	public CombatState State { get; private set; }
	public int PatrolIndex { get; private set; } = -1;
	public TankTarget Target { get; private set; }
	public uint TargetVersion { get; private set; }
	public Vector3 EngageOrigin { get; private set; }
	public Vector3 LastSeenPosition { get; private set; }
	public double LastSeenTime { get; private set; } = -1d;
	public float ContactDistance { get; private set; }
	public string LastStopReason { get; private set; } = "";

	public bool StartPatrol(Transform[] points)
	{
		if (!isActiveAndEnabled || owner == null || !owner.CanAct || owner.Sensor == null
			|| !owner.Sensor.isActiveAndEnabled || points == null || points.Length == 0) return false;

		for (int i = 0; i < points.Length; i++)
			if (points[i] == null) return false;

		owner.StopActions();
		if (!owner.SetMoveMode(TankAI.MoveMode.NavMesh)) return false;

		patrol = points;
		PatrolIndex = 0;
		moveCommand = owner.CommandId;
		LastStopReason = "";
		ChangeState(CombatState.Patrol, "Patrol started");
		return true;
	}

	internal void Tick(Vector3 position)
	{
		if (State == CombatState.Idle) return;
		if (owner.CommandId != moveCommand)
		{
			ClearState("Command replaced");
			return;
		}

		TankSensor sensor = owner.Sensor;
		if (sensor == null || !sensor.isActiveAndEnabled)
		{
			StopCombat("Sensor unavailable");
			return;
		}

		if (State == CombatState.Patrol)
		{
			UpdatePatrol(sensor, position);
			return;
		}

		if (Target == null || Target.Version != TargetVersion || !Target.CanTarget)
		{
			StopCombat("Tracked target unavailable");
			return;
		}

		if (State == CombatState.WaitForSight || State == CombatState.MoveToLastSeen)
		{
			UpdateSearch(sensor, position);
			return;
		}

		if (!ReadSighting(sensor, Target))
		{
			BeginSightWait(sensor);
			return;
		}

		if (State == CombatState.Approach) UpdateApproach(position);
		else UpdateFire();
	}

	private void UpdatePatrol(TankSensor sensor, Vector3 position)
	{
		if (owner.MoveStatus == TankAI.MoveState.Idle && !MovePatrol()) return;
		if (owner.MoveStatus == TankAI.MoveState.Failed)
		{
			StopCombat(owner.MoveFailure);
			return;
		}

		TankTarget target = sensor.SelectedTarget;
		if (target != null && ReadSighting(sensor, target))
		{
			Target = target;
			TargetVersion = target.Version;
			EngageOrigin = position;
			BeginEngagement(position, "Detected");
			return;
		}

		if (owner.MoveStatus != TankAI.MoveState.Arrived) return;

		PatrolIndex = (PatrolIndex + 1) % patrol.Length;
		MovePatrol();
	}

	private void BeginEngagement(Vector3 position, string eventName)
	{
		ContactDistance = Mathf.Sqrt(FlatDistanceSquared(LastSeenPosition, position));
		string reason = $"{eventName} {Target.name} at {ContactDistance:0.0} m, waypoint {PatrolIndex + 1}";
		if (ContactDistance <= fireRange)
		{
			BeginFire(reason);
			return;
		}

		double now = owner.Runner.SimulationTime;
		approachDeadline = now + approachTimeout;
		ChangeState(CombatState.Approach, reason);
		if (!TryPlanApproach(position, now, false, out string failure)) StopCombat(failure);
	}

	private void BeginSightWait(TankSensor sensor)
	{
		owner.StopAttack();
		owner.StopMove();
		moveCommand = owner.CommandId;
		attackStarted = false;

		searchStepDeadline = owner.Runner.SimulationTime + sightWait;
		searchDeadline = LastSeenTime + Mathf.Min(sensor.MemoryTime, MaxSearchTime);
		ChangeState(CombatState.WaitForSight, "Tracked target no longer visible");
	}

	private void UpdateSearch(TankSensor sensor, Vector3 position)
	{
		double now = owner.Runner.SimulationTime;
		if (now >= searchDeadline)
		{
			StopCombat("Last sighting expired");
			return;
		}

		if (ReadSighting(sensor, Target))
		{
			BeginEngagement(position, "Reacquired");
			return;
		}

		if (State == CombatState.WaitForSight)
		{
			if (now >= searchStepDeadline) BeginSearchMove(position, now);
			return;
		}

		if (owner.MoveStatus == TankAI.MoveState.Failed)
			StopCombat("Search movement failed: " + owner.MoveFailure);
		else if (!IsRouteInRange(position))
			StopCombat("Search left engagement radius");
		else if (owner.MoveStatus == TankAI.MoveState.Arrived)
			StopCombat("Reached last seen position");
		else if (now >= searchStepDeadline)
			StopCombat("Search movement timed out");
	}

	private void BeginSearchMove(Vector3 position, double now)
	{
		TankPath path = owner.Path;
		if (path == null || !IsInEngageRange(LastSeenPosition)
			|| !path.TryMeasure(position, LastSeenPosition, EngageOrigin, engageRadius, out Vector3 point, out _))
		{
			StopCombat("No reachable path to last seen position within engagement radius");
			return;
		}

		if (!owner.MoveTo(point))
		{
			StopCombat("Search movement order rejected");
			return;
		}
		moveCommand = owner.CommandId;
		if (owner.MoveStatus == TankAI.MoveState.Failed)
		{
			StopCombat("Search movement failed: " + owner.MoveFailure);
			return;
		}
		if (!IsRouteInRange(position))
		{
			StopCombat("Search left engagement radius");
			return;
		}

		searchStepDeadline = now + searchMoveTimeout;
		ChangeState(CombatState.MoveToLastSeen, "Moving to last seen position");
	}

	private void UpdateApproach(Vector3 position)
	{
		if (owner.MoveStatus == TankAI.MoveState.Failed)
		{
			StopCombat(owner.MoveFailure);
			return;
		}

		if (!IsRouteInRange(position))
		{
			StopCombat("Approach left engagement radius");
			return;
		}

		bool arrived = owner.MoveStatus == TankAI.MoveState.Arrived;
		float range = arrived ? fireRange : approachRange;
		if (FlatDistanceSquared(position, LastSeenPosition) <= range * range)
		{
			BeginFire(arrived ? "Approach arrived in range" : "Target within approach distance");
			return;
		}

		double now = owner.Runner.SimulationTime;
		if (now >= approachDeadline)
		{
			StopCombat("Approach timed out");
			return;
		}

		if (now < nextPlanTime || owner.IsRetrying
			|| !arrived && FlatDistanceSquared(plannedTarget, LastSeenPosition) < replanDistance * replanDistance) return;

		if (!TryPlanApproach(position, now, !arrived, out string failure)
			&& (arrived || owner.MoveStatus == TankAI.MoveState.Failed)) StopCombat(failure);
	}

	private bool TryPlanApproach(Vector3 position, double now, bool keepRoute, out string failure)
	{
		plannedTarget = LastSeenPosition;
		nextPlanTime = now + replanInterval;
		failure = "No reachable approach point";
		if (!TryFindApproach(position, out Vector3 point)) return false;
		failure = "";

		float stopRadius = owner.StopRadius;
		if (keepRoute && owner.MoveStatus == TankAI.MoveState.Moving && owner.Path.HasPath
			&& FlatDistanceSquared(point, owner.Destination) <= stopRadius * stopRadius
			&& Mathf.Abs(point.y - owner.Destination.y) <= stopRadius) return true;

		if (!owner.MoveTo(point))
		{
			failure = string.IsNullOrEmpty(owner.MoveBlockReason) ? "Approach order rejected" : owner.MoveBlockReason;
			return false;
		}
		moveCommand = owner.CommandId;
		failure = owner.MoveFailure;
		return owner.MoveStatus != TankAI.MoveState.Failed;
	}

	private bool TryFindApproach(Vector3 position, out Vector3 point)
	{
		point = default;
		TankPath path = owner.Path;
		if (path == null) return false;

		Vector3 offset = position - LastSeenPosition;
		float height = offset.y;
		offset.y = 0f;
		float distance = offset.magnitude;
		if (distance <= approachRange || distance <= 0.01f) return false;

		Vector3 dir = offset / distance;
		Vector3 center = LastSeenPosition + dir * approachRange;
		center.y += height * (approachRange / distance);
		Vector3 side = new Vector3(-dir.z, 0f, dir.x) * sideOffset;
		float bestLength = float.PositiveInfinity;
		for (int i = -1; i <= 1; i++)
		{
			if (!path.TryMeasure(position, center + side * i, EngageOrigin, engageRadius,
					out Vector3 candidate, out float length)
				|| length >= bestLength || length <= owner.StopRadius) continue;

			float distanceSquared = FlatDistanceSquared(candidate, LastSeenPosition);
			if (distanceSquared > fireRange * fireRange || distanceSquared >= distance * distance) continue;

			point = candidate;
			bestLength = length;
		}
		return bestLength < float.PositiveInfinity;
	}

	private void BeginFire(string reason)
	{
		if (owner.MoveStatus != TankAI.MoveState.Arrived)
		{
			owner.StopMove();
			moveCommand = owner.CommandId;
		}
		ChangeState(CombatState.StopAndFire, reason);
	}

	private void UpdateFire()
	{
		if (attackStarted)
		{
			if (owner.AttackStatus == TankAI.AttackState.Idle) StopCombat("Attack target unavailable");
			return;
		}

		if (owner.MoveStatus != TankAI.MoveState.Arrived && !owner.UpdateSettle()) return;

		if (!owner.Attack(Target.AimPoint))
		{
			StopCombat("Attack request rejected");
			return;
		}
		attackStarted = true;
		moveCommand = owner.CommandId;
	}

	private static float FlatDistanceSquared(Vector3 from, Vector3 to)
	{
		Vector3 offset = to - from;
		offset.y = 0f;
		return offset.sqrMagnitude;
	}

	private bool IsInEngageRange(Vector3 point) => FlatDistanceSquared(point, EngageOrigin) <= engageRadius * engageRadius;

	private bool IsRouteInRange(Vector3 position)
	{
		TankPath path = owner.Path;
		return IsInEngageRange(position) && (path == null || path.IsWithin(EngageOrigin, engageRadius));
	}

	private bool ReadSighting(TankSensor sensor, TankTarget target)
	{
		if (!sensor.TryGetSighting(target, out Vector3 position, out double time)) return false;

		LastSeenPosition = position;
		LastSeenTime = time;
		return true;
	}

	private bool MovePatrol()
	{
		Transform point = patrol[PatrolIndex];
		if (point == null || !owner.MoveTo(point.position))
		{
			StopCombat("Patrol waypoint unavailable");
			return false;
		}
		moveCommand = owner.CommandId;
		return true;
	}

	private void StopCombat(string reason) => owner.StopActions(reason);

	internal void ClearState(string reason)
	{
		if (State == CombatState.Idle) return;

		LastStopReason = reason;
		ChangeState(CombatState.Idle, reason);
		patrol = null;
		moveCommand = 0;
		PatrolIndex = -1;
		Target = null;
		TargetVersion = 0;
		EngageOrigin = LastSeenPosition = default;
		LastSeenTime = -1d;
		ContactDistance = 0f;
		plannedTarget = default;
		approachDeadline = nextPlanTime = 0d;
		searchStepDeadline = searchDeadline = 0d;
		attackStarted = false;
	}

	private void ChangeState(CombatState next, string reason)
	{
		if (next == State) return;

		CombatState previous = State;
		State = next;
		Debug.Log($"[AI COMBAT] {previous} -> {State}: {reason}", this);
	}

	private void OnDisable()
	{
		if (State == CombatState.Idle) return;

		if (owner != null && owner.IsHost) owner.StopActions();
		else ClearState("Combat disabled");
	}
}
