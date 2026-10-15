using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(SphereCollider))]
public sealed class TankProjectile : MonoBehaviour
{
    public const float TestLifetime = 5f;
    [Min(0.01f)] public float Lifetime = TestLifetime;
    public TankShotConditions LaunchConditions { get; private set; }
    public TankPrediction LaunchPrediction { get; private set; }
    public TankImpactComparison ImpactComparison { get; private set; }
    public void SetLaunchPrediction(TankPrediction prediction) => LaunchPrediction = prediction;
    public int ShotId { get; private set; }
    public bool HasEnded { get; private set; }
    public bool IsInitialized { get; private set; }
    public string EndReason { get; private set; }
    public Vector3 LaunchPosition { get; private set; }
    public Vector3 LaunchDirection { get; private set; }
    public float Damage { get; private set; }

    private Rigidbody body;
    private SphereCollider sphere;
    private TankGun source;
    private Rigidbody owner;
    private float bornAt;

    public static bool ValidateConfiguration(TankProjectile projectile, out string reason)
    {
        if (projectile == null) { reason = "Missing Projectile Prefab"; return false; }
        if (!projectile.enabled || !projectile.gameObject.activeSelf)
        { reason = "Projectile must be active and enabled"; return false; }
        var rb = projectile.GetComponent<Rigidbody>();
        var collider = projectile.GetComponent<SphereCollider>();
        if (rb == null || collider == null)
        { reason = "Projectile requires its own Rigidbody and SphereCollider"; return false; }
        if (rb.isKinematic || !rb.useGravity || rb.linearDamping != 0f
            || rb.constraints != RigidbodyConstraints.None
            || rb.collisionDetectionMode != CollisionDetectionMode.ContinuousDynamic)
        { reason = "Projectile needs dynamic / gravity / zero damping / free motion / Continuous Dynamic"; return false; }
        if (float.IsNaN(projectile.Lifetime) || float.IsInfinity(projectile.Lifetime) || projectile.Lifetime <= 0f)
        { reason = "Projectile Lifetime must be finite and positive"; return false; }
        if (!collider.enabled || collider.isTrigger || collider.radius <= 0f)
        { reason = "Projectile needs an enabled non-trigger sphere with positive radius"; return false; }
        Vector3 scale = projectile.transform.lossyScale;
        if (scale.x <= 0f || scale.y <= 0f || scale.z <= 0f)
        { reason = "Projectile scale must be positive"; return false; }
        if (projectile.GetComponentsInChildren<Rigidbody>(true).Length != 1
            || projectile.GetComponentsInChildren<Collider>(true).Length != 1)
        { reason = "Projectile must have exactly one Rigidbody and one Collider"; return false; }
        reason = ""; return true;
    }

    public bool Initialize(int id, TankGun gun, Rigidbody ownerBody,
        System.Collections.Generic.IReadOnlyList<Collider> ownerColliders, TankShotConditions conditions, float damage, out string reason)
    {
        if (IsInitialized) { reason = "Projectile was already initialized"; return false; }
        if (!ValidateConfiguration(this, out reason)) return false;
        if (ownerBody == null || ownerColliders == null || gun == null)
        { reason = "Missing projectile owner"; return false; }
        body = GetComponent<Rigidbody>();
        sphere = GetComponent<SphereCollider>();
        if (body == ownerBody) { reason = "Projectile cannot use the Tank Rigidbody"; return false; }
        for (int i = 0; i < ownerColliders.Count; i++)
        {
            Collider ownerCollider = ownerColliders[i];
            if (ownerCollider != null && ownerCollider.attachedRigidbody == ownerBody)
                Physics.IgnoreCollision(sphere, ownerCollider, true);
        }
        ShotId = id;
        source = gun;
        owner = ownerBody;
        LaunchPosition = body.position;
        LaunchDirection = conditions.Velocity.normalized;
        LaunchConditions = conditions;
        Damage = damage;
        bornAt = Time.time;
        body.linearVelocity = conditions.Velocity;
        IsInitialized = true;
        reason = "";
        return true;
    }

    private void Update()
    {
        if (IsInitialized && !HasEnded && Time.time - bornAt >= LaunchConditions.Lifetime)
            Finish("Lifetime expired", null, body.position);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsInitialized || HasEnded) return;
        if (collision.collider.attachedRigidbody == owner) return;
        Vector3 point = collision.contactCount > 0 ? collision.GetContact(0).point : body.position;
        Finish("Collision", collision.collider, point, collision.contactCount > 0);
    }

    private void Finish(string reason, Collider target, Vector3 point, bool hasContact = false)
    {
        if (HasEnded) return;
        HasEnded = true;
        EndReason = reason;
        sphere.enabled = false;
        string targetName = target != null ? target.name : "(none)";
        try
        {
            TankPrediction actual = new TankPrediction { Collider = target };
            actual.CopyIdentity();
            bool comparable = LaunchPrediction.HasHit && target != null && hasContact;
            bool same = comparable && LaunchPrediction.ColliderId == actual.ColliderId;
            float error = comparable ? Vector3.Distance(LaunchPrediction.Point, point) : -1f;
            ImpactComparison = new TankImpactComparison {
                ShotId = ShotId, ExpectedColliderId = LaunchPrediction.ColliderId, ActualColliderId = actual.ColliderId,
                ExpectedTargetId = LaunchPrediction.PhysicalTargetId, ActualTargetId = actual.PhysicalTargetId,
                ExpectedName = LaunchPrediction.ColliderName ?? LaunchPrediction.State.ToString(), ActualName = actual.ColliderName ?? "No actual hit",
                ExpectedPoint = LaunchPrediction.Point, ActualPoint = point, Error = error, Comparable = comparable, SameCollider = same,
                Passed = same && error <= .25f,
                Result = !comparable ? target == null ? "Lifetime expired; no actual hit" : "No comparable prediction/contact"
                    : !same ? LaunchPrediction.PhysicalTargetId == actual.PhysicalTargetId ? "Compound collider boundary; different collider" : "Different first collider"
                    : error <= .25f ? "Collider agrees; error <= 0.25m" : "Contact error exceeds 0.25m"
            };
            source?.RecordImpactComparison(ImpactComparison);
            if (target != null)
            {
                TankHealth health = target.GetComponentInParent<TankHealth>();
                float before = 0f, after = 0f;
                string result = "No HP component";
                bool applied = health != null && health.ApplyDamage(Damage, ShotId, out before, out after, out result);
                source?.RecordDamageResult(ShotId, health, Damage, applied, before, after, result);
            }
            source?.RecordProjectileEnd(ShotId, reason, targetName, point);
            Debug.Log($"Projectile #{ShotId}: {reason}; target={targetName} [{actual.ColliderId}]; point={point.ToString("F3")}; predicted={ImpactComparison.ExpectedName} [{ImpactComparison.ExpectedColliderId}]; error={(comparable ? error.ToString("F4") : "n/a")}; {ImpactComparison.Result}");
        }
        finally
        {
            Destroy(gameObject);
        }
    }
}
