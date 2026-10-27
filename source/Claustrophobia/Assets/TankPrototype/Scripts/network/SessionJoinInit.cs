using Fusion;
using Fusion.Sockets;
using System;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(NetworkRunner))]
public class SessionJoinInit : MonoBehaviour, INetworkRunnerCallbacks
{
    private NetworkRunner runner;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private async void Start()
    {
        runner = GetComponent<NetworkRunner>();

        runner.AddCallbacks(this);

        GameMode mode = Application.isEditor
        ? GameMode.Host
        : GameMode.Client;

        StartGameArgs startGameArgs = new StartGameArgs
        {
            GameMode = mode,
            SessionName = "MySession",
            PlayerCount = 3, // Set the maximum number of players for the session
            IsOpen = true, // Allow other players to join the session
            IsVisible = true, // Make the session visible in the lobby
            
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
        
    }

    public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
    public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason) { }
    public void OnConnectedToServer(NetworkRunner runner) 
    {
        Debug.Log("[CONNECTED] Server ¿¬°áµÊ");
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
    public void OnSceneLoadDone(NetworkRunner runner) { }
    public void OnSceneLoadStart(NetworkRunner runner) { }
    public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }

    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key, float progress) { }

    public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }

}
