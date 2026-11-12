using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>A read-only health meter and two continuous lever-position indicators.</summary>
[DisallowMultipleComponent]
public sealed class TankStatusPanel : MonoBehaviour
{
	[SerializeField] private CanvasGroup content;
	[SerializeField] private Image hpFill;
	[SerializeField] private TMP_Text hpText;
	[SerializeField] private RectTransform leftHandle;
	[SerializeField] private RectTransform rightHandle;
	[SerializeField] private CanvasGroup hitBorder;

	[SerializeField] private float bottom = -204f;
	[SerializeField] private float top = 42f;
	[SerializeField, Range(0f, 1f)] private float warnAt = 0.5f;
	[SerializeField, Range(0f, 1f)] private float dangerAt = 0.25f;
	[SerializeField] private Color healthy = new Color(0.24f, 0.75f, 0.36f, 1f);
	[SerializeField] private Color warning = new Color(0.95f, 0.58f, 0.16f, 1f);
	[SerializeField] private Color danger = new Color(0.9f, 0.22f, 0.18f, 1f);
	[SerializeField, Min(0f)] private float hitDuration = 0.22f;

	private TankStatus status;
	private TankControl control;
	private float lastRatio = -1f;
	private Vector2 lastLevers = new Vector2(float.NaN, float.NaN);
	private int lastPercent = -1;
	private int lastHits = -1;
	private float hitEndsAt;
	private bool visible;

	public bool IsBound => status != null;

	public bool Bind(TankStatus next, TankControl nextControl)
	{
		if (next == null || nextControl == null || content == null || hpFill == null || hpText == null
			|| leftHandle == null || rightHandle == null || hitBorder == null) return false;

		status = next;
		control = nextControl;
		ResetDisplay();
		if (isActiveAndEnabled) Refresh();
		return true;
	}

	public void Unbind()
	{
		status = null;
		control = null;
		ResetDisplay();
	}

	private void OnEnable() => ResetDisplay();

	private void Update() => Refresh();

	private void Refresh()
	{
		bool ready = status != null && status.IsReady && status.MaxHP > 0f;
		if (visible != ready)
		{
			visible = ready;
			if (content != null) content.alpha = ready ? 1f : 0f;
		}
		if (!ready) return;

		float ratio = Mathf.Clamp01(status.HP / status.MaxHP);
		if (lastRatio != ratio)
		{
			lastRatio = ratio;
			hpFill.fillAmount = ratio;
			hpFill.color = ratio <= dangerAt ? danger : ratio <= warnAt ? warning : healthy;
		}

		int percent = Mathf.RoundToInt(ratio * 100f);
		if (lastPercent != percent)
		{
			lastPercent = percent;
			hpText.SetText("{0}%", percent);
		}

		Vector2 levers = status.GetLevers(control);
		if (lastLevers.x != levers.x) MoveHandle(leftHandle, levers.x);
		if (lastLevers.y != levers.y) MoveHandle(rightHandle, levers.y);
		lastLevers = levers;

		int hits = status.Hits;
		if (lastHits >= 0 && hits > lastHits) hitEndsAt = Time.unscaledTime + hitDuration;
		lastHits = hits;
		bool hit = Time.unscaledTime < hitEndsAt;
		float hitAlpha = hit ? 1f : 0f;
		if (hitBorder.alpha != hitAlpha) hitBorder.alpha = hitAlpha;
	}

	private void MoveHandle(RectTransform handle, float input)
	{
		Vector2 pos = handle.anchoredPosition;
		pos.y = Mathf.Lerp(bottom, top, (Mathf.Clamp(input, -1f, 1f) + 1f) * 0.5f);
		handle.anchoredPosition = pos;
	}

	private void ResetDisplay()
	{
		lastRatio = -1f;
		lastPercent = lastHits = -1;
		lastLevers = new Vector2(float.NaN, float.NaN);
		hitEndsAt = 0f;
		visible = false;
		if (content != null) content.alpha = 0f;
		if (hitBorder != null) hitBorder.alpha = 0f;
	}

	private void OnDisable() => ResetDisplay();
}
