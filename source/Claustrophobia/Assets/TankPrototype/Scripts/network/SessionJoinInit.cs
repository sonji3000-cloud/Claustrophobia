using Fusion;
using Fusion.Sockets;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using static Unity.VectorGraphics.SVGParser;

[RequireComponent(typeof(NetworkRunner))]
public class SessionJoinInit : MonoBehaviour, INetworkRunnerCallbacks
{
    private NetworkRunner runner;

    public TankControl tankControl; // Reference to the TankControl component

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private async void Start()
    {
        runner = GetComponent<NetworkRunner>();

        runner.ProvideInput = true; // Enable input provision for the network runner
        runner.AddCallbacks(this);


        int sceneIndex = SceneManager.GetActiveScene().buildIndex;

        if (sceneIndex < 0)
        {
            Debug.LogError("현재 씬이 Build Profiles에 등록되지 않았습니다.");
            return;
        }

        SceneRef sceneRef = SceneRef.FromIndex(sceneIndex);

        NetworkSceneInfo sceneInfo = new NetworkSceneInfo();
        sceneInfo.AddSceneRef(sceneRef, LoadSceneMode.Single);

        // Fusion이 씬과 NetworkObject를 관리하도록 지정
        NetworkSceneManagerDefault sceneManager =
            GetComponent<NetworkSceneManagerDefault>();

        if (sceneManager == null)
            sceneManager = gameObject.AddComponent<NetworkSceneManagerDefault>();


        StartGameArgs startGameArgs = new StartGameArgs
        {
            GameMode = GameMode.AutoHostOrClient,
            SessionName = "MySession",
            PlayerCount = 3, // Set the maximum number of players for the session
            IsOpen = true, // Allow other players to join the session
            IsVisible = true, // Make the session visible in the lobby

            Scene = sceneInfo,
            SceneManager = sceneManager

        };

        runner.StartGame(startGameArgs);
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    // Implementing INetworkRunnerCallbacks interface methods
    public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
    { // when a player joins the session, this method is called
        Debug.Log($"Player {player} joined the session.");
    }
    public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
    { // when a player leaves the session, this method is called
       
        Debug.Log($"Player {player} left the session.");
    }
    public void OnInput(NetworkRunner runner, NetworkInput input)
    {
        if (tankControl == null)
        {
            input.Set(default(TankCrewNetworkInput));
            return;
        }


        tankControl.SubmitNetworkInput(input);
    }

    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnConnectedToServer(NetworkRunner runner) 
    {
        Debug.Log("[CONNECTED] Server 연결됨");
    }
    public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason) 
    {
        
    }
    public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
    public void OnSceneLoadDone(NetworkRunner runner)
    {
        Debug.Log(
            $"[SCENE LOAD DONE] Mode={runner.GameMode}"
        );

        // Unity 6.6 API
        TankMotor[] motors =
            FindObjectsByType<TankMotor>(
                FindObjectsInactive.Include
            );

        foreach (TankMotor motor in motors)
        {
            NetworkObject netObj =
                motor.GetComponentInParent<NetworkObject>();

            Debug.Log(
                $"[TANK FOUND] " +
                $"Name={motor.name}, " +
                $"EntityId={motor.GetEntityId()}, " +
                $"NetworkObject={netObj != null}, " +
                $"Scene={motor.gameObject.scene.name}",
                motor
            );
        }
    }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }

    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }


    // local methods
    public void RegisterTankControl(TankControl control)
    {
        tankControl = control;

        Debug.Log(
            $"[TANK REGISTERED] ID={tankControl.GetEntityId()}" + 
            $"Role={control.GetComponent<TankCrew>().CurrentRole}"
        );
    }

}
