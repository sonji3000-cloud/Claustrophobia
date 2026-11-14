using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Host-only observations. Does not issue commands or retain last-seen positions.
[DisallowMultipleComponent]
[RequireComponent(typeof(TankTarget))]
public sealed class TankSensor : MonoBehaviour
{
	public enum SightState
	{
		Invalid, Self, SameTeam, OtherSession, LocalDisabled,
		OutOfRange, OutOfView, Occluded, QueryFull, Confirming, Visible
	}

	public struct Reading
	{
		public TankTarget Target;
		public SightState State;
		public float Distance;
		public float ConfirmSeconds;
		public int VisibleMask;
		public Collider Blocker;
		internal double VisibleSince;
	}

	[SerializeField] private TankAI owner;
	[SerializeField] private TankTarget self;
	[SerializeField] private Transform origin;
	// Default rays, excluding InteriorDevice (8) and TankInterior (9).
	[SerializeField] private LayerMask sightMask = -773;
	[SerializeField, Min(0.02f)] private float interval = 0.1f;
	[SerializeField, Min(0.1f)] private float acquireRange = 40f;
	[SerializeField, Min(0.1f)] private float keepRange = 50f;
	[SerializeField, Range(1f, 360f)] private float acquireAngle = 120f;
	[SerializeField, Range(1f, 360f)] private float keepAngle = 140f;
	[SerializeField, Min(0f)] private float confirmTime = 0.3f;

	private readonly List<Reading> readings = new List<Reading>(16);
	private readonly RaycastHit[] hits = new RaycastHit[64];
	private readonly RaycastHit[] extraHits = new RaycastHit[256];
	private readonly Collider[] overlaps = new Collider[32];
	private double nextScan;
	private bool allowLocalTargets;
	private PhysicsScene physicsScene;
	private SightState originState;
	private Collider originBlocker;

	public TankTarget SelectedTarget { get; private set; }
	public bool IsVisible { get; private set; }
	public int ScanCount { get; private set; }
	public int RayCount { get; private set; }
	public bool QueryOverflow { get; private set; }
	public int CandidateCount => readings.Count;
	public Transform Origin => origin;
	public float Interval => interval;
	public float ConfirmTime => confirmTime;
	public bool AllowsLocalTargets => allowLocalTargets;
	public bool CanObserve => owner != null && owner.IsHost && owner.isActiveAndEnabled && isActiveAndEnabled
		&& self != null && self.HasFaction && self.Health != null
		&& self.Health.isActiveAndEnabled && self.Health.IsReady && !self.Health.Destroyed
		&& origin != null && origin.IsChildOf(transform);

	private void Awake()
	{
		if (owner == null) owner = GetComponent<TankAI>();
		if (self == null) self = GetComponent<TankTarget>();
	}

	private void OnEnable()
	{
		allowLocalTargets = false;
		ClearState();
	}

	private void OnDisable()
	{
		allowLocalTargets = false;
		ClearState();
	}

	public Reading ReadCandidate(int index) => readings[index];

	public void SetLocalTargets(bool allow)
	{
		if (allowLocalTargets == allow) return;

		allowLocalTargets = allow;
		ClearState();
	}

	public void ClearState()
	{
		readings.Clear();
		SelectedTarget = null;
		IsVisible = false;
		nextScan = 0;
		ScanCount = RayCount = 0;
		QueryOverflow = false;
	}

	// Called by TankAI after its host check, independently of motor/weapon readiness.
	public void Tick()
	{
		if (!CanObserve)
		{
			ClearState();
			return;
		}

		double now = owner.Runner.SimulationTime;
		if (now < nextScan) return;

		nextScan = now + interval;
		Scan(now);
	}

	private void Scan(double now)
	{
		physicsScene = gameObject.scene.GetPhysicsScene();
		if (!physicsScene.IsValid()) { ClearState(); return; }

		SyncCandidates();
		RayCount = 0;
		QueryOverflow = false;
		CheckOrigin();
		Vector3 forward = transform.forward;
		forward.y = 0f;
		forward.Normalize();
		TankTarget keep = null;
		TankTarget nearest = null;
		float bestDistance = float.PositiveInfinity;

		for (int i = 0; i < readings.Count; i++)
		{
			Reading reading = readings[i];
			Evaluate(ref reading, forward, now);
			readings[i] = reading;
			if (reading.State != SightState.Visible) continue;

			if (reading.Target == SelectedTarget) keep = reading.Target;
			if (reading.Distance < bestDistance)
			{
				bestDistance = reading.Distance;
				nearest = reading.Target;
			}
		}

		SelectedTarget = keep != null ? keep : nearest;
		IsVisible = SelectedTarget != null;
		ScanCount++;
	}

	private void SyncCandidates()
	{
		for (int i = readings.Count - 1; i >= 0; i--)
			if (readings[i].Target == null || !readings[i].Target.isActiveAndEnabled) readings.RemoveAt(i);

		IReadOnlyList<TankTarget> candidates = TankTarget.Candidates;
		for (int i = 0; i < candidates.Count; i++)
		{
			TankTarget candidate = candidates[i];
			bool found = false;
			for (int j = 0; j < readings.Count; j++)
				if (readings[j].Target == candidate) { found = true; break; }

			if (!found && candidate != null)
				readings.Add(new Reading { Target = candidate, VisibleSince = -1 });
		}
	}

	private SightState CheckCandidate(TankTarget candidate)
	{
		if (candidate == null || !candidate.CanTarget) return SightState.Invalid;
		if (candidate == self) return SightState.Self;
		if (candidate.IsPlayer == self.IsPlayer) return SightState.SameTeam;

		if (candidate.Health.Object != null)
			return candidate.Health.Object.IsValid && candidate.Health.Runner == owner.Runner
				? SightState.Visible : SightState.OtherSession;

		return allowLocalTargets && candidate.gameObject.scene.GetPhysicsScene() == physicsScene
			? SightState.Visible : SightState.LocalDisabled;
	}

	private void Evaluate(ref Reading reading, Vector3 forward, double now)
	{
		reading.VisibleMask = 0;
		reading.Blocker = null;
		reading.Distance = 0f;
		SightState state = CheckCandidate(reading.Target);
		if (state != SightState.Visible) { Reject(ref reading, state); return; }

		Vector3 groundOffset = reading.Target.GroundPoint.position - transform.position;
		groundOffset.y = 0f;
		reading.Distance = groundOffset.magnitude;
		bool maintaining = reading.Target == SelectedTarget && IsVisible;
		float range = maintaining ? keepRange : acquireRange;
		float cosine = Mathf.Cos((maintaining ? keepAngle : acquireAngle) * 0.5f * Mathf.Deg2Rad);
		int inRange = 0;
		int inView = 0;
		bool full = false;

		for (int i = 0; i < reading.Target.SightPointCount; i++)
		{
			Vector3 offset = reading.Target.GetSightPoint(i).position - origin.position;
			if (offset.sqrMagnitude > range * range) continue;
			inRange++;

			Vector3 horizontal = offset;
			horizontal.y = 0f;
			if (horizontal.sqrMagnitude > 0.0001f && Vector3.Dot(forward, horizontal.normalized) < cosine) continue;
			inView++;

			SightState sight = CheckLine(reading.Target, offset, out Collider blocker);
			if (sight == SightState.Visible) reading.VisibleMask |= 1 << i;
			else if (reading.Blocker == null) reading.Blocker = blocker;
			if (sight == SightState.QueryFull) full = true;
		}

		if (reading.VisibleMask == 0)
		{
			Reject(ref reading, inRange == 0 ? SightState.OutOfRange
				: inView == 0 ? SightState.OutOfView : full ? SightState.QueryFull : SightState.Occluded);
			return;
		}

		if (reading.VisibleSince < 0) reading.VisibleSince = now;
		reading.ConfirmSeconds = (float)(now - reading.VisibleSince);
		reading.State = maintaining || now - reading.VisibleSince + 0.00001 >= confirmTime
			? SightState.Visible : SightState.Confirming;
	}

	private static void Reject(ref Reading reading, SightState state)
	{
		reading.State = state;
		reading.VisibleSince = -1;
		reading.ConfirmSeconds = 0f;
	}

	private bool IsOwn(Collider collider) => collider != null && collider.transform.IsChildOf(transform);

	private void CheckOrigin()
	{
		originBlocker = null;
		int count = physicsScene.OverlapSphere(origin.position, 0.01f, overlaps, sightMask, QueryTriggerInteraction.Ignore);
		originState = count == overlaps.Length ? SightState.QueryFull : SightState.Visible;
		if (originState == SightState.QueryFull) QueryOverflow = true;

		for (int i = 0; i < count; i++)
		{
			if (IsOwn(overlaps[i])) continue;

			originBlocker = overlaps[i];
			if (originState != SightState.QueryFull) originState = SightState.Occluded;
			return;
		}
	}

	private SightState CheckLine(TankTarget candidate, Vector3 offset, out Collider blocker)
	{
		blocker = originBlocker;
		if (originState != SightState.Visible) return originState;

		float distance = offset.magnitude;
		if (distance < 0.0001f) return SightState.Visible;

		Vector3 direction = offset / distance;
		RaycastHit[] buffer = hits;
		int count = physicsScene.Raycast(origin.position, direction, buffer, distance + 0.02f, sightMask, QueryTriggerInteraction.Ignore);
		RayCount++;
		if (count == buffer.Length)
		{
			buffer = extraHits;
			count = physicsScene.Raycast(origin.position, direction, buffer, distance + 0.02f, sightMask, QueryTriggerInteraction.Ignore);
			RayCount++;
			if (count == buffer.Length) { QueryOverflow = true; return SightState.QueryFull; }
		}

		float nearest = float.PositiveInfinity;
		for (int i = 0; i < count; i++)
		{
			Collider hit = buffer[i].collider;
			if (hit == null || IsOwn(hit) || buffer[i].distance >= nearest) continue;

			nearest = buffer[i].distance;
			blocker = hit;
		}

		if (blocker == null || blocker.transform.IsChildOf(candidate.transform))
		{
			blocker = null;
			return SightState.Visible;
		}
		return SightState.Occluded;
	}

	private void OnValidate()
	{
		interval = Finite(interval, 0.1f, 0.02f);
		acquireRange = Finite(acquireRange, 40f, 0.1f);
		keepRange = Mathf.Max(acquireRange, Finite(keepRange, 50f, 0.1f));
		acquireAngle = Mathf.Clamp(Finite(acquireAngle, 120f, 1f), 1f, 360f);
		keepAngle = Mathf.Clamp(Finite(keepAngle, 140f, acquireAngle), acquireAngle, 360f);
		confirmTime = Finite(confirmTime, 0.3f, 0f);
	}

	private static float Finite(float value, float fallback, float minimum)
		=> float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Max(minimum, value);

	private void OnDrawGizmosSelected()
	{
		if (origin == null) return;

		Gizmos.color = Color.cyan;
		Gizmos.DrawWireSphere(origin.position, 0.15f);
		Vector3 forward = transform.forward;
		forward.y = 0f;
		forward.Normalize();
		Gizmos.color = Color.yellow;
		Gizmos.DrawLine(origin.position, origin.position + Quaternion.AngleAxis(-acquireAngle * 0.5f, Vector3.up) * forward * acquireRange);
		Gizmos.DrawLine(origin.position, origin.position + Quaternion.AngleAxis(acquireAngle * 0.5f, Vector3.up) * forward * acquireRange);
		Gizmos.color = Color.cyan;
		Gizmos.DrawLine(origin.position, origin.position + Quaternion.AngleAxis(-keepAngle * 0.5f, Vector3.up) * forward * keepRange);
		Gizmos.DrawLine(origin.position, origin.position + Quaternion.AngleAxis(keepAngle * 0.5f, Vector3.up) * forward * keepRange);
		for (int i = 0; i < readings.Count; i++)
		{
			Reading reading = readings[i];
			if (reading.Target == null || !reading.Target.IsConfigured || reading.Target == self) continue;
			for (int j = 0; j < reading.Target.SightPointCount; j++)
			{
				Gizmos.color = (reading.VisibleMask & 1 << j) != 0 ? Color.green : Color.red;
				Gizmos.DrawLine(origin.position, reading.Target.GetSightPoint(j).position);
			}
		}
	}
}
