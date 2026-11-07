using System;
using UnityEditor;
using UnityEngine;

/// <summary>Manual U04 API checks on the selected link. Never saves scene or prefab assets.</summary>
public static class TankLinkDebug
{
	private const string BindPath = "Tools/Tank/Scene Link/Bind Input APIs (Play Mode)";
	private const string UnbindPath = "Tools/Tank/Scene Link/Unbind Input APIs (Play Mode)";

	[MenuItem(BindPath)]
	public static void BindInput()
	{
		TankSceneLink link = GetLink();
		if (!link.ValidateRefs(out string reason) || !link.PrepareInterior())
			throw new InvalidOperationException(reason ?? "The active interior could not be prepared.");

		var fields = new SerializedObject(link);
		var control = (TankControl)fields.FindProperty("control").objectReferenceValue;
		var input = (TankInput)fields.FindProperty("input").objectReferenceValue;
		var cameras = (TankCrewCameras)fields.FindProperty("cameras").objectReferenceValue;
		var hud = (TankKeyboardDebug)fields.FindProperty("tankHud").objectReferenceValue;
		TankInterior interior = link.Interior;
		control.SetInputEnabled(false);

		bool bound = control.Bind(interior.Crew, interior.DriveDevice, interior.AimDevice, interior.Sight)
			&& input.Bind(interior.Seats, interior.Interaction, interior.Mode)
			&& interior.Mode.Bind(input, cameras, hud) && interior.Mode.ApplyMode();
		if (!bound)
		{
			interior.Mode.Unbind();
			input.Unbind();
			control.Unbind();
			throw new InvalidOperationException("Input API binding failed; input remains blocked.");
		}

		control.SetInputEnabled(true);
		Debug.Log($"[TankLinkDebug] U04 input APIs bound. Control={control.IsBound}, Input={input.IsBound}, Mode={interior.Mode.IsBound}, CanInput={control.CanInput}. Release held keys before operating. Device/feed binding and scene loading remain outside this check.", link);
	}

	[MenuItem(UnbindPath)]
	public static void UnbindInput()
	{
		TankSceneLink link = GetLink();
		var fields = new SerializedObject(link);
		var control = (TankControl)fields.FindProperty("control").objectReferenceValue;
		var input = (TankInput)fields.FindProperty("input").objectReferenceValue;
		if (control != null) control.SetInputEnabled(false);
		if (link.Interior != null && link.Interior.Mode != null) link.Interior.Mode.Unbind();
		if (input != null) input.Unbind();
		if (control != null) control.Unbind();
		Debug.Log("[TankLinkDebug] U04 input APIs unbound; input and screen output are suspended. Use Bind Input APIs to reconnect, or exit Play Mode.", link);
	}

	[MenuItem(BindPath, true)]
	[MenuItem(UnbindPath, true)]
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
