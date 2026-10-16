using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>F1/F2/F3 select crew stations. Only the commander supports mouse look.</summary>
[DisallowMultipleComponent]
public sealed class TankCrewCameras : MonoBehaviour
{
    public enum CrewStation { Driver, Gunner, Commander }

    [SerializeField] private Camera driverCamera;
    [SerializeField] private Camera gunnerCamera;
    [SerializeField] private Camera commanderCamera;
    [SerializeField] private Camera observationCamera;
    [SerializeField] private CrewStation startingStation = CrewStation.Driver;
    [Tooltip("Degrees per mouse pixel while holding the right mouse button.")]
    [SerializeField, Min(0f)] private float mouseSensitivity = 0.15f;
    [SerializeField, Range(-89f, 0f)] private float minimumPitch = -80f;
    [SerializeField, Range(0f, 89f)] private float maximumPitch = 80f;

    private Quaternion commanderBaseRotation;
    private float commanderYaw;
    private float commanderPitch;
    private bool observationWasEnabled;
    private AudioListener observationListener;
    private bool observationListenerWasEnabled;

    public CrewStation CurrentStation { get; private set; }
    public float CommanderYaw => commanderYaw;
    public float CommanderPitch => commanderPitch;
    public Camera ActiveCamera => CurrentStation == CrewStation.Driver ? driverCamera
        : CurrentStation == CrewStation.Gunner ? gunnerCamera : commanderCamera;

    private void Awake()
    {
        commanderBaseRotation = commanderCamera != null
            ? commanderCamera.transform.localRotation : Quaternion.identity;
    }

    private void OnEnable()
    {
        if (observationCamera != null)
        {
            observationWasEnabled = observationCamera.enabled;
            observationListener = observationCamera.GetComponent<AudioListener>();
            observationListenerWasEnabled = observationListener != null && observationListener.enabled;
            observationCamera.enabled = false;
            if (observationListener != null) observationListener.enabled = false;
        }
        SwitchView((int)startingStation);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.f1Key.wasPressedThisFrame) SwitchView(0);
        else if (keyboard.f2Key.wasPressedThisFrame) SwitchView(1);
        else if (keyboard.f3Key.wasPressedThisFrame) SwitchView(2);
    }

    private void LateUpdate()
    {
        if (CurrentStation != CrewStation.Commander || commanderCamera == null) return;
        Mouse mouse = Mouse.current;
        if (mouse == null || !mouse.rightButton.isPressed || !Application.isFocused) return;
        Vector2 delta = mouse.delta.ReadValue();
        commanderYaw = Mathf.Repeat(commanderYaw + delta.x * mouseSensitivity, 360f);
        commanderPitch = Mathf.Clamp(commanderPitch - delta.y * mouseSensitivity,
            minimumPitch, maximumPitch);
        commanderCamera.transform.localRotation = commanderBaseRotation
            * Quaternion.Euler(commanderPitch, commanderYaw, 0f);
    }

    /// <param name="station">0 = driver, 1 = gunner, 2 = commander.</param>
    public void SwitchView(int station)
    {
        if (station < 0 || station > 2) return;
        Camera selected = station == 0 ? driverCamera : station == 1 ? gunnerCamera : commanderCamera;
        if (selected == null) return;
        CurrentStation = (CrewStation)station;
        SetCameraState(driverCamera, driverCamera == selected);
        SetCameraState(gunnerCamera, gunnerCamera == selected);
        SetCameraState(commanderCamera, commanderCamera == selected);
    }

    private static void SetCameraState(Camera camera, bool active)
    {
        if (camera == null) return;
        camera.enabled = active;
        camera.tag = active ? "MainCamera" : "Untagged";
        AudioListener listener = camera.GetComponent<AudioListener>();
        if (listener != null) listener.enabled = active;
    }

    private void OnDisable()
    {
        SetCameraState(driverCamera, false);
        SetCameraState(gunnerCamera, false);
        SetCameraState(commanderCamera, false);
        if (observationCamera != null) observationCamera.enabled = observationWasEnabled;
        if (observationListener != null) observationListener.enabled = observationListenerWasEnabled;
    }
}
