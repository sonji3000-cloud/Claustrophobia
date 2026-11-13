using System;
using UnityEngine;
using UnityEngine.InputSystem;

// Authored local fixtures use the same ground probe and motor as the real tanks.
public sealed class TankGroundDriveDemo : MonoBehaviour
{
	[Serializable]
	public struct TestTank
	{
		public TankMotor Motor;
		public Rigidbody Body;
		public BoxCollider Hull;
	}

	public enum ControlSource { None, Keyboard, Mouse, Timed }

	[SerializeField] private TestTank tiger;
	[SerializeField] private TestTank sherman;
	[SerializeField] private Transform cases;
	[SerializeField] private Terrain terrain;
	[SerializeField] private Camera driveCamera;

	private bool useTiger = true;
	private bool reversed;
	private Vector2 pad;
	private int guiKeys;
	private float tapUntil;
	private Vector2 tap;
	private float autoUntil;
	private Vector3 startPos;

#if UNITY_EDITOR
	private static readonly Type gameViewType = typeof(UnityEditor.EditorWindow).Assembly.GetType("UnityEditor.GameView");
#endif

	public TankMotor ActiveMotor => useTiger ? tiger.Motor : sherman.Motor;
	public bool UsesKeyboard { get; private set; }
	public bool UsesTiger => useTiger;
	public bool FollowsTank { get; private set; }
	public int CaseIndex { get; private set; }
	public int CaseCount => cases != null ? cases.childCount : 0;
	public float MoveInput { get; private set; }
	public float TurnInput { get; private set; }
	public ControlSource Source { get; private set; }
	public string CaseName(int index) => cases.GetChild(index).name;
	public TestTank ReadTank(int index) => index == 0 ? tiger : sherman;
	public float Distance => ActiveMotor != null ? Vector3.Distance(startPos, (useTiger ? tiger.Body : sherman.Body).position) : 0f;
	public float ActualSpeed => ActiveMotor != null
		? Vector3.Dot((useTiger ? tiger.Body : sherman.Body).linearVelocity, ActiveMotor.transform.forward) : 0f;

	public bool HasInputFocus
	{
		get
		{
			if (!Application.isFocused) return false;
#if UNITY_EDITOR
			return gameViewType != null && gameViewType.IsInstanceOfType(UnityEditor.EditorWindow.focusedWindow);
#else
			return true;
#endif
		}
	}

	private void Start()
	{
		Place(0);
		SetKeyboard(true);
	}

	private void Update()
	{
		if (!UsesKeyboard || ActiveMotor == null) return;
		if (!HasInputFocus)
		{
			if (Source != ControlSource.None || guiKeys != 0 || pad != Vector2.zero || autoUntil > 0f || tapUntil > 0f) StopTanks();
			return;
		}

		Keyboard keyboard = Keyboard.current;
		if (ReadKey(keyboard, Key.Space, KeyCode.Space, 16))
		{
			StopTanks();
			return;
		}

		float move = (ReadKey(keyboard, Key.W, KeyCode.W, 1) || ReadKey(keyboard, Key.UpArrow, KeyCode.UpArrow, 1) ? 1f : 0f)
			- (ReadKey(keyboard, Key.S, KeyCode.S, 2) || ReadKey(keyboard, Key.DownArrow, KeyCode.DownArrow, 2) ? 1f : 0f);
		float turn = (ReadKey(keyboard, Key.D, KeyCode.D, 8) || ReadKey(keyboard, Key.RightArrow, KeyCode.RightArrow, 8) ? 1f : 0f)
			- (ReadKey(keyboard, Key.A, KeyCode.A, 4) || ReadKey(keyboard, Key.LeftArrow, KeyCode.LeftArrow, 4) ? 1f : 0f);
		Source = ControlSource.None;
		if (move != 0f || turn != 0f) Source = ControlSource.Keyboard;
		else if (pad != Vector2.zero)
		{
			move = pad.x;
			turn = pad.y;
			Source = ControlSource.Mouse;
		}
		else if (Time.unscaledTime < tapUntil)
		{
			move = tap.x;
			turn = tap.y;
			Source = ControlSource.Keyboard;
		}
		else if (Time.time < autoUntil)
		{
			move = 1f;
			Source = ControlSource.Timed;
		}

		MoveInput = move;
		TurnInput = turn;
		ActiveMotor.SetLeverInputs(Mathf.Clamp(move + turn, -1f, 1f), Mathf.Clamp(move - turn, -1f, 1f));
	}

	private bool ReadKey(Keyboard keyboard, Key key, KeyCode legacy, int bit)
	{
		bool pressed = (guiKeys & bit) != 0 || (keyboard != null && keyboard[key].isPressed);
#if ENABLE_LEGACY_INPUT_MANAGER
		pressed |= Input.GetKey(legacy);
#endif
		return pressed;
	}

	// The Game View key event also reaches short taps and editor/IME input paths.
	// UI is authored in the scene; this method only reads keyboard events.
	private void OnGUI()
	{
		if (!UsesKeyboard || !HasInputFocus) return;
		Event e = Event.current;
		if (e.type != EventType.KeyDown && e.type != EventType.KeyUp) return;
		int bit = KeyBit(e.keyCode);
		if (bit != 0)
		{
			if (e.type == EventType.KeyDown)
			{
				guiKeys |= bit;
				tap = new Vector2(((guiKeys & 1) != 0 ? 1f : 0f) - ((guiKeys & 2) != 0 ? 1f : 0f),
					((guiKeys & 8) != 0 ? 1f : 0f) - ((guiKeys & 4) != 0 ? 1f : 0f));
				tapUntil = Time.unscaledTime + 0.12f;
			}
			else guiKeys &= ~bit;
			if (bit == 16) StopTanks();
			e.Use();
			return;
		}
		if (e.type != EventType.KeyDown) return;
		if (e.keyCode == KeyCode.Tab) SelectTank(!useTiger);
		else if (e.keyCode == KeyCode.R) Restart();
		else if (e.keyCode >= KeyCode.Alpha1 && e.keyCode <= KeyCode.Alpha6) Place((int)e.keyCode - (int)KeyCode.Alpha1);
		else return;
		e.Use();
	}

	private static int KeyBit(KeyCode key)
	{
		switch (key)
		{
			case KeyCode.W: case KeyCode.UpArrow: return 1;
			case KeyCode.S: case KeyCode.DownArrow: return 2;
			case KeyCode.A: case KeyCode.LeftArrow: return 4;
			case KeyCode.D: case KeyCode.RightArrow: return 8;
			case KeyCode.Space: return 16;
			default: return 0;
		}
	}

	private void LateUpdate()
	{
		if (FollowsTank && driveCamera != null && driveCamera.enabled && ActiveMotor != null)
			FrameCamera(ActiveMotor.transform.position);
	}

	public void SelectTank(bool selectTiger)
	{
		StopTanks();
		useTiger = selectTiger;
		startPos = (useTiger ? tiger.Body : sherman.Body).position;
		if (FollowsTank) ShowCamera();
	}

	public void SetKeyboard(bool enabled)
	{
		StopTanks();
		UsesKeyboard = Application.isPlaying && enabled;
		if (UsesKeyboard) ShowCamera();
	}

	public void SetPad(Vector2 input)
	{
		pad = input;
		guiKeys = 0;
		tapUntil = autoUntil = 0f;
	}

	public void RunForward()
	{
		StopTanks();
		autoUntil = Time.time + 2f;
	}

	public void ToggleCamera()
	{
		FollowsTank = !FollowsTank;
		ShowCamera();
	}

	public void ShowCamera()
	{
		if (driveCamera == null) return;
		driveCamera.enabled = true;
		FrameCamera(FollowsTank ? ActiveMotor.transform.position : (tiger.Body.position + sherman.Body.position) * 0.5f);
	}

	private void FrameCamera(Vector3 center)
	{
		Vector3 offset = new Vector3(14f, 16f, -20f);
		Quaternion rot = Quaternion.LookRotation(-offset);
		Vector3 target = center + Vector3.up - rot * Vector3.right * 4f;
		driveCamera.transform.SetPositionAndRotation(target + offset, rot);
		driveCamera.orthographicSize = 14f;
	}

	public void StopTanks()
	{
		pad = Vector2.zero;
		guiKeys = 0;
		tapUntil = autoUntil = 0f;
		MoveInput = TurnInput = 0f;
		Source = ControlSource.None;
		if (tiger.Motor != null) tiger.Motor.Stop();
		if (sherman.Motor != null) sherman.Motor.Stop();
	}

	public void Place(int index)
	{
		if (index < 0 || index >= CaseCount || terrain == null) return;
		CaseIndex = index;
		reversed = false;
		Restart();
	}

	public void ReverseStart()
	{
		reversed = !reversed;
		Restart();
	}

	public void Restart()
	{
		StopTanks();
		Transform station = cases.GetChild(CaseIndex);
		PlaceTank(tiger, station.GetChild(0));
		PlaceTank(sherman, station.GetChild(1));
		Physics.SyncTransforms();
		startPos = (useTiger ? tiger.Body : sherman.Body).position;
		ShowCamera();
	}

	private void PlaceTank(TestTank tank, Transform marker)
	{
		if (tank.Motor == null || tank.Body == null || tank.Hull == null) return;
		Quaternion rot = Quaternion.Euler(0f, marker.eulerAngles.y + (reversed ? 180f : 0f), 0f);
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
	private void OnApplicationFocus(bool focused)
	{
		if (!focused) StopTanks();
	}

	private void OnDisable()
	{
		UsesKeyboard = false;
		StopTanks();
	}
}
