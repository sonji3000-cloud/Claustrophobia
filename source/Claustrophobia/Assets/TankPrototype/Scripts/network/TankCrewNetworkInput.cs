using Fusion;
using UnityEngine;

public enum TankInputButton
{
    Fire,
    Insert,
    Close,
    OpenEmpty,
    Open,
    TakeRound,
    ReturnRound
}

public struct TankCrewNetworkInput : INetworkInput
{
    public float LeftLever;
    public float RightLever;
    public Vector2 Aim;
    public NetworkButtons Buttons;
    public Vector3 RoundPosition;
    public Quaternion RoundRotation;
    public TankCrew.Role RoundRole;
}
