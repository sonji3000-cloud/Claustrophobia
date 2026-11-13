using UnityEngine;
using UnityEngine.UI;

public sealed class TankGroundDrivePanel : MonoBehaviour
{
	[SerializeField] private TankGroundDriveDemo demo;
	[SerializeField] private Text selection;
	[SerializeField] private Text state;
	[SerializeField] private Text readings;
	[SerializeField] private Text cameraLabel;
	[SerializeField] private Image tiger;
	[SerializeField] private Image sherman;
	[SerializeField] private Image[] terrainButtons;
	[SerializeField] private Text[] captions;

	private Font font;
	private float nextRefresh;
	private string lastText;
	private static readonly string[] Names = { "평지", "5° 경사", "10° 경사", "15° 경사", "옆경사", "둔덕", "공중 낙하", "가장자리", "먼 지면" };
	private static readonly Color Selected = new Color(0.2f, 0.65f, 0.5f);
	private static readonly Color Normal = new Color(0.2f, 0.25f, 0.31f);

	private void Awake()
	{
		// Use the installed Korean font for this PC-only test overlay, without bundling an OS font.
		font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Arial" }, 20);
		if (font == null) return;
		for (int i = 0; i < captions.Length; i++)
			if (captions[i] != null) captions[i].font = font;
	}

	private void Update()
	{
		if (demo == null || Time.unscaledTime < nextRefresh) return;
		nextRefresh = Time.unscaledTime + 0.1f;
		TankMotor motor = demo.ActiveMotor;
		if (motor == null || motor.Ground == null) return;

		selection.text = (demo.UsesTiger ? "TIGER · 노란 전차" : "SHERMAN · 흰 전차") + "  /  " + Names[demo.CaseIndex];
		tiger.color = demo.UsesTiger ? Selected : Normal;
		sherman.color = demo.UsesTiger ? Normal : Selected;
		for (int i = 0; i < terrainButtons.Length; i++)
			terrainButtons[i].color = i == demo.CaseIndex ? Selected : Normal;
		cameraLabel.text = demo.FollowsTank ? "카메라: 따라가기" : "카메라: 고정";

		if (!demo.HasInputFocus) state.text = "Game 화면을 클릭하면 키보드로 조작할 수 있어요";
		else if (!motor.Ground.IsGrounded) state.text = "공중 · 착지하면 주행을 다시 할 수 있어요";
		else if (!motor.CanDrive) state.text = "허용 경사를 넘었거나 주행 설정을 확인해야 해요";
		else state.text = demo.Source == TankGroundDriveDemo.ControlSource.None ? "준비 완료 · WASD 또는 아래 운전 버튼" :
			demo.Source == TankGroundDriveDemo.ControlSource.Mouse ? "마우스로 운전 중 · 버튼을 놓으면 감속" :
			demo.Source == TankGroundDriveDemo.ControlSource.Timed ? "2초 전진 시험 중 · 끝나면 자동 감속" : "키보드로 운전 중";

		string text = $"입력  전후 {demo.MoveInput:+0.0;-0.0;0.0} / 선회 {demo.TurnInput:+0.0;-0.0;0.0}\n"
			+ $"모터  왼쪽 {motor.LeftInput:+0.0;-0.0;0.0} / 오른쪽 {motor.RightInput:+0.0;-0.0;0.0}\n"
			+ $"실제 속도 {demo.ActualSpeed:0.00} m/s   이동 {demo.Distance:0.00} m\n"
			+ $"경사 {motor.Ground.SlopeAngle:0.0}°   접지 {(motor.Ground.IsGrounded ? "YES" : "NO")} ({motor.Ground.ContactCount}/5)";
		if (text == lastText) return;
		lastText = text;
		readings.text = text;
	}

	private void OnDestroy()
	{
		if (font != null) Destroy(font);
	}
}
