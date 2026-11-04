using UnityEngine;

/// <summary>Owns one carried round independently of sustained device use.</summary>
[DisallowMultipleComponent]
public sealed class CrewCarry : MonoBehaviour
{
	[SerializeField] private CrewInteract interaction;
	[SerializeField] private CrewSeats seats;
	[SerializeField] private Transform holdPoint;

	public TankRound Held { get; private set; }

	internal bool TryTake(TankRound round)
	{
		if (!isActiveAndEnabled || Held != null || round == null || round.Device == null || holdPoint == null
			|| !holdPoint.gameObject.activeInHierarchy || interaction == null || !interaction.CanSelect
			|| interaction.Active != null || round.Device != interaction.Selected
			|| !round.Device.CanUse(interaction.Crew)) return false;

		if (!round.TryHold(this, holdPoint)) return false;

		Held = round;
		return true;
	}

	public void ReturnRound()
	{
		if (Held == null) return;

		TankRound round = Held;
		Held = null;
		round.Return(this);
	}

	internal TankRound CommitLoad(Transform loadPoint)
	{
		TankRound round = Held;
		Held = null;
		round.Load(this, loadPoint);
		return round;
	}

	private void OnSeatApplied(TankCrew.Role role) => ReturnRound();

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
