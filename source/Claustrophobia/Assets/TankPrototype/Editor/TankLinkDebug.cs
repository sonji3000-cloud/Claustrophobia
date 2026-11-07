using System;
using UnityEditor;
using UnityEngine;

/// <summary>Manual session link checks. Never saves scene or prefab assets.</summary>
public static class TankLinkDebug
{
	private const string BindPath = "Tools/Tank/Scene Link/Bind Session APIs (Play Mode)";
	private const string UnbindPath = "Tools/Tank/Scene Link/Unbind Session APIs (Play Mode)";
	private const string ReportPath = "Tools/Tank/Scene Link/Report Presentation Bindings (Play Mode)";
	private const string SessionPath = "Tools/Tank/Scene Link/Report Session Readiness (Play Mode)";

	[MenuItem(SessionPath)]
	public static void ReportSession()
	{
		TankSceneLink link = GetLink();
		SessionJoinInit session = link.Session;
		Debug.Log($"[TankLinkDebug] Stage={link.CurrentStage}, Ready={link.IsReady}, "
			+ $"Session={session.CurrentState}, Started={session.StartSucceeded}, SceneLoaded={session.HasSceneLoaded}, "
			+ $"SceneReady={session.IsSceneReady}, Target={session.TargetScenePath}, "
			+ $"JoystickAimDeferred={link.JoystickAimDeferred}, Error={link.LastError ?? session.LastError}.", link);
	}

	[MenuItem(ReportPath)]
	public static void ReportPresentation()
	{
		TankSceneLink link = GetLink();
		var fields = new SerializedObject(link.Interior);
		var driver = (DriverScreen)fields.FindProperty("driverScreen").objectReferenceValue;
		var breech = (TankBreech)fields.FindProperty("breech").objectReferenceValue;
		var joystick = (TankJoystick)fields.FindProperty("joystick").objectReferenceValue;
		var hud = (CrewInteractHud)fields.FindProperty("interactionHud").objectReferenceValue;
		var external = new SerializedObject(link);
		var tankHud = (TankKeyboardDebug)external.FindProperty("tankHud").objectReferenceValue;

		Debug.Log($"[TankLinkDebug] U05 references: DriverFeed={driver != null && driver.Feed != null}, "
			+ $"GunnerFeed={link.Interior.Sight != null && link.Interior.Sight.Feed != null}, BreechGun={breech != null && breech.Gun != null}, "
			+ $"JoystickControl={joystick != null && joystick.Control != null}, JoystickAim={joystick != null && joystick.Aim != null}, "
			+ $"HudControl={hud != null && hud.Control != null}, HudCrew={tankHud != null && tankHud.Crew != null}. "
			+ "Read-only report; no reference is repaired or rebound.", link);
	}

	[MenuItem(BindPath)]
	public static void BindInput()
	{
		TankSceneLink link = GetLink();
		if (!link.Bind(link.Interior))
			throw new InvalidOperationException(link.LastError ?? "The session or designated tank is not ready.");
		Debug.Log($"[TankLinkDebug] U06 Stage={link.CurrentStage}, Ready={link.IsReady}, JoystickAimDeferred={link.JoystickAimDeferred}.", link);
	}

	[MenuItem(UnbindPath)]
	public static void UnbindInput()
	{
		TankSceneLink link = GetLink();
		link.Unbind();
		Debug.Log("[TankLinkDebug] U06 connection released; input and output remain blocked until an explicit Bind or scene restart.", link);
	}

	[MenuItem(BindPath, true)]
	[MenuItem(UnbindPath, true)]
	[MenuItem(ReportPath, true)]
	[MenuItem(SessionPath, true)]
	private static bool CanDebug()
	{
		GameObject selected = Selection.activeGameObject;
		return EditorApplication.isPlaying && selected != null && selected.scene.IsValid()
			&& selected.GetComponent<TankSceneLink>() != null;
	}

	private static TankSceneLink GetLink()
	{
		if (!CanDebug()) throw new InvalidOperationException("In Play Mode, select the scene object with TankSceneLink (NetworkAsset in TankTest).");
		return Selection.activeGameObject.GetComponent<TankSceneLink>();
	}
}
