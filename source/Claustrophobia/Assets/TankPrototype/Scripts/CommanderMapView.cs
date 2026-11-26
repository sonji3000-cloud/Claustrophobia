using UnityEngine;
using UnityEngine.UI;
using Phase = CarentanMission.Phase;

[DisallowMultipleComponent]
public sealed class CommanderMapView : MonoBehaviour
{
	[SerializeField] private CrewInteract interaction;
	[SerializeField] private CrewDevice mapDevice;
	[SerializeField] private TankTestMode mode;
	[SerializeField] private CrewLook look;

	[SerializeField] private GameObject screen;
	[SerializeField] private RawImage map;
	[SerializeField] private AspectRatioFitter mapAspect;
	[SerializeField] private RectTransform ally;
	[SerializeField] private RectTransform goal;
	[SerializeField] private Text heading;
	[SerializeField] private Text targetText;
	[SerializeField] private Text returnText;
	[SerializeField] private GameObject targetCheck;
	[SerializeField] private GameObject returnCheck;

	[SerializeField] private Color highlight = new(1f, .83f, .47f);
	[SerializeField] private Color completed = new(.61f, .9f, .78f);
	[SerializeField] private Color dimmed = new(.61f, .66f, .66f);

	private CarentanDrive drive;
	private Camera mapCamera;
	private Transform tiger;
	private Font mapFont;
	private Phase shownPhase;

	public bool IsOpen => screen != null && screen.activeSelf;

	private bool CanOpen => isActiveAndEnabled && drive != null && mapCamera != null && map.texture != null
		&& mode.CanApply && mode.IsInterior && !mode.IsObserving && !interaction.IsObserving
		&& interaction.View != null && interaction.View.isActiveAndEnabled;

	internal void Bind(CarentanDrive source, Camera camera, Transform player)
	{
		Unbind();
		drive = source;
		mapCamera = camera;
		tiger = player;
		if (camera == null || camera.targetTexture == null) return;

		RenderTexture texture = camera.targetTexture;
		map.texture = texture;
		mapAspect.aspectRatio = (float)texture.width / texture.height;
		camera.aspect = mapAspect.aspectRatio;
	}

	internal void Unbind()
	{
		Close();
		drive = null;
		mapCamera = null;
		tiger = null;
		map.texture = null;
	}

	private void OnEnable() => interaction.UseChanged += OnUseChanged;

	private void OnDisable()
	{
		interaction.UseChanged -= OnUseChanged;
		Close();
	}

	private void OnDestroy()
	{
		if (mapFont != null) Destroy(mapFont);
	}

	private void OnUseChanged(CrewDevice device, bool active)
	{
		if (device != mapDevice || drive == null) return;
		if (!active)
		{
			Hide();
			return;
		}
		if (!CanOpen)
		{
			interaction.StopUse();
			return;
		}

		if (mapFont == null)
		{
			mapFont = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Noto Sans KR", "Arial" }, 32);
			heading.font = targetText.font = returnText.font = mapFont;
		}

		targetText.text = drive.MissionTargetText;
		returnText.text = drive.MissionReturnText;
		ShowPhase();
		look.SetLookPaused(true);
		screen.SetActive(true);
	}

	private void Close()
	{
		if (!IsOpen) return;
		Hide();
		if (interaction.Active == mapDevice) interaction.StopUse();
	}

	private void Hide()
	{
		screen.SetActive(false);
		look.SetLookPaused(false);
	}

	private void LateUpdate()
	{
		if (!IsOpen) return;
		if (!CanOpen || interaction.Active != mapDevice)
		{
			Close();
			return;
		}

		if (shownPhase != drive.MissionPhase) ShowPhase();
		SetMarker(goal, drive.TryGetMissionPoint(out Vector3 point), point);
		SetMarker(ally, tiger != null, tiger != null ? tiger.position : default);
		if (tiger == null) return;

		Vector3 forward = mapCamera.transform.InverseTransformDirection(tiger.forward);
		float angle = Mathf.Atan2(forward.x, forward.y) * Mathf.Rad2Deg;
		ally.localRotation = Quaternion.Euler(0f, 0f, -angle);
	}

	private void ShowPhase()
	{
		shownPhase = drive.MissionPhase;
		bool targetDone = shownPhase is Phase.ReturnSouth or Phase.Succeeded;
		bool targetActive = shownPhase is Phase.EnterTown or Phase.DestroyTarget;
		bool returned = shownPhase == Phase.Succeeded;

		heading.text = shownPhase is Phase.Succeeded or Phase.Failed ? drive.MissionObjective : "임무";
		heading.color = returned ? completed : shownPhase == Phase.Failed ? new Color(1f, .56f, .50f) : Color.white;
		targetText.color = targetDone ? completed : targetActive ? highlight : dimmed;
		returnText.color = returned ? completed : shownPhase == Phase.ReturnSouth ? highlight : dimmed;
		targetCheck.SetActive(targetDone);
		returnCheck.SetActive(returned);
	}

	private void SetMarker(RectTransform marker, bool available, Vector3 world)
	{
		Vector3 point = available ? mapCamera.WorldToViewportPoint(world) : default;
		bool visible = available && point.z > 0f && point.x >= 0f && point.x <= 1f && point.y >= 0f && point.y <= 1f;
		if (marker.gameObject.activeSelf != visible) marker.gameObject.SetActive(visible);
		if (!visible) return;

		Rect rect = map.rectTransform.rect;
		marker.anchoredPosition = new Vector2((point.x - .5f) * rect.width, (point.y - .5f) * rect.height);
	}
}
