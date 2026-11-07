using System;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Connects the authored interior after the designated session and tank are ready.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-300)]
public sealed class TankSceneLink : MonoBehaviour
{
	public enum Stage { Idle, Waiting, Prepared, Binding, Ready, Stopped, Failed }

	[SerializeField] private TankInterior interior;
	[SerializeField] private SessionJoinInit session;
	[SerializeField] private TankControl control;
	[SerializeField] private TankInput input;
	[SerializeField] private TankCrewCameras cameras;
	[SerializeField] private TankKeyboardDebug tankHud;
	[SerializeField] private TankGun gun;
	[SerializeField] private TankAim aim;
	[SerializeField] private Camera driverFeed;
	[SerializeField] private Camera gunnerFeed;
	[Tooltip("Disable only to defer the existing joystick aim omission. Control still binds.")]
	[SerializeField] private bool bindJoystickAim = true;

	private NetworkObject tankObject;
	private Scene worldScene;
	private string worldPath;
	private TankInterior boundInterior;
	private bool joystickAimBound;
	private bool started;
	private bool attached;
	private uint version;
	private Stage stage;

	public Stage CurrentStage => stage;
	public TankInterior Interior => interior;
	public SessionJoinInit Session => session;
	public bool IsReady => stage == Stage.Ready && boundInterior != null && CanBindSession() && control.CanInput;
	public bool JoystickAimDeferred => !bindJoystickAim;
	public string LastError { get; private set; }
	public event Action<Stage> StageChanged;

	private void Awake()
	{
		if (control == null) return;
		tankObject = control.GetComponent<NetworkObject>();
		worldScene = control.gameObject.scene;
		worldPath = worldScene.path;
	}

	private void OnEnable()
	{
		Attach();
		SetStage(Stage.Waiting);
		if (!started) return;
		BlockWorld();
		TryConnect();
	}

	private void Start()
	{
		started = true;
		BlockWorld();
		if (!ValidateRefs(out string reason)) { Fail(reason); return; }
		SetStage(Stage.Waiting);
		TryConnect();
	}

	private void Update()
	{
		if (stage == Stage.Waiting) TryConnect();
		else if (stage == Stage.Ready && !CanBindSession()) OnSessionStopped();
	}

	public bool ValidateRefs(out string reason) => ValidateInterior(interior, out reason);

	private bool ValidateInterior(TankInterior next, out string reason)
	{
		if (next == null || session == null || control == null || input == null || cameras == null
			|| tankHud == null || gun == null || aim == null || driverFeed == null || gunnerFeed == null)
		{
			reason = "TankSceneLink requires an interior and all explicit external endpoints.";
			return false;
		}
		if (input.gameObject != control.gameObject || cameras.gameObject != control.gameObject
			|| tankHud.gameObject != control.gameObject || gun.gameObject != control.gameObject
			|| aim.gameObject != control.gameObject || session.tankControl != control)
		{
			reason = "TankSceneLink endpoints must belong to the designated tank.";
			return false;
		}
		if (!next.ValidateRefs(out reason)) return false;
		if (!control.isActiveAndEnabled || !input.isActiveAndEnabled || !next.Mode.isActiveAndEnabled)
		{
			reason = "The control, input and mode must be active before completing the scene link.";
			return false;
		}
		if (driverFeed == gunnerFeed || !driverFeed.transform.IsChildOf(control.transform)
			|| !gunnerFeed.transform.IsChildOf(control.transform) || !next.DriverScreen.CanBind(driverFeed)
			|| !next.Sight.CanBind(gunnerFeed) || !next.Breech.CanBind(gun))
		{
			reason = "A monitor feed or chamber endpoint cannot bind to the designated tank.";
			return false;
		}
		if (next.Joystick.Aim != null && next.Joystick.Aim != aim)
		{
			reason = "The joystick references another tank's aim component.";
			return false;
		}
		reason = null;
		return true;
	}

	private bool CanBindSession()
	{
		return session != null && session.IsSceneReady && tankObject != null && tankObject.IsValid
			&& tankObject.Runner == session.Runner && control != null
			&& control.gameObject.scene == worldScene && worldScene.isLoaded
			&& worldScene.buildIndex == session.TargetSceneIndex && worldPath == session.TargetScenePath;
	}

	public bool PrepareInterior()
	{
		if (!Application.isPlaying || !isActiveAndEnabled || !ValidateRefs(out _) || !interior.Prepare()) return false;
		if (stage == Stage.Idle || stage == Stage.Prepared) SetStage(Stage.Prepared);
		return true;
	}

	/// <summary>Invalid targets preserve an existing binding. Partial failure stays blocked.</summary>
	public bool Bind(TankInterior next)
	{
		if (!Application.isPlaying || !isActiveAndEnabled || !started || !CanBindSession()) return false;
		if (!ValidateInterior(next, out string reason)) { LastError = reason; return false; }
		if (boundInterior == next && stage == Stage.Ready) return true;

		Unbind();
		if (!CanBindSession()) return false;
		uint request = ++version;
		boundInterior = next;
		joystickAimBound = bindJoystickAim;
		SetStage(Stage.Binding);
		if (request != version) return false;
		try
		{
			if (!next.Prepare() || !Connect(next) || request != version || !CanBindSession())
				return Abort(next, request, "Interior connection was interrupted or rejected.");
			if (SceneManager.GetActiveScene() != worldScene && !SceneManager.SetActiveScene(worldScene))
				return Abort(next, request, "The external tank scene could not be made active.");
			if (!next.Mode.ApplyMode() || request != version || !CanBindSession())
				return Abort(next, request, "The initial mode could not be applied.");
		}
		catch (Exception e) { return Abort(next, request, e.Message); }

		control.SetInputEnabled(true);
		LastError = null;
		SetStage(Stage.Ready);
		return IsReady;
	}

	private bool Connect(TankInterior next)
	{
		if (!control.Bind(next.Crew, next.DriveDevice, next.AimDevice, next.Sight)
			|| !input.Bind(next.Seats, next.Interaction, next.Mode) || !next.Mode.Bind(input, cameras, tankHud)
			|| !next.DriverScreen.Bind(driverFeed) || !next.Sight.Bind(gunnerFeed) || !next.Breech.Bind(gun)) return false;
		for (int i = 0; i < next.Levers.Length; i++)
			if (!next.Levers[i].Bind(control)) return false;
		bool joystick = joystickAimBound ? next.Joystick.Bind(control, aim) : next.Joystick.BindControl(control);
		return joystick && next.Hud.Bind(control) && tankHud.Bind(next.Crew);
	}

	public void Unbind()
	{
		version++;
		if (started) BlockWorld();
		TankInterior previous = boundInterior;
		boundInterior = null;
		if (previous != null) Disconnect(previous);
		SetStage(Stage.Idle);
	}

	private void Disconnect(TankInterior previous)
	{
		if (input != null) input.SuspendInput();
		if (previous.Mode != null) previous.Mode.Unbind();
		if (input != null) input.Unbind();
		if (previous.Interaction != null) previous.Interaction.StopUse();
		if (previous.DriverScreen != null) previous.DriverScreen.Unbind();
		if (previous.Sight != null) previous.Sight.Unbind();
		if (previous.Breech != null) previous.Breech.Unbind();
		foreach (TankLever lever in previous.Levers)
			if (lever != null) lever.Unbind();
		if (previous.Joystick != null)
		{
			if (joystickAimBound) previous.Joystick.Unbind();
			else previous.Joystick.UnbindControl();
		}
		if (previous.Hud != null) previous.Hud.Unbind();
		if (tankHud != null) tankHud.Unbind();
		if (control != null) control.Unbind();
	}

	private bool Abort(TankInterior next, uint request, string reason)
	{
		Disconnect(next);
		if (boundInterior == next) boundInterior = null;
		if (request == version) Fail(reason);
		return false;
	}

	private void BlockWorld()
	{
		if (control != null) control.SetInputEnabled(false);
		if (cameras != null) cameras.SetOutput(false);
		if (tankHud != null) tankHud.enabled = false;
	}

	private void TryConnect()
	{
		if (!started || stage != Stage.Waiting) return;
		if (session == null) { Fail("The session endpoint is missing."); return; }
		if (session.CurrentState == SessionJoinInit.State.Stopped || session.CurrentState == SessionJoinInit.State.Failed)
		{
			OnSessionStopped();
			return;
		}
		if (CanBindSession() && !Bind(interior)) Fail(LastError ?? "Interior binding failed.");
	}

	private void OnSessionReady()
	{
		if (isActiveAndEnabled && stage == Stage.Waiting) TryConnect();
	}

	private void OnSessionStopped()
	{
		Unbind();
		LastError = session != null ? session.LastError : "The session was destroyed.";
		bool waiting = session != null && (session.CurrentState == SessionJoinInit.State.Starting || session.CurrentState == SessionJoinInit.State.Running);
		SetStage(waiting ? Stage.Waiting : LastError == null ? Stage.Stopped : Stage.Failed);
	}

	private void Fail(string reason)
	{
		Unbind();
		LastError = reason;
		SetStage(Stage.Failed);
	}

	private void Attach()
	{
		if (attached || session == null) return;
		session.SceneReady += OnSessionReady;
		session.Stopped += OnSessionStopped;
		attached = true;
	}

	private void OnDisable()
	{
		if (attached && session != null)
		{
			session.SceneReady -= OnSessionReady;
			session.Stopped -= OnSessionStopped;
		}
		attached = false;
		Unbind();
	}

	private void SetStage(Stage next)
	{
		if (stage == next) return;
		stage = next;
		StageChanged?.Invoke(next);
	}

	[ContextMenu("Debug/Prepare Local Interior (Play Mode)")]
	private void DebugPrepare() => Debug.Log($"[TankSceneLink] Prepared={PrepareInterior()}, Stage={stage}.", this);

	[ContextMenu("Debug/Report Readiness")]
	private void ReportReadiness() => Debug.Log($"[TankSceneLink] Stage={stage}, Ready={IsReady}, JoystickAimDeferred={JoystickAimDeferred}, Error={LastError}", this);
}
