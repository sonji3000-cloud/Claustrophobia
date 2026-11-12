using UnityEngine;

/// <summary>Supplies one round at a time to an empty-handed crew member.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(CrewDevice))]
public sealed class TankAmmoRack : MonoBehaviour
{
	[SerializeField] private CrewDevice device;
	[SerializeField] private TankRound roundPrefab;

	internal bool TryTake(CrewInteract user)
	{
		if (!isActiveAndEnabled || device == null || roundPrefab == null || user == null
			|| user.Carry == null || !user.Carry.CanTake(device)) return false;

		TankRound round = Instantiate(roundPrefab, transform.root);
		round.MarkIssued();
		if (user.Carry.TryTake(round, device)) return true;

		round.Consume();
		return false;
	}
}
