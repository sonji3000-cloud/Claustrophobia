using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class TankGun : MonoBehaviour
{
    public enum ChamberState { EmptyOpen, EmptyClosed, LoadedOpen, Ready, Recovering }
    [SerializeField] private Transform muzzle;
    [SerializeField] private TankProjectile projectilePrefab;
    [SerializeField] private Rigidbody ownerBody;
    [Tooltip("Additional test blocker only. Ready chamber state is always required.")]
    public bool TestAllowFire = true;
    [Min(0.01f)] public float ProjectileSpeed = 20f;
    [Min(0.01f)] public float ShotDamage = 25f;
    [Tooltip("Test delay for recovery, breech opening and case ejection; not final gameplay timing.")]
    [Min(0f)] public float RecoveryDuration = 1.5f;

    public ChamberState State { get; private set; } = ChamberState.EmptyOpen;
    public bool HasLiveRound => State == ChamberState.LoadedOpen || State == ChamberState.Ready;
    public bool CanInsert => isActiveAndEnabled && State == ChamberState.EmptyOpen;
    public float RecoverRemaining => State == ChamberState.Recovering
        ? Mathf.Max(0f, recoveryEndsAt - Time.time) : 0f;
    public string LastWeaponRequest { get; private set; } = "None";
    public string LastWeaponResult { get; private set; } = "No request";
    public int DamageHitCount { get; private set; }
    public string LastHitSummary { get; private set; } = "No impact";
    private float recoveryEndsAt;
    private readonly System.Collections.Generic.List<Collider> ownerColliders = new System.Collections.Generic.List<Collider>();

    public Transform Muzzle => muzzle;
    public TankProjectile ProjectilePrefab => projectilePrefab;
    public bool CanFire => GetBlockReason() == "";
    public string FireBlockReason => GetBlockReason();
    public int ShotCount { get; private set; }
    public float LastSuccessfulFireTime { get; private set; } = -1f;
    public string LastFireResult { get; private set; } = "No request";
    public TankProjectile LastProjectile { get; private set; }
    public int EndedProjectileCount { get; private set; }
    public int LastEndedShotId { get; private set; }
    public string LastProjectileEndReason { get; private set; } = "None";
    public string LastHitName { get; private set; } = "None";
    public Vector3 LastHitPoint { get; private set; }
    public TankImpactComparison LastImpactComparison { get; private set; }

    // No chamber transitions or fire approval are performed by this query.
    public bool TryGetShotConditions(out TankShotConditions conditions, out string reason)
    {
        conditions = default;
        if (muzzle == null || !muzzle.gameObject.activeInHierarchy || ownerBody == null
            || !ownerBody.gameObject.activeInHierarchy)
        { reason = "Missing or inactive Muzzle/owner"; return false; }
        if (!muzzle.IsChildOf(ownerBody.transform))
        { reason = "Muzzle must belong to owner Tank"; return false; }
        if (float.IsNaN(ProjectileSpeed) || float.IsInfinity(ProjectileSpeed) || ProjectileSpeed <= 0f)
        { reason = "Projectile Speed must be finite and positive"; return false; }
        if (!TankProjectile.ValidateConfiguration(projectilePrefab, out reason)) return false;
        var sphere = projectilePrefab.GetComponent<SphereCollider>();
        Vector3 scale = projectilePrefab.transform.localScale;
        Quaternion rotation = muzzle.rotation;
        Vector3 position = muzzle.position;
        conditions = new TankShotConditions {
            Position = position, Rotation = rotation, Velocity = rotation * Vector3.forward * ProjectileSpeed,
            Gravity = Physics.gravity, Step = Time.fixedDeltaTime, Lifetime = projectilePrefab.Lifetime,
            Center = position + rotation * Vector3.Scale(sphere.center, scale),
            Radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)),
            Layer = projectilePrefab.gameObject.layer
        };
        ownerBody.GetComponentsInChildren(true, ownerColliders);
        for (int i = ownerColliders.Count - 1; i >= 0; i--)
            if (ownerColliders[i].attachedRigidbody != ownerBody) ownerColliders.RemoveAt(i);
        return true;
    }

    public System.Collections.Generic.IReadOnlyList<Collider> OwnerColliders => ownerColliders;

    private string GetBlockReason()
    {
        if (!isActiveAndEnabled) return "Fire component inactive";
        if (State != ChamberState.Ready) return "Chamber " + State + "; Ready required";
        if (!TestAllowFire) return "Test Allow Fire is false";
        if (muzzle == null) return "Missing Muzzle";
        if (ownerBody == null) return "Missing owner Rigidbody";
        if (!muzzle.IsChildOf(ownerBody.transform)) return "Muzzle must belong to owner Tank";
        if (float.IsNaN(ProjectileSpeed) || float.IsInfinity(ProjectileSpeed) || ProjectileSpeed <= 0f)
            return "Projectile Speed must be finite and positive";
        if (float.IsNaN(ShotDamage) || float.IsInfinity(ShotDamage) || ShotDamage <= 0f)
            return "Shot Damage must be finite and positive";
        if (float.IsNaN(RecoveryDuration) || float.IsInfinity(RecoveryDuration) || RecoveryDuration < 0f)
            return "Recovery Duration must be finite and non-negative";
        if (!TankProjectile.ValidateConfiguration(projectilePrefab, out string reason)) return reason;
        return "";
    }

    // Called before weapon input. Absolute end time avoids subtracting a frame
    // that elapsed before the shot, and rejection never restarts the timer.
    public void TickRecovery()
    {
        if (State == ChamberState.Recovering && Time.time >= recoveryEndsAt)
            ChangeState(ChamberState.EmptyOpen);
    }

    private void ChangeState(ChamberState next)
    {
        ChamberState previous = State;
        State = next;
        Debug.Log($"Tank chamber: {previous} -> {next}", this);
    }

    private bool Reject(string request, string reason)
    {
        LastWeaponRequest = request;
        LastWeaponResult = "Rejected: " + reason;
        if (request == "Fire") LastFireResult = LastWeaponResult;
        Debug.Log($"Tank weapon {request}: {LastWeaponResult}", this);
        return false;
    }

    private bool CompleteOperation(string request, ChamberState next)
    {
        LastWeaponRequest = request;
        ChangeState(next);
        LastWeaponResult = request + " completed; " + State;
        return true;
    }

    public bool RequestInsert()
    {
        TickRecovery();
        if (!isActiveAndEnabled) return Reject("Insert", "Weapon component inactive");
        if (State != ChamberState.EmptyOpen) return Reject("Insert", "Requires EmptyOpen; current " + State);
        return CompleteOperation("Insert", ChamberState.LoadedOpen);
    }

    public bool RequestClose()
    {
        TickRecovery();
        if (!isActiveAndEnabled) return Reject("Close", "Weapon component inactive");
        if (State == ChamberState.EmptyOpen) return CompleteOperation("Close", ChamberState.EmptyClosed);
        if (State == ChamberState.LoadedOpen) return CompleteOperation("Close", ChamberState.Ready);
        return Reject("Close", "Requires open chamber; current " + State);
    }

    public bool RequestOpenEmpty()
    {
        TickRecovery();
        if (!isActiveAndEnabled) return Reject("Open empty", "Weapon component inactive");
        if (State != ChamberState.EmptyClosed) return Reject("Open empty", "Requires EmptyClosed; current " + State);
        return CompleteOperation("Open empty", ChamberState.EmptyOpen);
    }

    // All requests are validated and consumed here, including rejected requests.
    public bool RequestFire()
    {
        TickRecovery();
        LastWeaponRequest = "Fire";
        string reason = GetBlockReason();
        if (reason != "")
            return Reject("Fire", reason);

        if (!TryGetShotConditions(out TankShotConditions conditions, out reason))
            return Reject("Fire", reason);
        float speed = conditions.Velocity.magnitude;
        float damage = ShotDamage;
        float recovery = RecoveryDuration;
        var predictor = GetComponent<TankBallistics>();
        TankPrediction predicted = new TankPrediction { State = TankPredictionState.Disabled, Reason = "Prediction disabled" };
        if (predictor != null && predictor.isActiveAndEnabled && predictor.PredictionEnabled)
            predicted = predictor.Predict(conditions, ownerColliders);
        predicted.CopyIdentity();
        TankProjectile projectile = null;
        try
        {
            projectile = Instantiate(projectilePrefab, conditions.Position, conditions.Rotation);
            if (projectile.transform.parent != null) projectile.transform.SetParent(null, true);
            if (!projectile.Initialize(ShotCount + 1, this, ownerBody,
                ownerColliders, conditions, damage, out reason))
                throw new InvalidOperationException(reason);
            projectile.SetLaunchPrediction(predicted);
            recoveryEndsAt = Time.time + recovery;
            ChangeState(ChamberState.Recovering);
            ShotCount++;
            LastSuccessfulFireTime = Time.time;
            LastFireResult = "Fired #" + ShotCount;
            LastWeaponResult = LastFireResult + "; Recovering";
            LastProjectile = projectile;
            Debug.Log($"Tank fire: {LastFireResult}; speed={speed:F1} m/s; damage={damage:F1}", this);
            return true;
        }
        catch (Exception exception)
        {
            if (projectile != null)
            {
                var collider = projectile.GetComponent<Collider>();
                if (collider != null) collider.enabled = false;
                Destroy(projectile.gameObject);
            }
            LastFireResult = "Failed: " + exception.Message;
            LastWeaponResult = LastFireResult;
            Debug.Log("Tank fire: " + LastFireResult, this);
            return false;
        }
    }

    public void RecordProjectileEnd(int id, string reason, string targetName, Vector3 point)
    {
        EndedProjectileCount++;
        LastEndedShotId = id;
        LastProjectileEndReason = reason;
        LastHitName = targetName;
        LastHitPoint = point;
    }
    public void RecordImpactComparison(TankImpactComparison comparison) => LastImpactComparison = comparison;

    public void RecordDamageResult(int id, TankHealth target, float damage,
        bool applied, float before, float after, string result)
    {
        if (applied) DamageHitCount++;
        LastHitSummary = target != null
            ? $"#{id} {target.name}: Damage {damage:F1}, HP {before:F1} -> {after:F1}; {result}"
            : $"#{id}: no HP on collider; projectile removed";
    }
}
