using UnityEngine;

/// <summary>Shows the authored driver camera feed while the monitor is enabled.</summary>
[DisallowMultipleComponent]
public sealed class DriverScreen : MonoBehaviour
{
	[SerializeField] private Camera feed;
	[SerializeField] private Renderer display;

	private void OnEnable()
	{
		bool active = feed != null && feed.targetTexture != null && display != null;

		if (feed != null) feed.enabled = active;
		if (display != null) display.enabled = active;
	}

	private void OnDisable()
	{
		if (feed != null) feed.enabled = false;
		if (display != null) display.enabled = false;
	}
}
