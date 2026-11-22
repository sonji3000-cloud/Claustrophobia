using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody))]
public sealed class StreetProp : MonoBehaviour
{
	private enum Response { Splinter, Dent, Topple, Collapse }

	[SerializeField] private Response response;
	[SerializeField, Min(0.1f)] private float impactSpeed = 3f;
	[SerializeField, Min(0f)] private float pushTime = 0.3f;
	[SerializeField] private MeshFilter mesh;
	[SerializeField] private MeshRenderer visual;
	[SerializeField] private Mesh damagedMesh;
	[SerializeField] private Collider[] solidColliders;
	[SerializeField] private Collider releasedCollider;
	[SerializeField] private Rigidbody[] fragments;

	private Rigidbody body;
	private StreetPropLifetime lifetime;
	private Rigidbody pushingTank;
	private double lastPush = double.NegativeInfinity;
	private float pushedFor;
	private bool changed;

	private void Awake()
	{
		body = GetComponent<Rigidbody>();
		TryGetComponent(out lifetime);
	}

	private void OnCollisionEnter(Collision collision) => CheckImpact(collision);
	private void OnCollisionStay(Collision collision) => CheckImpact(collision);

	private void CheckImpact(Collision collision)
	{
		if (changed || collision.contactCount == 0) return;

		Rigidbody tank = collision.rigidbody;
		if (tank == null || tank.isKinematic || !tank.TryGetComponent(out TankMotor motor)) return;

		ContactPoint contact = collision.GetContact(0);
		Vector3 toward = Vector3.ProjectOnPlane(contact.point - tank.worldCenterOfMass, Vector3.up);
		Vector3 dir = Vector3.ProjectOnPlane(contact.normal, Vector3.up).normalized;
		if (dir.sqrMagnitude < 0.01f || toward.sqrMagnitude < 0.01f) return;
		if (Vector3.Dot(dir, toward) < 0f) dir = -dir;

		float speed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, dir));
		if (speed < impactSpeed && (!body.isKinematic || !IsPushed(tank, motor, dir))) return;

		changed = true;
		switch (response)
		{
			case Response.Splinter:
				Splinter(dir);
				break;
			case Response.Dent:
				mesh.sharedMesh = damagedMesh;
				SwapCollider();
				break;
			case Response.Topple:
			case Response.Collapse:
				if (damagedMesh != null) mesh.sharedMesh = damagedMesh;
				SwapCollider();
				body.isKinematic = false;
				body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
				body.linearVelocity = dir * Mathf.Clamp(speed, 1.15f, 3f);
				body.angularVelocity = Vector3.Cross(Vector3.up, dir) * 1.7f;
				break;
		}
		if (lifetime != null) lifetime.RefreshTankContacts();
	}

	private bool IsPushed(Rigidbody tank, TankMotor motor, Vector3 dir)
	{
		double now = Time.fixedTimeAsDouble;
		Vector3 drive = tank.rotation * Vector3.forward * motor.ForwardCommand;
		if (Vector3.Dot(drive, dir) <= 0.08f)
		{
			if (tank == pushingTank) pushedFor = 0f;
			return false;
		}
		if (tank == pushingTank && now == lastPush) return false;
		if (tank != pushingTank || now - lastPush > Time.fixedDeltaTime * 1.5)
			pushedFor = 0f;

		pushingTank = tank;
		lastPush = now;
		pushedFor += Time.fixedDeltaTime;
		return pushedFor >= pushTime;
	}

	private void SwapCollider()
	{
		foreach (var collider in solidColliders) collider.enabled = false;
		if (releasedCollider != null) releasedCollider.enabled = true;
	}

	private void Splinter(Vector3 dir)
	{
		Vector3 velocity = body.linearVelocity;
		Vector3 center = body.worldCenterOfMass;
		visual.enabled = false;
		SwapCollider();
		body.detectCollisions = false;
		body.isKinematic = true;

		Transform parent = transform.parent;
		foreach (Rigidbody fragment in fragments)
		{
			Transform piece = fragment.transform;
			Vector3 scatter = (piece.position - center).normalized;
			piece.SetParent(parent, true);
			fragment.gameObject.SetActive(true);
			fragment.linearVelocity = velocity + 1.4f * dir + 0.7f * scatter + Vector3.up * 0.5f;
			fragment.angularVelocity = Vector3.Cross(scatter, Vector3.up) * 3f;
		}
	}
}
