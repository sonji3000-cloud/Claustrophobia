using UnityEngine;

/// <summary>Shows the authored driver camera feed while the monitor is enabled.</summary>
[DisallowMultipleComponent]
public sealed class DriverScreen : MonoBehaviour
{
	[SerializeField] private Camera feed;
	[SerializeField] private Renderer display;

	public Camera Feed => feed;

	/// <summary>The camera must already target the display's authored RenderTexture.</summary>
	public bool Bind(Camera next)
	{
		if (!CanBind(next)) return false;
		if (feed == next) return true;

		if (isActiveAndEnabled && feed != null) feed.enabled = false;
		feed = next;
		if (isActiveAndEnabled) ApplyFeed();
		return true;
	}

	public bool CanBind(Camera next) => next != null && next.targetTexture != null && display != null
		&& display.sharedMaterial != null && display.sharedMaterial.mainTexture == next.targetTexture;

	public void Unbind()
	{
		if (isActiveAndEnabled) StopFeed();
		feed = null;
	}

	private void OnEnable() => ApplyFeed();

	private void ApplyFeed()
	{
		bool active = feed != null && feed.targetTexture != null && display != null;

		if (feed != null) feed.enabled = active;
		if (display != null) display.enabled = active;
	}

	private void OnDisable() => StopFeed();

	private void StopFeed()
	{
		if (feed != null) feed.enabled = false;
		if (display != null) display.enabled = false;
	}
}
