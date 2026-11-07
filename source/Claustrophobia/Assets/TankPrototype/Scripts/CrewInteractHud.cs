using UnityEngine;
using UnityEngine.UI;

/// <summary>Uses authored UI; diagnostic rows show the selector's actual results.</summary>
[DefaultExecutionOrder(20)]
public sealed class CrewInteractHud : MonoBehaviour
{
	[SerializeField] private CrewInteract interaction;
	[SerializeField] private CrewDebugView debugView;
	[SerializeField] private TankControl control;
	[SerializeField] private RectTransform reticle;
	[SerializeField] private RectTransform ring;
	[SerializeField] private RectTransform retainRing;
	[SerializeField] private Text prompt;
	[SerializeField] private Text[] rows;
	[SerializeField] private RectTransform[] markers;

	private string[] readyText;
	private string[] activeText;
	private string[] heldText;
	private string[,] reasons;
	private string fireReason;
	private string fireText;
	private bool usingJoystick;
	private static readonly string[] ReasonText = { "Ready", "Disabled", "Wrong role", "In use", "Too far / no surface in range", "Behind camera", "Outside center", "Occluded", "Wrong layer / no active collider" };

	public TankControl Control => control;

	public bool Bind(TankControl next)
	{
		if (next == null) return false;
		if (control == next) return true;

		control = next;
		ClearFireText();
		if (isActiveAndEnabled) RefreshHud();
		return true;
	}

	public void Unbind()
	{
		control = null;
		ClearFireText();
		if (isActiveAndEnabled) RefreshHud();
	}

	private void ClearFireText()
	{
		fireReason = null;
		fireText = null;
		usingJoystick = false;
	}

	private void Awake()
	{
		CrewDevice[] devices = interaction.Devices;
		readyText = new string[devices.Length];
		activeText = new string[devices.Length];
		heldText = new string[devices.Length];
		reasons = new string[devices.Length, ReasonText.Length];
		for (int i = 0; i < devices.Length; i++)
		{
			string label = devices[i] != null ? devices[i].Label : "Missing device";
			readyText[i] = "Space - " + (devices[i] != null && devices[i].Round != null ? "Pick up " : "Use ") + label;
			activeText[i] = label + " in use | Space / Esc - Exit";
			heldText[i] = "Carrying " + label + " | Esc - Return to rack";
			for (int j = 0; j < ReasonText.Length; j++) reasons[i, j] = label + ": " + ReasonText[j];
		}
	}

	private void OnEnable() => RefreshHud();

	private void LateUpdate() => RefreshHud();

	private void RefreshHud()
	{
		if (interaction == null || interaction.View == null || interaction.Candidates == null || readyText == null) return;

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
		TankRound held = interaction.Carry != null ? interaction.Carry.Held : null;
		for (int i = 0; i < interaction.Devices.Length; i++)
		{
			CrewDevice device = interaction.Devices[i];
			if (held != null && device != null && device.Round == held) text = heldText[i];
			else if (held == null && device != null && device == interaction.Active) text = activeText[i];
			else if (held == null && interaction.Active == null && device != null && device == interaction.Selected) text = readyText[i];

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

		CrewDevice selected = interaction.Selected;
		if (interaction.Active == null && selected != null && selected.Breech != null)
			text = selected.Breech.GetPrompt(interaction);

		bool showFire = control != null && control.AimDevice != null
			&& (interaction.Active == control.AimDevice || interaction.Active == null && selected == control.AimDevice);
		Color color = Color.white;
		if (showFire)
		{
			string reason = control.GetFireBlock(interaction);
			bool active = interaction.Active == control.AimDevice;
			if (fireText == null || fireReason != reason || usingJoystick != active)
			{
				fireReason = reason;
				usingJoystick = active;
				string keys = active
					? "Turret controls | Arrows - Aim | Wheel - Zoom | Space / Esc - Exit\n"
					: "Space - Use turret controls\n";
				fireText = keys + (reason.Length == 0 ? "FIRE READY | Enter - Fire one round" : "FIRE BLOCKED: " + reason);
			}
			text = fireText;
			color = reason.Length == 0 ? new Color(.4f, 1f, .5f) : new Color(1f, .85f, .35f);
		}

		if (prompt.text != text) prompt.text = text;
		if (prompt.color != color) prompt.color = color;
	}
}
