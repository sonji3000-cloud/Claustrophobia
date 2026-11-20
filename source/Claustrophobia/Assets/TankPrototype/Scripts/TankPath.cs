using UnityEngine;
using UnityEngine.AI;

// Owns a calculated route. TankAI remains the only source of driving input.
[DisallowMultipleComponent]
public sealed class TankPath : MonoBehaviour
{
	[SerializeField] private int agentId = 174295371;
	[SerializeField] private int areaMask = NavMesh.AllAreas;
	[SerializeField, Min(0.05f)] private float sampleDistance = 0.75f;
	[SerializeField] private bool drawPath = true;

	private readonly Vector3[] corners = new Vector3[64];
	private readonly Vector3[] probeCorners = new Vector3[64];
	private NavMeshPath route;
	private NavMeshPath probe;

	public int CornerCount { get; private set; }
	public int CornerIndex { get; private set; }
	public bool HasPath => CornerCount > 1;
	public bool IsLast => HasPath && CornerIndex == CornerCount - 1;
	public Vector3 Point => HasPath ? corners[CornerIndex] : transform.position;
	public Vector3 Goal => HasPath ? corners[CornerCount - 1] : transform.position;
	public float CornerAngle { get; private set; }

	private void Awake()
	{
		route = new NavMeshPath();
		probe = new NavMeshPath();
	}

	public bool TryBuild(Vector3 start, Vector3 goal, out string reason)
	{
		Clear();
		route ??= new NavMeshPath();
		if (!TryCalculate(start, goal, route, corners, out int count, out reason)) return false;

		CornerCount = count;
		CornerIndex = 1;
		UpdateCorner();
		return true;
	}

	internal bool TryMeasure(Vector3 start, Vector3 goal, Vector3 center, float radius,
		out Vector3 destination, out float length)
	{
		destination = default;
		length = 0f;
		probe ??= new NavMeshPath();
		if (!TryCalculate(start, goal, probe, probeCorners, out int count, out _)
			|| !IsWithin(probeCorners, count, center, radius)) return false;

		destination = probeCorners[count - 1];
		for (int i = 1; i < count; i++)
			length += Vector3.Distance(probeCorners[i - 1], probeCorners[i]);
		return true;
	}

	internal bool IsWithin(Vector3 center, float radius) => IsWithin(corners, CornerCount, center, radius);

	internal bool IsStraight(Vector3 start, Vector3 goal)
	{
		if (!isActiveAndEnabled) return false;

		var filter = new NavMeshQueryFilter { agentTypeID = agentId, areaMask = areaMask };
		if (!NavMesh.SamplePosition(start, out NavMeshHit from, sampleDistance, filter)
			|| !NavMesh.SamplePosition(goal, out NavMeshHit to, sampleDistance, filter)) return false;

		// A sample may snap sideways to an edge. Require the actual reverse line to be on the surface.
		Vector3 fromOffset = from.position - start;
		Vector3 toOffset = to.position - goal;
		fromOffset.y = toOffset.y = 0f;
		return fromOffset.sqrMagnitude <= 0.0225f && toOffset.sqrMagnitude <= 0.0225f
			&& !NavMesh.Raycast(from.position, to.position, out _, filter);
	}

	private static bool IsWithin(Vector3[] points, int count, Vector3 center, float radius)
	{
		float limit = radius * radius;
		for (int i = 0; i < count; i++)
		{
			Vector3 offset = points[i] - center;
			offset.y = 0f;
			if (offset.sqrMagnitude > limit) return false;
		}
		return true;
	}

	private bool TryCalculate(Vector3 start, Vector3 goal, NavMeshPath result, Vector3[] points,
		out int count, out string reason)
	{
		count = 0;
		reason = "";
		if (!isActiveAndEnabled)
		{
			reason = "Path component is disabled";
			return false;
		}
		var filter = new NavMeshQueryFilter { agentTypeID = agentId, areaMask = areaMask };
		if (!NavMesh.SamplePosition(start, out NavMeshHit from, sampleDistance, filter))
		{
			reason = "Start is outside the navigation surface";
			return false;
		}
		if (!NavMesh.SamplePosition(goal, out NavMeshHit to, sampleDistance, filter))
		{
			reason = "Destination is outside the navigation surface";
			return false;
		}
		if (!NavMesh.CalculatePath(from.position, to.position, filter, result)
			|| result.status != NavMeshPathStatus.PathComplete)
		{
			reason = "No complete path to destination";
			return false;
		}

		count = result.GetCornersNonAlloc(points);
		// A full buffer may contain a truncated route; never follow it as a complete one.
		if (count <= 0 || count >= points.Length)
		{
			reason = "Path corner buffer is empty or full";
			return false;
		}
		if (count == 1)
		{
			points[0] = from.position;
			points[1] = to.position;
			count = 2;
		}
		if ((points[count - 1] - to.position).sqrMagnitude > 0.01f)
		{
			reason = "Path does not end at destination";
			return false;
		}

		return true;
	}

	public void Advance()
	{
		if (!HasPath || IsLast) return;

		CornerIndex++;
		UpdateCorner();
	}

	public void Clear()
	{
		CornerCount = 0;
		CornerIndex = 0;
		CornerAngle = 0f;
	}

	private void UpdateCorner()
	{
		CornerAngle = 0f;
		if (IsLast) return;

		Vector3 before = corners[CornerIndex] - corners[CornerIndex - 1];
		Vector3 after = corners[CornerIndex + 1] - corners[CornerIndex];
		before.y = after.y = 0f;
		CornerAngle = Vector3.Angle(before, after);
	}

	private void OnDrawGizmos()
	{
		if (!drawPath || !HasPath) return;

		Vector3 lift = Vector3.up * 0.1f;
		Gizmos.color = Color.cyan;
		for (int i = 1; i < CornerCount; i++)
			Gizmos.DrawLine(corners[i - 1] + lift, corners[i] + lift);

		Gizmos.color = Color.yellow;
		Gizmos.DrawSphere(Point + lift, 0.2f);
		Gizmos.DrawLine(transform.position + lift, Point + lift);
	}

	private void OnValidate()
	{
		if (float.IsNaN(sampleDistance) || float.IsInfinity(sampleDistance)) sampleDistance = 0.75f;
		sampleDistance = Mathf.Max(0.05f, sampleDistance);
	}
}
