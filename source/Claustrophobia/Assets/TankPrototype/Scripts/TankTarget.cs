using System.Collections.Generic;
using UnityEngine;

// Describes one tank. Visibility and sighting memory belong to each observer.
[DisallowMultipleComponent]
[RequireComponent(typeof(TankHealth))]
public sealed class TankTarget : MonoBehaviour
{
	public const string PlayerTag = "player_tank";
	public const string EnemyTag = "enemy_tank";

	[SerializeField] private TankHealth health;
	[SerializeField] private Transform[] sightPoints = new Transform[3];
	[SerializeField] private Transform aimPoint;
	[SerializeField] private Transform groundPoint;

	private static readonly List<TankTarget> candidates = new List<TankTarget>(32);

	public static IReadOnlyList<TankTarget> Candidates => candidates;
	public TankHealth Health => health;
	public Transform AimPoint => aimPoint;
	public Transform GroundPoint => groundPoint;
	public int SightPointCount => sightPoints != null ? sightPoints.Length : 0;
	public bool IsPlayer => CompareTag(PlayerTag);
	public bool IsEnemy => CompareTag(EnemyTag);
	public bool HasFaction => IsPlayer || IsEnemy;
	public bool CanTarget => isActiveAndEnabled && HasFaction && IsConfigured
		&& health.isActiveAndEnabled && health.IsReady && !health.Destroyed;

	public bool IsConfigured
	{
		get
		{
			if (health == null || health.gameObject != gameObject
				|| !OwnsPoint(aimPoint) || !OwnsPoint(groundPoint) || SightPointCount != 3) return false;

			for (int i = 0; i < sightPoints.Length; i++)
			{
				if (!OwnsPoint(sightPoints[i])) return false;

				for (int j = 0; j < i; j++)
					if (sightPoints[i] == sightPoints[j]) return false;
			}

			return true;
		}
	}

	private void Awake()
	{
		if (health == null) health = GetComponent<TankHealth>();
	}

	private void OnEnable()
	{
		if (Application.isPlaying) Register();
	}

	private void Register()
	{
		if (!candidates.Contains(this)) candidates.Add(this);
	}

	private void OnDisable()
	{
		candidates.Remove(this);
	}

	private void OnDestroy()
	{
		candidates.Remove(this);
	}

	public Transform GetSightPoint(int index) => sightPoints[index];

	private bool OwnsPoint(Transform point) => point != null && point.IsChildOf(transform);

	// A child collider's own tag does not determine the tank's faction or identity.
	public static bool TryResolve(Collider hit, out TankTarget target)
	{
		target = hit != null ? hit.GetComponentInParent<TankTarget>() : null;
		return target != null && target.HasFaction && target.IsConfigured;
	}

	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
	private static void ClearRegistry()
	{
		candidates.Clear();
	}

	// One initialization scan also covers Play Mode with domain/scene reload disabled.
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
	private static void RestoreRegistry()
	{
		TankTarget[] targets = FindObjectsByType<TankTarget>();
		for (int i = 0; i < targets.Length; i++)
			if (targets[i].isActiveAndEnabled) targets[i].Register();
	}

	private void OnDrawGizmosSelected()
	{
		Gizmos.color = Color.yellow;
		for (int i = 0; i < SightPointCount; i++)
			if (sightPoints[i] != null) Gizmos.DrawWireSphere(sightPoints[i].position, 0.12f);

		Gizmos.color = Color.red;
		if (aimPoint != null) Gizmos.DrawWireSphere(aimPoint.position, 0.16f);

		Gizmos.color = Color.cyan;
		if (groundPoint != null) Gizmos.DrawWireSphere(groundPoint.position, 0.2f);
	}
}
