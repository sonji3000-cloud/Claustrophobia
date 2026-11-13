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
	private NavMeshPath route;

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
	}

	public bool TryBuild(Vector3 start, Vector3 goal, out string reason)
	{
		Clear();
		reason = "";
		if (!isActiveAndEnabled)
		{
			reason = "Path component is disabled";
			return false;
		}
		if (route == null) route = new NavMeshPath();

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
		if (!NavMesh.CalculatePath(from.position, to.position, filter, route)
			|| route.status != NavMeshPathStatus.PathComplete)
		{
			reason = "No complete path to destination";
			return false;
		}

		int count = route.GetCornersNonAlloc(corners);
		// A full buffer may contain a truncated route; never follow it as a complete one.
		if (count <= 0 || count >= corners.Length)
		{
			reason = "Path corner buffer is empty or full";
			return false;
		}
		if (count == 1)
		{
			corners[0] = from.position;
			corners[1] = to.position;
			count = 2;
		}
		if ((corners[count - 1] - to.position).sqrMagnitude > 0.01f)
		{
			reason = "Path does not end at destination";
			return false;
		}

		CornerCount = count;
		CornerIndex = 1;
		UpdateCorner();
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
