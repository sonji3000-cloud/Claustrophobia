using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(TankHealth))]
public sealed class CarentanMission : NetworkBehaviour, IAfterTick
{
	public enum Phase { Preparing, EnterTown, DestroyTarget, ReturnSouth, Succeeded, Failed }

	[Networked] private Phase NetworkPhase { get; set; }

	private Rigidbody body;
	private TankHealth tiger;
	private TankHealth target;
	private BoxCollider entry;
	private BoxCollider exit;
	private Vector3 previous;
	private bool enteredExit;

	public bool IsSpawned => Object != null && Object.IsValid;
	public Phase CurrentPhase => IsSpawned ? NetworkPhase : Phase.Preparing;
#if UNITY_EDITOR
	internal bool EntrySkipped { get; private set; }
#endif

	private void Awake()
	{
		body = GetComponent<Rigidbody>();
		tiger = GetComponent<TankHealth>();
	}

	public override void Despawned(NetworkRunner runner, bool hasState)
	{
		target = null;
		entry = null;
		exit = null;
		enteredExit = false;
#if UNITY_EDITOR
		EntrySkipped = false;
#endif
	}

	internal void Begin(BoxCollider entryZone, BoxCollider exitZone, TankHealth designatedTarget)
	{
		if (!IsSpawned || !HasStateAuthority || !Runner.IsServer || CurrentPhase != Phase.Preparing) return;

		entry = entryZone;
		exit = exitZone;
		target = designatedTarget;
		ResetRouteSample();
		SetPhase(Phase.EnterTown);
	}

#if UNITY_EDITOR
	internal void SkipEntryForShot()
	{
		if (!IsSpawned || !HasStateAuthority || !Runner.IsServer) return;

		ResetRouteSample();
		if (CurrentPhase != Phase.EnterTown) return;

		EntrySkipped = true;
		SetPhase(Phase.DestroyTarget);
	}
#endif

	private void ResetRouteSample()
	{
		previous = body.worldCenterOfMass;
		enteredExit = false;
	}

	void IAfterTick.AfterTick()
	{
		if (!HasStateAuthority || !Runner.IsServer || !Runner.IsForward) return;

		Phase phase = NetworkPhase;
		if (phase is Phase.Preparing or Phase.Succeeded or Phase.Failed) return;

		Vector3 current = body.worldCenterOfMass;
		if (tiger.Destroyed) SetPhase(Phase.Failed);
		else if ((phase is Phase.EnterTown or Phase.DestroyTarget) && target != null && target.IsReady && target.Destroyed)
			SetPhase(Phase.ReturnSouth);
		else if (phase == Phase.EnterTown && CrossedEntry(previous, current)) SetPhase(Phase.DestroyTarget);
		else if (phase == Phase.ReturnSouth && CrossedExit(previous, current)) SetPhase(Phase.Succeeded);

		previous = current;
	}

	private void SetPhase(Phase phase)
	{
		NetworkPhase = phase;
		Debug.Log($"[Carentan Mission] {phase}", this);
	}

	private bool CrossedEntry(Vector3 from, Vector3 to)
	{
		var bounds = new Bounds(entry.center, entry.size);
		Vector3 start = entry.transform.InverseTransformPoint(from);
		Vector3 end = entry.transform.InverseTransformPoint(to);
		if (bounds.Contains(start)) return false;
		if (bounds.Contains(end)) return true;

		Vector3 travel = end - start;
		float length = travel.magnitude;
		return length > 0f && bounds.IntersectRay(new Ray(start, travel / length), out float distance)
			&& distance <= length;
	}

	private bool CrossedExit(Vector3 from, Vector3 to)
	{
		Vector3 start = exit.transform.InverseTransformPoint(from) - exit.center;
		Vector3 end = exit.transform.InverseTransformPoint(to) - exit.center;
		Vector3 half = exit.size * .5f;

		// The trigger's local +Z face is the town side, and its -Z face is the way out.
		if (!enteredExit && start.z > half.z && end.z <= half.z)
			enteredExit = IsAcrossFace(AtDepth(start, end, half.z), half);

		if (!enteredExit) return false;
		if (end.z < -half.z)
		{
			enteredExit = false;
			return start.z >= -half.z && IsAcrossFace(AtDepth(start, end, -half.z), half);
		}

		if (end.z > half.z || !IsAcrossFace(end, half)) enteredExit = false;
		return false;
	}

	private static Vector3 AtDepth(Vector3 from, Vector3 to, float depth) =>
		Vector3.LerpUnclamped(from, to, (depth - from.z) / (to.z - from.z));

	private static bool IsAcrossFace(Vector3 point, Vector3 half) =>
		Mathf.Abs(point.x) <= half.x && Mathf.Abs(point.y) <= half.y;
}
