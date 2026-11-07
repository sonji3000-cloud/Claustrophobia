using UnityEngine;

/// <summary>Shows the authored gunner camera feed on the interior sight.</summary>
[DisallowMultipleComponent]
public sealed class GunnerSight : MonoBehaviour
{
	[SerializeField] private Camera feed;
	[SerializeField] private Renderer display;

	[SerializeField, Range(1f, 179f)] private float minFov = 15f;
	[SerializeField, Range(1f, 179f)] private float maxFov = 55f;
	[Tooltip("FOV change in degrees per scroll tick.")]
	[SerializeField, Min(0f)] private float zoomStep = 5f;

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

		if (active) feed.fieldOfView = Mathf.Clamp(feed.fieldOfView, minFov, maxFov);
	}

	public void ApplyZoom(float steps)
	{
		if (!isActiveAndEnabled || feed == null || !feed.isActiveAndEnabled || steps == 0f) return;

		float fov = Mathf.Clamp(feed.fieldOfView - steps * zoomStep, minFov, maxFov);
		if (feed.fieldOfView != fov) feed.fieldOfView = fov;
	}

	private void OnDisable() => StopFeed();

	private void StopFeed()
	{
		if (feed != null) feed.enabled = false;
		if (display != null) display.enabled = false;
	}

	private void OnValidate()
	{
		minFov = Mathf.Clamp(minFov, 1f, 179f);
		maxFov = Mathf.Clamp(maxFov, minFov, 179f);
		zoomStep = Mathf.Max(0f, zoomStep);
	}
}
