using UnityEngine;

[DisallowMultipleComponent]
public sealed class TankCombat : MonoBehaviour
{
	public enum CombatState { Idle, Patrol, Approach, StopAndFire }

	[SerializeField] private TankAI owner;
	[SerializeField, Min(0f)] private float fireRange = 28f;

	private Transform[] patrol;
	private uint moveCommand;

	public CombatState State { get; private set; }
	public int PatrolIndex { get; private set; } = -1;
	public TankTarget Target { get; private set; }
	public uint TargetVersion { get; private set; }
	public Vector3 EngageOrigin { get; private set; }
	public Vector3 LastSeenPosition { get; private set; }
	public double LastSeenTime { get; private set; } = -1d;
	public float ContactDistance { get; private set; }

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
			StopPatrol("Sensor unavailable");
			return;
		}
		if (State != CombatState.Patrol)
		{
			if (Target != null && Target.Version == TargetVersion) ReadSighting(sensor, Target);
			return;
		}

		if (owner.MoveStatus == TankAI.MoveState.Idle && !MovePatrol()) return;
		if (owner.MoveStatus == TankAI.MoveState.Failed)
		{
			StopPatrol(owner.MoveFailure);
			return;
		}

		TankTarget target = sensor.SelectedTarget;
		if (target != null && ReadSighting(sensor, target))
		{
			Target = target;
			TargetVersion = target.Version;
			EngageOrigin = position;
			Vector3 offset = LastSeenPosition - position;
			offset.y = 0f;
			ContactDistance = offset.magnitude;

			owner.StopMove();
			moveCommand = owner.CommandId;
			CombatState next = ContactDistance <= fireRange ? CombatState.StopAndFire : CombatState.Approach;
			ChangeState(next, $"Detected {target.name} at {ContactDistance:0.0} m, waypoint {PatrolIndex + 1}");
			return;
		}

		if (owner.MoveStatus != TankAI.MoveState.Arrived) return;

		PatrolIndex = (PatrolIndex + 1) % patrol.Length;
		MovePatrol();
	}

	private bool ReadSighting(TankSensor sensor, TankTarget target)
	{
		if (sensor.SelectedTarget != target || !sensor.IsVisible
			|| !sensor.TryGetLastSeen(out Vector3 position, out double time)) return false;

		LastSeenPosition = position;
		LastSeenTime = time;
		return true;
	}

	private bool MovePatrol()
	{
		Transform point = patrol[PatrolIndex];
		if (point == null || !owner.MoveTo(point.position))
		{
			StopPatrol("Patrol waypoint unavailable");
			return false;
		}
		moveCommand = owner.CommandId;
		return true;
	}

	private void StopPatrol(string reason)
	{
		ClearState(reason);
		owner.StopActions();
	}

	internal void ClearState(string reason)
	{
		ChangeState(CombatState.Idle, reason);
		patrol = null;
		moveCommand = 0;
		PatrolIndex = -1;
		Target = null;
		TargetVersion = 0;
		EngageOrigin = LastSeenPosition = default;
		LastSeenTime = -1d;
		ContactDistance = 0f;
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
