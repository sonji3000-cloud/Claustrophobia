using UnityEngine;

/// <summary>Owns one carried round independently of sustained device use.</summary>
[DisallowMultipleComponent]
public sealed class CrewCarry : MonoBehaviour
{
	[SerializeField] private CrewInteract interaction;
	[SerializeField] private CrewSeats seats;
	[SerializeField] private Transform holdPoint;

	public TankRound Held { get; private set; }
	public bool IsLoading { get; private set; }

	private bool returnPending;

	internal bool CanTake(CrewDevice source)
	{
		return isActiveAndEnabled && Held == null && !IsLoading && source != null && holdPoint != null
			&& holdPoint.gameObject.activeInHierarchy && interaction != null && interaction.CanSelect
			&& interaction.Active == null && source == interaction.Selected && source.CanUse(interaction.Crew);
	}

	internal bool TryTake(TankRound round) => TryTake(round, round != null ? round.Device : null);

	internal bool TryTake(TankRound round, CrewDevice source)
	{
		if (!CanTake(source) || round == null || round.Device == null
			|| !round.Device.CanUse(interaction.Crew)) return false;

		if (!round.TryHold(this, holdPoint)) return false;

		Held = round;
		return true;
	}

	public void ReturnRound()
	{
		if (IsLoading)
		{
			returnPending = true;
			return;
		}
		if (Held == null) return;

		TankRound round = Held;
		Held = null;
		round.Return(this);
	}

	internal TankRound CommitLoad(Transform loadPoint)
	{
		TankRound round = Held;
		Held = null;
		IsLoading = false;
		returnPending = false;
		round.Load(this, loadPoint);
		return round;
	}

	internal bool BeginLoad()
	{
		if (Held == null || IsLoading) return false;

		IsLoading = true;
		return true;
	}

	internal void EndLoad()
	{
		IsLoading = false;
		bool shouldReturn = returnPending;
		returnPending = false;
		if (shouldReturn) ReturnRound();
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
