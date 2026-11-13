using UnityEngine;
using UnityEngine.UI;

public sealed class TankTestMenu : MonoBehaviour
{
	public enum TestMode { Menu, AI, Ground }

	[SerializeField] private TankAttackDemo ai;
	[SerializeField] private TankGroundDriveDemo ground;
	[SerializeField] private Camera aiCamera;
	[SerializeField] private Camera groundCamera;
	[SerializeField] private GameObject menuPanel;
	[SerializeField] private GameObject aiPanel;
	[SerializeField] private GameObject groundPanel;
	[SerializeField] private Text aiState;
	[SerializeField] private Button[] aiButtons;
	[SerializeField] private Text[] captions;
	private Font font;
	private float nextRefresh;
#if UNITY_EDITOR
	private UnityEditor.EditorWindow gameWindow;
	private bool wasMaximized;
#endif

	public TestMode Mode { get; private set; }

	private void Awake()
	{
		GetComponent<Canvas>().enabled = true;
		font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Arial" }, 20);
		if (font != null)
			for (int i = 0; i < captions.Length; i++) captions[i].font = font;
		ShowMenu();
	}

	public void ShowMenu() => SelectMode(TestMode.Menu);
	public void ShowAI() => SelectMode(TestMode.AI);
	public void ShowGround() => SelectMode(TestMode.Ground);
	public void RunAIPath(int index) { if (Mode == TestMode.AI) ai.RunPathTest(index); }
	private void Start() => ExpandView();

	private void ExpandView()
	{
#if UNITY_EDITOR
		if (gameWindow != null) return;
		var window = UnityEditor.EditorWindow.focusedWindow;
		if (window == null || window.GetType().Name != "GameView") return;
		gameWindow = window;
		wasMaximized = window.maximized;
		window.maximized = true;
#endif
	}

	private void SelectMode(TestMode mode)
	{
		if (mode != TestMode.Menu) ExpandView();
		ai.enabled = false;
		ground.SetKeyboard(false);
		ground.enabled = false;
		groundCamera.enabled = false;
		aiCamera.enabled = true;
		Mode = mode;
		menuPanel.SetActive(mode == TestMode.Menu);
		aiPanel.SetActive(mode == TestMode.AI);
		groundPanel.SetActive(mode == TestMode.Ground);

		if (mode == TestMode.AI) ai.enabled = true;
		else if (mode == TestMode.Ground)
		{
			ground.enabled = true;
			ground.Place(0);
			ground.SetKeyboard(true);
		}
		nextRefresh = 0f;
	}

	private void Update()
	{
		if (Mode != TestMode.AI || Time.unscaledTime < nextRefresh) return;
		nextRefresh = Time.unscaledTime + 0.1f;
		for (int i = 0; i < aiButtons.Length; i++) aiButtons[i].interactable = ai.CanRunTests;
		if (!ai.CanRunTests)
		{
			aiState.text = "로컬 호스트를 준비하고 있어요…";
			return;
		}
		TankAI enemy = ai.Enemy;
		if (enemy == null) { aiState.text = "시험 구역을 선택하세요"; return; }
		aiState.text = $"구역: {ai.TestName}\n이동: {enemy.MoveStatus} / 공격: {enemy.AttackStatus}\n"
			+ $"재시도: {enemy.RetryCount}/{enemy.RetryLimit}\n"
			+ (ai.TargetHealth != null ? $"표적 HP: {ai.TargetHealth.CurrentHP:0}\n" : "")
			+ (!string.IsNullOrEmpty(enemy.MoveFailure) ? enemy.MoveFailure : enemy.AttackBlockReason);
	}

	private void OnDestroy()
	{
#if UNITY_EDITOR
		if (gameWindow != null) gameWindow.maximized = wasMaximized;
#endif
		if (font != null) Destroy(font);
	}
}
