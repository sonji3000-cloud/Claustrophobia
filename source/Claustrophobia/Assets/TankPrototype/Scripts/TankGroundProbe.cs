using Fusion;
using UnityEngine;

// Reports ground support only. TankMotor owns movement and Rigidbody changes.
[DisallowMultipleComponent]
[DefaultExecutionOrder(-50)]
public sealed class TankGroundProbe : MonoBehaviour
{
	public const int PointCount = 5;

	[SerializeField] private LayerMask groundMask = 1;
	[SerializeField] private Vector3 center;
	[SerializeField] private Vector2 halfSize = new Vector2(1.2f, 2.6f);
	[SerializeField, Min(0.05f)] private float startHeight = 0.6f;
	[SerializeField, Min(0.05f)] private float scanDepth = 1f;
	[SerializeField, Min(0f)] private float contactGap = 0.2f;
	[SerializeField, Min(0f)] private float penetration = 0.12f;
	[SerializeField, Range(1, PointCount)] private int minContacts = 2;
	[SerializeField, Range(0f, 85f)] private float maxSlope = 60f;
	[SerializeField, Min(1f)] private float normalSpeed = 180f;
	[SerializeField] private bool drawGizmos = true;

	private readonly RaycastHit[] hits = new RaycastHit[16];
	private readonly GroundPoint[] points = new GroundPoint[PointCount];
	private NetworkObject networkObject;
	private bool hasNormal;

	public struct GroundPoint
	{
		public Vector3 Origin;
		public Vector3 Point;
		public Vector3 Normal;
		public float Gap;
		public bool Hit;
		public bool Contact;
	}

	public bool IsGrounded { get; private set; }
	public int HitCount { get; private set; }
	public int ContactCount { get; private set; }
	public bool BufferFull { get; private set; }
	public Vector3 GroundNormal { get; private set; } = Vector3.up;
	public float SlopeAngle => Vector3.Angle(Vector3.up, GroundNormal);
	public Vector3 Center => center;
	public Vector2 HalfSize => halfSize;
	public float RayLength => startHeight + scanDepth;

	private void Awake()
	{
		networkObject = GetComponent<NetworkObject>();
	}

	private void OnEnable()
	{
		hasNormal = false;
		Sample();
	}

	private void FixedUpdate()
	{
		if (networkObject != null && (!networkObject.IsValid || !networkObject.HasStateAuthority)) return;

		Sample();
	}

	public GroundPoint ReadPoint(int index) => points[index];

	public void Sample()
	{
		HitCount = 0;
		ContactCount = 0;
		BufferFull = false;
		float minUp = Mathf.Cos(maxSlope * Mathf.Deg2Rad);

		for (int i = 0; i < PointCount; i++)
		{
			Vector3 foot = transform.TransformPoint(LocalPoint(i));
			GroundPoint point = CastPoint(foot, minUp);
			points[i] = point;
			if (point.Hit) HitCount++;
			if (point.Contact) ContactCount++;
		}

		IsGrounded = ContactCount >= minContacts;
		if (!IsGrounded) return;

		// Keep the previous supported plane when only one end of the hull remains.
		if (hasNormal && HitCount < 3) return;

		Vector3 normal = FindNormal();
		GroundNormal = !hasNormal || !Application.isPlaying
			? normal
			: Vector3.RotateTowards(GroundNormal, normal,
				normalSpeed * Mathf.Deg2Rad * Time.fixedDeltaTime, 0f).normalized;
		hasNormal = true;
	}

	private GroundPoint CastPoint(Vector3 foot, float minUp)
	{
		var point = new GroundPoint { Origin = foot + Vector3.up * startHeight };
		int count = Physics.RaycastNonAlloc(point.Origin, Vector3.down, hits,
			RayLength, groundMask, QueryTriggerInteraction.Ignore);

		// NonAlloc results are unordered and a full buffer can hide the nearest hit.
		if (count == hits.Length)
		{
			BufferFull = true;
			return point;
		}

		float nearest = float.PositiveInfinity;
		for (int i = 0; i < count; i++)
		{
			RaycastHit hit = hits[i];
			Collider collider = hit.collider;
			if (collider == null || collider.attachedRigidbody != null
				|| collider.transform.IsChildOf(transform) || hit.normal.y < minUp) continue;
			if (hit.distance >= nearest) continue;

			nearest = hit.distance;
			point.Hit = true;
			point.Point = hit.point;
			point.Normal = hit.normal;
			point.Gap = hit.distance - startHeight;
			point.Contact = point.Gap >= -penetration && point.Gap <= contactGap;
		}

		return point;
	}

	private Vector3 FindNormal()
	{
		Vector3 plane = Vector3.zero;
		Vector3 normals = Vector3.zero;
		for (int i = 0; i < PointCount; i++)
		{
			if (!points[i].Hit) continue;
			normals += points[i].Normal;
			for (int j = i + 1; j < PointCount; j++)
			{
				if (!points[j].Hit) continue;
				for (int k = j + 1; k < PointCount; k++)
				{
					if (!points[k].Hit) continue;
					Vector3 cross = Vector3.Cross(points[j].Point - points[i].Point,
						points[k].Point - points[i].Point);
					plane += cross.y >= 0f ? cross : -cross;
				}
			}
		}

		Vector3 normal = plane.sqrMagnitude > 0.0001f ? plane.normalized : normals.normalized;
		// A ledge between different heights must not invent a near-vertical support plane.
		return Vector3.Angle(Vector3.up, normal) <= maxSlope ? normal : normals.normalized;
	}

	private Vector3 LocalPoint(int index)
	{
		if (index == 0) return center;

		float x = index == 1 || index == 3 ? -halfSize.x : halfSize.x;
		float z = index <= 2 ? halfSize.y : -halfSize.y;
		return center + new Vector3(x, 0f, z);
	}

	private void OnDrawGizmosSelected()
	{
		if (!drawGizmos) return;
		if (!Application.isPlaying) Sample();

		for (int i = 0; i < PointCount; i++)
		{
			GroundPoint point = points[i];
			Gizmos.color = point.Contact ? Color.green : point.Hit ? Color.yellow : Color.red;
			Vector3 end = point.Hit ? point.Point : point.Origin + Vector3.down * RayLength;
			Gizmos.DrawLine(point.Origin, end);
			Gizmos.DrawWireSphere(end, 0.08f);
		}

		Vector3 pos = transform.TransformPoint(center);
		Gizmos.color = IsGrounded ? Color.cyan : Color.gray;
		Gizmos.DrawLine(pos, pos + GroundNormal * 2f);
	}

	private void OnValidate()
	{
		halfSize.x = Mathf.Max(0.05f, halfSize.x);
		halfSize.y = Mathf.Max(0.05f, halfSize.y);
		startHeight = Mathf.Max(0.05f, startHeight);
		scanDepth = Mathf.Max(0.05f, scanDepth);
		contactGap = Mathf.Clamp(contactGap, 0f, scanDepth);
		penetration = Mathf.Clamp(penetration, 0f, startHeight);
		minContacts = Mathf.Clamp(minContacts, 1, PointCount);
		maxSlope = Mathf.Clamp(maxSlope, 0f, 85f);
		normalSpeed = Mathf.Max(1f, normalSpeed);
	}
}
