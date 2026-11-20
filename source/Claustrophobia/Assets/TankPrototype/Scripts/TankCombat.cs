using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class TankCombat : MonoBehaviour
{
	public enum CombatState
	{
		Idle, Patrol, Approach, StopAndFire, WaitForSight, MoveToLastSeen, CheckSurroundings, ReturnToPatrol, Reposition,
		ForcedMove, Retreat, CheckAfterHit, BackOff
	}

	private const float MaxSearchTime = 10f;
	private const float HitCheckTime = 3f;
	private const float ReapproachRange = 32f;
	private const double ReapproachDelay = 0.5d;
	private const float CloseRange = 8f;
	private const float ClearRange = 12f;
	private const float ReverseLimit = 6f;
	private const double CloseDelay = 0.3d;
	private const double ReverseTimeout = 5d;
	private const double ReverseStallTime = 1d;

	[SerializeField] private TankAI owner;
	[SerializeField, Min(0f)] private float fireRange = 28f;
	[SerializeField, Min(0f)] private float approachRange = 24f;
	[SerializeField, Min(0f)] private float sideOffset = 4f;
	[SerializeField, Min(0f)] private float engageRadius = 35f;
	[SerializeField, Min(0f)] private float replanDistance = 3f;
	[SerializeField, Min(0.02f)] private float replanInterval = 0.5f;
	[SerializeField, Min(0.1f)] private float approachTimeout = 8f;
	[SerializeField, Min(0.02f)] private float shotBlockWait = 0.3f;
	[SerializeField, Min(0f)] private float repositionAdvance = 5.5f;
	[SerializeField, Min(0.1f)] private float repositionTimeout = 8f;
	[SerializeField, Min(0.02f)] private float sightWait = 0.75f;
	[SerializeField, Min(0.1f)] private float searchMoveTimeout = 6f;
	[SerializeField, Range(1f, 90f)] private float sweepAngle = 45f;
	[SerializeField, Min(0.1f)] private float sweepTimeout = 3f;

	private Transform[] patrol;
	private readonly List<(TankTarget Target, uint Version)> suppressedTargets = new(2);
	private int suppressionScan;
	private int damageHits;
	private uint moveCommand;
	private Vector3 plannedTarget;
	private double approachDeadline;
	private double nextPlanTime;
	private double outOfRangeSince = -1d;
	private double repositionDeadline;
	private double searchStepDeadline;
	private double searchDeadline;
	private Vector3 sweepForward;
	private bool sweepingRight;
	private bool attackStarted;
	private double closeSince = -1d;
	private double backOffDeadline;
	private double backOffProgressTime;
	private float backOffProgress;
	private float stoppedReverseTravel;
	private bool repositionRefunded;

	public CombatState State { get; private set; }
	public int PatrolIndex { get; private set; } = -1;
	public TankTarget Target { get; private set; }
	public uint TargetVersion { get; private set; }
	public Vector3 EngageOrigin { get; private set; }
	public Vector3 LastSeenPosition { get; private set; }
	public double LastSeenTime { get; private set; } = -1d;
	public float ContactDistance { get; private set; }
	public float OutOfRangeSeconds => outOfRangeSince >= 0d && owner.IsHost
		? (float)(owner.Runner.SimulationTime - outOfRangeSince) : 0f;
	public float RepositionBlockSeconds { get; private set; }
	public int RepositionCount { get; private set; }
	public int SuppressedTargetCount => suppressedTargets.Count;
	public string LastRepositionFailure { get; private set; } = "";
	public string LastStopReason { get; private set; } = "";
	public int BackOffCount { get; private set; }
	public float BackOffStartDistance { get; private set; }
	public float BackOffEndDistance { get; private set; }
	public float BackOffTravel => State == CombatState.BackOff ? owner.ReverseTravel : stoppedReverseTravel;
	public string LastBackOffReason { get; private set; } = "";

	// Read after Tick, which handles sight loss and command replacement.
	internal Transform MoveAimPoint => State is (CombatState.Approach or CombatState.Reposition or CombatState.BackOff) && Target != null
		? Target.AimPoint : null;

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
		damageHits = owner.DamageHitCount;
		moveCommand = owner.CommandId;
		LastStopReason = "";
		LastRepositionFailure = "";
		LastBackOffReason = "";
		BackOffStartDistance = BackOffEndDistance = stoppedReverseTravel = 0f;
		ChangeState(CombatState.Patrol, "Patrol started");
		return true;
	}

	// Like MoveTo, true means the order was accepted. Path failures are reported in LastStopReason.
	public bool ForceMove(Vector3 point) => StartOrder(CombatState.ForcedMove, point);
	public bool RetreatTo(Vector3 point) => StartOrder(CombatState.Retreat, point);

	public bool CancelOrder()
	{
		if (!isActiveAndEnabled || owner == null || !owner.CanAct || State == CombatState.Idle
			|| owner.CommandId != moveCommand) return false;

		StopCombat("Order cancelled");
		return true;
	}

	private bool StartOrder(CombatState state, Vector3 point)
	{
		if (!isActiveAndEnabled || owner == null) return false;

		bool replaced = State != CombatState.Idle && owner.CommandId != moveCommand;
		if (!owner.TryMove(point, TankAI.MoveMode.NavMesh)) return false;

		if (replaced) ClearState("Command replaced");
		else ClearEngagement();
		moveCommand = owner.CommandId;
		LastStopReason = "";
		LastRepositionFailure = "";
		ChangeState(state, $"Ordered to {point:F2}");
		if (owner.MoveStatus == TankAI.MoveState.Failed) UpdateOrder();
		return true;
	}

	internal void Tick(Vector3 position)
	{
		int hits = owner.DamageHitCount;
		bool wasHit = hits > damageHits;
		damageHits = hits;

		if (State == CombatState.Idle) return;
		if (owner.CommandId != moveCommand)
		{
			if (State == CombatState.BackOff) owner.StopReverse();
			ClearState("Command replaced");
			return;
		}

		if (State == CombatState.ForcedMove || State == CombatState.Retreat)
		{
			UpdateOrder();
			return;
		}

		TankSensor sensor = owner.Sensor;
		if (sensor == null || !sensor.isActiveAndEnabled)
		{
			StopCombat("Sensor unavailable");
			return;
		}

		UpdateSuppression(sensor);
		if (State == CombatState.Patrol)
		{
			UpdatePatrol(sensor, position, wasHit);
			return;
		}
		if (State == CombatState.ReturnToPatrol)
		{
			UpdateReturn();
			return;
		}

		if (State == CombatState.CheckAfterHit)
		{
			double now = owner.Runner.SimulationTime;
			if (now >= searchStepDeadline || !TryEngage(sensor, position, "Detected while checking after hit"))
				UpdateSweep(now);
			return;
		}

		if (Target == null || Target.Version != TargetVersion || !sensor.CanTrack(Target))
		{
			BeginReturn("Tracked target unavailable");
			return;
		}

		if (State == CombatState.WaitForSight || State == CombatState.MoveToLastSeen || State == CombatState.CheckSurroundings)
		{
			UpdateSearch(sensor, position);
			return;
		}

		if (!ReadSighting(sensor, Target))
		{
			BeginSightWait(sensor);
			return;
		}

		float distanceSq = FlatDistanceSquared(position, LastSeenPosition);
		double tickTime = owner.Runner.SimulationTime;
		if (distanceSq > CloseRange * CloseRange) closeSince = -1d;
		else if (closeSince < 0d) closeSince = tickTime;

		if (State == CombatState.Approach) UpdateApproach(position);
		else if (State == CombatState.StopAndFire) UpdateFire(position, distanceSq, tickTime);
		else if (State == CombatState.Reposition) UpdateReposition(position, distanceSq);
		else if (State == CombatState.BackOff) UpdateBackOff(distanceSq, tickTime);
	}

	private void UpdateOrder()
	{
		bool retreating = State == CombatState.Retreat;
		string order = retreating ? "Retreat" : "Forced move";
		if (owner.MoveStatus == TankAI.MoveState.Failed)
		{
			StopCombat(order + " failed: " + owner.MoveFailure);
			return;
		}
		if (owner.MoveStatus != TankAI.MoveState.Arrived) return;

		if (retreating || patrol == null) StopCombat(order + " completed");
		else BeginReturn("Forced move completed");
	}

	private void UpdatePatrol(TankSensor sensor, Vector3 position, bool wasHit)
	{
		if (owner.MoveStatus == TankAI.MoveState.Idle && !MovePatrol()) return;
		if (owner.MoveStatus == TankAI.MoveState.Failed)
		{
			StopCombat(owner.MoveFailure);
			return;
		}

		if (TryEngage(sensor, position, "Detected")) return;

		if (wasHit && sensor.CanObserve && !sensor.IsVisible)
		{
			BeginSweep(owner.Runner.SimulationTime, CombatState.CheckAfterHit, "Hit during patrol");
			return;
		}

		if (owner.MoveStatus != TankAI.MoveState.Arrived) return;

		PatrolIndex = (PatrolIndex + 1) % patrol.Length;
		MovePatrol();
	}

	private bool TryEngage(TankSensor sensor, Vector3 position, string reason)
	{
		TankTarget target = SelectPatrolTarget(sensor);
		if (target == null || !ReadSighting(sensor, target)) return false;

		Target = target;
		TargetVersion = target.Version;
		EngageOrigin = position;
		BeginEngagement(position, reason);
		return true;
	}

	private TankTarget SelectPatrolTarget(TankSensor sensor)
	{
		TankTarget target = sensor.SelectedTarget;
		if (!IsSuppressed(target)) return target;

		TankTarget nearest = null;
		float distance = float.PositiveInfinity;
		for (int i = 0; i < sensor.CandidateCount; i++)
		{
			TankSensor.Reading reading = sensor.ReadCandidate(i);
			if (reading.State != TankSensor.SightState.Visible || reading.Target == null
				|| reading.Distance >= distance || IsSuppressed(reading.Target)) continue;

			nearest = reading.Target;
			distance = reading.Distance;
		}
		return nearest;
	}

	private bool IsSuppressed(TankTarget target)
	{
		if (target == null) return false;

		for (int i = 0; i < suppressedTargets.Count; i++)
			if (suppressedTargets[i].Target == target && suppressedTargets[i].Version == target.Version) return true;
		return false;
	}

	private void UpdateSuppression(TankSensor sensor)
	{
		if (suppressedTargets.Count == 0 || !sensor.CanObserve || suppressionScan == sensor.ScanCount) return;

		suppressionScan = sensor.ScanCount;
		for (int i = suppressedTargets.Count - 1; i >= 0; i--)
		{
			(TankTarget target, uint version) = suppressedTargets[i];
			if (target == null || target.Version != version || !sensor.TryGetSighting(target, out _, out _))
				suppressedTargets.RemoveAt(i);
		}
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

		BeginApproach(position, reason);
	}

	private void BeginApproach(Vector3 position, string reason)
	{
		double now = owner.Runner.SimulationTime;
		approachDeadline = now + approachTimeout;
		ChangeState(CombatState.Approach, reason);
		if (!TryPlanApproach(position, now, false, out string failure)) StopCombat(failure);
	}

	private void BeginSightWait(TankSensor sensor)
	{
		closeSince = -1d;
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
			BeginReturn("Last sighting expired");
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
		if (State == CombatState.CheckSurroundings)
		{
			UpdateSweep(now);
			return;
		}

		if (owner.MoveStatus == TankAI.MoveState.Failed)
			BeginReturn("Search movement failed: " + owner.MoveFailure);
		else if (!IsRouteInRange(position))
			BeginReturn("Search left engagement radius");
		else if (owner.MoveStatus == TankAI.MoveState.Arrived)
			BeginSweep(now, CombatState.CheckSurroundings, "Reached last seen position");
		else if (now >= searchStepDeadline)
			BeginReturn("Search movement timed out");
	}

	private void BeginSearchMove(Vector3 position, double now)
	{
		TankPath path = owner.Path;
		if (path == null || !IsInEngageRange(LastSeenPosition)
			|| !path.TryMeasure(position, LastSeenPosition, EngageOrigin, engageRadius, out Vector3 point, out _))
		{
			BeginReturn("No reachable path to last seen position within engagement radius");
			return;
		}

		if (!owner.MoveTo(point))
		{
			BeginReturn("Search movement order rejected");
			return;
		}
		moveCommand = owner.CommandId;
		if (owner.MoveStatus == TankAI.MoveState.Failed)
		{
			BeginReturn("Search movement failed: " + owner.MoveFailure);
			return;
		}
		if (!IsRouteInRange(position))
		{
			BeginReturn("Search left engagement radius");
			return;
		}

		searchStepDeadline = now + searchMoveTimeout;
		ChangeState(CombatState.MoveToLastSeen, "Moving to last seen position");
	}

	private void BeginSweep(double now, CombatState state, string reason)
	{
		sweepForward = owner.transform.forward;
		sweepForward.y = 0f;
		sweepingRight = false;
		searchStepDeadline = now + (state == CombatState.CheckAfterHit ? HitCheckTime : sweepTimeout);
		ChangeState(state, reason);
		SetSweepDrive(-1f);
	}

	private void UpdateSweep(double now)
	{
		Vector3 forward = owner.transform.forward;
		forward.y = 0f;
		float angle = Vector3.SignedAngle(sweepForward, forward, Vector3.up);
		if (sweepingRight && angle >= sweepAngle)
		{
			BeginReturn("Finished checking surroundings");
			return;
		}
		if (now >= searchStepDeadline)
		{
			BeginReturn("Surroundings check timed out");
			return;
		}
		if (!sweepingRight && angle <= -sweepAngle)
		{
			sweepingRight = true;
			SetSweepDrive(1f);
		}
	}

	private void SetSweepDrive(float turn)
	{
		owner.StopMove();
		if (!owner.SetDrive(turn, -turn))
		{
			BeginReturn("Surroundings turn order rejected");
			return;
		}
		moveCommand = owner.CommandId;
	}

	private void BeginReturn(string reason)
	{
		owner.StopAttack();
		owner.StopMove();
		ChangeState(CombatState.ReturnToPatrol, reason);
		if (MovePatrol()) UpdateReturn();
	}

	private void UpdateReturn()
	{
		if (owner.MoveStatus == TankAI.MoveState.Failed)
		{
			StopCombat("Patrol return failed: " + owner.MoveFailure);
			return;
		}
		if (owner.MoveStatus != TankAI.MoveState.Arrived) return;

		ClearEngagement();
		ChangeState(CombatState.Patrol, "Reached patrol waypoint");
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

		double now = owner.Runner.SimulationTime;
		if (now >= approachDeadline)
		{
			StopCombat("Approach timed out");
			return;
		}

		bool arrived = owner.MoveStatus == TankAI.MoveState.Arrived;
		float range = arrived ? fireRange : approachRange;
		if (FlatDistanceSquared(position, LastSeenPosition) <= range * range)
		{
			BeginFire(arrived ? "Approach arrived in range" : "Target within approach distance");
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

	private void UpdateFire(Vector3 position, float distanceSq, double now)
	{
		if (distanceSq > ReapproachRange * ReapproachRange)
		{
			WaitToApproach(position);
			return;
		}
		outOfRangeSince = -1d;

		if (!attackStarted && owner.MoveStatus != TankAI.MoveState.Arrived && !owner.UpdateSettle()) return;
		if (BackOffCount == 0 && closeSince >= 0d && now - closeSince >= CloseDelay && TryBackOff(position, distanceSq, now)) return;

		if (attackStarted)
		{
			if (owner.AttackStatus == TankAI.AttackState.Idle) StopCombat("Attack target unavailable");
			else if (owner.IsShotBlocked && owner.ShotBlockedSeconds + 0.0001f >= shotBlockWait
				&& distanceSq > CloseRange * CloseRange
				&& (distanceSq > ClearRange * ClearRange || BackOffCount == 0 && !repositionRefunded))
			{
				if (RepositionCount == 0) BeginReposition(position);
				else FailReposition("Shot path still blocked after reposition");
			}
			return;
		}

		if (!owner.Attack(Target.AimPoint))
		{
			StopCombat("Attack request rejected");
			return;
		}
		attackStarted = true;
		moveCommand = owner.CommandId;
	}

	private bool TryBackOff(Vector3 position, float distanceSq, double now)
	{
		BackOffCount++;
		BackOffStartDistance = BackOffEndDistance = Mathf.Sqrt(distanceSq);
		stoppedReverseTravel = 0f;
		Vector3 rear = owner.RearDirection;
		if (Vector3.Dot(LastSeenPosition - position, rear) >= 0f)
		{
			LastBackOffReason = "Reverse would not increase target distance";
			return false;
		}

		if (!owner.TryReverse(rear, ReverseLimit, out string reason))
		{
			LastBackOffReason = reason;
			return false;
		}

		moveCommand = owner.CommandId;
		attackStarted = false;
		backOffDeadline = now + ReverseTimeout;
		backOffProgressTime = now;
		backOffProgress = 0f;
		LastBackOffReason = "";
		ChangeState(CombatState.BackOff, "Target stayed within 8 m");
		return true;
	}

	private void UpdateBackOff(float distanceSq, double now)
	{
		BackOffEndDistance = Mathf.Sqrt(distanceSq);
		float travel = owner.ReverseTravel;
		if (travel >= backOffProgress + 0.12f)
		{
			backOffProgress = travel;
			backOffProgressTime = now;
		}

		if (owner.MoveStatus == TankAI.MoveState.Failed) BeginFire(owner.MoveFailure);
		else if (distanceSq >= ClearRange * ClearRange) BeginFire("Target distance reached 12 m");
		else if (travel >= ReverseLimit - 0.05f) BeginFire("Reverse travel limit reached");
		else if (now >= backOffDeadline) BeginFire("Reverse timed out");
		else if (now - backOffProgressTime >= ReverseStallTime) BeginFire("Reverse stalled");
	}

	private void WaitToApproach(Vector3 position)
	{
		double now = owner.Runner.SimulationTime;
		if (outOfRangeSince < 0d)
		{
			outOfRangeSince = now;
			if (attackStarted)
			{
				owner.StopAttack();
				moveCommand = owner.CommandId;
				attackStarted = false;
			}
		}

		if (now - outOfRangeSince >= ReapproachDelay)
			BeginApproach(position, "Visible target stayed beyond 32 m for 0.5 s");
	}

	private void BeginReposition(Vector3 position)
	{
		LastRepositionFailure = "";
		RepositionBlockSeconds = owner.ShotBlockedSeconds;
		if (!TryFindReposition(position, out Vector3 point))
		{
			FailReposition("No reachable reposition point");
			return;
		}
		if (!owner.MoveTo(point))
		{
			FailReposition("Reposition order rejected");
			return;
		}

		moveCommand = owner.CommandId;
		attackStarted = false;
		if (owner.MoveStatus == TankAI.MoveState.Failed)
		{
			FailReposition("Reposition failed: " + owner.MoveFailure);
			return;
		}

		RepositionCount++;
		repositionDeadline = owner.Runner.SimulationTime + repositionTimeout;
		ChangeState(CombatState.Reposition, $"Shot path blocked for {RepositionBlockSeconds:F2}s, moving to {point:F2}");
	}

	private bool TryFindReposition(Vector3 position, out Vector3 point)
	{
		point = default;
		TankPath path = owner.Path;
		Vector3 forward = LastSeenPosition - position;
		forward.y = 0f;
		float distance = forward.magnitude;
		if (path == null || sideOffset <= 0f || distance <= 0.01f) return false;

		forward /= distance;
		// A forward component helps keep the target in view as the hull turns.
		Vector3 center = position + forward * Mathf.Min(repositionAdvance, distance * 0.5f);
		Vector3 side = new Vector3(forward.z, 0f, -forward.x) * sideOffset;
		float bestLength = float.PositiveInfinity;
		for (int i = -1; i <= 1; i += 2)
		{
			if (!path.TryMeasure(position, center + side * i, EngageOrigin, engageRadius,
					out Vector3 candidate, out float length)
				|| length <= owner.StopRadius || length >= bestLength
				|| FlatDistanceSquared(candidate, LastSeenPosition) > fireRange * fireRange) continue;

			point = candidate;
			bestLength = length;
		}
		return bestLength < float.PositiveInfinity;
	}

	private void UpdateReposition(Vector3 position, float distanceSq)
	{
		if (distanceSq <= CloseRange * CloseRange)
		{
			if (!repositionRefunded && owner.MoveStatus != TankAI.MoveState.Arrived)
			{
				RepositionCount--;
				repositionRefunded = true;
			}
			BeginFire("Reposition stopped for close target");
			return;
		}

		if (owner.MoveStatus == TankAI.MoveState.Failed)
			FailReposition("Reposition failed: " + owner.MoveFailure);
		else if (!IsRouteInRange(position))
			FailReposition("Reposition left engagement radius");
		else if (owner.MoveStatus == TankAI.MoveState.Arrived)
		{
			if (distanceSq <= fireRange * fireRange)
				BeginFire("Reposition arrived, retrying fire");
			else FailReposition("Target outside fire range after reposition");
		}
		else if (owner.Runner.SimulationTime >= repositionDeadline)
			FailReposition("Reposition timed out");
	}

	private void FailReposition(string reason)
	{
		LastRepositionFailure = reason;
		suppressedTargets.Add((Target, TargetVersion));
		suppressionScan = owner.Sensor.ScanCount;
		BeginReturn(reason);
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
			StopCombat(State == CombatState.ReturnToPatrol ? "Patrol return failed: waypoint unavailable" : "Patrol waypoint unavailable");
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
		suppressedTargets.Clear();
		ClearEngagement();
	}

	private void ClearEngagement()
	{
		Target = null;
		TargetVersion = 0;
		EngageOrigin = LastSeenPosition = default;
		LastSeenTime = -1d;
		ContactDistance = 0f;
		RepositionBlockSeconds = 0f;
		RepositionCount = 0;
		BackOffCount = 0;
		closeSince = -1d;
		repositionRefunded = false;
		plannedTarget = default;
		approachDeadline = nextPlanTime = 0d;
		repositionDeadline = 0d;
		searchStepDeadline = searchDeadline = 0d;
		sweepForward = default;
		sweepingRight = false;
		attackStarted = false;
	}

	private void ChangeState(CombatState next, string reason)
	{
		if (next == State) return;

		CombatState previous = State;
		if (previous == CombatState.StopAndFire) outOfRangeSince = -1d;
		if (previous == CombatState.BackOff)
		{
			stoppedReverseTravel = owner.ReverseTravel;
			LastBackOffReason = next switch
			{
				CombatState.ForcedMove => "Forced move order",
				CombatState.Retreat => "Retreat order",
				_ => reason
			};
		}
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
