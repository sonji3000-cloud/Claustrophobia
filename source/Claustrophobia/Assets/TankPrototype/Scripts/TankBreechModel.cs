using UnityEngine;

[DisallowMultipleComponent]
public sealed class TankBreechModel : MonoBehaviour
{
	[SerializeField] private Transform pitch;
	[SerializeField] private Transform recoil;
	[SerializeField] private Transform block;
	[SerializeField] private Transform lever;
	[SerializeField] private Transform extractors;
	[SerializeField] private Transform round;
	[SerializeField] private Transform caseSeat;
	[SerializeField] private Rigidbody spentCase;

	[SerializeField, Min(0.01f)] private float travelTime = 0.35f;
	[SerializeField, Min(0.01f)] private float loadTime = 0.48f;
	[SerializeField] private float blockDrop = 0.433f;
	[SerializeField] private float leverTurn = -66f;
	[SerializeField] private float loadStroke = 1.5f;
	[SerializeField] private float recoilStroke = 0.4f;
	[SerializeField] private float extractStroke = 1.23f;

	private TankGun gun;
	private TankAim aim;
	private TankGunRecoil gunRecoil;
	private TankGun.ChamberState state;
	private Quaternion pitchRot;
	private Quaternion leverRot;
	private Vector3 recoilPos;
	private Vector3 blockPos;
	private Vector3 extractorPos;
	private float openness;
	private float loadPhase;
	private bool hasCase;
	private const float ExtractTime = 0.20f;
	private bool initialized;
	private bool released;
	private float ejectedAt = -1f;
	private Vector3 ejectPos;
	private Quaternion ejectRot;

	internal bool IsMoving => gun != null && (openness != GetTarget() || loadPhase < 1f || hasCase
		|| ejectedAt >= 0f && Time.time - ejectedAt < ExtractTime);

	private void Awake() => Init();

	private void Init()
	{
		if (initialized) return;
		initialized = true;
		pitchRot = pitch.localRotation;
		leverRot = lever.localRotation;
		recoilPos = recoil.localPosition;
		blockPos = block.localPosition;
		extractorPos = extractors.localPosition;
		round.gameObject.SetActive(false);
		spentCase.gameObject.SetActive(false);
	}

	internal void Bind(TankGun next)
	{
		Init();
		gun = next;
		aim = next != null ? next.GetComponent<TankAim>() : null;
		gunRecoil = next != null ? next.GetComponent<TankGunRecoil>() : null;
		state = next != null ? next.State : TankGun.ChamberState.EmptyOpen;
		openness = GetTarget();
		loadPhase = 1f;
		bool autoOpening = next != null && next.OpensAfterRecovery && state == TankGun.ChamberState.Recovering;
		if (autoOpening) openness = GetRecoveryOpen();
		hasCase = autoOpening && openness < 1f;
		ResetCase();
		if (next == null)
		{
			round.gameObject.SetActive(false);
			pitch.localRotation = pitchRot;
			recoil.localPosition = recoilPos;
			extractors.localPosition = extractorPos;
			return;
		}

		ApplyMechanism();
		ApplyRound();
	}

	internal void SetState(TankGun.ChamberState next)
	{
		if (state == next) return;

		if (next == TankGun.ChamberState.LoadedOpen && state == TankGun.ChamberState.EmptyOpen)
			loadPhase = 0f;
		else if (next != TankGun.ChamberState.LoadedOpen)
			loadPhase = 1f;

		if (next == TankGun.ChamberState.Recovering && gun.OpensAfterRecovery)
		{
			ResetCase();
			hasCase = true;
		}

		state = next;
	}

	private float GetTarget() => state == TankGun.ChamberState.EmptyOpen ||
		state == TankGun.ChamberState.LoadedOpen ? 1f : 0f;

	private void LateUpdate()
	{
		if (gun == null) return;

		float target = GetTarget();
		if (hasCase && gunRecoil != null && gunRecoil.IsRecoiling)
			openness = 0f;
		else if (gun.OpensAfterRecovery && state == TankGun.ChamberState.Recovering)
			openness = GetRecoveryOpen();
		else
			openness = Mathf.MoveTowards(openness, target, Time.deltaTime / travelTime);

		if (openness >= 1f)
			loadPhase = Mathf.MoveTowards(loadPhase, 1f, Time.deltaTime / loadTime);

		ApplyMechanism();
		ApplyRound();
		ApplyCase();
	}

	private float GetRecoveryOpen()
	{
		float elapsed = gun.RecoveryDuration - gun.RecoverRemaining;
		float recoilTime = gunRecoil != null ? gunRecoil.Duration : 0f;
		return Mathf.Clamp01((elapsed - recoilTime) / travelTime);
	}

	private void ApplyMechanism()
	{
		float phase = gunRecoil != null ? gunRecoil.RecoilPhase : 1f;
		const float peak = 1f / 7f;
		float kick = phase <= peak ? Mathf.SmoothStep(0f, 1f, phase / peak)
			: Mathf.SmoothStep(1f, 0f, (phase - peak) / (1f - peak));
		float elevation = aim != null ? aim.CurrentPitch : 0f;
		pitch.localRotation = pitchRot * Quaternion.AngleAxis(-elevation, Vector3.right);
		recoil.localPosition = recoilPos - Vector3.forward * (recoilStroke * kick);

		float travel = Mathf.SmoothStep(0f, 1f, openness);
		block.localPosition = blockPos - Vector3.up * (blockDrop * travel);
		lever.localRotation = leverRot * Quaternion.AngleAxis(leverTurn * travel, Vector3.right);
	}

	private void ApplyRound()
	{
		bool visible = (state == TankGun.ChamberState.LoadedOpen || state == TankGun.ChamberState.Ready)
			&& openness > 0.1f && (loadPhase >= 1f || openness >= 1f);
		if (round.gameObject.activeSelf != visible) round.gameObject.SetActive(visible);
		if (!visible) return;

		float stroke = Mathf.SmoothStep(loadStroke, 0f, loadPhase);
		round.localPosition = Vector3.back * stroke;
	}

	private void ApplyCase()
	{
		if (hasCase && openness >= 1f)
		{
			hasCase = false;
			ejectedAt = Time.time;
			ejectPos = caseSeat.position;
			ejectRot = caseSeat.rotation;
		}

		float elapsed = Time.time - ejectedAt;
		bool ejecting = ejectedAt >= 0f && elapsed < 2.5f;
		bool visible = hasCase || ejecting;
		if (spentCase.gameObject.activeSelf != visible) spentCase.gameObject.SetActive(visible);

		float pull = ejecting ? Mathf.Sin(Mathf.Clamp01(elapsed / 0.24f) * Mathf.PI) * 0.075f : 0f;
		extractors.localPosition = extractorPos - Vector3.forward * pull;
		if (hasCase)
			spentCase.transform.SetPositionAndRotation(caseSeat.position, caseSeat.rotation);

		if (!ejecting || released) return;

		Vector3 back = ejectRot * Vector3.back;
		float slide = Mathf.Clamp01(elapsed / ExtractTime);
		spentCase.transform.SetPositionAndRotation(ejectPos + back * (extractStroke * slide), ejectRot);
		if (slide < 1f) return;

		released = true;
		spentCase.isKinematic = false;
		spentCase.detectCollisions = true;
		spentCase.linearVelocity = back * 2.5f + ejectRot * Vector3.right * 0.7f + Vector3.up * 0.45f;
		spentCase.angularVelocity = ejectRot * new Vector3(3f, -1.5f, 1f);
	}

	private void ResetCase()
	{
		ejectedAt = -1f;
		released = false;
		spentCase.isKinematic = true;
		spentCase.detectCollisions = false;
		spentCase.gameObject.SetActive(false);
	}
}