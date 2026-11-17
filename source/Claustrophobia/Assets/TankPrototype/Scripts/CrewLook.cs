using Fusion;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class CrewLook : NetworkBehaviour
{
	[SerializeField] private Camera view;
	[SerializeField] private Transform yawPivot;
	[SerializeField] private Transform pitchPivot;
	[SerializeField] private CrewInteract interaction;
	[SerializeField] private Renderer[] bodyRenderers = System.Array.Empty<Renderer>();

	[SerializeField, Min(0f)] private float sensitivity = 0.15f;
	[SerializeField, Range(-89f, 0f)] private float minPitch = -80f;
	[SerializeField, Range(0f, 89f)] private float maxPitch = 80f;

	[Networked] private Vector2 LookAngles { get; set; }
	[Networked] private TankCrew.Role LookRole { get; set; }

	private Quaternion yawBase;
	private Quaternion pitchBase;
	private ShadowCastingMode[] shadowModes;
	private float yaw;
	private float pitch;
	private bool bodyHidden;
	private bool ownsCursor;
	private CursorLockMode savedLock;
	private bool savedVisible;
	private SessionJoinInit session;
	private TankInterior inputInterior;
	private CrewLook localLook;
	private bool avatarSpawned;
	private bool avatarVisible;
	private float nextSend;
	private float lastSend;
	private Vector2 sentAngles;
	private TankCrew.Role sentRole;

	public bool HasCursor => ownsCursor;
	public Transform Pivot => pitchPivot;

	private void Awake()
	{
		yawBase = yawPivot != null ? yawPivot.localRotation : Quaternion.identity;
		pitchBase = pitchPivot != null ? pitchPivot.localRotation : Quaternion.identity;

		shadowModes = new ShadowCastingMode[bodyRenderers.Length];
		for (int i = 0; i < bodyRenderers.Length; i++)
			if (bodyRenderers[i] != null)
				shadowModes[i] = bodyRenderers[i].shadowCastingMode;
	}

	private void Update()
	{
		if (avatarSpawned) return;

		bool hasView = view != null && view.isActiveAndEnabled;
		SetBodyHidden(hasView);

		bool canLook = view != null && view.gameObject.activeInHierarchy
			&& (hasView || interaction != null && interaction.IsObserving);
		if (!canLook || !Application.isFocused || yawPivot == null || pitchPivot == null)
		{
			ReleaseCursor();
			return;
		}

		Keyboard keyboard = Keyboard.current;
		if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
		{
			if (interaction == null || interaction.Active == null
				&& (interaction.Carry == null || interaction.Carry.Held == null)) ReleaseCursor();
			return;
		}

		Mouse mouse = Mouse.current;
		if (mouse == null) return;

		if (!ownsCursor)
		{
			if (mouse.leftButton.wasPressedThisFrame) CaptureCursor();
			return;
		}

		if (Cursor.lockState != CursorLockMode.Locked)
		{
			ReleaseCursor();
			return;
		}

		ReadLook(mouse);
	}

	private void ReadLook(Mouse mouse)
	{
		Vector2 delta = mouse.delta.ReadValue() * sensitivity;
		yaw = Mathf.Repeat(yaw + delta.x, 360f);
		pitch = Mathf.Clamp(pitch - delta.y, minPitch, maxPitch);
		ApplyLook(yaw, pitch);
	}

	private void ApplyLook(float yaw, float pitch)
	{
		if (yawPivot != null) yawPivot.localRotation = yawBase * Quaternion.Euler(0f, yaw, 0f);
		if (pitchPivot != null) pitchPivot.localRotation = pitchBase * Quaternion.Euler(pitch, 0f, 0f);
	}

	public override void Spawned()
	{
		avatarSpawned = true;
		session = Runner.GetComponent<SessionJoinInit>();
		if (view != null)
		{
			view.enabled = false;
			view.tag = "Untagged";
			if (view.TryGetComponent(out AudioListener listener)) listener.enabled = false;
		}

		if (TryGetComponent(out CrewDebugView debugView)) debugView.enabled = false;
		if (TryGetComponent(out CrewSeats seats)) seats.enabled = false;
		if (interaction != null) interaction.enabled = false;
		SetAvatarVisible(false);
	}

	public override void Render()
	{
		if (!avatarSpawned) return;

		TankInterior interior = !HasInputAuthority && session != null ? session.Interior : null;
		TankCrew.Role role = interior != null ? session.GetCrewRole(Object.InputAuthority) : TankCrew.Role.None;
		Transform anchor = interior != null ? interior.Seats.GetAnchor(role) : null;
		bool visible = anchor != null && anchor.gameObject.activeInHierarchy;
		if (visible)
		{
			transform.SetPositionAndRotation(anchor.position, anchor.rotation);
			Vector2 angles = LookRole == role ? LookAngles : Vector2.zero;
			ApplyLook(angles.x, angles.y);
		}
		if (avatarVisible == visible) return;

		SetAvatarVisible(visible);
	}

	private void SetAvatarVisible(bool visible)
	{
		avatarVisible = visible;
		foreach (Renderer renderer in bodyRenderers)
			if (renderer != null) renderer.forceRenderingOff = !visible;
	}

	private void LateUpdate()
	{
		if (!avatarSpawned || !HasInputAuthority || session == null) return;
		TankInterior interior = session.Interior;
		if (inputInterior != interior)
		{
			inputInterior = interior;
			localLook = interior != null ? interior.Crew.GetComponent<CrewLook>() : null;
		}
		if (localLook == null || localLook == this) return;

		TankCrew.Role role = interior.Crew.CurrentRole;
		if (role == TankCrew.Role.None) return;
		float now = Time.unscaledTime;
		if (now < nextSend) return;

		Vector2 angles = new Vector2(Mathf.DeltaAngle(0f, localLook.yaw), localLook.pitch);
		// Resend unchanged angles periodically so a lost final sample can recover.
		if (role == sentRole && (angles - sentAngles).sqrMagnitude < .01f && now - lastSend < .25f) return;

		nextSend = now + .05f;
		lastSend = now;
		sentRole = role;
		sentAngles = angles;
		RPC_SetLook(role, angles);
	}

	[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority, Channel = RpcChannel.Unreliable,
		HostMode = RpcHostMode.SourceIsHostPlayer)]
	private void RPC_SetLook(TankCrew.Role role, Vector2 angles, RpcInfo info = default)
	{
		if (!avatarSpawned || !HasStateAuthority || session == null || info.Source != Object.InputAuthority
			|| role == TankCrew.Role.None || session.GetCrewRole(info.Source) != role) return;
		if (!(angles.x >= -180f && angles.x <= 180f && angles.y >= minPitch && angles.y <= maxPitch)) return;

		LookRole = role;
		LookAngles = angles;
	}

	public void CaptureCursor()
	{
		if (avatarSpawned || ownsCursor || !isActiveAndEnabled || !Application.isFocused) return;

		savedLock = Cursor.lockState;
		savedVisible = Cursor.visible;
		ownsCursor = true;

		Cursor.lockState = CursorLockMode.Locked;
		Cursor.visible = false;
	}

	public void ResetLook()
	{
		yaw = pitch = 0f;
		ApplyLook(0f, 0f);
	}

	public void ReleaseCursor()
	{
		if (!ownsCursor) return;

		Cursor.lockState = savedLock;
		Cursor.visible = savedVisible;
		ownsCursor = false;
	}

	private void SetBodyHidden(bool hidden)
	{
		if (hidden == bodyHidden || shadowModes == null) return;

		bodyHidden = hidden;
		for (int i = 0; i < bodyRenderers.Length; i++)
			if (bodyRenderers[i] != null)
				bodyRenderers[i].shadowCastingMode = hidden
					? ShadowCastingMode.ShadowsOnly : shadowModes[i];
	}

	private void OnApplicationFocus(bool focused)
	{
		if (!focused) ReleaseCursor();
	}

	private void OnDisable()
	{
		ReleaseCursor();
		SetBodyHidden(false);
	}
}
