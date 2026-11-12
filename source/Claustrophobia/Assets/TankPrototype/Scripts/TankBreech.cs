using UnityEngine;

/// <summary>Animates the breech and loads carried rounds through the gun's chamber state.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CrewDevice))]
public sealed class TankBreech : MonoBehaviour
{
	[SerializeField] private CrewDevice device;
	[SerializeField] private TankGun gun;
	[SerializeField] private Transform loadPoint;
	[SerializeField] private Transform block;
	[SerializeField] private Transform openPose;
	[SerializeField] private Transform closedPose;
	[SerializeField] private TankBreechModel model;

	[SerializeField, Min(0f)] private float doorTime = .35f;
	[SerializeField, Min(0f)] private float recoilDistance = .2f;

	private const float RecoilPeak = 1f / 7f; // Peak at frame 5 of the 35-frame Tiger recoil clip.

	private TankRound loaded;
	private TankGun loadedGun;
	private TankGun subscribedGun;
	private TankRound pendingRound;
	private int loadRequest;

	private TankGunRecoil recoil;
	private Vector3 restPos;
	private Vector3 rearDir;
	private float travel;
	private float door;

	public TankGun Gun => gun;
	internal bool IsMoving => model != null && model.IsMoving;

	private void Awake()
	{
		restPos = transform.localPosition;
		if (loadPoint == null) return;

		Vector3 rear = -loadPoint.forward;
		rearDir = transform.parent != null ? transform.parent.InverseTransformVector(rear) : rear;
	}

	private void LateUpdate()
	{
		if (subscribedGun == null || model != null) return;

		bool hasRecoil = recoil != null && recoil.isActiveAndEnabled && recoil.Duration > 0f;
		float phase = hasRecoil ? recoil.RecoilPhase : 1f;
		UpdateRecoil(phase);

		TankGun.ChamberState state = subscribedGun.State;
		bool autoOpen = hasRecoil && subscribedGun.OpensAfterRecovery
			&& state == TankGun.ChamberState.Recovering;
		bool movingBack = phase < 1f;
		SetDoor(!movingBack && (IsOpen(state) || autoOpen), movingBack && door > 0f);
	}

	private void UpdateRecoil(float phase)
	{
		float next = 0f;
		if (phase < 1f)
		{
			float amount = phase < RecoilPeak
				? phase / RecoilPeak
				: (1f - phase) / (1f - RecoilPeak);
			next = Mathf.SmoothStep(0f, recoilDistance, amount);
		}

		if (next == travel) return;
		travel = next;
		transform.localPosition = restPos + rearDir * travel;
	}

	private void SetDoor(bool open, bool snap = false)
	{
		if (block == null) return;

		float target = open ? 1f : 0f;
		if (!snap && door == target) return;

		if (openPose == null || closedPose == null)
		{
			Transform pose = open ? openPose : closedPose;
			if (pose != null) block.SetPositionAndRotation(pose.position, pose.rotation);
			door = target;
			return;
		}

		door = snap || doorTime <= 0f ? target
			: Mathf.MoveTowards(door, target, Time.deltaTime / doorTime);
		float t = Mathf.SmoothStep(0f, 1f, door);
		block.SetPositionAndRotation(
			Vector3.Lerp(closedPose.position, openPose.position, t),
			Quaternion.Slerp(closedPose.rotation, openPose.rotation, t));
	}

	private static bool IsOpen(TankGun.ChamberState state)
	{
		return state == TankGun.ChamberState.EmptyOpen || state == TankGun.ChamberState.LoadedOpen;
	}

	/// <summary>A loaded round stays with its gun; this API does not transfer ammunition.</summary>
	public bool Bind(TankGun next)
	{
		if (!CanBind(next)) return false;
		if (gun == next)
		{
			if (isActiveAndEnabled) Attach();
			return true;
		}

		Detach();
		gun = next;
		if (isActiveAndEnabled) Attach();
		return true;
	}

	public bool CanBind(TankGun next) => next != null && (loaded == null || loadedGun == null || loadedGun == next);

	public void Unbind()
	{
		Detach();
		gun = null;
	}

	public bool CanInsert(CrewInteract user)
	{
		return isActiveAndEnabled && device != null && gun != null && gun.CanInsert
			&& pendingRound == null && !IsMoving
			&& loadPoint != null && loadPoint.gameObject.activeInHierarchy
			&& user != null && user.CanSelect && user.Active == null && user.Crew != null
			&& gun.CanLoad(user.Crew) && device.CanUse(user.Crew) && device.User == null
			&& user.Carry != null && user.Carry.isActiveAndEnabled && user.Carry.Held != null
			&& user.Carry.Held.isActiveAndEnabled && user.Carry.Held.Holder == user.Carry;
	}

	internal bool TryUse(CrewInteract user)
	{
		if (!isActiveAndEnabled || gun == null || !gun.isActiveAndEnabled || device == null
			|| user == null || !user.CanSelect || user.Selected != device || user.Active != null
			|| !gun.CanLoad(user.Crew) || !device.CanUse(user.Crew) || IsMoving) return false;

		bool holding = user.Carry != null && user.Carry.Held != null;
		if (holding && gun.State != TankGun.ChamberState.EmptyClosed) return TryInsert(user);

		if (gun.State == TankGun.ChamberState.EmptyClosed || gun.State == TankGun.ChamberState.Ready)
			return gun.RequestOpen();

		return gun.RequestClose();
	}

	private bool TryInsert(CrewInteract user)
	{
		if (!CanInsert(user) || user.Selected != device) return false;
		if (gun.UsesNetwork)
		{
			if (!user.Carry.BeginLoad()) return false;

			pendingRound = user.Carry.Held;
			if (++loadRequest == 0) loadRequest++;
			if (!gun.RequestLoad(loadRequest))
			{
				pendingRound = null;
				user.Carry.EndLoad();
				return false;
			}

			user.ClearSelection();
			return true;
		}

		if (!gun.RequestInsert()) return false;

		CommitLoad(user.Carry);
		user.ClearSelection();
		return true;
	}

	private void OnLoadResolved(int request, bool accepted)
	{
		if (request != loadRequest || pendingRound == null) return;

		TankRound round = pendingRound;
		pendingRound = null;
		CrewCarry carry = round.Holder;
		if (accepted && gun != null && carry != null && carry.Held == round)
			CommitLoad(carry);
		else if (carry != null) carry.EndLoad();
	}

	private void CommitLoad(CrewCarry carry)
	{
		loaded = carry.CommitLoad(loadPoint);
		loadedGun = gun;
		if (model != null || gun.State == TankGun.ChamberState.Ready) loaded.gameObject.SetActive(false);
	}

	public string GetPrompt(CrewInteract user)
	{
		if (gun == null || !gun.isActiveAndEnabled) return "Chamber unavailable";
		if (pendingRound != null) return "Loading round | Waiting for host";
		if (gun.State == TankGun.ChamberState.Recovering) return "FIRED | Recovering";
		if (IsMoving) return "Chamber mechanism moving";
		if (CanInsert(user)) return "OPEN | EMPTY | Space - Insert round | Esc - Return to rack";
		bool holding = user != null && user.Carry != null && user.Carry.Held != null;
		switch (gun.State)
		{
			case TankGun.ChamberState.EmptyOpen: return "OPEN | EMPTY | Space - Close | Pick up a round to load";
			case TankGun.ChamberState.EmptyClosed: return "CLOSED | EMPTY | Space - Open";
			case TankGun.ChamberState.LoadedOpen: return holding
				? "OPEN | LOADED | Esc - Return extra round" : "OPEN | LOADED | Space - Close";
			case TankGun.ChamberState.Ready: return holding
				? "CLOSED | LOADED | Esc - Return extra round" : "CLOSED | LOADED | Space - Open";
			default: return "FIRED | Recovering";
		}
	}

	private void OnStateChanged(TankGun.ChamberState state)
	{
		if (gun == null) return;

		if (model != null) model.SetState(state);
		else if (loaded != null) loaded.gameObject.SetActive(IsOpen(state) && gun.HasLiveRound);
		if (loaded == null || gun.HasLiveRound) return;

		loaded.Consume();
		loaded = null;
		loadedGun = null;
	}

	private void OnEnable() => Attach();

	private void Attach()
	{
		if (gun == null || subscribedGun == gun) return;

		subscribedGun = gun;
		recoil = gun.GetComponent<TankGunRecoil>();
		subscribedGun.StateChanged += OnStateChanged;
		subscribedGun.LoadResolved += OnLoadResolved;
		if (model != null)
			model.Bind(gun);
		else
		{
			SetDoor(IsOpen(gun.State), true);
			OnStateChanged(gun.State);
		}
	}

	private void OnDisable() => Detach();

	private void Detach()
	{
		if (subscribedGun != null)
		{
			subscribedGun.StateChanged -= OnStateChanged;
			subscribedGun.LoadResolved -= OnLoadResolved;
			if (model != null) model.Bind(null);
		}
		subscribedGun = null;
		recoil = null;
		UpdateRecoil(1f);
		if (pendingRound != null && pendingRound.Holder != null) pendingRound.Holder.EndLoad();
		pendingRound = null;
	}
}
