using UnityEngine;

/// <summary>Holds the explicit tank/interior endpoints. Session binding is added in a later step.</summary>
[DisallowMultipleComponent]
public sealed class TankSceneLink : MonoBehaviour
{
	public enum Stage { Idle, Prepared }

	[SerializeField] private TankInterior interior;
	[SerializeField] private SessionJoinInit session;
	[SerializeField] private TankControl control;
	[SerializeField] private TankInput input;
	[SerializeField] private TankCrewCameras cameras;
	[SerializeField] private TankKeyboardDebug tankHud;
	[SerializeField] private TankGun gun;
	[SerializeField] private TankAim aim;
	[SerializeField] private Camera driverFeed;
	[SerializeField] private Camera gunnerFeed;

	private Stage stage;

	public Stage CurrentStage => stage;
	public TankInterior Interior => interior;

	public bool ValidateRefs(out string reason)
	{
		if (interior == null || session == null || control == null || input == null || cameras == null
			|| tankHud == null || gun == null || aim == null || driverFeed == null || gunnerFeed == null)
		{
			reason = "TankSceneLink requires an interior and all explicit external endpoints.";
			return false;
		}
		if (input.gameObject != control.gameObject || cameras.gameObject != control.gameObject
			|| tankHud.gameObject != control.gameObject || gun.gameObject != control.gameObject
			|| aim.gameObject != control.gameObject || session.tankControl != control)
		{
			reason = "TankSceneLink endpoints must belong to the designated tank.";
			return false;
		}
		if (driverFeed == gunnerFeed || driverFeed.targetTexture == null || gunnerFeed.targetTexture == null)
		{
			reason = "TankSceneLink requires distinct authored monitor feeds with RenderTextures.";
			return false;
		}

		return interior.ValidateRefs(out reason);
	}

	/// <summary>Local preparation only. Does not bind, enable input, start a session or load a scene.</summary>
	public bool PrepareInterior()
	{
		if (!Application.isPlaying || !isActiveAndEnabled || !ValidateRefs(out _) || !interior.Prepare())
		{
			stage = Stage.Idle;
			return false;
		}

		stage = Stage.Prepared;
		return true;
	}

	private void OnDisable() => stage = Stage.Idle;

	[ContextMenu("Debug/Prepare Local Interior (Play Mode)")]
	private void DebugPrepare()
	{
		bool prepared = PrepareInterior();
		Debug.Log($"[TankSceneLink] Prepare={prepared}, Stage={stage}. This stage does not enable the new binding path.", this);
	}

	[ContextMenu("Debug/Report Readiness")]
	private void ReportReadiness()
	{
		bool valid = ValidateRefs(out string reason);
		Debug.Log($"[TankSceneLink] Refs={valid}, Stage={stage}, ControlBound={control != null && control.IsBound}, InputBound={input != null && input.IsBound}, ModeBound={interior != null && interior.Mode != null && interior.Mode.IsBound}, CanInput={control != null && control.CanInput}. {reason}", this);
	}
}
