using UnityEngine;

/// <summary>Shows the authored driver camera feed while driving is authorized.</summary>
[DisallowMultipleComponent]
public sealed class DriverScreen : MonoBehaviour
{
	[SerializeField] private TankControl control;
	[SerializeField] private Camera feed;
	[SerializeField] private Renderer display;

	private void OnEnable()
	{
		if (control != null) control.DriveChanged += OnDriveChanged;
		OnDriveChanged();
	}

	private void OnDriveChanged()
	{
		bool active = control != null && control.IsDriving
			&& feed != null && feed.targetTexture != null && display != null;

		if (feed != null) feed.enabled = active;
		if (display != null) display.enabled = active;
	}

	private void OnDisable()
	{
		if (control != null) control.DriveChanged -= OnDriveChanged;

		if (feed != null) feed.enabled = false;
		if (display != null) display.enabled = false;
	}
}
