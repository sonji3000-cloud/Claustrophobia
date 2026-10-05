using Fusion;
using Fusion.Matchmaking;
using Fusion.Photon.Realtime;
using Photon.Realtime;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;


public class FusionRoomTest : MonoBehaviour, IMatchmakingCallbacks
{
    [Header("UI")]
    [SerializeField]
    private TMP_InputField roomNameInput;

    [SerializeField]
    private TMP_Text statusText;


    private RealtimeClient realtimeClient;
    private NetworkRunner runner;

    private GameMode pendingGameMode;

    private bool isBusy;
    

    private void Update()
    {
        // 아직 Fusion에게 RealtimeClient를 넘기기 전에는
        // 우리가 직접 Service()를 실행해야 한다.
        if (runner == null)
        {
            realtimeClient?.Service();
            
        }
    }


    private void SetupRealtimeClient()
    {
        if (realtimeClient != null)
            return;

        realtimeClient =
            new RealtimeClient()
                .SetupForFusion();

        realtimeClient.AddCallbackTarget(this);
    }


    // =========================================================
    // 방 생성
    // =========================================================

    public async void CreateRoom()
    {
        if (isBusy)
            return;

        string roomName =
            roomNameInput.text.Trim();

        if (string.IsNullOrEmpty(roomName))
        {
            SetStatus("방 이름을 입력하세요.");
            return;
        }

        isBusy = true;

        SetStatus(
            $"Photon 연결 중...\n방 생성 : {roomName}"
        );

        try
        {
            SetupRealtimeClient();


            var matchmakingArgs =
                new MatchmakingArguments
                {
                    RoomName = roomName,
                    MaxPlayers = 3
                }
                .SetupForFusion();


            if (!realtimeClient.IsConnectedAndReady)
            {
                await realtimeClient
                    .ConnectUsingSettingsAsync(
                        matchmakingArgs
                            .PhotonSettings
                    );
            }


            pendingGameMode =
                GameMode.Host;


            var enterRoomArgs =
                new EnterRoomArgs
                {
                    RoomName = roomName,

                    RoomOptions =
                        new RoomOptions
                        {
                            MaxPlayers = 3,
                            IsOpen = true,
                            IsVisible = true,

                            Plugins = new[]
                            {
                            matchmakingArgs
                                .PluginName
                            }
                        }
                };


            bool requestSent =
                realtimeClient
                    .OpCreateRoom(
                        enterRoomArgs
                    );


            if (!requestSent)
            {
                isBusy = false;

                SetStatus(
                    "방 생성 요청을 " +
                    "전송하지 못했습니다."
                );

                return;
            }


            SetStatus(
                $"방 생성 요청 전송 : {roomName}"
            );
        }
        catch (Exception e)
        {
            isBusy = false;

            Debug.LogException(e);

            SetStatus(
                $"Photon 연결 실패\n" +
                e.Message
            );
        }
    }


    // =========================================================
    // 방 참여
    // =========================================================

    public async void JoinRoom()
    {
        if (isBusy)
            return;

        string roomName =
            roomNameInput.text.Trim();

        if (string.IsNullOrEmpty(roomName))
        {
            SetStatus("방 이름을 입력하세요.");
            return;
        }


        isBusy = true;

        SetStatus(
            $"Photon 연결 중...\n방 참여 : {roomName}"
        );


        try
        {
            SetupRealtimeClient();


            if (!realtimeClient.IsConnectedAndReady)
            {
                await realtimeClient
                    .ConnectUsingSettingsAsync(
                        PhotonAppSettings
                            .Global
                            .AppSettings
                    );
            }


            pendingGameMode =
                GameMode.Client;


            var enterRoomArgs =
                new EnterRoomArgs
                {
                    RoomName = roomName
                };


            bool requestSent =
                realtimeClient
                    .OpJoinRoom(
                        enterRoomArgs
                    );


            if (!requestSent)
            {
                isBusy = false;

                SetStatus(
                    "방 참여 요청을 전송하지 못했습니다."
                );
            }
            else
            {
                SetStatus(
                    $"방 참여 요청 전송 : {roomName}"
                );
            }
        }
        catch (Exception e)
        {
            isBusy = false;

            Debug.LogException(e);

            SetStatus(
                $"Photon 연결 실패\n{e.Message}"
            );
        }
    }


    // =========================================================
    // Fusion 시작
    // =========================================================

    private async void StartFusion()
    {
        SetStatus(
            $"Realtime 연결 성공\nFusion {pendingGameMode} 시작 중..."
        );


        // NetworkRunner는 1회용이므로
        // 새 GameObject에 생성
        GameObject runnerObject =
            new GameObject(
                $"NetworkRunner_{pendingGameMode}"
            );


        runner =
            runnerObject
                .AddComponent<NetworkRunner>();


        NetworkSceneManagerDefault
            sceneManager =
                runnerObject
                    .AddComponent<
                        NetworkSceneManagerDefault
                    >();


        // 현재 Scene
        Scene activeScene =
            SceneManager.GetActiveScene();


        SceneRef sceneRef =
            SceneRef.FromIndex(
                activeScene.buildIndex
            );


        NetworkSceneInfo sceneInfo =
            new NetworkSceneInfo();


        if (sceneRef.IsValid)
        {
            sceneInfo.AddSceneRef(
                sceneRef,
                LoadSceneMode.Additive
            );
        }


        var result =
            await runner.StartGame(
                new StartGameArgs
                {
                    GameMode =
                        pendingGameMode,

                    // Realtime에서 이미
                    // 방 생성/참여를 완료했으므로
                    // 이 Client를 그대로 Fusion에 넘긴다.
                    RealtimeClient =
                        realtimeClient,

                    PlayerCount = 3,

                    Scene = sceneInfo,

                    SceneManager =
                        sceneManager
                }
            );


        if (result.Ok)
        {
            SetStatus(
                $"Fusion 시작 성공\n" +
                $"Mode : {pendingGameMode}\n" +
                $"Room : {runner.SessionInfo.Name}"
            );

            Debug.Log(
                $"Fusion Started : " +
                $"{runner.SessionInfo.Name}"
            );
        }
        else
        {
            SetStatus(
                $"Fusion 시작 실패\n" +
                result.ShutdownReason
            );

            Debug.LogError(
                result.ShutdownReason
            );
        }


        isBusy = false;
    }


    // =========================================================
    // Photon Realtime Matchmaking Callbacks
    // =========================================================

    public void OnCreatedRoom()
    {
        Debug.Log(
            "Realtime Room 생성 성공"
        );

        SetStatus(
            $"방 생성 성공\n" +
            $"{realtimeClient.CurrentRoom.Name}"
        );
    }


    public void OnJoinedRoom()
    {
        Debug.Log(
            $"Realtime Room 입장 : " +
            realtimeClient.CurrentRoom.Name
        );


        // 생성 성공도 OnJoinedRoom이 호출되고
        // 일반 참여 성공도 OnJoinedRoom이 호출된다.
        //
        // pendingGameMode를 이용해서
        // Host / Client를 구분한다.

        StartFusion();
    }


    public void OnCreateRoomFailed(
        short returnCode,
        string message)
    {
        isBusy = false;

        Debug.LogWarning(
            $"Create Failed : " +
            $"{returnCode} / {message}"
        );


        if (returnCode ==
            ErrorCode.GameIdAlreadyExists)
        {
            SetStatus(
                "방 생성 실패\n" +
                "이미 사용 중인 방 이름입니다."
            );
        }
        else
        {
            SetStatus(
                $"방 생성 실패\n" +
                $"{returnCode} : {message}"
            );
        }
    }


    public void OnJoinRoomFailed(
        short returnCode,
        string message)
    {
        isBusy = false;

        Debug.LogWarning(
            $"Join Failed : " +
            $"{returnCode} / {message}"
        );


        SetStatus(
            $"방 참여 실패\n" +
            $"{returnCode} : {message}"
        );
    }


    // 사용하지 않는 Callback들
    public void OnJoinRandomFailed(
        short returnCode,
        string message)
    {
    }

    public void OnLeftRoom()
    {
    }

    public void OnFriendListUpdate(
        List<FriendInfo> friendList)
    {
    }


    private void SetStatus(
        string message)
    {
        Debug.Log(message);

        if (statusText != null)
        {
            statusText.text =
                message;
        }
    }


    private void OnDestroy()
    {
        if (realtimeClient != null)
        {
            realtimeClient
                .RemoveCallbackTarget(this);
        }
    }
}