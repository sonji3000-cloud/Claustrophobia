using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Connects the authored interior after the designated session and tank are ready.</summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-300)]
public sealed class TankSceneLink : MonoBehaviour
{
	public enum Stage { Idle, Waiting, Prepared, Binding, Ready, Stopped, Failed, Loading }

	[SerializeField] private string interiorPath;
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
	private TankCrewRegistry crewRegistry;
	private Scene worldScene;
	private string worldPath;
	private TankInterior interior;
	private Scene interiorScene;
	private string loadedPath;
	private bool ownsInterior;
	private AsyncOperation loadOp;
	private AsyncOperation unloadOp;
	private string loadPath;
	private uint loadVersion;
	private bool destroyed;
	private readonly List<GameObject> roots = new List<GameObject>(3);
	private readonly List<TankInterior> candidates = new List<TankInterior>(2);
	private TankInterior boundInterior;
	private bool joystickAimBound;
	private bool started;
	private bool attached;
	private uint version;
	private Stage stage;

	public Stage CurrentStage => stage;
	public TankInterior Interior => interior;
	public SessionJoinInit Session => session;
	public bool IsReady => stage == Stage.Ready && boundInterior != null && HasInterior && CanBindSession() && control.CanInput;
	public string InteriorPath => interiorPath;
	public Scene InteriorScene => interiorScene;
	public bool OwnsInterior => ownsInterior && interiorScene.IsValid() && interiorScene.isLoaded;
	public bool IsLoading => loadOp != null;
	public bool IsUnloading => unloadOp != null;
	public bool JoystickAimDeferred => !bindJoystickAim;
	public string LastError { get; private set; }
	public event Action<Stage> StageChanged;
	private bool HasInterior => interior != null && interiorScene.isLoaded
		&& interior.gameObject.scene == interiorScene && loadedPath == interiorPath;

	private void Awake()
	{
		destroyed = false;
		if (control == null) return;
		tankObject = control.GetComponent<NetworkObject>();
		crewRegistry = control.GetComponent<TankCrewRegistry>();
		worldScene = control.gameObject.scene;
		worldPath = worldScene.path;
	}

	private void OnEnable()
	{
		Attach();
		SetStage(Stage.Waiting);
		if (!started) return;
		BlockInput();
		TryConnect();
	}

	private void Start()
	{
		started = true;
		BlockInput();
		if (!ValidateWorld(out string reason) || !ValidatePath(out reason)) { Fail(reason); return; }
		SetStage(Stage.Waiting);
		TryConnect();
	}

	private void Update()
	{
		if (stage == Stage.Waiting) TryConnect();
		else if (stage == Stage.Ready || stage == Stage.Loading)
		{
			if (!CanBindSession()) OnSessionStopped();
			else if (stage == Stage.Ready && !HasInterior) Fail("The bound interior scene is no longer available.");
		}
	}

	public bool ValidateRefs(out string reason) => ValidateInterior(interior, out reason);

	private bool ValidateWorld(out string reason)
	{
		if (session == null || control == null || input == null || cameras == null
			|| tankHud == null || gun == null || aim == null || driverFeed == null || gunnerFeed == null)
		{
			reason = "TankSceneLink requires all explicit external endpoints.";
			return false;
		}
		if (input.gameObject != control.gameObject || cameras.gameObject != control.gameObject
			|| tankHud.gameObject != control.gameObject || gun.gameObject != control.gameObject
			|| aim.gameObject != control.gameObject || session.tankControl != control)
		{
			reason = "TankSceneLink endpoints must belong to the designated tank.";
			return false;
		}
		if (!control.isActiveAndEnabled || !input.isActiveAndEnabled)
		{
			reason = "The control and input must be active before completing the scene link.";
			return false;
		}
		if (driverFeed == gunnerFeed || !driverFeed.transform.IsChildOf(control.transform)
			|| !gunnerFeed.transform.IsChildOf(control.transform))
		{
			reason = "The two monitor feeds must belong to the designated tank.";
			return false;
		}
		reason = null;
		return true;
	}

	private bool ValidatePath(out string reason)
	{
		if (string.IsNullOrWhiteSpace(interiorPath) || interiorPath == worldPath
			|| SceneUtility.GetBuildIndexByScenePath(interiorPath) < 0)
		{
			reason = "The interior path must name a separate enabled scene in Build Settings.";
			return false;
		}
		reason = null;
		return true;
	}

	private bool ValidateInterior(TankInterior next, out string reason)
	{
		if (!ValidateWorld(out reason)) return false;
		if (next == null || next != interior || !HasInterior)
		{
			reason = "The designated interior scene has not been acquired.";
			return false;
		}
		if (!next.ValidateRefs(out reason)) return false;
		if (!next.Mode.isActiveAndEnabled || !next.DriverScreen.CanBind(driverFeed)
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

		ClearBinding();
		if (!CanBindSession()) return false;
		uint request = ++version;
		boundInterior = next;
		joystickAimBound = bindJoystickAim;
		SetStage(Stage.Binding);
		if (request != version) return false;
		try
		{
			if (!next.Prepare())
				return Abort(next, request, "Interior preparation was rejected.");
			if (request != version) return false;
			if (!Connect(next, request) || request != version || !CanBindSession())
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

	private bool Connect(TankInterior next, uint request)
	{
		if (crewRegistry != null && !crewRegistry.BindSeats(next.Seats)) return false;
		if (request != version || !control.Bind(next.Crew, next.DriveDevice, next.AimDevice, next.Sight, next.Breech)) return false;
		if (request != version || !input.Bind(next.Seats, next.Interaction, next.Mode)) return false;
		if (request != version || !next.Mode.Bind(input, cameras, tankHud)) return false;
		if (request != version || !next.DriverScreen.Bind(driverFeed)) return false;
		if (request != version || !next.Sight.Bind(gunnerFeed)) return false;
		if (request != version || !next.Breech.Bind(gun)) return false;

		for (int i = 0; i < next.Levers.Length; i++)
			if (request != version || !next.Levers[i].Bind(control)) return false;

		if (request != version) return false;
		bool joystick = joystickAimBound ? next.Joystick.Bind(control, aim) : next.Joystick.BindControl(control);
		return request == version && joystick && next.Hud.Bind(control)
			&& request == version && tankHud.Bind(next.Crew);
	}

	public void Unbind()
	{
		ClearBinding();
		SetStage(Stage.Idle);
	}

	private void ClearBinding()
	{
		// Internal transitions publish their final stage only after cleanup.
		version++;
		if (started) BlockInput();
		TankInterior previous = boundInterior;
		boundInterior = null;
		if (!ReferenceEquals(previous, null)) Disconnect(previous);
		if (started && Application.isPlaying) ShowExterior();
	}

	private void Disconnect(TankInterior previous)
	{
		if (input != null) input.SuspendInput();
		if (previous != null && previous.Mode != null) previous.Mode.Unbind();
		if (input != null) input.Unbind();
		if (previous != null)
		{
			if (crewRegistry != null) crewRegistry.UnbindSeats(previous.Seats);
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
		}
		if (tankHud != null) tankHud.Unbind();
		if (control != null) control.Unbind();
	}

	private bool Abort(TankInterior next, uint request, string reason)
	{
		if (request != version)
		{
			if (boundInterior == null)
			{
				Disconnect(next);
				if (started && Application.isPlaying) ShowExterior();
			}
			return false;
		}

		Disconnect(next);
		if (boundInterior == next) boundInterior = null;
		Fail(reason);
		return false;
	}

	private void BlockInput()
	{
		if (control != null) control.SetInputEnabled(false);
	}

	private void ShowExterior()
	{
		if (cameras == null || !cameras.isActiveAndEnabled) return;

		cameras.ExitObservation();
		cameras.SetOutput(true);
		if (tankHud != null) tankHud.enabled = true;
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
		if (!CanBindSession()) return;
		if (loadOp != null)
		{
			loadVersion = version;
			SetStage(Stage.Loading);
			return;
		}
		if (unloadOp != null) { SetStage(Stage.Loading); return; }
		if (!ValidateWorld(out string reason) || !ValidatePath(out reason)) { Fail(reason); return; }
		if (interiorScene.IsValid() && loadedPath != interiorPath)
		{
			ReleaseInterior();
			if (unloadOp != null) { SetStage(Stage.Loading); return; }
		}
		if (!FindScene(interiorPath, out Scene scene, out reason)) { Fail(reason); return; }
		if (scene.IsValid())
		{
			if (!scene.isLoaded) { Fail("The interior scene is being loaded outside this link."); return; }
			if (scene != interiorScene) ownsInterior = false;
			interiorScene = scene;
			loadedPath = interiorPath;
			AcquireInterior();
			return;
		}

		loadPath = interiorPath;
		loadVersion = version;
		try
		{
			loadOp = SceneManager.LoadSceneAsync(loadPath, LoadSceneMode.Additive);
			if (loadOp == null) { Fail("The interior load returned no operation."); return; }
			SetStage(Stage.Loading);
			loadOp.completed += OnLoadDone;
		}
		catch (Exception e) { loadOp = null; Fail(e.Message); }
	}

	/// <summary>Requests acquisition or retry. A pending load is shared across repeated requests.</summary>
	public bool LoadInterior()
	{
		if (!Application.isPlaying || !isActiveAndEnabled || !started || destroyed) return false;
		if (IsReady || stage == Stage.Loading) return true;
		if (!ValidateWorld(out string reason) || !ValidatePath(out reason)) { Fail(reason); return false; }

		LastError = null;
		SetStage(Stage.Waiting);
		TryConnect();
		return stage != Stage.Failed && stage != Stage.Stopped;
	}

	private static bool FindScene(string path, out Scene scene, out string reason)
	{
		scene = default;
		for (int i = 0; i < SceneManager.sceneCount; i++)
		{
			Scene candidate = SceneManager.GetSceneAt(i);
			if (candidate.path != path) continue;
			if (scene.IsValid()) { reason = "More than one interior scene is loaded at the configured path."; return false; }
			scene = candidate;
		}
		reason = null;
		return true;
	}

	private void OnLoadDone(AsyncOperation operation)
	{
		if (operation != loadOp) return;
		loadOp = null;
		if (!Application.isPlaying) return;
		if (!FindScene(loadPath, out Scene scene, out string reason) || !scene.IsValid() || !scene.isLoaded)
		{
			if (!destroyed && this != null && isActiveAndEnabled) Fail(reason ?? "The completed interior scene could not be found.");
			return;
		}

		interiorScene = scene;
		loadedPath = loadPath;
		ownsInterior = true;
		bool wanted = !destroyed && this != null && isActiveAndEnabled && started
			&& loadVersion == version && stage == Stage.Loading && loadPath == interiorPath && CanBindSession();
		loadPath = null;
		if (wanted) AcquireInterior();
		else
		{
			ReleaseInterior();
			if (!destroyed && this != null && isActiveAndEnabled && stage == Stage.Loading)
			{
				SetStage(Stage.Waiting);
				TryConnect();
			}
		}
	}

	private void AcquireInterior()
	{
		TankInterior found = null;
		interiorScene.GetRootGameObjects(roots);
		foreach (GameObject root in roots)
		{
			root.GetComponentsInChildren(true, candidates);
			foreach (TankInterior candidate in candidates)
			{
				if (found != null)
				{
					Fail("The interior scene must contain exactly one TankInterior.", release: true);
					return;
				}
				found = candidate;
			}
		}
		if (found == null)
		{
			Fail("The interior scene contains no TankInterior.", release: true);
			return;
		}

		interior = found;
		uint request = version;
		// A callback may cancel this request or finish a newer binding.
		if (!Bind(found) && request == version) Fail(LastError ?? "Interior binding failed.");
	}

	private void ReleaseInterior()
	{
		Scene previous = interiorScene;
		string previousPath = loadedPath;
		bool release = ownsInterior && previous.IsValid() && previous.isLoaded;
		interior = null;
		interiorScene = default;
		loadedPath = null;
		ownsInterior = false;
		if (!release || !Application.isPlaying) return;

		try
		{
			unloadOp = SceneManager.UnloadSceneAsync(previous);
			if (unloadOp == null) throw new InvalidOperationException("The owned interior could not be unloaded.");
			unloadOp.completed += OnUnloadDone;
		}
		catch (Exception e)
		{
			interiorScene = previous;
			loadedPath = previousPath;
			ownsInterior = true;
			LastError = e.Message;
		}
	}

	private void OnUnloadDone(AsyncOperation operation)
	{
		if (operation != unloadOp) return;
		unloadOp = null;
		if (!Application.isPlaying || destroyed || this == null || !isActiveAndEnabled || stage != Stage.Loading) return;
		SetStage(Stage.Waiting);
		TryConnect();
	}

	private void OnSessionReady()
	{
		if (isActiveAndEnabled && stage == Stage.Waiting) TryConnect();
	}

	private void OnSessionStopped()
	{
		ClearBinding();
		LastError = session != null ? session.LastError : "The session was destroyed.";
		bool waiting = session != null && (session.CurrentState == SessionJoinInit.State.Starting || session.CurrentState == SessionJoinInit.State.Running);
		if (!waiting) ReleaseInterior();
		SetStage(waiting ? Stage.Waiting : LastError == null ? Stage.Stopped : Stage.Failed);
	}

	private void Fail(string reason, bool release = false)
	{
		ClearBinding();
		LastError = reason;
		if (release) ReleaseInterior();
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

	private void OnDestroy()
	{
		destroyed = true;
		version++;
		ReleaseInterior();
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
	private void ReportReadiness() => Debug.Log($"[TankSceneLink] Stage={stage}, Ready={IsReady}, Interior={interiorPath}, Loading={IsLoading}, Owned={ownsInterior}, JoystickAimDeferred={JoystickAimDeferred}, Error={LastError}", this);
}
