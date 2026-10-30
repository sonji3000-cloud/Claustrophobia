using UnityEngine;
using UnityEngine.UI;

/// <summary>Uses authored UI; diagnostic rows show the selector's actual results.</summary>
[DefaultExecutionOrder(20)]
public sealed class CrewInteractHud : MonoBehaviour
{
	[SerializeField] private CrewInteract interaction;
	[SerializeField] private CrewDebugView debugView;
	[SerializeField] private RectTransform reticle;
	[SerializeField] private RectTransform ring;
	[SerializeField] private RectTransform retainRing;
	[SerializeField] private Text prompt;
	[SerializeField] private Text[] rows;
	[SerializeField] private RectTransform[] markers;

	private string[] readyText;
	private string[] activeText;
	private string[,] reasons;
	private static readonly string[] ReasonText = { "Ready", "Disabled", "Wrong role", "In use", "Too far / no surface in range", "Behind camera", "Outside center", "Occluded" };

	private void Awake()
	{
		CrewDevice[] devices = interaction.Devices;
		readyText = new string[devices.Length];
		activeText = new string[devices.Length];
		reasons = new string[devices.Length, ReasonText.Length];
		for (int i = 0; i < devices.Length; i++)
		{
			string label = devices[i] != null ? devices[i].Label : "Missing device";
			readyText[i] = "Space - Use " + label;
			activeText[i] = label + " in use | Space / Esc - Exit";
			for (int j = 0; j < ReasonText.Length; j++) reasons[i, j] = label + ": " + ReasonText[j];
		}
	}

	private void LateUpdate()
	{
		bool debug = debugView != null && debugView.ShowDebug;
		bool observe = debugView != null && debugView.IsObserving;
		Camera view = interaction.View;
		Rect rect = view.pixelRect;
		reticle.gameObject.SetActive(!observe);
		ring.gameObject.SetActive(debug && !observe);
		retainRing.gameObject.SetActive(debug && !observe);
		reticle.position = rect.center;
		ring.position = rect.center;
		retainRing.position = rect.center;
		ring.sizeDelta = Vector2.one * (2f * interaction.Radius * rect.height);
		retainRing.sizeDelta = Vector2.one * (2f * interaction.RetainRadius * rect.height);

		string text = observe ? "Rear view | Mouse - Look | F7 - First person" : "";
		for (int i = 0; i < interaction.Devices.Length; i++)
		{
			CrewDevice device = interaction.Devices[i];
			if (device != null && device == interaction.Active) text = activeText[i];
			else if (interaction.Active == null && device != null && device == interaction.Selected) text = readyText[i];

			CrewInteract.Candidate candidate = interaction.Candidates[i];
			if (i < rows.Length && rows[i] != null)
			{
				rows[i].gameObject.SetActive(debug);
				string row = reasons[i, (int)candidate.Reason];
				if (rows[i].text != row) rows[i].text = row;
				rows[i].color = device != null && (device == interaction.Selected || device == interaction.Active) ? Color.white : Color.gray;
			}

			if (i >= markers.Length || markers[i] == null) continue;
			Vector3 screen = view.WorldToScreenPoint(candidate.Point);
			bool show = debug && !observe && candidate.Reason >= CrewInteract.Reason.Behind && screen.z > 0f && rect.Contains(screen);
			if (candidate.Reason == CrewInteract.Reason.Ready) show = debug && !observe;
			markers[i].gameObject.SetActive(show);
			if (show) markers[i].position = screen;
		}

		if (prompt.text != text) prompt.text = text;
	}
}
