using Fusion;
using UnityEngine;

public enum TankInputButton
{
    Fire,
    Insert,
    Close,
    OpenEmpty
}

public struct TankCrewNetworkInput : INetworkInput
{
    public float LeftLever;
    public float RightLever;
    public Vector2 Aim;
    public NetworkButtons Buttons;
}