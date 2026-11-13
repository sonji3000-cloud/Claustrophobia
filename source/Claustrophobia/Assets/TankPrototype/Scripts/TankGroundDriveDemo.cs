using System;
using UnityEngine;
using UnityEngine.InputSystem;

// Fixed scene samples that exercise the production motor without crew/network inputs.
public sealed class TankGroundDriveDemo : MonoBehaviour
{
	[Serializable]
	public struct TestTank
	{
		public TankMotor Motor;
		public Rigidbody Body;
		public BoxCollider Hull;
	}

	[SerializeField] private TestTank tiger;
	[SerializeField] private TestTank sherman;
	[SerializeField] private Transform cases;
	[SerializeField] private Terrain terrain;
	[SerializeField] private Camera driveCamera;

	private bool useTiger = true;

	public TankMotor ActiveMotor => useTiger ? tiger.Motor : sherman.Motor;
	public bool UsesKeyboard { get; private set; }
	public int CaseCount => cases != null ? cases.childCount : 0;
	public string CaseName(int index) => cases.GetChild(index).name;
	public TestTank ReadTank(int index) => index == 0 ? tiger : sherman;

	private void Update()
	{
		if (!UsesKeyboard || ActiveMotor == null) return;
		Keyboard keyboard = Keyboard.current;
		if (keyboard == null) return;
		if (keyboard.spaceKey.isPressed)
		{
			ActiveMotor.Stop();
			return;
		}

		float move = (keyboard.wKey.isPressed ? 1f : 0f) - (keyboard.sKey.isPressed ? 1f : 0f);
		float turn = (keyboard.dKey.isPressed ? 1f : 0f) - (keyboard.aKey.isPressed ? 1f : 0f);
		ActiveMotor.SetLeverInputs(Mathf.Clamp(move + turn, -1f, 1f),
			Mathf.Clamp(move - turn, -1f, 1f));
	}

	private void LateUpdate()
	{
		if (driveCamera == null || !driveCamera.enabled || ActiveMotor == null) return;

		Vector3 pos = ActiveMotor.transform.position;
		driveCamera.transform.SetPositionAndRotation(pos + new Vector3(13f, 11f, -17f),
			Quaternion.LookRotation(new Vector3(-13f, -8f, 17f)));
	}

	public void SelectTank(bool selectTiger)
	{
		StopTanks();
		useTiger = selectTiger;
	}

	public void SetKeyboard(bool enabled)
	{
		StopTanks();
		UsesKeyboard = Application.isPlaying && enabled;
		if (UsesKeyboard) ShowCamera();
	}

	public void ShowCamera()
	{
		if (driveCamera != null) driveCamera.enabled = true;
	}

	public void StopTanks()
	{
		if (tiger.Motor != null) tiger.Motor.Stop();
		if (sherman.Motor != null) sherman.Motor.Stop();
	}

	public void Place(int index)
	{
		if (index < 0 || index >= CaseCount || terrain == null) return;

		Transform station = cases.GetChild(index);
		PlaceTank(tiger, station.GetChild(0));
		PlaceTank(sherman, station.GetChild(1));
		Physics.SyncTransforms();
	}

	private void PlaceTank(TestTank tank, Transform marker)
	{
		if (tank.Motor == null || tank.Body == null || tank.Hull == null) return;

		tank.Motor.Stop();
		Quaternion rot = Quaternion.Euler(0f, marker.eulerAngles.y, 0f);
		Vector3 pos = marker.position;
		Vector3 bottom = marker.position + marker.rotation * tank.Motor.Ground.Center;
		float lift = Mathf.Max(0f, bottom.y - GroundHeight(bottom));
		Vector3 center = tank.Hull.center - Vector3.up * (tank.Hull.size.y * 0.5f);
		float height = GroundHeight(pos);
		for (int i = 0; i < 4; i++)
		{
			Vector3 corner = pos + rot * (center + new Vector3(
				(i & 1) == 0 ? -tank.Hull.size.x * 0.5f : tank.Hull.size.x * 0.5f,
				0f, i < 2 ? -tank.Hull.size.z * 0.5f : tank.Hull.size.z * 0.5f));
			height = Mathf.Max(height, GroundHeight(corner));
		}
		pos.y = height - center.y + 0.04f + lift;
		tank.Body.position = pos;
		tank.Body.rotation = rot;
		tank.Body.linearVelocity = Vector3.zero;
		tank.Body.angularVelocity = Vector3.zero;
		tank.Body.WakeUp();
	}

	private float GroundHeight(Vector3 pos) => terrain.SampleHeight(pos) + terrain.transform.position.y;

	private void OnDisable()
	{
		UsesKeyboard = false;
		StopTanks();
	}
}
