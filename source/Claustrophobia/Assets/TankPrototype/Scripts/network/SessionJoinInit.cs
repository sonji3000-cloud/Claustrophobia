using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Fusion;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(NetworkRunner))]
public class SessionJoinInit : MonoBehaviour, INetworkRunnerCallbacks
{
	public enum State { Idle, Starting, Running, Stopped, Failed }

	public TankControl tankControl;
	[SerializeField] private NetworkSceneManagerDefault sceneManager;
	[Tooltip("Use the authored scene manager. Legacy prototype scenes may retain their old fallback.")]
	[SerializeField] private bool requireSceneManager;

	private NetworkRunner runner;
	private TankCrewRegistry crewRegistry;
	private bool attached;
	private bool startSucceeded;
	private bool sceneLoaded;
	private bool sceneReady;
	private uint version;
	private Task stopTask;
	private CancellationTokenSource startCancel;
	private string scenePath;
	private int sceneIndex = -1;

	public NetworkRunner Runner => runner;
	public State CurrentState { get; private set; }
	public string TargetScenePath => scenePath;
	public int TargetSceneIndex => sceneIndex;
	public bool StartSucceeded => startSucceeded;
	public bool HasSceneLoaded => sceneLoaded;
	public bool IsSceneReady => sceneReady && isActiveAndEnabled && runner != null && runner.IsRunning;
	public string LastError { get; private set; }

	public event Action SceneReady;
	public event Action Stopped;

	private void Awake()
	{
		runner = GetComponent<NetworkRunner>();
		if (tankControl != null) crewRegistry = tankControl.GetComponent<TankCrewRegistry>();
		if (sceneManager == null) sceneManager = GetComponent<NetworkSceneManagerDefault>();
		// Cache the owning scene before Fusion can move the runner to DontDestroyOnLoad.
		scenePath = gameObject.scene.path;
		sceneIndex = SceneUtility.GetBuildIndexByScenePath(scenePath);
	}

	private async void Start() => await StartSession();

	/// <summary>Returns startup success. SceneReady separately confirms the target scene callback.</summary>
	public async Task<bool> StartSession()
	{
		if (!Application.isPlaying || !isActiveAndEnabled) return false;
		if (CurrentState != State.Idle) return CurrentState == State.Running;
		uint request = ++version;
		CurrentState = State.Starting;
		LastError = null;
		if (sceneIndex < 0 || string.IsNullOrEmpty(scenePath))
		{
			EndSession("The owning scene is not enabled in Build Settings.");
			return false;
		}
		if (sceneManager == null && !requireSceneManager)
			sceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>();
		if (sceneManager == null)
		{
			EndSession("The authored NetworkSceneManagerDefault is missing.");
			return false;
		}

		Attach();
		runner.ProvideInput = true;
		var scenes = new NetworkSceneInfo();
		scenes.AddSceneRef(SceneRef.FromIndex(sceneIndex), LoadSceneMode.Single);
		var cancel = new CancellationTokenSource();
		startCancel = cancel;
		try
		{
			var result = await runner.StartGame(new StartGameArgs
			{
				GameMode = GameMode.AutoHostOrClient,
				SessionName = "MySession",
				PlayerCount = 3,
				IsOpen = true,
				IsVisible = true,
				Scene = scenes,
				SceneManager = sceneManager,
				StartGameCancellationToken = cancel.Token
			});
			if (request != version || !Application.isPlaying || !isActiveAndEnabled) return false;
			if (!result.Ok)
			{
				EndSession(result.ShutdownReason.ToString());
				return false;
			}

			startSucceeded = true;
			CurrentState = State.Running;
			ApplyReady();
			return true;
		}
		catch (Exception e)
		{
			if (request == version) EndSession(e.Message);
			return false;
		}
		finally
		{
			if (startCancel == cancel) startCancel = null;
			cancel.Dispose();
		}
	}

	public Task StopSession()
	{
		EndSession(null);
		startCancel?.Cancel();
		if (stopTask == null) stopTask = ShutdownRunner();
		return stopTask;
	}

	private async Task ShutdownRunner()
	{
		if (runner == null || runner.IsShutdown) return;
		try { await runner.Shutdown(false); }
		catch (Exception e) { LastError = e.Message; }
	}

	private void ApplyReady()
	{
		if (sceneReady || !startSucceeded || !sceneLoaded || !isActiveAndEnabled
			|| CurrentState != State.Running || runner == null || !runner.IsRunning) return;

		sceneReady = true;
		SceneReady?.Invoke();
	}

	private void EndSession(string error)
	{
		if (CurrentState == State.Stopped || CurrentState == State.Failed) return;
		version++;
		sceneReady = false;
		startSucceeded = false;
		sceneLoaded = false;
		LastError = error;
		CurrentState = error == null ? State.Stopped : State.Failed;
		Stopped?.Invoke();
	}

	private void Attach()
	{
		if (attached || runner == null) return;
		runner.AddCallbacks(this);
		attached = true;
	}

	private void Detach()
	{
		if (attached && runner != null) runner.RemoveCallbacks(this);
		attached = false;
	}

	private async void OnDisable()
	{
		if (Application.isPlaying) await StopSession();
		Detach();
	}

	public void OnInput(NetworkRunner source, NetworkInput input)
	{
		if (source != runner || !IsSceneReady || tankControl == null || !tankControl.CanInput)
		{
			input.Set(default(TankCrewNetworkInput));
			return;
		}
		tankControl.SubmitNetworkInput(input);
	}

	public void OnSceneLoadStart(NetworkRunner source)
	{
		if (source != runner || CurrentState != State.Starting && CurrentState != State.Running) return;
		bool notify = sceneReady;
		sceneReady = false;
		sceneLoaded = false;
		if (notify) Stopped?.Invoke();
	}

	public void OnSceneLoadDone(NetworkRunner source)
	{
		if (source != runner || !isActiveAndEnabled || CurrentState != State.Starting && CurrentState != State.Running) return;
		Scene scene = SceneManager.GetSceneByPath(scenePath);
		sceneLoaded = scene.IsValid() && scene.isLoaded && scene.buildIndex == sceneIndex;
		ApplyReady();
	}

	public void OnShutdown(NetworkRunner source, ShutdownReason reason)
	{
		if (source == runner) EndSession(reason == ShutdownReason.Ok ? null : reason.ToString());
	}

	public void OnPlayerJoined(NetworkRunner source, PlayerRef player)
	{
		if (source == runner && source.IsServer && crewRegistry != null) crewRegistry.Register(player);
		Debug.Log($"Player {player} joined the session.");
	}

	public void OnPlayerLeft(NetworkRunner source, PlayerRef player)
	{
		if (source == runner && source.IsServer && crewRegistry != null) crewRegistry.Unregister(player);
		Debug.Log($"Player {player} left the session.");
	}
	public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
	public void OnConnectedToServer(NetworkRunner runner) => Debug.Log("[CONNECTED] Server connected");
	public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) { }
	public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
	public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
	public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
	public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
	public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
	public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
	public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
	public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
	public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
	public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }
	public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }
}
