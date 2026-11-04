using UnityEngine;

/// <summary>Shows the authored gunner camera feed on the interior sight.</summary>
[DisallowMultipleComponent]
public sealed class GunnerSight : MonoBehaviour
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
