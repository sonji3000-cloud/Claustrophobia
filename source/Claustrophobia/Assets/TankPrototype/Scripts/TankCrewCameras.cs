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
	private bool outputEnabled = true;
	private Camera[] cameras;
	private AudioListener[] listeners;

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

		cameras = new[] { driverCamera, gunnerCamera, commanderCamera, thirdPersonCamera, observationCamera };
		listeners = new AudioListener[cameras.Length];
		for (int i = 0; i < cameras.Length; i++)
			if (cameras[i] != null) listeners[i] = cameras[i].GetComponent<AudioListener>();

		observationListener = listeners[4];
	}

	private void OnEnable()
	{
		if (observationCamera != null)
		{
			observationWasEnabled = observationCamera.enabled;
			observationListenerWasEnabled = observationListener != null && observationListener.enabled;
			observationCamera.enabled = false;
			if (observationListener != null) observationListener.enabled = false;
		}
		SwitchView((int)startingStation);
	}

	public void Look(Vector2 delta)
	{
		if (!isActiveAndEnabled || !outputEnabled || CurrentStation != CrewStation.Commander || commanderCamera == null) return;

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
		ApplyOutput();
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
		ApplyOutput();
	}

	public void SetOutput(bool enabled)
	{
		outputEnabled = enabled;
		ApplyOutput();
	}

	private void ApplyOutput()
	{
		if (cameras == null) return;

		Camera selected = outputEnabled && isActiveAndEnabled ? ActiveCamera : null;
		for (int i = 0; i < cameras.Length; i++)
		{
			Camera camera = cameras[i];
			if (camera == null) continue;

			bool active = camera == selected;
			camera.enabled = active;
			camera.tag = active ? "MainCamera" : "Untagged";
			if (listeners[i] != null) listeners[i].enabled = active;
		}
	}

	private void OnDisable()
	{
		ApplyOutput();
		RestoreObservation();
	}

	private void RestoreObservation()
	{
		if (observationCamera != null)
		{
			observationCamera.enabled = outputEnabled && observationWasEnabled;
			observationCamera.tag = observationCamera.enabled ? "MainCamera" : "Untagged";
		}
		if (observationListener != null) observationListener.enabled = outputEnabled && observationListenerWasEnabled;
	}
}
