using Fusion;
using UnityEngine;

/// <summary>Replicates the host's health and applied lever positions for interior instruments.</summary>
[DisallowMultipleComponent]
public sealed class TankStatus : NetworkBehaviour
{
	[SerializeField] private TankHealth health;
	[SerializeField] private TankMotor motor;

	[Networked] private float NetworkHP { get; set; }
	[Networked] private float NetworkMaxHP { get; set; }
	[Networked] private int NetworkHits { get; set; }
	[Networked] private Vector2 NetworkLevers { get; set; }

	private NetworkObject entity;
	private bool spawned;

	public bool IsReady => health != null && (entity == null || spawned);
	public float HP => spawned && !HasStateAuthority ? NetworkHP : health != null ? health.CurrentHP : 0f;
	public float MaxHP => spawned && !HasStateAuthority ? NetworkMaxHP : health != null ? health.MaxHP : 0f;
	public int Hits => spawned && !HasStateAuthority ? NetworkHits : health != null ? health.DamageHitCount : 0;

	private void Awake()
	{
		entity = GetComponent<NetworkObject>();
	}

	public override void Spawned()
	{
		spawned = true;
		if (HasStateAuthority) Capture();
	}

	public override void Despawned(NetworkRunner runner, bool hasState) => spawned = false;

	public override void FixedUpdateNetwork()
	{
		if (HasStateAuthority) Capture();
	}

	private void Capture()
	{
		if (health == null) return;

		NetworkHP = health.CurrentHP;
		NetworkMaxHP = health.MaxHP;
		NetworkHits = health.DamageHitCount;
		NetworkLevers = motor != null ? new Vector2(motor.LeftInput, motor.RightInput) : Vector2.zero;
	}

	public Vector2 GetLevers(TankControl control)
	{
		// Match the physical handles immediately on the local driver's machine.
		if (control != null && control.IsDriving)
			return new Vector2(control.LeftLever, control.RightLever);

		if (spawned) return NetworkLevers;
		return motor != null ? new Vector2(motor.LeftInput, motor.RightInput) : Vector2.zero;
	}
}
