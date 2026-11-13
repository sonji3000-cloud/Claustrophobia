using UnityEngine;
using UnityEngine.EventSystems;

public sealed class TankGroundDrivePad : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
{
	[SerializeField] private TankGroundDriveDemo demo;
	[SerializeField] private Vector2 input;
	private bool held;

	public void OnPointerDown(PointerEventData e)
	{
		if (e.button != PointerEventData.InputButton.Left || demo == null) return;
		held = true;
		demo.SetPad(input);
	}

	public void OnPointerUp(PointerEventData e) => Release();
	public void OnPointerExit(PointerEventData e) => Release();
	private void OnDisable() => Release();

	private void Release()
	{
		if (!held) return;
		held = false;
		if (demo != null) demo.SetPad(Vector2.zero);
	}
}
