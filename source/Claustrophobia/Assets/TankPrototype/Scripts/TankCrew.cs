using UnityEngine;

/// <summary>Owns the local test operator's role and tank action permissions.</summary>
[DisallowMultipleComponent]
public sealed class TankCrew : MonoBehaviour
{
	public enum Role { None, Driver, Gunner, Commander }

	[SerializeField] private Role role = Role.Driver;

	public Role CurrentRole => isActiveAndEnabled ? role : Role.None;
	public bool CanDrive => CurrentRole == Role.Driver;
	public bool CanAim => CurrentRole == Role.Gunner;
	public bool CanFire => CurrentRole == Role.Gunner;
	public bool CanLook => CurrentRole == Role.Commander;
	public bool CanLoad => CanAim || CanLook;

	// Role assignment is separate from observation keys and camera selection.
	public void SetRole(Role next)
	{
		if ((uint)next > (uint)Role.Commander) return;

		role = next;
	}

	public bool CanView(int station)
	{
		if (!isActiveAndEnabled) return false;

		switch (station)
		{
			case 0: return CurrentRole == Role.Driver;
			case 1: return CurrentRole == Role.Gunner;
			case 2: return CurrentRole == Role.Commander;
			case 3: return true; // F4 is a role-independent prototype view.
			default: return false;
		}
	}
}
