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
    [Tooltip("Recovery delay after firing.")]
    [Min(0f)] public float RecoveryDuration = 1.5f;
	[SerializeField] private bool openAfterRecovery;

    [Networked, OnChangedRender(nameof(ApplyNetworkState))]
    private ChamberState NetworkState { get; set; }
    [Networked] private TickTimer RecoveryTimer { get; set; }
    [Networked, OnChangedRender(nameof(ApplyNetworkShot))]
    private int NetworkShots { get; set; }
	[Networked] private TankRound GunnerRound { get; set; }
	[Networked] private TankRound CommanderRound { get; set; }
	[Networked] private TankRound ChamberRound { get; set; }

    public ChamberState State { get; private set; } = ChamberState.EmptyOpen;
    public bool HasLiveRound => State == ChamberState.LoadedOpen || State == ChamberState.Ready;
    public bool CanInsert => isActiveAndEnabled && State == ChamberState.EmptyOpen;
	public bool OpensAfterRecovery => openAfterRecovery;
    public bool UsesNetwork { get; private set; }

	public event Action<ChamberState> StateChanged;
    public float RecoverRemaining => State != ChamberState.Recovering ? 0f
        : IsSpawned ? RecoveryTimer.RemainingTime(Runner) ?? 0f
        : Mathf.Max(0f, recoveryEndsAt - Time.time);
    public string LastWeaponRequest { get; private set; } = "None";
    public string LastWeaponResult { get; private set; } = "No request";
    public int DamageHitCount { get; private set; }
    public string LastHitSummary { get; private set; } = "No impact";
    private float recoveryEndsAt;
    private TankCrewRegistry crewRegistry;
	private TankBreech breech;
	private CrewInteract interaction;
	private TankAI ai;
	private TankInputButton aiAction;
	private bool hasAIRequest;
	private bool aiTargetRequest;
    private readonly List<Collider> ownerColliders = new();
	private readonly Queue<(PlayerRef player, TankCrew.Role role, TankInputButton action, int source, NetworkId round)> requests = new(4);

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

	public bool CanLoad(TankCrew crew) => crew != null && crew.CanLoad;

	public bool RequestInsert() => UsesNetwork && breech != null
		? RequestLoad(GetLocalRound()) : Request(TankInputButton.Insert);
	internal bool RequestLoad(TankRound round) => round != null
		&& Request(TankInputButton.Insert, round: round.Object.Id);
	public bool RequestClose() => Request(TankInputButton.Close);
	public bool RequestOpenEmpty() => Request(TankInputButton.OpenEmpty);
	public bool RequestOpen() => Request(TankInputButton.Open);
	public bool RequestFire() => Request(TankInputButton.Fire);

	// Approval here means queued; State and ShotCount report the executed result after the tick.
	internal bool RequestAI(TankInputButton action, bool targetRequest = false)
	{
		if (!IsSpawned || !HasStateAuthority || !Runner.IsServer || crewRegistry != null
			|| ai == null || !ai.CanAct || hasAIRequest || targetRequest && !ai.HasLiveTarget) return false;

		switch (action)
		{
			case TankInputButton.Insert:
			case TankInputButton.Close:
			case TankInputButton.OpenEmpty:
			case TankInputButton.Open:
			case TankInputButton.Fire:
				aiAction = action;
				aiTargetRequest = targetRequest;
				hasAIRequest = true;
				return true;
			default:
				return false;
		}
	}

	internal void ClearAIRequest()
	{
		hasAIRequest = false;
		aiTargetRequest = false;
	}

	internal void BindBreech(TankBreech next, CrewInteract user)
	{
		breech = next;
		interaction = user;
	}

	internal void UnbindBreech(TankBreech previous)
	{
		if (breech != previous) return;
		breech = null;
		interaction = null;
	}

	internal TankRound GetLocalRound()
	{
		if (!IsSpawned || crewRegistry == null) return null;
		PlayerRef player = Runner.LocalPlayer;
		TankRound round = GetRound(crewRegistry.GetRole(player));
		return round != null && round.Object.InputAuthority == player ? round : null;
	}

	internal bool RequestTake(CrewDevice source)
	{
		if (interaction == null || source == null || source.AmmoRack == null) return false;
		int index = Array.IndexOf(interaction.Devices, source);
		return index >= 0 && Request(TankInputButton.TakeRound, source: index);
	}

	internal void RequestReturn()
	{
		TankRound round = GetLocalRound();
		if (round != null) Request(TankInputButton.ReturnRound, round: round.Object.Id);
	}

	internal void SampleRoundPose(ref TankCrewNetworkInput input)
	{
		input.RoundRole = TankCrew.Role.None;
		if (interaction != null && interaction.Carry != null) interaction.Carry.SamplePose(ref input);
	}

	private bool Request(TankInputButton action, int source = -1, NetworkId round = default)
	{
		if (!UsesNetwork) return Execute(action);
		if (!IsSpawned || !Runner.IsRunning || crewRegistry == null) return false;
		TankCrew.Role role = crewRegistry.GetRole(Runner.LocalPlayer);
		if (!CanRequest(role, action)) return false;

		RpcInvokeInfo info = RPC_Request(role, action, source, round);
		return info.LocalInvokeResult == RpcLocalInvokeResult.Invoked
			|| info.SendMessageResult == RpcSendMessageResult.Sent;
	}

	[Rpc(RpcSources.All, RpcTargets.StateAuthority, HostMode = RpcHostMode.SourceIsHostPlayer)]
	private RpcInvokeInfo RPC_Request(TankCrew.Role role, TankInputButton action, int source, NetworkId round, RpcInfo info = default)
	{
		requests.Enqueue((info.Source, role, action, source, round));
		return default;
	}

	private static bool CanRequest(TankCrew.Role role, TankInputButton action) => action == TankInputButton.Fire
		? role == TankCrew.Role.Gunner : role == TankCrew.Role.Gunner || role == TankCrew.Role.Commander;

	// TankAim has applied the tick's authoritative turret and gun rotations by this point.
	void IAfterTick.AfterTick()
	{
		if (!HasStateAuthority) return;
		if (ai != null && !ai.CanAct)
		{
			ClearAIRequest();
			requests.Clear();
			return;
		}
		if (State == ChamberState.Recovering && RecoveryTimer.Expired(Runner))
		{
			RecoveryTimer = TickTimer.None;
			FinishRecovery();
		}

		if (hasAIRequest)
		{
			TankInputButton action = aiAction;
			bool targetRequest = aiTargetRequest;
			ClearAIRequest();
			if (ai != null && ai.CanAct && crewRegistry == null && (!targetRequest || ai.HasLiveTarget)) Execute(action);
		}

		while (requests.TryDequeue(out var request))
		{
			TankCrew.Role role = crewRegistry != null ? crewRegistry.GetRole(request.player) : TankCrew.Role.None;
			bool allowed = role == request.role && CanRequest(role, request.action);
			ChamberState previous = State;
			bool accepted = allowed && ExecuteCrew(request.player, role, request.action, request.source, request.round);
			if (!allowed)
				Debug.Log($"[WEAPON HOST] Tick={Runner.Tick}, Player={request.player}, Action={request.action}, Rejected: Role {role}", this);
			else if (accepted && request.action == TankInputButton.Fire)
				Debug.Log($"[WEAPON HOST] Tick={Runner.Tick}, Player={request.player}, Action=Fire, ShotId={ShotCount}, {previous} -> {State}", this);
			else
				Debug.Log($"[WEAPON HOST] Tick={Runner.Tick}, Player={request.player}, Action={request.action}, {previous} -> {State}, {LastWeaponResult}", this);
		}
	}

	public override void FixedUpdateNetwork()
	{
		if (!HasStateAuthority || crewRegistry == null) return;
		UpdateHeldRound(TankCrew.Role.Gunner, crewRegistry.GunnerPlayer);
		UpdateHeldRound(TankCrew.Role.Commander, crewRegistry.CommanderPlayer);
	}

	private bool ExecuteCrew(PlayerRef player, TankCrew.Role role, TankInputButton action, int source, NetworkId id)
	{
		switch (action)
		{
			case TankInputButton.TakeRound: return TakeRound(player, role, source);
			case TankInputButton.ReturnRound: return ReturnRound(player, role, id);
			case TankInputButton.Insert when breech != null || id.IsValid: return LoadRound(player, role, id);
			default: return Execute(action);
		}
	}

	private TankRound GetRound(TankCrew.Role role) => role switch
	{
		TankCrew.Role.Gunner => GunnerRound,
		TankCrew.Role.Commander => CommanderRound,
		_ => null
	};

	private void SetRound(TankCrew.Role role, TankRound round)
	{
		if (role == TankCrew.Role.Gunner) GunnerRound = round;
		else if (role == TankCrew.Role.Commander) CommanderRound = round;
	}

	private bool TakeRound(PlayerRef player, TankCrew.Role role, int index)
	{
		if (interaction == null || (uint)index >= (uint)interaction.Devices.Length || GetRound(role) != null)
			return Reject("Take round", "No available rack or hands already occupied");

		CrewDevice source = interaction.Devices[index];
		TankRound prefab = source != null && source.AmmoRack != null && source.AmmoRack.isActiveAndEnabled
			? source.AmmoRack.RoundPrefab : null;
		if (prefab == null || !source.CanUse(role) || !TryGetRoundPose(player, role, out var pose))
			return Reject("Take round", "Rack or carry pose unavailable");

		TankRound round = Runner.Spawn(prefab, pose.RoundPosition, pose.RoundRotation, player);
		if (round == null) return Reject("Take round", "Spawn failed");
		SetRound(role, round);
		LastWeaponResult = "Round issued";
		return true;
	}

	private bool ReturnRound(PlayerRef player, TankCrew.Role role, NetworkId id)
	{
		TankRound round = GetRound(role);
		if (round == null || round.Object.InputAuthority != player || round.Object.Id != id)
			return Reject("Return round", "No matching carried round");
		RemoveHeldRound(role, round);
		LastWeaponResult = "Round returned";
		return true;
	}

	private bool LoadRound(PlayerRef player, TankCrew.Role role, NetworkId id)
	{
		TankRound round = GetRound(role);
		Transform point = breech != null ? breech.LoadPoint : null;
		if (point == null || !point.gameObject.activeInHierarchy || round == null
			|| round.Object.InputAuthority != player || round.Object.Id != id || !CanInsert)
			return Reject("Insert", "Requires an open empty chamber and the player's carried round");

		SetRound(role, null);
		ChamberRound = round;
		round.Seat(point.position, point.rotation);
		return Insert();
	}

	private void UpdateHeldRound(TankCrew.Role role, PlayerRef player)
	{
		TankRound round = GetRound(role);
		if (round == null) return;
		if (interaction == null || player.IsNone || round.Object.InputAuthority != player)
		{
			RemoveHeldRound(role, round);
			return;
		}

		if (TryGetRoundPose(player, role, out var pose))
			round.transform.SetPositionAndRotation(pose.RoundPosition, pose.RoundRotation);
	}

	private void RemoveHeldRound(TankCrew.Role role, TankRound round)
	{
		SetRound(role, null);
		Runner.Despawn(round.Object);
	}

	private bool TryGetRoundPose(PlayerRef player, TankCrew.Role role, out TankCrewNetworkInput pose)
	{
		pose = default;
		Transform anchor = interaction != null && interaction.Carry != null ? interaction.Carry.GetAnchor(role) : null;
		if (anchor == null || !Runner.TryGetInputForPlayer(player, out pose) || pose.RoundRole != role) return false;

		// RoundHold sits beside the seat and rotates with the player's local view.
		Quaternion rotation = pose.RoundRotation;
		return (pose.RoundPosition - anchor.position).sqrMagnitude <= 4f
			&& Mathf.Abs(Quaternion.Dot(rotation, rotation) - 1f) < .01f;
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
		if (ai != null && !ai.CanAct) return "AI inactive or destroyed";
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
            FinishRecovery();
    }

	private void FinishRecovery()
	{
		ChangeState(openAfterRecovery ? ChamberState.EmptyOpen : ChamberState.EmptyClosed);
	}

    private void ChangeState(ChamberState next)
    {
        ChamberState previous = State;
		if (UsesNetwork)
		{
			NetworkState = next;
			TankRound round = ChamberRound;
			if (round != null)
			{
				if (next == ChamberState.LoadedOpen || next == ChamberState.Ready)
					round.SetVisible(next == ChamberState.LoadedOpen && (breech == null || !breech.HasModel));
				else
				{
					ChamberRound = null;
					Runner.Despawn(round.Object);
				}
			}
		}
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
