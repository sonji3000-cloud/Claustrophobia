using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

[DisallowMultipleComponent]
public sealed class CrewLook : MonoBehaviour
{
	[SerializeField] private Camera view;
	[SerializeField] private Transform yawPivot;
	[SerializeField] private Transform pitchPivot;
	[SerializeField] private Renderer[] bodyRenderers = System.Array.Empty<Renderer>();

	[SerializeField, Min(0f)] private float sensitivity = 0.15f;
	[SerializeField, Range(-89f, 0f)] private float minPitch = -80f;
	[SerializeField, Range(0f, 89f)] private float maxPitch = 80f;

	private Quaternion yawBase;
	private Quaternion pitchBase;
	private ShadowCastingMode[] shadowModes;
	private float yaw;
	private float pitch;
	private bool bodyHidden;
	private bool ownsCursor;
	private CursorLockMode savedLock;
	private bool savedVisible;

	private void Awake()
	{
		yawBase = yawPivot != null ? yawPivot.localRotation : Quaternion.identity;
		pitchBase = pitchPivot != null ? pitchPivot.localRotation : Quaternion.identity;

		shadowModes = new ShadowCastingMode[bodyRenderers.Length];
		for (int i = 0; i < bodyRenderers.Length; i++)
			if (bodyRenderers[i] != null)
				shadowModes[i] = bodyRenderers[i].shadowCastingMode;
	}

	private void Update()
	{
		bool hasView = view != null && view.isActiveAndEnabled;
		SetBodyHidden(hasView);

		if (!hasView || !Application.isFocused || yawPivot == null || pitchPivot == null)
		{
			ReleaseCursor();
			return;
		}

		Keyboard keyboard = Keyboard.current;
		if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
		{
			ReleaseCursor();
			return;
		}

		Mouse mouse = Mouse.current;
		if (mouse == null) return;

		if (!ownsCursor)
		{
			if (mouse.leftButton.wasPressedThisFrame) CaptureCursor();
			return;
		}

		if (Cursor.lockState != CursorLockMode.Locked)
		{
			ReleaseCursor();
			return;
		}

		Vector2 delta = mouse.delta.ReadValue() * sensitivity;
		yaw = Mathf.Repeat(yaw + delta.x, 360f);
		pitch = Mathf.Clamp(pitch - delta.y, minPitch, maxPitch);

		yawPivot.localRotation = yawBase * Quaternion.Euler(0f, yaw, 0f);
		pitchPivot.localRotation = pitchBase * Quaternion.Euler(pitch, 0f, 0f);
	}

	private void CaptureCursor()
	{
		savedLock = Cursor.lockState;
		savedVisible = Cursor.visible;
		ownsCursor = true;

		Cursor.lockState = CursorLockMode.Locked;
		Cursor.visible = false;
	}

	private void ReleaseCursor()
	{
		if (!ownsCursor) return;

		Cursor.lockState = savedLock;
		Cursor.visible = savedVisible;
		ownsCursor = false;
	}

	private void SetBodyHidden(bool hidden)
	{
		if (hidden == bodyHidden || shadowModes == null) return;

		bodyHidden = hidden;
		for (int i = 0; i < bodyRenderers.Length; i++)
			if (bodyRenderers[i] != null)
				bodyRenderers[i].shadowCastingMode = hidden
					? ShadowCastingMode.ShadowsOnly : shadowModes[i];
	}

	private void OnApplicationFocus(bool focused)
	{
		if (!focused) ReleaseCursor();
	}

	private void OnDisable()
	{
		ReleaseCursor();
		SetBodyHidden(false);
	}
}
