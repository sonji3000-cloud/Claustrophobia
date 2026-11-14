using UnityEngine;

// Authored-scene test controls. All sensing still runs through the production host AI.
public sealed class TankSensorTest : MonoBehaviour
{
	public enum TestCase { Front, Rear, Far, Wall, Partial, Two, NearerB, SwitchToB, KeepRange, BeyondRange, KeepAngle, BeyondAngle }

	[SerializeField] private TankTestMenu menu;
	[SerializeField] private TankAttackDemo demo;
	[SerializeField] private Transform spawnPoint;
	[SerializeField] private TankTarget targetA;
	[SerializeField] private TankTarget targetB;
	[SerializeField] private Transform wall;
	[SerializeField] private Camera testCamera;

	private bool requested;
	private bool active;
	private Vector3 posA, posB, cameraPos;
	private Quaternion rotA, rotB, cameraRot;
	private bool activeA, activeB, wasOrthographic;
	private float cameraSize;

	public TankSensor Sensor { get; private set; }
	public bool IsRunning => active && demo != null && demo.CanRunTests && demo.IsSensorTest && Sensor != null;
	public bool IsWaiting => requested;
	public TestCase Case { get; private set; }
	public TankTarget TargetA => targetA;
	public TankTarget TargetB => targetB;
	public Transform Wall => wall;
	public TankAttackDemo Demo => demo;

	public void StartTest()
	{
		if (!Application.isPlaying || menu == null || demo == null || spawnPoint == null
			|| targetA == null || targetB == null || wall == null || testCamera == null) return;

		if (active) EndTest();
		requested = true;
		if (menu.Mode != TankTestMenu.TestMode.AI) menu.ShowAI();
	}

	private void Update()
	{
		if (requested && menu.Mode != TankTestMenu.TestMode.AI) requested = false;
		if (requested && demo.CanRunTests && !demo.IsStarting)
		{
			requested = false;
			BeginTest();
		}
		if (active && (!demo.isActiveAndEnabled || !demo.IsSensorTest)) EndTest();
	}

	private void BeginTest()
	{
		posA = targetA.transform.position;
		posB = targetB.transform.position;
		rotA = targetA.transform.rotation;
		rotB = targetB.transform.rotation;
		activeA = targetA.gameObject.activeSelf;
		activeB = targetB.gameObject.activeSelf;
		if (!demo.PrepareSensorTest(spawnPoint)) return;

		Sensor = demo.Enemy.Sensor;
		active = true;
		cameraPos = testCamera.transform.position;
		cameraRot = testCamera.transform.rotation;
		wasOrthographic = testCamera.orthographic;
		cameraSize = testCamera.orthographicSize;
		Vector3 center = spawnPoint.position + new Vector3(0f, 0f, 15f);
		Vector3 view = new Vector3(34f, 35f, -38f);
		testCamera.transform.SetPositionAndRotation(center + view, Quaternion.LookRotation(-view));
		testCamera.orthographic = true;
		testCamera.orthographicSize = 34f;
		SetCase(TestCase.Front);
	}

	public void SetCase(TestCase test)
	{
		if (!IsRunning) return;

		Case = test;
		wall.gameObject.SetActive(false);
		targetA.gameObject.SetActive(true);
		targetB.gameObject.SetActive(false);
		Place(targetA.transform, new Vector3(0f, 0f, 20f));

		switch (test)
		{
			case TestCase.Front:
				Sensor.ClearState();
				break;
			case TestCase.Rear:
				Place(targetA.transform, new Vector3(0f, 0f, -20f)); break;
			case TestCase.Far:
			case TestCase.BeyondRange:
				Place(targetA.transform, new Vector3(0f, 0f, 60f)); break;
			case TestCase.Wall:
			case TestCase.Partial:
				wall.gameObject.SetActive(true);
				float height = test == TestCase.Partial ? 2.15f : 3.5f;
				Place(wall, new Vector3(0f, height * 0.5f, 10f));
				wall.localScale = new Vector3(8f, height, 1f);
				break;
			case TestCase.Two:
			case TestCase.NearerB:
			case TestCase.SwitchToB:
				Place(targetA.transform, test == TestCase.SwitchToB ? new Vector3(0f, 0f, -20f) : new Vector3(-6f, 0f, 22f));
				targetB.gameObject.SetActive(true);
				Place(targetB.transform, test == TestCase.Two ? new Vector3(8f, 0f, 28f) : new Vector3(6f, 0f, 12f));
				if (test == TestCase.Two) Sensor.ClearState();
				break;
			case TestCase.KeepRange:
				Place(targetA.transform, new Vector3(0f, 0f, 45f)); break;
			case TestCase.KeepAngle:
			case TestCase.BeyondAngle:
				float angle = test == TestCase.KeepAngle ? 65f : 85f;
				Place(targetA.transform, Quaternion.Euler(0f, angle, 0f) * Vector3.forward * 24f); break;
		}
		Physics.SyncTransforms();
	}

	private void Place(Transform item, Vector3 offset)
	{
		Vector3 pos = spawnPoint.position;
		pos.y = 0f;
		item.SetPositionAndRotation(pos + spawnPoint.rotation * offset, spawnPoint.rotation);
	}

	public void EndTest()
	{
		requested = false;
		if (!active) return;

		active = false;
		if (Sensor != null) Sensor.SetLocalTargets(false);
		if (targetA != null)
		{
			targetA.transform.SetPositionAndRotation(posA, rotA);
			targetA.gameObject.SetActive(activeA);
		}
		if (targetB != null)
		{
			targetB.transform.SetPositionAndRotation(posB, rotB);
			targetB.gameObject.SetActive(activeB);
		}
		if (wall != null) wall.gameObject.SetActive(false);
		if (demo != null && demo.IsSensorTest)
		{
			demo.EndSensorTest();
			if (testCamera != null)
			{
				testCamera.transform.SetPositionAndRotation(cameraPos, cameraRot);
				testCamera.orthographic = wasOrthographic;
				testCamera.orthographicSize = cameraSize;
			}
		}
		Sensor = null;
	}

	private void OnDisable() => EndTest();
}
