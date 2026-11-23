using System;
using System.Collections;
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

	private sealed class RetryHost : MonoBehaviour { }

	private static readonly NetworkObjectGuid CrewPlayerGuid = NetworkObjectGuid.Parse("9375fc0e9b7510b4ab57ee31aeb2f740");

	public TankControl tankControl;
	[SerializeField] private NetworkSceneManagerDefault sceneManager;
	[Tooltip("Use the authored scene manager. Legacy prototype scenes may retain their old fallback.")]
	[SerializeField] private bool requireSceneManager;
	[SerializeField] private bool localSession;

	private NetworkRunner runner;
	private TankCrewRegistry crewRegistry;
	private TankSceneLink sceneLink;
	private readonly Dictionary<PlayerRef, NetworkObject> crewPlayers = new(3);
	private bool attached;
	private bool startSucceeded;
	private bool sceneLoaded;
	private bool sceneReady;
	private bool connectedToHost;
	private bool joinOnly;
	private uint version;
	private Task stopTask;
	private CancellationTokenSource startCancel;
	private string scenePath;
	private int sceneIndex = -1;

	public NetworkRunner Runner => runner;
	public State CurrentState { get; private set; }
	public bool IsLocalSession => localSession;
	public string TargetScenePath => scenePath;
	public int TargetSceneIndex => sceneIndex;
	public bool StartSucceeded => startSucceeded;
	public bool HasSceneLoaded => sceneLoaded;
	public bool IsSceneReady => sceneReady && isActiveAndEnabled && runner != null && runner.IsRunning;
	public string LastError { get; private set; }
	internal TankInterior Interior => sceneLink != null && sceneLink.IsReady ? sceneLink.Interior : null;
	private bool IsConnectedGuest => connectedToHost && (CurrentState == State.Starting || CurrentState == State.Running);

	public event Action SceneReady;
	public event Action Stopped;

	internal TankCrew.Role GetCrewRole(PlayerRef player) => crewRegistry != null
		? crewRegistry.GetRole(player) : TankCrew.Role.None;

	private void Awake()
	{
		runner = GetComponent<NetworkRunner>();
		if (tankControl != null) crewRegistry = tankControl.GetComponent<TankCrewRegistry>();
		if (sceneManager == null) sceneManager = GetComponent<NetworkSceneManagerDefault>();
		TankSceneLink link = GetComponent<TankSceneLink>();
		if (link != null && link.Session == this) sceneLink = link;
		// Cache the owning scene before Fusion can move the runner to DontDestroyOnLoad.
		scenePath = gameObject.scene.path;
		sceneIndex = SceneUtility.GetBuildIndexByScenePath(scenePath);
	}

	private async void Start() => await StartSession();

	/// <summary>Returns startup success. SceneReady separately confirms the target scene callback.</summary>
	public Task<bool> StartSession() => StartSession(false);

	private async Task<bool> StartSession(bool joinOnly)
	{
		if (!Application.isPlaying || !isActiveAndEnabled) return false;
		if (CurrentState != State.Idle) return CurrentState == State.Running;
		uint request = ++version;
		CurrentState = State.Starting;
		LastError = null;
		this.joinOnly = joinOnly;
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
				GameMode = localSession ? GameMode.Single : joinOnly ? GameMode.Client : GameMode.AutoHostOrClient,
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
				FailStart(result.ShutdownReason);
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

	private void FailStart(ShutdownReason reason)
	{
		if (!localSession && reason is ShutdownReason.GameIsFull or ShutdownReason.GameClosed
			or ShutdownReason.GameNotFound or ShutdownReason.PhotonCloudTimeout
			or ShutdownReason.ConnectionTimeout or ShutdownReason.OperationTimeout)
		{
			Scene scene = SceneManager.GetSceneByPath(scenePath);
			if (scene.IsValid() && scene.isLoaded)
			{
				bool clientOnly = joinOnly || reason is ShutdownReason.GameIsFull or ShutdownReason.GameClosed;
				// Fusion destroys the failed runner's GameObject before returning from StartGame.
				var host = new GameObject("Session Join Retry").AddComponent<RetryHost>();
				SceneManager.MoveGameObjectToScene(host.gameObject, scene);
				host.StartCoroutine(RetrySession(host, scene, clientOnly));
				Debug.Log($"[SESSION RETRY] {reason}. Retrying in 1 second.", this);
			}
		}

		EndSession(reason.ToString());
	}

	private static IEnumerator RetrySession(RetryHost host, Scene scene, bool joinOnly)
	{
		yield return new WaitForSecondsRealtime(1f);
		if (SceneManager.GetActiveScene() != scene)
		{
			Destroy(host.gameObject);
			yield break;
		}

		int buildIndex = scene.buildIndex;
		SceneManager.sceneLoaded += OnSceneReloaded;
		try
		{
			SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Single);
		}
		catch
		{
			SceneManager.sceneLoaded -= OnSceneReloaded;
			Destroy(host.gameObject);
			throw;
		}

		void OnSceneReloaded(Scene loaded, LoadSceneMode mode)
		{
			if (loaded.buildIndex != buildIndex || mode != LoadSceneMode.Single) return;
			SceneManager.sceneLoaded -= OnSceneReloaded;
			foreach (GameObject root in loaded.GetRootGameObjects())
			{
				if (!root.TryGetComponent(out SessionJoinInit session) || !session.isActiveAndEnabled) continue;
				_ = session.StartSession(joinOnly);
				break;
			}
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
		if (runner.IsServer && sceneLink != null)
		{
			foreach (PlayerRef player in runner.CommittedPlayers) SpawnCrewPlayer(player);
		}
		SceneReady?.Invoke();
	}

	private void SpawnCrewPlayer(PlayerRef player)
	{
		if (!IsSceneReady || sceneLink == null || player.IsNone) return;
		if (crewPlayers.TryGetValue(player, out NetworkObject existing) && existing != null && existing.IsValid) return;

		NetworkSpawnStatus status = runner.TrySpawn(CrewPlayerGuid, out NetworkObject avatar, inputAuthority: player);
		if (status != NetworkSpawnStatus.Spawned)
		{
			Debug.LogError($"[CREW AVATAR] Spawn failed. Player={player}, Status={status}", this);
			return;
		}

		crewPlayers[player] = avatar;
		runner.SetPlayerObject(player, avatar);
		Debug.Log($"[CREW AVATAR] Spawned. Player={player}, Object={avatar.Id}", this);
	}

	private void EndSession(string error)
	{
		if (CurrentState == State.Stopped || CurrentState == State.Failed) return;
		version++;
		connectedToHost = false;
		sceneReady = false;
		startSucceeded = false;
		sceneLoaded = false;
		LastError = error;
		CurrentState = error == null ? State.Stopped : State.Failed;
		Stopped?.Invoke();
	}

	private void QuitGuest(string reason)
	{
		EndSession(reason);
		Debug.Log($"[HOST DISCONNECTED] Closing the guest game. Reason={reason}", this);
		Application.Quit();
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
		if (source != runner) return;
		crewPlayers.Clear();

		if (IsConnectedGuest) QuitGuest(reason.ToString());
		else if (CurrentState == State.Starting && reason != ShutdownReason.Ok) FailStart(reason);
		else EndSession(reason == ShutdownReason.Ok ? null : reason.ToString());
	}

	public void OnPlayerJoined(NetworkRunner source, PlayerRef player)
	{
		if (source == runner && source.IsServer)
		{
			if (crewRegistry != null) crewRegistry.Register(player);
			SpawnCrewPlayer(player);
		}
		Debug.Log($"Player {player} joined the session.");
	}

	public void OnPlayerLeft(NetworkRunner source, PlayerRef player)
	{
		if (source == runner && source.IsServer)
		{
			if (crewRegistry != null) crewRegistry.Unregister(player);
			if (crewPlayers.Remove(player, out NetworkObject avatar) && avatar != null && avatar.IsValid && avatar.HasStateAuthority)
				source.Despawn(avatar);
		}
		Debug.Log($"Player {player} left the session.");
	}
	public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
	public void OnConnectedToServer(NetworkRunner source)
	{
		if (source != runner || !source.IsClient) return;
		connectedToHost = true;
		Debug.Log("[CONNECTED] Server connected");
	}
	public void OnDisconnectedFromServer(NetworkRunner source, NetDisconnectReason reason)
	{
		if (source == runner && IsConnectedGuest) QuitGuest(reason.ToString());
	}
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
