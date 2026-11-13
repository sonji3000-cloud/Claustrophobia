using UnityEngine;

/// <summary>Owns one carried round independently of sustained device use.</summary>
[DisallowMultipleComponent]
public sealed class CrewCarry : MonoBehaviour
{
	[SerializeField] private CrewInteract interaction;
	[SerializeField] private CrewSeats seats;
	[SerializeField] private Transform holdPoint;

	private TankGun gun;
	private TankRound held;

	public TankRound Held => gun != null ? gun.GetLocalRound() : held;

	internal bool UsesNetwork => gun != null;
	internal void BindGun(TankGun next) => gun = next;
	internal bool RequestTake(CrewDevice source) => gun != null && gun.RequestTake(source);
	internal Transform GetAnchor(TankCrew.Role role) => seats != null ? seats.GetAnchor(role) : null;

	internal void SamplePose(ref TankCrewNetworkInput input)
	{
		input.RoundRole = TankCrew.Role.None;
		if (!isActiveAndEnabled || holdPoint == null || !holdPoint.gameObject.activeInHierarchy
			|| interaction == null || interaction.Crew == null || !interaction.Crew.CanLoad) return;

		input.RoundRole = interaction.Crew.CurrentRole;
		holdPoint.GetPositionAndRotation(out input.RoundPosition, out input.RoundRotation);
	}

	internal bool CanTake(CrewDevice source)
	{
		return isActiveAndEnabled && Held == null && source != null && holdPoint != null
			&& holdPoint.gameObject.activeInHierarchy && interaction != null && interaction.CanSelect
			&& interaction.Active == null && source == interaction.Selected && source.CanUse(interaction.Crew);
	}

	internal bool TryTake(TankRound round) => TryTake(round, round != null ? round.Device : null);

	internal bool TryTake(TankRound round, CrewDevice source)
	{
		if (!CanTake(source) || round == null || round.Device == null
			|| !round.Device.CanUse(interaction.Crew)) return false;
		if (UsesNetwork) return RequestTake(source);

		if (!round.TryHold(this, holdPoint)) return false;
		held = round;
		return true;
	}

	public void ReturnRound()
	{
		if (gun != null)
		{
			gun.RequestReturn();
			return;
		}

		if (held == null) return;
		TankRound round = held;
		held = null;
		round.Return(this);
	}

	internal TankRound CommitLoad(Transform loadPoint)
	{
		TankRound round = held;
		held = null;
		round.Load(this, loadPoint);
		return round;
	}

	private void OnSeatApplied(TankCrew.Role role)
	{
		if (!UsesNetwork) ReturnRound();
	}

	private void OnEnable()
	{
		if (seats != null) seats.SeatApplied += OnSeatApplied;
	}

	private void OnDisable()
	{
		if (seats != null) seats.SeatApplied -= OnSeatApplied;
		ReturnRound();
	}
}
