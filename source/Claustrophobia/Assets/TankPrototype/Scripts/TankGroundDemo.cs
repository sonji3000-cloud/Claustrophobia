using UnityEngine;

// Authored, parked samples for checking sensors. No motor or AI is exercised here.
public sealed class TankGroundDemo : MonoBehaviour
{
	[SerializeField] private TankGroundProbe tiger;
	[SerializeField] private TankGroundProbe sherman;
	[SerializeField] private Transform cases;
	[SerializeField] private Camera testCamera;

	public TankGroundProbe Tiger => tiger;
	public TankGroundProbe Sherman => sherman;
	public int CaseCount => cases != null ? cases.childCount : 0;
	public int CaseIndex { get; private set; }
	public string CaseName(int index) => cases.GetChild(index).name;

	public void Place(int index)
	{
		if (index < 0 || index >= CaseCount || tiger == null || sherman == null) return;

		Transform station = cases.GetChild(index);
		PlaceProbe(tiger, station.GetChild(0));
		PlaceProbe(sherman, station.GetChild(1));
		CaseIndex = index;
		Physics.SyncTransforms();
		tiger.Sample();
		sherman.Sample();
	}

	public void ShowSamples()
	{
		if (testCamera == null || tiger == null || sherman == null) return;

		Vector3 pos = (tiger.transform.position + sherman.transform.position) * 0.5f;
		testCamera.transform.SetPositionAndRotation(pos + new Vector3(16f, 15f, -20f),
			Quaternion.LookRotation(new Vector3(-16f, -12f, 20f)));
		testCamera.orthographic = true;
		testCamera.orthographicSize = 12f;
	}

	private static void PlaceProbe(TankGroundProbe probe, Transform marker)
	{
		probe.transform.SetPositionAndRotation(marker.position, marker.rotation);
	}
}
