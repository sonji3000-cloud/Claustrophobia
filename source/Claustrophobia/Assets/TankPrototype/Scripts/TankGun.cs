using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class TankGun : NetworkBehaviour, IAfterTick
{
    public enum ChamberState { EmptyOpen, EmptyClosed, LoadedOpen, Ready, Recovering }
    [SerializeField] private Transform muzzle;
    [SerializeField] private TankProjectile projectilePrefab;
    [SerializeField] private Rigidbody ownerBody;
    [Tooltip("Additional test blocker only. Ready chamber state is always required.")]
    public bool TestAllowFire = true;
    [Min(0.01f)] public float ProjectileSpeed = 20f;
    [Min(0.01f)] public float ShotDamage = 25f;
    [Tooltip("Recovery delay after firing. The empty chamber stays closed until manually opened.")]
    [Min(0f)] public float RecoveryDuration = 1.5f;

    [Networked, OnChangedRender(nameof(ApplyNetworkState))]
    private ChamberState NetworkState { get; set; }
    [Networked] private TickTimer RecoveryTimer { get; set; }
    [Networked, OnChangedRender(nameof(ApplyNetworkShot))]
    private int NetworkShots { get; set; }

    public ChamberState State { get; private set; } = ChamberState.EmptyOpen;
    public bool HasLiveRound => State == ChamberState.LoadedOpen || State == ChamberState.Ready;
    public bool CanInsert => isActiveAndEnabled && State == ChamberState.EmptyOpen;
    public bool UsesNetwork { get; private set; }

	public event Action<ChamberState> StateChanged;
	internal event Action<int, bool> LoadResolved;
    public float RecoverRemaining => State != ChamberState.Recovering ? 0f
        : IsSpawned ? RecoveryTimer.RemainingTime(Runner) ?? 0f
        : Mathf.Max(0f, recoveryEndsAt - Time.time);
    public string LastWeaponRequest { get; private set; } = "None";
    public string LastWeaponResult { get; private set; } = "No request";
    public int DamageHitCount { get; private set; }
    public string LastHitSummary { get; private set; } = "No impact";
    private float recoveryEndsAt;
    private TankCrewRegistry crewRegistry;
	private TankAI ai;
	private TankInputButton aiAction;
	private bool hasAIRequest;
    private readonly List<Collider> ownerColliders = new();
    private readonly Queue<(PlayerRef player, TankInputButton action, int load)> requests = new(4);

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

	private bool IsSpawned => Object != null && Object.IsValid;

	private void Awake()
	{
		UsesNetwork = TryGetComponent<NetworkObject>(out _);
		crewRegistry = GetComponent<TankCrewRegistry>();
		ai = GetComponent<TankAI>();
	}

	public override void Spawned()
	{
		ShotCount = NetworkShots;
		LastSuccessfulFireTime = -1f;
		ApplyNetworkState();
	}

	public override void Despawned(NetworkRunner runner, bool hasState)
	{
		requests.Clear();
		ClearAIRequest();
	}

	private void ApplyNetworkState() => ApplyState(NetworkState);

	private void ApplyNetworkShot()
	{
		if (ShotCount == NetworkShots) return;

		ShotCount = NetworkShots;
		LastSuccessfulFireTime = Time.time;
	}

	public bool CanLoad(TankCrew crew) => crew != null && (UsesNetwork ? crew.CanAim : crew.CanLoad);

	public bool RequestInsert() => Request(TankInputButton.Insert);
	internal bool RequestLoad(int load) => Request(TankInputButton.Insert, load);
	public bool RequestClose() => Request(TankInputButton.Close);
	public bool RequestOpenEmpty() => Request(TankInputButton.OpenEmpty);
	public bool RequestOpen() => Request(TankInputButton.Open);
	public bool RequestFire() => Request(TankInputButton.Fire);

	// Approval here means queued; State and ShotCount report the executed result after the tick.
	internal bool RequestAI(TankInputButton action)
	{
		if (!IsSpawned || !HasStateAuthority || !Runner.IsServer || crewRegistry != null
			|| ai == null || !ai.CanAct || hasAIRequest) return false;

		switch (action)
		{
			case TankInputButton.Insert:
			case TankInputButton.Close:
			case TankInputButton.OpenEmpty:
			case TankInputButton.Open:
			case TankInputButton.Fire:
				aiAction = action;
				hasAIRequest = true;
				return true;
			default:
				return false;
		}
	}

	internal void ClearAIRequest() => hasAIRequest = false;

	private bool Request(TankInputButton action, int load = 0)
	{
		if (!UsesNetwork) return Execute(action);
		if (!IsSpawned || !Runner.IsRunning || crewRegistry == null || !crewRegistry.CanAim(Runner.LocalPlayer))
			return false;

		RpcInvokeInfo info = RPC_Request(action, load);
		return info.LocalInvokeResult == RpcLocalInvokeResult.Invoked
			|| info.SendMessageResult == RpcSendMessageResult.Sent;
	}

	[Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
	private RpcInvokeInfo RPC_Request(TankInputButton action, int load, RpcInfo info = default)
	{
		requests.Enqueue((info.Source, action, load));
		return default;
	}

	[Rpc(RpcSources.StateAuthority, RpcTargets.All)]
	private void RPC_LoadResolved([RpcTarget] PlayerRef player, int load, NetworkBool accepted)
	{
		LoadResolved?.Invoke(load, accepted);
	}

	// TankAim has applied the tick's authoritative turret and gun rotations by this point.
	void IAfterTick.AfterTick()
	{
		if (!HasStateAuthority) return;
		if (State == ChamberState.Recovering && RecoveryTimer.Expired(Runner))
		{
			RecoveryTimer = TickTimer.None;
			ChangeState(ChamberState.EmptyClosed);
		}

		if (hasAIRequest)
		{
			TankInputButton action = aiAction;
			hasAIRequest = false;
			if (ai != null && ai.CanAct && crewRegistry == null) Execute(action);
		}

		while (requests.TryDequeue(out var request))
		{
			bool gunner = crewRegistry != null && crewRegistry.CanAim(request.player);
			ChamberState previous = State;
			bool accepted = gunner && Execute(request.action);
			if (!gunner)
				Debug.Log($"[WEAPON HOST] Tick={Runner.Tick}, Player={request.player}, Action={request.action}, Rejected: Gunner required", this);
			else if (accepted && request.action == TankInputButton.Fire)
				Debug.Log($"[WEAPON HOST] Tick={Runner.Tick}, Player={request.player}, Action=Fire, ShotId={ShotCount}, {previous} -> {State}", this);
			else
				Debug.Log($"[WEAPON HOST] Tick={Runner.Tick}, Player={request.player}, Action={request.action}, {previous} -> {State}, {LastWeaponResult}", this);

			if (request.action != TankInputButton.Insert || request.load == 0) continue;
			if (request.player == Runner.LocalPlayer) LoadResolved?.Invoke(request.load, accepted);
			else if (Runner.IsPlayerCommitted(request.player)) RPC_LoadResolved(request.player, request.load, accepted);
		}
	}

	private bool Execute(TankInputButton action)
	{
		switch (action)
		{
			case TankInputButton.Insert: return Insert();
			case TankInputButton.Close: return Close();
			case TankInputButton.OpenEmpty: return OpenEmpty();
			case TankInputButton.Open: return Open();
			case TankInputButton.Fire: return Fire();
			default: return Reject(action.ToString(), "Unknown action");
		}
	}

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
        if (State == ChamberState.Recovering) return "Recovering after firing";
        if (State == ChamberState.EmptyOpen || State == ChamberState.EmptyClosed) return "Not loaded - open chamber and insert a round";
        if (State == ChamberState.LoadedOpen) return "Chamber open - close it before firing";
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
        if (!UsesNetwork && State == ChamberState.Recovering && Time.time >= recoveryEndsAt)
            ChangeState(ChamberState.EmptyClosed);
    }

    private void ChangeState(ChamberState next)
    {
        ChamberState previous = State;
        if (UsesNetwork) NetworkState = next;
        Debug.Log($"Tank chamber: {previous} -> {next}", this);
        ApplyState(next);
    }

	private void ApplyState(ChamberState next)
	{
		if (State == next) return;
		State = next;
		StateChanged?.Invoke(next);
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

    private bool Insert()
    {
        TickRecovery();
        if (!isActiveAndEnabled) return Reject("Insert", "Weapon component inactive");
        if (State != ChamberState.EmptyOpen) return Reject("Insert", "Requires EmptyOpen; current " + State);
        return CompleteOperation("Insert", ChamberState.LoadedOpen);
    }

    private bool Close()
    {
        TickRecovery();
        if (!isActiveAndEnabled) return Reject("Close", "Weapon component inactive");
        if (State == ChamberState.EmptyOpen) return CompleteOperation("Close", ChamberState.EmptyClosed);
        if (State == ChamberState.LoadedOpen) return CompleteOperation("Close", ChamberState.Ready);
        return Reject("Close", "Requires open chamber; current " + State);
    }

    private bool OpenEmpty()
    {
        TickRecovery();
        if (!isActiveAndEnabled) return Reject("Open empty", "Weapon component inactive");
        if (State != ChamberState.EmptyClosed) return Reject("Open empty", "Requires EmptyClosed; current " + State);
        return CompleteOperation("Open empty", ChamberState.EmptyOpen);
    }

	private bool Open()
	{
		TickRecovery();
		if (!isActiveAndEnabled) return Reject("Open", "Weapon component inactive");
		if (State == ChamberState.Ready) return CompleteOperation("Open", ChamberState.LoadedOpen);
		return OpenEmpty();
	}

    // All requests are validated and consumed here, including rejected requests.
    private bool Fire()
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
        int shot = ShotCount + 1;
        TankProjectile projectile = null;
        try
        {
            projectile = UsesNetwork
                ? Runner.Spawn(projectilePrefab, conditions.Position, conditions.Rotation,
                    onBeforeSpawned: (_, obj) => obj.GetComponent<TankProjectile>().SetShotId(shot))
                : Instantiate(projectilePrefab, conditions.Position, conditions.Rotation);
            if (projectile == null) throw new InvalidOperationException("Projectile spawn failed");
            if (!UsesNetwork && projectile.transform.parent != null) projectile.transform.SetParent(null, true);
            if (!projectile.Initialize(shot, this, ownerBody,
                ownerColliders, conditions, damage, out reason))
                throw new InvalidOperationException(reason);
            projectile.SetLaunchPrediction(predicted);
        }
        catch (Exception exception)
        {
            if (projectile != null) projectile.Remove();
            LastFireResult = "Failed: " + exception.Message;
            LastWeaponResult = LastFireResult;
            Debug.Log("Tank fire: " + LastFireResult, this);
            return false;
        }

        if (UsesNetwork) RecoveryTimer = TickTimer.CreateFromSeconds(Runner, recovery);
        else recoveryEndsAt = Time.time + recovery;
        ShotCount = shot;
        if (UsesNetwork) NetworkShots = ShotCount;
        LastSuccessfulFireTime = Time.time;
        LastFireResult = "Fired #" + ShotCount;
        LastWeaponResult = LastFireResult + "; Recovering";
        LastProjectile = projectile;
        ChangeState(ChamberState.Recovering);
        Debug.Log($"Tank fire: {LastFireResult}; speed={speed:F1} m/s; damage={damage:F1}", this);
        return true;
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
