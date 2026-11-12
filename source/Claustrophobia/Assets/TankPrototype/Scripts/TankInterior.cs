using UnityEngine;

/// <summary>Describes the authored interior and prepares its local seat state.</summary>
[DisallowMultipleComponent]
public sealed class TankInterior : MonoBehaviour
{
	[SerializeField] private TankCrew crew;
	[SerializeField] private CrewSeats seats;
	[SerializeField] private CrewInteract interaction;
	[SerializeField] private TankTestMode mode;
	[SerializeField] private CrewDevice driveDevice;
	[SerializeField] private CrewDevice aimDevice;
	[SerializeField] private GunnerSight sight;
	[SerializeField] private DriverScreen driverScreen;
	[SerializeField] private TankBreech breech;
	[SerializeField] private TankLever[] levers = System.Array.Empty<TankLever>();
	[SerializeField] private TankJoystick joystick;
	[SerializeField] private CrewInteractHud interactionHud;
	[SerializeField] private TankStatusPanel statusPanel;

	private bool prepared;

	public TankCrew Crew => crew;
	public CrewSeats Seats => seats;
	public CrewInteract Interaction => interaction;
	public TankTestMode Mode => mode;
	public CrewDevice DriveDevice => driveDevice;
	public CrewDevice AimDevice => aimDevice;
	public GunnerSight Sight => sight;
	public DriverScreen DriverScreen => driverScreen;
	public TankBreech Breech => breech;
	public TankLever[] Levers => levers;
	public TankJoystick Joystick => joystick;
	public CrewInteractHud Hud => interactionHud;
	public TankStatusPanel StatusPanel => statusPanel;
	public bool IsPrepared => prepared && seats != null && seats.IsInitialized;

	public bool ValidateRefs(out string reason)
	{
		if (crew == null || seats == null || interaction == null || mode == null
			|| driveDevice == null || aimDevice == null || sight == null || driverScreen == null
			|| breech == null || joystick == null || interactionHud == null || statusPanel == null)
		{
			reason = "TankInterior requires all local component references.";
			return false;
		}
		if (seats.gameObject != crew.gameObject || interaction.gameObject != crew.gameObject
			|| seats.Crew != crew || interaction.Crew != crew || driveDevice == aimDevice)
		{
			reason = "TankInterior crew, seats and interaction must describe the same player.";
			return false;
		}
		if (levers == null || levers.Length != 2 || levers[0] == null || levers[1] == null || levers[0] == levers[1])
		{
			reason = "TankInterior requires two distinct lever components.";
			return false;
		}
		if (!seats.ValidateRefs(out reason)) return false;
		if (crew.gameObject.scene != gameObject.scene || mode.gameObject.scene != gameObject.scene
			|| interactionHud.gameObject.scene != gameObject.scene || statusPanel.gameObject.scene != gameObject.scene)
		{
			reason = "The player, mode and HUD must belong to the interior scene.";
			return false;
		}

		reason = null;
		return true;
	}

	/// <summary>Call after the loaded interior has completed Awake. Does not bind external systems.</summary>
	public bool Prepare()
	{
		if (!isActiveAndEnabled || !ValidateRefs(out _)) return false;
		if (IsPrepared) return true;
		prepared = seats.Initialize();
		return prepared;
	}

	private void OnDisable() => prepared = false;

	[ContextMenu("Debug/Report Local Preparation")]
	private void ReportPreparation()
	{
		bool valid = ValidateRefs(out string reason);
		Debug.Log($"[TankInterior] Refs={valid}, Prepared={IsPrepared}, Seats={seats != null && seats.IsInitialized}. {reason}", this);
	}
}
