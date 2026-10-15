using UnityEngine;
using Unity.Profiling;
#if UNITY_6000_4_OR_NEWER
using TankObjectId = UnityEngine.EntityId;
#else
using TankObjectId = System.Int32;
#endif

// Preserve the complete object identity: EntityId must never be narrowed to an int.
internal static class TankObjectIdentity
{
    public static TankObjectId Get(Object value)
    {
#if UNITY_6000_4_OR_NEWER
        return value.GetEntityId();
#else
        return value.GetInstanceID();
#endif
    }
}

// One captured launch condition is shared by actual firing and prediction.
public struct TankShotConditions
{
    public Vector3 Position, Velocity, Gravity, Center;
    public Quaternion Rotation;
    public float Radius, Step, Lifetime;
    public int Layer;
}

public static class TankBallisticStep
{
    public static void Advance(ref Vector3 center, ref Vector3 velocity, Vector3 gravity, float h)
    {
        velocity += gravity * h;
        center += velocity * h;
    }
}

public enum TankPredictionState { Hit, NoHit, InitialOverlap, Invalid, Disabled }

public struct TankPrediction
{
    public TankPredictionState State;
    public Collider Collider;
    public TankObjectId ColliderId, PhysicalTargetId;
    public int Steps, Queries;
    public string ColliderName, PhysicalTargetName, Reason;
    public Vector3 Point, Normal;
    public float HitTime, CheckedTime;
    public bool HasHit => State == TankPredictionState.Hit;
    public void CopyIdentity()
    {
        if (Collider == null) return;
        ColliderId = TankObjectIdentity.Get(Collider); ColliderName = Collider.name;
        Object target = Collider.attachedRigidbody;
        if (target == null) target = Collider.GetComponentInParent<TankHealth>();
        if (target == null) target = Collider;
        PhysicalTargetId = TankObjectIdentity.Get(target); PhysicalTargetName = target.name;
    }
}

public struct TankImpactComparison
{
    public int ShotId;
    public TankObjectId ExpectedColliderId, ActualColliderId, ExpectedTargetId, ActualTargetId;
    public string ExpectedName, ActualName, Result;
    public Vector3 ExpectedPoint, ActualPoint;
    public float Error;
    public bool Comparable, SameCollider, Passed;
}

[DisallowMultipleComponent]
[RequireComponent(typeof(TankGun))]
public sealed class TankBallistics : MonoBehaviour
{
    public bool PredictionEnabled = true;
    public bool ShowMarker = true;
    public bool ShowDiagnostics = true;
    [Min(0.001f)] public float UpdateInterval = .05f;
    [Min(0f)] public float MaxPredictionTime = 5f;
    [Min(0)] public int MaxSteps = 300;
    [Min(0.01f)] public float MarkerRadius = .4f;
    [Min(0.001f)] public float MarkerLineWidth = .025f;
    [Min(0f)] public float SurfaceOffset = .02f;
    [SerializeField] private Transform marker;
    [SerializeField] private LineRenderer ring;

    private TankGun gun;
    private float nextRefresh;
    private readonly RaycastHit[] hits = new RaycastHit[64];
    private readonly Collider[] overlaps = new Collider[64];
    private readonly Vector3[] circle = new Vector3[64];
    public TankPrediction Current { get; private set; }
    public TankShotConditions CurrentConditions { get; private set; }
    public bool HasPredictedHit => isActiveAndEnabled && PredictionEnabled && Current.HasHit;
    public bool MarkerVisible => marker != null && marker.gameObject.activeSelf;
    public Vector3 MarkerPosition => marker != null ? marker.position : Vector3.zero;
    public long PredictionCount { get; private set; }
    public int SaturatedQueryCount { get; private set; }
    public double LastComputeMilliseconds { get; private set; }
    private static readonly ProfilerMarker predictMarker = new ProfilerMarker("TankBallistics.Predict");
    private static readonly ProfilerMarker refreshMarker = new ProfilerMarker("TankBallistics.Refresh");

    private void Awake() { gun = GetComponent<TankGun>(); HideMarker(); }
    private void OnDisable() { Current = new TankPrediction { State = TankPredictionState.Disabled, Reason = "Prediction disabled" }; HideMarker(); }
    private void OnDestroy() { HideMarker(); }
    private void LateUpdate()
    {
        if (!PredictionEnabled) { Current = new TankPrediction { State = TankPredictionState.Disabled, Reason = "Prediction disabled" }; HideMarker(); return; }
        if (Time.time < nextRefresh) return;
        // Do not replay missed refreshes after a long frame.
        nextRefresh = Time.time + Mathf.Max(.001f, UpdateInterval);
        Refresh();
    }
    public void Refresh()
    {
        using (refreshMarker.Auto())
        {
            if (gun == null) gun = GetComponent<TankGun>();
            if (!isActiveAndEnabled || !PredictionEnabled)
            { Current = new TankPrediction { State = TankPredictionState.Disabled, Reason = "Prediction disabled" }; HideMarker(); return; }
            if (!gun.TryGetShotConditions(out TankShotConditions conditions, out string reason))
            { Current = new TankPrediction { State = TankPredictionState.Invalid, Reason = reason }; HideMarker(); return; }
            CurrentConditions = conditions;
            Current = Predict(conditions, gun.OwnerColliders);
            UpdateMarker();
        }
    }
    private static bool Own(Collider c, System.Collections.Generic.IReadOnlyList<Collider> owner)
    { for (int i = 0; i < owner.Count; i++) if (owner[i] == c) return true; return false; }
    private static int CollisionMask(int layer)
    {
        int mask = 0;
        for (int i = 0; i < 32; i++) if (!Physics.GetIgnoreLayerCollision(layer, i)) mask |= 1 << i;
        return mask; // Includes Ignore Raycast if the actual collision matrix permits it.
    }
    public TankPrediction Predict(TankShotConditions c, System.Collections.Generic.IReadOnlyList<Collider> owner)
    {
        using (predictMarker.Auto())
        {
            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            PredictionCount++;
            TankPrediction result = new TankPrediction { State = TankPredictionState.NoHit, Reason = "No hit within checked range" };
            if (c.Step <= 0f || c.Radius <= 0f || owner == null || float.IsNaN(MaxPredictionTime))
                return new TankPrediction { State = TankPredictionState.Invalid, Reason = "Invalid step, shape or budget" };
            // One synchronization per batch, never once per integration segment.
            Physics.SyncTransforms();
            int mask = CollisionMask(c.Layer);
            int n = Physics.OverlapSphereNonAlloc(c.Center, c.Radius, overlaps, mask, QueryTriggerInteraction.Ignore);
            result.Queries++;
            Collider[] initial = overlaps;
            if (n == overlaps.Length) { initial = Physics.OverlapSphere(c.Center, c.Radius, mask, QueryTriggerInteraction.Ignore); n = initial.Length; SaturatedQueryCount++; result.Queries++; }
            for (int i = 0; i < n; i++)
                if (!Own(initial[i], owner))
                { result.State = TankPredictionState.InitialOverlap; result.Reason = "Muzzle sphere overlaps an external collider"; return Finish(result, start); }
            int steps = Mathf.Min(Mathf.Max(0, MaxSteps), Mathf.FloorToInt(Mathf.Min(c.Lifetime, Mathf.Max(0f, MaxPredictionTime)) / c.Step));
            Vector3 center = c.Center, velocity = c.Velocity;
            for (int step = 0; step < steps; step++)
            {
                Vector3 next = center, nextVelocity = velocity;
                TankBallisticStep.Advance(ref next, ref nextVelocity, c.Gravity, c.Step);
                Vector3 delta = next - center; float distance = delta.magnitude;
                result.Steps = step + 1;
                if (distance > 0f)
                {
                    Vector3 direction = delta / distance;
                    n = Physics.SphereCastNonAlloc(center, c.Radius, direction, hits, distance, mask, QueryTriggerInteraction.Ignore);
                    RaycastHit[] segment = hits; result.Queries++;
                    if (n == hits.Length) { segment = Physics.SphereCastAll(center, c.Radius, direction, distance, mask, QueryTriggerInteraction.Ignore); n = segment.Length; SaturatedQueryCount++; result.Queries++; }
                    int best = -1; float nearest = float.PositiveInfinity;
                    for (int i = 0; i < n; i++)
                    {
                        if (segment[i].collider == null || Own(segment[i].collider, owner)) continue;
                        if (segment[i].distance < nearest) { best = i; nearest = segment[i].distance; }
                    }
                    if (best >= 0)
                    {
                        RaycastHit hit = segment[best];
                        if (hit.distance <= 0f)
                        { result.State = TankPredictionState.InitialOverlap; result.Reason = "Zero-distance sweep contact; invalid start segment"; return Finish(result, start); }
                        result.State = TankPredictionState.Hit; result.Collider = hit.collider;
                        result.Point = hit.point; result.Normal = hit.normal;
                        result.HitTime = step * c.Step + c.Step * Mathf.Clamp01(hit.distance / distance);
                        result.CheckedTime = result.HitTime; result.ColliderId = TankObjectIdentity.Get(hit.collider);
                        return Finish(result, start);
                    }
                }
                center = next; velocity = nextVelocity; result.CheckedTime = (step + 1) * c.Step;
            }
            result.Reason = MaxSteps * c.Step <= Mathf.Min(c.Lifetime, MaxPredictionTime) ? "Step budget reached; no hit in checked range"
                : MaxPredictionTime < c.Lifetime ? "Prediction time reached; no hit in checked range" : "Projectile lifetime reached; no hit in checked range";
            return Finish(result, start);
        }
    }
    private TankPrediction Finish(TankPrediction result, long start)
    {
        LastComputeMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        return result;
    }
    private void HideMarker() { if (marker != null) marker.gameObject.SetActive(false); }
    private void UpdateMarker()
    {
        if (!ShowMarker || !Current.HasHit || marker == null) { HideMarker(); return; }
        Vector3 normal = Current.Normal;
        bool validNormal = normal.sqrMagnitude > .01f;
        if (validNormal)
        {
            normal.Normalize();
            // Correct only the display normal; keep the raw contact point untouched.
            if (Current.Collider.Raycast(new Ray(Current.Point + normal * .2f, -normal), out RaycastHit face, .4f)) normal = face.normal;
        }
        marker.position = Current.Point + (validNormal ? normal * SurfaceOffset : Vector3.zero);
        marker.rotation = validNormal ? Quaternion.FromToRotation(Vector3.up, normal) : Quaternion.identity;
        if (ring != null)
        {
            ring.enabled = validNormal;
            ring.startWidth = ring.endWidth = MarkerLineWidth;
            for (int i = 0; i < circle.Length; i++)
            { float angle = i * Mathf.PI * 2f / circle.Length; circle[i] = new Vector3(Mathf.Cos(angle) * MarkerRadius, 0f, Mathf.Sin(angle) * MarkerRadius); }
            ring.SetPositions(circle);
        }
        marker.gameObject.SetActive(true);
    }
}
