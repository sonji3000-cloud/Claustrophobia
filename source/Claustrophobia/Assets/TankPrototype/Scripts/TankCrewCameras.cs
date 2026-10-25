using UnityEngine;

/// <summary>Applies prototype camera selection and commander look requested by TankControl.</summary>
[DisallowMultipleComponent]
public sealed class TankCrewCameras : MonoBehaviour
{
    public enum CrewStation { Driver, Gunner, Commander, ThirdPerson }

    [SerializeField] private Camera driverCamera;
    [SerializeField] private Camera gunnerCamera;
    [SerializeField] private Camera commanderCamera;
    [SerializeField] private Camera thirdPersonCamera;
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
    public Camera ActiveCamera => CurrentStation == CrewStation.ThirdPerson && thirdPersonCamera == null
        ? observationCamera : GetCamera(CurrentStation);

    private Camera GetCamera(CrewStation station)
    {
        switch (station)
        {
            case CrewStation.Driver: return driverCamera;
            case CrewStation.Gunner: return gunnerCamera;
            case CrewStation.Commander: return commanderCamera;
            case CrewStation.ThirdPerson: return thirdPersonCamera;
            default: return null;
        }
    }

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

    public void Look(Vector2 delta)
    {
        if (!isActiveAndEnabled || CurrentStation != CrewStation.Commander || commanderCamera == null) return;

        commanderYaw = Mathf.Repeat(commanderYaw + delta.x * mouseSensitivity, 360f);
        commanderPitch = Mathf.Clamp(commanderPitch - delta.y * mouseSensitivity,
            minimumPitch, maximumPitch);
        commanderCamera.transform.localRotation = commanderBaseRotation
            * Quaternion.Euler(commanderPitch, commanderYaw, 0f);
    }

    /// <param name="station">0 = driver, 1 = gunner, 2 = commander, 3 = third person.</param>
    public void SwitchView(int station)
    {
        if (station < 0 || station > 3) return;
        Camera selected = GetCamera((CrewStation)station);
        if (selected == null) return;
        if (observationCamera != null) observationCamera.enabled = false;
        if (observationListener != null) observationListener.enabled = false;
        CurrentStation = (CrewStation)station;
        SetCameraState(driverCamera, driverCamera == selected);
        SetCameraState(gunnerCamera, gunnerCamera == selected);
        SetCameraState(commanderCamera, commanderCamera == selected);
        SetCameraState(thirdPersonCamera, thirdPersonCamera == selected);
    }

    // Return to the prototype overview when the operator loses an observation permission.
    public void ExitObservation()
    {
        if (!isActiveAndEnabled) return;
        if (thirdPersonCamera != null)
        {
            SwitchView((int)CrewStation.ThirdPerson);
            return;
        }

        CurrentStation = CrewStation.ThirdPerson;
        SetCameraState(driverCamera, false);
        SetCameraState(gunnerCamera, false);
        SetCameraState(commanderCamera, false);
        // A permission fallback must render even if the overview started disabled.
        if (observationCamera != null) observationCamera.enabled = true;
        if (observationListener != null) observationListener.enabled = true;
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
        SetCameraState(thirdPersonCamera, false);
        RestoreObservation();
    }

    private void RestoreObservation()
    {
        if (observationCamera != null) observationCamera.enabled = observationWasEnabled;
        if (observationListener != null) observationListener.enabled = observationListenerWasEnabled;
    }
}
