using UnityEngine;
using UnityEngine.InputSystem;

[DefaultExecutionOrder(-100)]
public sealed class CrewDebugView : MonoBehaviour
{
	[SerializeField] private Camera playerView;
	[SerializeField] private Camera observer;
	[SerializeField] private AudioListener playerAudio;
	[SerializeField] private AudioListener observerAudio;
	[SerializeField] private CrewInteract interaction;
	[SerializeField] private LineRenderer sightLines;
	[SerializeField] private LineRenderer selectionRing;
	[SerializeField] private LineRenderer retainRing;
	[SerializeField] private float distance = 2f;
	[SerializeField] private float sensitivity = .15f;

	private float yaw = -120f;
	private float pitch = 20f;
	private bool savedView;
	private bool savedAudio;
	private readonly Vector3[] points = new Vector3[16];
	private readonly Vector3[] ringPoints = new Vector3[33];

	public bool ShowDebug { get; private set; }
	public bool IsObserving { get; private set; }

	private void Update()
	{
		if (!Application.isFocused) return;

		Keyboard keyboard = Keyboard.current;
		if (keyboard != null)
		{
			if (keyboard.f6Key.wasPressedThisFrame) ShowDebug = !ShowDebug;
			if (keyboard.f7Key.wasPressedThisFrame) SetObserving(!IsObserving);
		}

		if (!IsObserving) return;
		Mouse mouse = Mouse.current;
		if (mouse == null) return;
		if (mouse.rightButton.isPressed)
		{
			Vector2 delta = mouse.delta.ReadValue() * sensitivity;
			yaw += delta.x;
			pitch = Mathf.Clamp(pitch - delta.y, -75f, 75f);
		}
		distance = Mathf.Clamp(distance - mouse.scroll.ReadValue().y * .002f, .3f, 5f);
	}

	private void LateUpdate()
	{
		if (IsObserving && observer != null && playerView != null)
		{
			Quaternion rot = Quaternion.Euler(pitch, yaw, 0f);
			observer.transform.SetPositionAndRotation(playerView.transform.position - rot * Vector3.forward * distance, rot);
		}

		if (sightLines == null) return;
		sightLines.enabled = IsObserving && ShowDebug;
		if (selectionRing != null) selectionRing.enabled = sightLines.enabled;
		if (retainRing != null) retainRing.enabled = sightLines.enabled;
		if (!sightLines.enabled || playerView == null) return;

		Vector3 origin = playerView.transform.position;
		float reach = interaction.Reach;
		Vector3 a = playerView.ViewportToWorldPoint(new Vector3(0f, 0f, reach));
		Vector3 b = playerView.ViewportToWorldPoint(new Vector3(0f, 1f, reach));
		Vector3 c = playerView.ViewportToWorldPoint(new Vector3(1f, 1f, reach));
		Vector3 d = playerView.ViewportToWorldPoint(new Vector3(1f, 0f, reach));
		points[0] = origin;
		points[1] = a;
		points[2] = b;
		points[3] = origin;
		points[4] = c;
		points[5] = b;
		points[6] = c;
		points[7] = d;
		points[8] = origin;
		points[9] = d;
		points[10] = a;
		points[11] = origin;
		points[12] = playerView.ViewportToWorldPoint(new Vector3(.5f, .5f, reach));
		points[13] = origin;
		CrewDevice target = interaction.Active != null ? interaction.Active : interaction.Selected;
		Vector3 end = points[12];
		for (int i = 0; i < interaction.Devices.Length; i++)
			if (interaction.Devices[i] == target) end = interaction.Candidates[i].Point;
		points[14] = end;
		points[15] = origin;
		sightLines.SetPositions(points);
		DrawRing(selectionRing, interaction.Radius, reach);
		DrawRing(retainRing, interaction.RetainRadius, reach);
	}

	private void DrawRing(LineRenderer line, float radius, float reach)
	{
		if (line == null) return;

		for (int i = 0; i < ringPoints.Length; i++)
		{
			float angle = i * Mathf.PI * 2f / (ringPoints.Length - 1);
			ringPoints[i] = playerView.ViewportToWorldPoint(new Vector3(
				.5f + Mathf.Cos(angle) * radius / playerView.aspect,
				.5f + Mathf.Sin(angle) * radius, reach));
		}
		line.SetPositions(ringPoints);
	}

	public void SetObserving(bool enabled)
	{
		if (IsObserving == enabled || playerView == null || observer == null) return;

		IsObserving = enabled;
		if (enabled)
		{
			savedView = playerView.enabled;
			savedAudio = playerAudio != null && playerAudio.enabled;
			playerView.enabled = false;
			if (playerAudio != null) playerAudio.enabled = false;
			observer.enabled = true;
			if (observerAudio != null) observerAudio.enabled = savedAudio;
			ShowDebug = true;
		}
		else
		{
			observer.enabled = false;
			if (observerAudio != null) observerAudio.enabled = false;
			playerView.enabled = savedView;
			if (playerAudio != null) playerAudio.enabled = savedAudio;
		}
	}

	private void OnDisable()
	{
		SetObserving(false);
		if (sightLines != null) sightLines.enabled = false;
		if (selectionRing != null) selectionRing.enabled = false;
		if (retainRing != null) retainRing.enabled = false;
	}
}
